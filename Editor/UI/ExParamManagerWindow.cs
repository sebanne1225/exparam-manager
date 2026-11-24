using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor
{
    public sealed class ExParamManagerWindow : EditorWindow
    {
        private enum QuickFilterTab
        {
            All = 0,
            Reducible = 1,
            UnusedCandidate = 2,
            ChangedOnly = 3,
            Protected = 4,
        }

        private const double AutoRescanDebounceSeconds = 0.35;
        private const double AutoRescanDisableThresholdMs = 400.0;
        private const string SplitterPrefKey = "Sebanne.ExParamManager.LeftPaneRatio";
        private const float SplitterWidth = 8f;
        private const float MinLeftPaneWidth = 340f;
        private const float MinRightPaneWidth = 280f;

        private const float ColWidthTypeBit = 56f;
        private const float ColWidthToggle = 32f;
        private const float ColWidthAction = 100f;

        private static readonly string[] TypeFilterLabels = { "全型", "Float (8b)", "Int (8b)", "Bool (1b)" };
        private static readonly string[] SortModeLabels = { "消費bitが大きい順", "名前順", "参照が少ない順", "型順" };
        private static readonly string[] BoolActionLabels = { "そのまま", "削除 (0b)", "自分だけ" };
        private static readonly string[] NumericActionLabels = { "そのまま", "削除 (0b)", "自分だけ", "bit縮小" };

        private VRCAvatarDescriptor _targetAvatar;
        private AvatarScanResult _scanResult;

        private bool _autoRescanPending;
        private double _autoRescanAt;
        private double _lastScanDurationMs;
        private bool _autoRescanDisabledForPerformance;
        private bool _needsManualRescan;
        private bool _showTechNotes;

        private float _leftPaneRatio = 0.56f;
        private bool _isResizingSplitter;

        private string _searchText = string.Empty;
        private int _typeFilterIndex;
        private QuickFilterTab _quickTab = QuickFilterTab.All;
        private int _sortModeIndex; // 0=BitCostDesc, 1=NameAsc, 2=RefCountAsc, 3=TypeOrder
        private string _selectedPrefabGroupKey = string.Empty;
        private bool _showPrefabGroups = true;
        private bool _showReferencesSection;

        private string _selectedParamKey = string.Empty;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private readonly Dictionary<string, bool> _animatorGroupFoldouts = new Dictionary<string, bool>(StringComparer.Ordinal);

        // OnGUI 内で遅延初期化するスタイル
        private GUIStyle _boldNameStyle;
        private GUIStyle _subLabelStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _headerLabelStyle;
        private GUIStyle _wrappedMiniStyle;
        private GUIStyle _bigMetricStyle;
        private GUIStyle _bigDeltaStyle;

        [MenuItem("Tools/Sebanne/ExParam Manager")]
        public static void Open()
        {
            var win = GetWindow<ExParamManagerWindow>("ExParam Manager");
            win.minSize = new Vector2(720f, 480f);
            win.Show();
        }

        public static void OpenFor(VRCAvatarDescriptor descriptor)
        {
            var win = GetWindow<ExParamManagerWindow>("ExParam Manager");
            win.minSize = new Vector2(720f, 480f);
            if (descriptor != null)
            {
                win._targetAvatar = descriptor;
                win._selectedPrefabGroupKey = string.Empty;
                win._selectedParamKey = string.Empty;
                win.RunScan();
            }
            win.Show();
        }

        private void OnEnable()
        {
            _leftPaneRatio = Mathf.Clamp(EditorPrefs.GetFloat(SplitterPrefKey, 0.56f), 0.35f, 0.78f);
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;

            if (_targetAvatar == null)
            {
                _targetAvatar = FindObjectOfType<VRCAvatarDescriptor>();
            }

            if (_targetAvatar != null)
            {
                RunScan();
            }
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnHierarchyChanged()
        {
            if (_targetAvatar == null)
            {
                return;
            }

            if (_autoRescanDisabledForPerformance)
            {
                _needsManualRescan = true;
                Repaint();
                return;
            }

            _autoRescanPending = true;
            _autoRescanAt = EditorApplication.timeSinceStartup + AutoRescanDebounceSeconds;
        }

        private void OnEditorUpdate()
        {
            if (!_autoRescanPending || _targetAvatar == null)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _autoRescanAt)
            {
                _autoRescanPending = false;
                RunScan();
                Repaint();
            }
        }

        private void OnUndoRedo()
        {
            if (_targetAvatar != null)
            {
                RunScan();
                Repaint();
            }
        }

        private void RunScan()
        {
            _autoRescanPending = false;
            _needsManualRescan = false;

            if (_targetAvatar == null)
            {
                _scanResult = null;
                return;
            }

            var sw = Stopwatch.StartNew();
            _scanResult = AvatarScanner.Scan(_targetAvatar);
            sw.Stop();
            _lastScanDurationMs = sw.Elapsed.TotalMilliseconds;

            if (_lastScanDurationMs > AutoRescanDisableThresholdMs)
            {
                _autoRescanDisabledForPerformance = true;
            }

            EnsureValidSelection();
        }

        private void EnsureValidSelection()
        {
            if (_scanResult == null || _scanResult.Parameters.Count == 0)
            {
                _selectedParamKey = string.Empty;
                return;
            }

            if (!string.IsNullOrEmpty(_selectedParamKey) && _scanResult.Parameters.Any(p => p.EffectiveName == _selectedParamKey))
            {
                return;
            }

            ExParamManagerComponent comp = _targetAvatar != null
                ? _targetAvatar.GetComponentInChildren<ExParamManagerComponent>(true)
                : null;
            List<ParameterSnapshot> filtered = GetFilteredAndSortedParameters(comp);
            _selectedParamKey = filtered.Count > 0
                ? filtered[0].EffectiveName
                : _scanResult.Parameters[0].EffectiveName;
        }

        private void EnsureStyles()
        {
            if (_boldNameStyle != null)
            {
                return;
            }

            _boldNameStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };

            _subLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) },
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };

            _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
            };

            _headerLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            _wrappedMiniStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);

            _bigMetricStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
            };

            _bigDeltaStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.42f, 0.88f, 0.56f, 1f) },
            };
        }

        private void OnGUI()
        {
            EnsureStyles();

            ExParamManagerComponent comp = _targetAvatar != null
                ? _targetAvatar.GetComponentInChildren<ExParamManagerComponent>(true)
                : null;

            DrawTopBar(comp);

            if (_targetAvatar == null)
            {
                EditorGUILayout.HelpBox("上の「対象アバター」欄で編集・確認したいアバター（VRCAvatarDescriptor）を選択してください。", MessageType.Info);
                DrawBeginnerQuickGuide();
                return;
            }

            if (_scanResult == null)
            {
                RunScan();
            }

            DrawFilterAndPrefabSection(comp);

            EditorGUILayout.Space(4f);
            float availableWidth = Mathf.Max(MinLeftPaneWidth + MinRightPaneWidth + SplitterWidth, position.width - 16f);
            float maxLeftWidth = Mathf.Max(MinLeftPaneWidth, availableWidth - MinRightPaneWidth - SplitterWidth);
            float leftWidth = Mathf.Clamp(availableWidth * _leftPaneRatio, MinLeftPaneWidth, maxLeftWidth);

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawLeftTable(comp, leftWidth);
                DrawVerticalSplitter(availableWidth);
                DrawRightDetailPanel(comp);
            }
        }

        private void DrawVerticalSplitter(float availableWidth)
        {
            Rect splitterRect = GUILayoutUtility.GetRect(SplitterWidth, 10f, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                float cx = splitterRect.x + (splitterRect.width - 1f) * 0.5f;
                EditorGUI.DrawRect(
                    new Rect(cx, splitterRect.y + 4f, 1f, Mathf.Max(0f, splitterRect.height - 8f)),
                    new Color(1f, 1f, 1f, 0.16f));

                float cy = splitterRect.y + splitterRect.height * 0.5f;
                Color dotColor = new Color(1f, 1f, 1f, 0.50f);
                for (int d = -1; d <= 1; d++)
                {
                    EditorGUI.DrawRect(new Rect(cx - 0.5f, cy + d * 5f - 1f, 2f, 2f), dotColor);
                }
            }

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && splitterRect.Contains(evt.mousePosition))
            {
                _isResizingSplitter = true;
                evt.Use();
            }
            else if (_isResizingSplitter && evt.type == EventType.MouseDrag)
            {
                _leftPaneRatio = Mathf.Clamp((evt.mousePosition.x - 8f) / availableWidth, 0.35f, 0.78f);
                Repaint();
                evt.Use();
            }
            else if (_isResizingSplitter && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                _isResizingSplitter = false;
                EditorPrefs.SetFloat(SplitterPrefKey, _leftPaneRatio);
            }
        }

        private void DrawTopBar(ExParamManagerComponent comp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var nextAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                        "対象アバター",
                        _targetAvatar,
                        typeof(VRCAvatarDescriptor),
                        true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _targetAvatar = nextAvatar;
                        _selectedPrefabGroupKey = string.Empty;
                        _selectedParamKey = string.Empty;
                        _autoRescanDisabledForPerformance = false;
                        RunScan();
                    }

                    Color prevBg = GUI.backgroundColor;
                    if (_needsManualRescan)
                    {
                        GUI.backgroundColor = new Color(1f, 0.78f, 0.25f, 1f);
                    }

                    string refreshLabel = _needsManualRescan ? "更新*" : "更新";
                    if (GUILayout.Button(refreshLabel, GUILayout.Width(64f)))
                    {
                        RunScan();
                    }
                    GUI.backgroundColor = prevBg;

                    if (comp != null && CountChangedRules(comp) > 0)
                    {
                        if (GUILayout.Button("すべて元に戻す", GUILayout.Width(116f)))
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Reset All ExParam Rules");
                            comp.ClearAllRules();
                        }
                    }
                }

                if (_targetAvatar != null)
                {
                    if (comp == null)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField("このアバターはまだ閲覧モードです。削減ルールを設定するには下のボタンを押してください（元アバターは変更されません）。", _wrappedMiniStyle);
                            if (GUILayout.Button("ExParam Manager を追加して編集を始める", GUILayout.Height(26f)))
                            {
                                Undo.AddComponent<ExParamManagerComponent>(_targetAvatar.gameObject);
                                RunScan();
                            }
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        bool nextGlobalEnabled = EditorGUILayout.ToggleLeft(
                            "ビルド時に適用する（ツール全体 ON/OFF）",
                            comp.enabledOnBuild,
                            EditorStyles.boldLabel);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Toggle ExParam Manager Enabled");
                            comp.enabledOnBuild = nextGlobalEnabled;
                        }

                        if (!comp.enabledOnBuild)
                        {
                            EditorGUILayout.HelpBox(
                                "現在ツール全体が一時オフのため、ビルド時にどの削減ルールも適用されません（各パラメータの設定は保持されます）。",
                                MessageType.Warning);
                        }
                    }

                    if (_scanResult != null)
                    {
                        BitProjection proj = BitBarDrawer.CalculateProjection(_scanResult, comp, EnvironmentCapabilities.HasVrcSdk3106);
                        BitBarDrawer.DrawBitBar(proj);

                        if (_scanResult.HasSyncParameterSequence && (comp == null || !comp.allowOverrideWithSyncSequence))
                        {
                            EditorGUILayout.HelpBox("ModularAvatarSyncParameterSequence を検出したため、同期列を変える操作はビルド時に安全ブロックされます。", MessageType.Warning);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        "非破壊で動作します：元のアバター・元アセットは変更されません。ビルド時のみ適用され、コンポーネントを外せば元に戻ります。",
                        EditorStyles.miniLabel);
                    if (GUILayout.Button(_showTechNotes ? "？ 閉じる" : "？ 詳しい注意", EditorStyles.miniButton, GUILayout.Width(88f)))
                    {
                        _showTechNotes = !_showTechNotes;
                    }
                }

                if (_showTechNotes)
                {
                    string perfNote = _autoRescanDisabledForPerformance
                        ? $"・このアバターは規模が大きいため自動更新を停止しています（前回 {_lastScanDurationMs:0}ms）。階層変更後は右上の「更新*」を押してください。\n"
                        : string.Empty;

                    string sdkNote = EnvironmentCapabilities.HasVrcSdk3106
                        ? "・bit 幅縮小（ResizeBitWidth）は VRChat 2 クライアント間同期が未検証の実験的機能です。\n"
                        : "・現在の SDK（3.10.5 以下）では bit 幅縮小は適用されず、削除・自分だけ（非同期化）のみ有効です。\n";

                    EditorGUILayout.HelpBox(
                        perfNote
                        + sdkNote
                        + "・Edit Mode 解析のため VRCFury 等 IParameterProvider 未実装コンポーネントがビルド時に追加するパラメータはビルド後に加算されます。",
                        MessageType.Info);
                }
            }
        }

        private void DrawFilterAndPrefabSection(ExParamManagerComponent comp)
        {
            if (_scanResult == null)
            {
                return;
            }

            CountTabs(
                comp,
                out int totalCount,
                out int reducibleCount,
                out int unusedCount,
                out int changedCount,
                out int protectedCount,
                out int unappliedStrictIntCount,
                out int unappliedStrictIntSavings);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool showApplyAll = comp != null && EnvironmentCapabilities.HasVrcSdk3106 && unappliedStrictIntCount > 0;
                bool narrow = position.width < 880f;

                // クイック絞り込みタブ行
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawQuickTabButton(QuickFilterTab.All, $"すべて ({totalCount})");
                    DrawQuickTabButton(QuickFilterTab.Reducible, $"減らせそう ({reducibleCount})");
                    DrawQuickTabButton(QuickFilterTab.UnusedCandidate, $"未使用かも ({unusedCount})");
                    DrawQuickTabButton(QuickFilterTab.ChangedOnly, $"変更あり ({changedCount})");
                    DrawQuickTabButton(QuickFilterTab.Protected, $"注意あり ({protectedCount})");

                    GUILayout.FlexibleSpace();

                    if (showApplyAll && !narrow)
                    {
                        DrawApplyAllStrictIntButton(comp, unappliedStrictIntSavings);
                    }
                }

                if (showApplyAll && narrow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        DrawApplyAllStrictIntButton(comp, unappliedStrictIntSavings);
                    }
                }

                // 検索・型・並び替え行
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("検索 (名前 / メニュー名)", GUILayout.Width(134f));
                    _searchText = EditorGUILayout.TextField(_searchText, GUILayout.MinWidth(100f));
                    if (!string.IsNullOrEmpty(_searchText) && GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        _searchText = string.Empty;
                        GUI.FocusControl(null);
                    }

                    _typeFilterIndex = EditorGUILayout.Popup(_typeFilterIndex, TypeFilterLabels, GUILayout.Width(96f));
                    _sortModeIndex = EditorGUILayout.Popup(_sortModeIndex, SortModeLabels, GUILayout.Width(138f));
                }

                // Prefab / 衣装別集計（2 グループ以上あるときのみ表示）
                if (_scanResult.PrefabGroups.Count >= 2)
                {
                    _showPrefabGroups = EditorGUILayout.Foldout(_showPrefabGroups, $"衣装・Prefab 別の消費内訳（{_scanResult.PrefabGroups.Count} グループ）", true);
                    if (_showPrefabGroups)
                    {
                        DrawPrefabGroupChipsWrapped();
                    }
                }
            }
        }

        private void DrawApplyAllStrictIntButton(ExParamManagerComponent comp, int unappliedStrictIntSavings)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.32f, 0.72f, 0.45f, 1f);
            if (GUILayout.Button($"推奨の縮小をまとめて適用 (-{unappliedStrictIntSavings}b)", GUILayout.Height(20f)))
            {
                ApplyAllStrictIntRecommendations(comp);
            }
            GUI.backgroundColor = prev;
        }

        private void DrawQuickTabButton(QuickFilterTab tab, string label)
        {
            bool active = _quickTab == tab;
            Color prev = GUI.backgroundColor;
            if (active)
            {
                GUI.backgroundColor = new Color(0.38f, 0.62f, 0.90f, 1f);
            }

            if (GUILayout.Toggle(active, label, EditorStyles.miniButton, GUILayout.Height(20f)))
            {
                if (!active)
                {
                    _quickTab = tab;
                    EnsureValidSelection();
                }
            }

            GUI.backgroundColor = prev;
        }

        private void DrawPrefabGroupChipsWrapped()
        {
            int maxPerRow = Mathf.Clamp(Mathf.FloorToInt((position.width - 44f) / 175f), 2, 5);
            int col = 0;

            EditorGUILayout.BeginHorizontal();
            try
            {
                Color prevAll = GUI.backgroundColor;
                if (string.IsNullOrEmpty(_selectedPrefabGroupKey))
                {
                    GUI.backgroundColor = new Color(0.38f, 0.62f, 0.90f, 1f);
                }
                if (GUILayout.Button("すべて", EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    _selectedPrefabGroupKey = string.Empty;
                    EnsureValidSelection();
                }
                GUI.backgroundColor = prevAll;
                col = 1;

                for (int i = 0; i < _scanResult.PrefabGroups.Count; i++)
                {
                    if (col >= maxPerRow)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        col = 0;
                    }

                    PrefabBitGroup g = _scanResult.PrefabGroups[i];
                    bool active = _selectedPrefabGroupKey == g.GroupKey;
                    string label = $"{g.DisplayName} ({g.TotalSyncedBits}b)";
                    Color prev = GUI.backgroundColor;
                    if (active)
                    {
                        GUI.backgroundColor = new Color(0.38f, 0.62f, 0.90f, 1f);
                    }
                    if (GUILayout.Button(new GUIContent(label, "クリックで絞り込み＆Hierarchy で場所を表示"), EditorStyles.miniButton))
                    {
                        _selectedPrefabGroupKey = g.GroupKey;
                        EnsureValidSelection();
                        if (g.RootObject != null)
                        {
                            EditorGUIUtility.PingObject(g.RootObject);
                        }
                    }
                    GUI.backgroundColor = prev;
                    col++;
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawLeftTable(ExParamManagerComponent comp, float paneWidth)
        {
            List<ParameterSnapshot> filtered = GetFilteredAndSortedParameters(comp);
            int total = _scanResult != null ? _scanResult.Parameters.Count : 0;
            const float fixedRowWidth = 5f + ColWidthTypeBit + ColWidthToggle + ColWidthAction + 42f;
            float nameColWidth = Mathf.Max(100f, paneWidth - fixedRowWidth);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(paneWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField($"パラメータ一覧（表示 {filtered.Count} / 全 {total} 件）", EditorStyles.boldLabel);

                // 固定幅列ヘッダー（データ行と同じ nameColWidth を使い列ズレを防ぐ）
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Space(5f);
                    GUILayout.Label("パラメータ / メニュー名", _headerLabelStyle, GUILayout.Width(nameColWidth));
                    GUILayout.Label("型 / bit", _headerLabelStyle, GUILayout.Width(ColWidthTypeBit));
                    GUILayout.Label("適用", _headerLabelStyle, GUILayout.Width(ColWidthToggle));
                    GUILayout.Label("削減方法", _headerLabelStyle, GUILayout.Width(ColWidthAction));
                }

                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                try
                {
                    if (filtered.Count == 0)
                    {
                        EditorGUILayout.Space(8f);
                        EditorGUILayout.HelpBox("現在の絞り込み条件に一致するパラメータがありません。", MessageType.Info);
                    }
                    else
                    {
                        for (int i = 0; i < filtered.Count; i++)
                        {
                            DrawParameterRow(comp, filtered[i], nameColWidth);
                        }
                    }
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawParameterRow(ExParamManagerComponent comp, ParameterSnapshot p, float nameColWidth)
        {
            ParameterRule rule = comp != null ? comp.FindRule(p.EffectiveName, p.SourceComponent) : null;
            ParameterAction curAction = rule != null ? rule.action : ParameterAction.Keep;
            bool isSelected = _selectedParamKey == p.EffectiveName;
            bool hasRule = rule != null && curAction != ParameterAction.Keep && !p.IsInternalParameter;
            bool isRuleActive = hasRule && !rule.disabled && (comp == null || comp.enabledOnBuild);

            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(38f));
            try
            {
                if (Event.current.type == EventType.Repaint)
                {
                    Color bg = isSelected
                        ? new Color(0.20f, 0.42f, 0.68f, 0.35f)
                        : new Color(1f, 1f, 1f, 0.03f);
                    EditorGUI.DrawRect(rowRect, bg);

                    if (hasRule)
                    {
                        Color stripeColor = !isRuleActive
                            ? new Color(0.50f, 0.50f, 0.50f, 0.70f)
                            : curAction == ParameterAction.Remove
                                ? new Color(0.85f, 0.30f, 0.30f, 1f)
                                : curAction == ParameterAction.MakeLocal
                                    ? new Color(0.28f, 0.58f, 0.90f, 1f)
                                    : new Color(0.90f, 0.65f, 0.20f, 1f);
                        EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 2f, 3f, rowRect.height - 4f), stripeColor);
                    }
                }

                GUILayout.Space(5f);

                // 列1: パラメータ名 + 状態バッジ・メニュー名/参照サマリ（明示幅でヘッダーと整列）
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(nameColWidth)))
                {
                    if (GUILayout.Button(new GUIContent(p.EffectiveName, p.EffectiveName), _boldNameStyle, GUILayout.Width(nameColWidth), GUILayout.Height(18f)))
                    {
                        _selectedParamKey = p.EffectiveName;
                        GUI.FocusControl(null);
                    }

                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(nameColWidth), GUILayout.Height(14f)))
                    {
                        DrawInlineRowBadge(p);
                        string subText = BuildRowSubText(p);
                        GUILayout.Label(subText, _subLabelStyle, GUILayout.Height(14f), GUILayout.ExpandWidth(true));
                    }
                }

                // 列2: 型 & bit
                int afterBit = EstimateSingleAfterBit(comp, p, rule);
                string bitText = p.EffectiveBitCost != afterBit
                    ? $"{p.EffectiveBitCost}→{afterBit}b"
                    : $"{p.EffectiveBitCost}b";
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ColWidthTypeBit)))
                {
                    GUILayout.Label(p.ValueKind.ToString(), EditorStyles.miniLabel, GUILayout.Width(ColWidthTypeBit), GUILayout.Height(16f));
                    GUILayout.Label(bitText, _subLabelStyle, GUILayout.Width(ColWidthTypeBit), GUILayout.Height(16f));
                }

                // 列3: 項目ごと適用 ON/OFF
                using (new EditorGUILayout.HorizontalScope(GUILayout.Width(ColWidthToggle), GUILayout.Height(34f)))
                {
                    if (comp != null && hasRule)
                    {
                        GUILayout.FlexibleSpace();
                        EditorGUI.BeginChangeCheck();
                        bool nextEnabled = EditorGUILayout.Toggle(
                            new GUIContent(string.Empty, "この項目の削減を適用する（オフで一時停止）"),
                            !rule.disabled,
                            GUILayout.Width(16f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Toggle ExParam Rule Enabled");
                            rule.disabled = !nextEnabled;
                            _selectedParamKey = p.EffectiveName;
                        }
                        GUILayout.FlexibleSpace();
                    }
                    else
                    {
                        GUILayout.Label(string.Empty, GUILayout.Width(ColWidthToggle));
                    }
                }

                // 列4: 削減方法
                DrawInlineActionSelector(comp, p, rule);
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                _selectedParamKey = p.EffectiveName;
                Repaint();
            }
        }

        private static string BuildRowSubText(ParameterSnapshot p)
        {
            if (!string.IsNullOrEmpty(p.PrimaryMenuLabel))
            {
                return "メニュー: " + p.PrimaryMenuLabel;
            }

            if (p.References.Count == 0)
            {
                return "参照なし（未使用かも）";
            }

            var parts = new List<string>();
            if (p.AnimatorRefCount > 0) parts.Add($"アニメ {p.AnimatorRefCount}");
            if (p.DriverRefCount > 0) parts.Add($"Driver {p.DriverRefCount}");
            if (p.DynamicsRefCount > 0) parts.Add($"PhysBone等 {p.DynamicsRefCount}");
            if (p.ReactiveRefCount > 0) parts.Add($"MA {p.ReactiveRefCount}");
            return parts.Count > 0
                ? "メニューなし・" + string.Join(" / ", parts)
                : $"参照 {p.References.Count}件";
        }

        private void DrawInlineRowBadge(ParameterSnapshot p)
        {
            GetBadgeInfo(p, out string text, out Color color);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Color prev = _badgeStyle.normal.textColor;
            _badgeStyle.normal.textColor = color;
            GUILayout.Label($"[{text}]", _badgeStyle, GUILayout.Height(14f), GUILayout.ExpandWidth(false));
            _badgeStyle.normal.textColor = prev;
        }

        private static void GetBadgeInfo(ParameterSnapshot p, out string text, out Color color)
        {
            if (p.IsInternalParameter)
            {
                text = "内部 (v1.1)";
                color = new Color(0.68f, 0.68f, 0.72f, 1f);
                return;
            }

            if (p.Suggestion.HasStrictIntRecommendation)
            {
                text = $"縮小おすすめ ({p.Suggestion.RecommendedIntBitWidth}b)";
                color = new Color(0.42f, 0.88f, 0.56f, 1f);
                return;
            }

            if (p.Suggestion.IsRadialFloatReferenceCandidate)
            {
                text = "スライダー候補";
                color = new Color(0.45f, 0.78f, 0.95f, 1f);
                return;
            }

            if (p.Suggestion.IsUnusedCandidate)
            {
                text = "未使用かも";
                color = new Color(0.95f, 0.78f, 0.35f, 1f);
                return;
            }

            if (p.ProtectionFlags != ProtectionFlags.None)
            {
                text = "保護注意";
                color = new Color(0.95f, 0.58f, 0.38f, 1f);
                return;
            }

            text = string.Empty;
            color = Color.white;
        }

        private void DrawInlineActionSelector(ExParamManagerComponent comp, ParameterSnapshot p, ParameterRule rule)
        {
            if (p.IsInternalParameter)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.Button("確認のみ (v1.1)", EditorStyles.miniButton, GUILayout.Width(ColWidthAction));
                }
                return;
            }

            if (comp == null)
            {
                GUILayout.Label("閲覧中", EditorStyles.miniLabel, GUILayout.Width(ColWidthAction), GUILayout.Height(34f));
                return;
            }

            ParameterAction curAction = rule != null ? rule.action : ParameterAction.Keep;
            bool isBool = p.ValueKind == ParameterValueKind.Bool;
            int curIndex = ActionToPopupIndex(curAction, isBool);
            string[] labels = isBool ? BoolActionLabels : NumericActionLabels;

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(curIndex, labels, GUILayout.Width(ColWidthAction));
            if (EditorGUI.EndChangeCheck())
            {
                ParameterAction nextAction = PopupIndexToAction(nextIndex, isBool);
                ApplyParameterAction(comp, p, nextAction);
                _selectedParamKey = p.EffectiveName;
            }
        }

        private void ApplyParameterAction(ExParamManagerComponent comp, ParameterSnapshot p, ParameterAction nextAction)
        {
            if (comp == null || p == null || p.IsInternalParameter)
            {
                return;
            }

            if (nextAction == ParameterAction.Keep)
            {
                ExParamManagerEditor.RecordEdit(comp, "Reset ExParam Rule to Keep");
                comp.RemoveRule(p.EffectiveName, p.SourceComponent);
                return;
            }

            ExParamManagerEditor.RecordEdit(comp, "Change ExParam Rule Action");
            ParameterRule targetRule = comp.GetOrCreateRule(
                p.EffectiveName,
                p.SourceComponent,
                p.SourceObjectPath,
                p.IsInternalParameter,
                p.OriginalName);
            targetRule.disabled = false;
            targetRule.action = nextAction;

            if (nextAction == ParameterAction.ResizeBitWidth)
            {
                if (p.Suggestion.HasStrictIntRecommendation)
                {
                    targetRule.targetBitWidth = p.Suggestion.RecommendedIntBitWidth;
                }
                else if (p.Suggestion.IsRadialFloatReferenceCandidate)
                {
                    targetRule.targetBitWidth = 4;
                }
            }
        }

        private void ApplyAllStrictIntRecommendations(ExParamManagerComponent comp)
        {
            if (comp == null || _scanResult == null)
            {
                return;
            }

            ExParamManagerEditor.RecordEdit(comp, "Apply Recommended Int BitWidths");
            for (int i = 0; i < _scanResult.Parameters.Count; i++)
            {
                ParameterSnapshot p = _scanResult.Parameters[i];
                if (!p.Suggestion.HasStrictIntRecommendation || p.IsInternalParameter)
                {
                    continue;
                }

                ParameterRule existing = comp.FindRule(p.EffectiveName, p.SourceComponent);
                if (existing != null && existing.action != ParameterAction.Keep)
                {
                    continue;
                }

                ParameterRule r = comp.GetOrCreateRule(
                    p.EffectiveName,
                    p.SourceComponent,
                    p.SourceObjectPath,
                    p.IsInternalParameter,
                    p.OriginalName);
                r.disabled = false;
                r.action = ParameterAction.ResizeBitWidth;
                r.targetBitWidth = p.Suggestion.RecommendedIntBitWidth;
            }
        }

        private void DrawRightDetailPanel(ExParamManagerComponent comp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                ParameterSnapshot p = _scanResult?.Parameters.FirstOrDefault(x => x.EffectiveName == _selectedParamKey);
                if (p == null)
                {
                    DrawBeginnerQuickGuide();
                    return;
                }

                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                try
                {
                    ParameterRule rule = comp != null ? comp.FindRule(p.EffectiveName, p.SourceComponent) : null;
                    DrawDetailOverviewCard(p);
                    EditorGUILayout.Space(6f);
                    DrawDetailActionSection(comp, p, rule);
                    EditorGUILayout.Space(6f);
                    DrawDetailComparisonCard(comp, p, rule);
                    EditorGUILayout.Space(6f);
                    DrawDetailReferencesSection(p);
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawBeginnerQuickGuide()
        {
            EditorGUILayout.LabelField("ExParam Manager ガイド", _sectionHeaderStyle);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "使い方 3 ステップ:\n"
                + "1. 左の一覧から気になるパラメータをクリックします（メニュー名も表示されます）。\n"
                + "2. 上のタブ「減らせそう」「未使用かも」で節約候補をすぐに見つけられます。\n"
                + "3. 右パネルで「削除」「自分だけ」「bit縮小」を選び、そのままアップロードすれば非破壊で適用されます。",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "削減方法（どれを選ぶとどうなる？）:\n"
                + "・そのまま: 変更せずそのまま使います。\n"
                + "・削除 (0b): パラメータを消し、専用メニュー項目も隠します（使わないギミック向け）。\n"
                + "・自分だけ (0b): 自分の画面では動きますが他プレイヤーには同期しません（自分専用設定向け）。\n"
                + "・bit縮小 (実験): Int/Float の消費 bit を 2〜8 bit に減らします（SDK 3.10.6以降・2クライアント同期未検証）。",
                MessageType.None);
        }

        private void DrawDetailOverviewCard(ParameterSnapshot p)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("メニュー / パラメータ", _subLabelStyle);
                string heading = !string.IsNullOrEmpty(p.PrimaryMenuLabel)
                    ? $"{p.PrimaryMenuLabel}（{p.EffectiveName}）"
                    : $"{p.EffectiveName}（メニューなし）";
                EditorGUILayout.LabelField(heading, _sectionHeaderStyle);

                string syncText = p.IsSynced ? "同期あり" : "非同期 (0b)";
                string savedText = p.Saved ? "あり" : "なし";
                EditorGUILayout.LabelField(
                    $"型: {p.ValueKind} / 消費: {p.EffectiveBitCost} bit ({syncText}) / 状態保存(Saved): {savedText} / 初期値: {p.DefaultValue}",
                    _wrappedMiniStyle);

                string originText = string.IsNullOrEmpty(p.SourceObjectPath)
                    ? $"出所: {p.PrefabGroupDisplayName}"
                    : $"出所: {p.PrefabGroupDisplayName} ({p.SourceObjectPath})";
                EditorGUILayout.LabelField(originText, _wrappedMiniStyle);

                EditorGUILayout.Space(2f);
                UnityEngine.Object paramTarget = ResolveParameterAssetOrComponent(p);
                UnityEngine.Object menuTarget = ResolvePrimaryMenuTarget(p);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(paramTarget == null))
                    {
                        if (GUILayout.Button("パラメーターを見る", GUILayout.Height(24f)))
                        {
                            PingAndSelect(paramTarget);
                        }
                    }

                    using (new EditorGUI.DisabledScope(menuTarget == null))
                    {
                        string menuBtnText = menuTarget != null ? "メニューを見る" : "メニューなし";
                        if (GUILayout.Button(menuBtnText, GUILayout.Height(24f)))
                        {
                            PingAndSelect(menuTarget);
                        }
                    }
                }

                if (p.ProtectionReasons.Count > 0)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.HelpBox("保護・注意理由:\n・" + string.Join("\n・", p.ProtectionReasons), MessageType.Warning);
                }
            }
        }

        private UnityEngine.Object ResolveParameterAssetOrComponent(ParameterSnapshot p)
        {
            if (p == null)
            {
                return null;
            }

            if (p.SourceComponent != null && (_targetAvatar == null || p.SourceComponent != _targetAvatar))
            {
                return p.SourceComponent;
            }

            if (_targetAvatar != null && _targetAvatar.expressionParameters != null)
            {
                return _targetAvatar.expressionParameters;
            }

            return p.SourceComponent;
        }

        private static UnityEngine.Object ResolvePrimaryMenuTarget(ParameterSnapshot p)
        {
            if (p == null)
            {
                return null;
            }

            for (int i = 0; i < p.References.Count; i++)
            {
                ParameterReference rf = p.References[i];
                if (rf.Kind == ReferenceKind.MaMenuItem && rf.TargetObject != null)
                {
                    return rf.TargetObject;
                }
            }

            for (int i = 0; i < p.References.Count; i++)
            {
                ParameterReference rf = p.References[i];
                if ((rf.Kind == ReferenceKind.ExpressionsMenuAsset || rf.Kind == ReferenceKind.MaMenuInstaller)
                    && rf.TargetObject != null)
                {
                    return rf.TargetObject;
                }
            }

            return null;
        }

        private void DrawDetailActionSection(ExParamManagerComponent comp, ParameterSnapshot p, ParameterRule rule)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (comp == null)
                {
                    EditorGUILayout.LabelField("削減方法を選ぶ", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("アバターに ExParam Manager コンポーネントを追加すると、ここで操作を切り替えられます。", MessageType.Info);
                    if (_targetAvatar != null && GUILayout.Button("ExParam Manager を追加して編集を始める", GUILayout.Height(24f)))
                    {
                        Undo.AddComponent<ExParamManagerComponent>(_targetAvatar.gameObject);
                        RunScan();
                    }
                    return;
                }

                if (p.IsInternalParameter)
                {
                    EditorGUILayout.LabelField("削減方法を選ぶ", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("Modular Avatar の内部パラメータ（internalParameter）のため、初版では確認・逆引きのみ対応しています（変更操作は v1.1 で対応予定）。", MessageType.Info);
                    return;
                }

                ParameterAction curAction = rule != null ? rule.action : ParameterAction.Keep;
                bool isBool = p.ValueKind == ParameterValueKind.Bool;

                if (rule != null && curAction != ParameterAction.Keep)
                {
                    EditorGUI.BeginChangeCheck();
                    bool ruleEnabled = EditorGUILayout.ToggleLeft(
                        "削減を適用（オフで一時停止）",
                        !rule.disabled,
                        EditorStyles.boldLabel);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Toggle ExParam Rule Enabled");
                        rule.disabled = !ruleEnabled;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("削減方法を選ぶ", EditorStyles.boldLabel);
                }

                // 大きめ切替ボタン行
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawActionChoiceButton(comp, p, ParameterAction.Keep, curAction, "そのまま", new Color(0.50f, 0.55f, 0.62f, 1f));
                    DrawActionChoiceButton(comp, p, ParameterAction.Remove, curAction, "削除 (0b)", new Color(0.85f, 0.35f, 0.35f, 1f));
                    DrawActionChoiceButton(comp, p, ParameterAction.MakeLocal, curAction, "自分だけ (0b)", new Color(0.32f, 0.62f, 0.92f, 1f));
                    if (!isBool)
                    {
                        DrawActionChoiceButton(comp, p, ParameterAction.ResizeBitWidth, curAction, "bit縮小 (実験)", new Color(0.90f, 0.65f, 0.22f, 1f));
                    }
                }

                if (curAction == ParameterAction.Remove && rule != null)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUI.BeginChangeCheck();
                    string menuHint = !string.IsNullOrEmpty(p.PrimaryMenuLabel)
                        ? $"専用メニュー項目もビルド時に隠す（{p.PrimaryMenuLabel}）"
                        : "専用メニュー項目もビルド時に隠す";
                    bool hideMenu = EditorGUILayout.ToggleLeft(menuHint, rule.hideMenuControlsOnRemove);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Toggle Hide Menu On Remove");
                        rule.hideMenuControlsOnRemove = hideMenu;
                    }
                }
                else if (curAction == ParameterAction.ResizeBitWidth && EnvironmentCapabilities.HasVrcSdk3106 && rule != null)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUI.BeginChangeCheck();
                    int bw = EditorGUILayout.IntSlider("目標 bit 幅（実験）", rule.GetClampedBitWidth(), 2, 8);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Adjust Target BitWidth");
                        rule.targetBitWidth = bw;
                    }
                }

                // 自動提案カード
                if (p.Suggestion.HasStrictIntRecommendation || p.Suggestion.HasConditionalIntEstimate)
                {
                    EditorGUILayout.Space(4f);
                    DrawIntSuggestionBox(comp, p);
                }

                if (p.Suggestion.IsRadialFloatReferenceCandidate)
                {
                    EditorGUILayout.Space(4f);
                    DrawFloatSuggestionBox(comp, p);
                }
            }
        }

        private void DrawActionChoiceButton(
            ExParamManagerComponent comp,
            ParameterSnapshot p,
            ParameterAction buttonAction,
            ParameterAction curAction,
            string label,
            Color activeColor)
        {
            bool isActive = curAction == buttonAction;
            Color prev = GUI.backgroundColor;
            if (isActive)
            {
                GUI.backgroundColor = activeColor;
            }

            if (GUILayout.Button(label, GUILayout.Height(24f)))
            {
                ApplyParameterAction(comp, p, buttonAction);
            }

            GUI.backgroundColor = prev;
        }

        private void DrawDetailComparisonCard(ExParamManagerComponent comp, ParameterSnapshot p, ParameterRule rule)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("適用前 → 適用後 プレビュー", _sectionHeaderStyle);

                bool hasRule = rule != null && rule.action != ParameterAction.Keep && !p.IsInternalParameter;
                bool isGloballyDisabled = comp != null && !comp.enabledOnBuild;
                bool isRuleDisabled = hasRule && rule.disabled;
                bool isPaused = hasRule && (isGloballyDisabled || isRuleDisabled);

                int beforeBit = p.EffectiveBitCost;
                int afterBit = EstimateSingleAfterBit(comp, p, rule);
                int potentialAfterBit = hasRule ? EstimateRuleAfterBit(p, rule) : beforeBit;
                int maxScaleBit = p.ValueKind == ParameterValueKind.Bool ? 1 : 8;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (isPaused)
                    {
                        EditorGUILayout.LabelField($"{beforeBit} bit → {beforeBit} bit", _bigMetricStyle, GUILayout.ExpandWidth(false));
                        GUILayout.Space(6f);
                        EditorGUILayout.LabelField($"（一時オフ中・有効時 {potentialAfterBit} bit）", EditorStyles.miniLabel);
                    }
                    else if (afterBit < beforeBit)
                    {
                        EditorGUILayout.LabelField($"{beforeBit} bit → {afterBit} bit", _bigMetricStyle, GUILayout.ExpandWidth(false));
                        GUILayout.Space(6f);
                        EditorGUILayout.LabelField($"(-{beforeBit - afterBit} bit)", _bigDeltaStyle);
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"{beforeBit} bit（変更なし）", _bigMetricStyle);
                    }
                }

                BitBarDrawer.DrawParameterBitBar(beforeBit, afterBit, maxScaleBit, isPaused);

                if (_scanResult != null)
                {
                    EditorGUILayout.Space(6f);
                    BitProjection proj = BitBarDrawer.CalculateProjection(_scanResult, comp, EnvironmentCapabilities.HasVrcSdk3106);
                    int paramDelta = proj.BeforeSyncedParamCount - proj.AfterSyncedParamCount;
                    string countLine = paramDelta > 0
                        ? $"同期パラメータ数: {proj.BeforeSyncedParamCount} → {proj.AfterSyncedParamCount} 個（-{paramDelta} 個）"
                        : $"同期パラメータ数: {proj.BeforeSyncedParamCount} 個（増減なし）";
                    EditorGUILayout.LabelField($"アバター全体: {countLine}", EditorStyles.boldLabel);
                    BitBarDrawer.DrawBitBar(proj);
                }

                EditorGUILayout.Space(4f);
                if (isGloballyDisabled)
                {
                    EditorGUILayout.HelpBox("ツール全体が一時オフのため、ビルド時は元のまま出力されます。", MessageType.Warning);
                }
                else if (isRuleDisabled)
                {
                    EditorGUILayout.HelpBox("この項目は一時オフ中のため、現在の設定を保持したままビルド適用から除外されます。", MessageType.Warning);
                }
                else if (hasRule)
                {
                    switch (rule.action)
                    {
                        case ParameterAction.Remove:
                        {
                            string savedLine = p.Saved
                                ? $"\n・状態保存(Saved)は外れ、初期値 ({p.DefaultValue}) が Animator へ引き継がれます。"
                                : string.Empty;
                            EditorGUILayout.HelpBox(
                                $"使わない衣装・ギミックの整理向けです。元のアバター・アセットは変わりません。{savedLine}",
                                MessageType.Info);
                            break;
                        }
                        case ParameterAction.MakeLocal:
                            EditorGUILayout.HelpBox(
                                "自分の画面では動き、他のプレイヤーには同期されません（自分専用設定向け）。",
                                MessageType.Info);
                            break;
                        case ParameterAction.ResizeBitWidth:
                            EditorGUILayout.HelpBox(
                                EnvironmentCapabilities.HasVrcSdk3106
                                    ? "※ VRChat の 2 クライアント間同期は未検証の実験的機能です。問題があれば「そのまま」に戻して再アップロードしてください。"
                                    : "現在の VRChat SDK（3.10.5 以下）では bit 幅縮小はスキップされます。",
                                MessageType.Warning);
                            break;
                    }
                }
            }
        }

        private void DrawIntSuggestionBox(ExParamManagerComponent comp, ParameterSnapshot p)
        {
            if (p.Suggestion.HasStrictIntRecommendation)
            {
                string msg = $"縮小おすすめ: このアバターでは値 {p.Suggestion.ObservedMinInt}〜{p.Suggestion.ObservedMaxInt} しか使っていないため "
                    + $"{p.Suggestion.RecommendedIntBitWidth} bit で足ります（-{p.Suggestion.IntBitSavings} bit 節約）。";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                if (EnvironmentCapabilities.HasVrcSdk3106)
                {
                    if (GUILayout.Button($"推奨 {p.Suggestion.RecommendedIntBitWidth} bit を適用（実験）"))
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Apply Recommended Int BitWidth");
                        ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                        r.disabled = false;
                        r.action = ParameterAction.ResizeBitWidth;
                        r.targetBitWidth = p.Suggestion.RecommendedIntBitWidth;
                    }
                }
            }
            else if (p.Suggestion.HasConditionalIntEstimate)
            {
                EditorGUILayout.LabelField("参考試算: " + p.Suggestion.IntReason, _wrappedMiniStyle);
            }
        }

        private void DrawFloatSuggestionBox(ExParamManagerComponent comp, ParameterSnapshot p)
        {
            EditorGUILayout.HelpBox(
                $"スライダー縮小候補: スライダー（Radial Puppet）専用の Float です。\n"
                + $"4 bit 化（16段階）で -{p.Suggestion.RadialFloat4BitSavings} bit、5 bit 化（32段階）で -{p.Suggestion.RadialFloat5BitSavings} bit 削減できます。",
                MessageType.Info);

            if (EnvironmentCapabilities.HasVrcSdk3106)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"4 bit（16段階 / -{p.Suggestion.RadialFloat4BitSavings}b）を適用"))
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Apply Radial Float 4 bit");
                        ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                        r.disabled = false;
                        r.action = ParameterAction.ResizeBitWidth;
                        r.targetBitWidth = 4;
                    }
                    if (GUILayout.Button($"5 bit（32段階 / -{p.Suggestion.RadialFloat5BitSavings}b）を適用"))
                    {
                        ExParamManagerEditor.RecordEdit(comp, "Apply Radial Float 5 bit");
                        ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                        r.disabled = false;
                        r.action = ParameterAction.ResizeBitWidth;
                        r.targetBitWidth = 5;
                    }
                }
            }
        }

        private static string BuildReferencesFoldoutLabel(ParameterSnapshot p)
        {
            if (p.References.Count == 0)
            {
                return "どこで使われているか（0 件・未使用かも）";
            }

            var parts = new List<string>();
            if (p.MenuRefCount > 0) parts.Add($"メニュー {p.MenuRefCount}");
            if (p.AnimatorRefCount > 0) parts.Add($"Animator {p.AnimatorRefCount}");
            if (p.DriverRefCount > 0) parts.Add($"Driver {p.DriverRefCount}");
            if (p.DynamicsRefCount > 0) parts.Add($"PhysBone等 {p.DynamicsRefCount}");
            if (p.ReactiveRefCount > 0) parts.Add($"MA {p.ReactiveRefCount}");
            string breakdown = parts.Count > 0 ? "：" + string.Join(" / ", parts) : string.Empty;
            return $"どこで使われているか（合計 {p.References.Count} 件{breakdown}）";
        }

        private void DrawDetailReferencesSection(ParameterSnapshot p)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _showReferencesSection = EditorGUILayout.Foldout(_showReferencesSection, BuildReferencesFoldoutLabel(p), true);
                if (!_showReferencesSection)
                {
                    return;
                }

                if (p.References.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "このアバター内のメニュー・Animator・PhysBone 等からは参照が見つかりませんでした（OSC や VRCFury 等の外部ツールから使われている可能性だけご確認ください）。",
                        MessageType.Info);
                    return;
                }

                // 1. メニュー（同一メニューアセットは 1 ボタンにまとめる）
                if (p.MenuRefCount > 0)
                {
                    DrawReferenceCategoryHeader($"メニュー（{p.MenuRefCount} 件）");
                    DrawGroupedMenuReferences(p);
                }

                // 2. 表情・アニメ / Animator（コントローラ単位で折りたたみ）
                if (p.AnimatorRefCount > 0)
                {
                    DrawReferenceCategoryHeader($"表情・アニメ / Animator（{p.AnimatorRefCount} 件）");
                    DrawGroupedAnimatorReferences(p);
                }

                // 3. 自動切替 / ParameterDriver
                if (p.DriverRefCount > 0)
                {
                    DrawReferenceCategoryHeader($"自動切替 / ParameterDriver（{p.DriverRefCount} 件）");
                    for (int i = 0; i < p.References.Count; i++)
                    {
                        ParameterReference rf = p.References[i];
                        if (rf.Kind != ReferenceKind.ParameterDriverWrite && rf.Kind != ReferenceKind.ParameterDriverCopySource)
                        {
                            continue;
                        }

                        string suffix = rf.Kind == ReferenceKind.ParameterDriverWrite ? "へ書込" : "からコピー";
                        string label = string.IsNullOrEmpty(rf.Detail)
                            ? $"{rf.DisplayLabel}（{suffix}）"
                            : $"{rf.DisplayLabel}（{rf.Detail} {suffix}）";
                        DrawReferenceRow(label, rf.TargetObject);
                    }
                }

                // 4. 揺れもの・接触 / PhysBone・Contact
                if (p.DynamicsRefCount > 0)
                {
                    DrawReferenceCategoryHeader($"揺れもの・接触 / PhysBone・Contact（{p.DynamicsRefCount} 件）");
                    for (int i = 0; i < p.References.Count; i++)
                    {
                        ParameterReference rf = p.References[i];
                        if (rf.Kind != ReferenceKind.PhysBone && rf.Kind != ReferenceKind.ContactReceiver)
                        {
                            continue;
                        }

                        DrawReferenceRow(rf.DisplayLabel, rf.TargetObject);
                    }
                }

                // 5. Modular Avatar 連携
                if (p.ReactiveRefCount > 0)
                {
                    DrawReferenceCategoryHeader($"Modular Avatar 連携（{p.ReactiveRefCount} 件）");
                    for (int i = 0; i < p.References.Count; i++)
                    {
                        ParameterReference rf = p.References[i];
                        if (rf.Kind != ReferenceKind.MaReactive)
                        {
                            continue;
                        }

                        DrawReferenceRow(rf.DisplayLabel, rf.TargetObject);
                    }
                }
            }
        }

        private static void DrawGroupedMenuReferences(ParameterSnapshot p)
        {
            var assetGroups = new Dictionary<UnityEngine.Object, List<ParameterReference>>();
            var assetOrder = new List<UnityEngine.Object>();
            var sceneOrUnlinked = new List<ParameterReference>();

            for (int i = 0; i < p.References.Count; i++)
            {
                ParameterReference rf = p.References[i];
                if (rf.Kind != ReferenceKind.ExpressionsMenuAsset
                    && rf.Kind != ReferenceKind.MaMenuItem
                    && rf.Kind != ReferenceKind.MaMenuInstaller)
                {
                    continue;
                }

                bool isSceneObj = rf.TargetObject is GameObject || rf.TargetObject is Component;
                if (rf.TargetObject != null && !isSceneObj)
                {
                    if (!assetGroups.TryGetValue(rf.TargetObject, out List<ParameterReference> list))
                    {
                        list = new List<ParameterReference>();
                        assetGroups[rf.TargetObject] = list;
                        assetOrder.Add(rf.TargetObject);
                    }
                    list.Add(rf);
                }
                else
                {
                    sceneOrUnlinked.Add(rf);
                }
            }

            for (int g = 0; g < assetOrder.Count; g++)
            {
                UnityEngine.Object asset = assetOrder[g];
                List<ParameterReference> refs = assetGroups[asset];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{asset.name}（{refs.Count} 箇所）", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("メニューを見る", GUILayout.Width(100f)))
                    {
                        PingAndSelect(asset);
                    }
                }

                EditorGUI.indentLevel++;
                for (int i = 0; i < refs.Count; i++)
                {
                    ParameterReference rf = refs[i];
                    string typeJa = GetMenuControlTypeLabel(rf.MenuControlType);
                    string itemBase = !string.IsNullOrEmpty(rf.MenuItemLabel) ? rf.MenuItemLabel : rf.DisplayLabel;
                    string itemLabel = string.IsNullOrEmpty(typeJa) ? itemBase : $"{itemBase} [{typeJa}]";
                    EditorGUILayout.LabelField("・" + itemLabel, EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }

            for (int i = 0; i < sceneOrUnlinked.Count; i++)
            {
                ParameterReference rf = sceneOrUnlinked[i];
                string typeJa = GetMenuControlTypeLabel(rf.MenuControlType);
                string label = string.IsNullOrEmpty(typeJa) ? rf.DisplayLabel : $"{rf.DisplayLabel} [{typeJa}]";
                DrawReferenceRow(label, rf.TargetObject);
            }
        }

        private void DrawGroupedAnimatorReferences(ParameterSnapshot p)
        {
            var grouped = new Dictionary<string, List<ParameterReference>>(StringComparer.Ordinal);
            var order = new List<string>();

            for (int i = 0; i < p.References.Count; i++)
            {
                ParameterReference rf = p.References[i];
                if (rf.Kind != ReferenceKind.AnimatorTransition
                    && rf.Kind != ReferenceKind.AnimatorAnyStateTransition
                    && rf.Kind != ReferenceKind.AnimatorBlendTree
                    && rf.Kind != ReferenceKind.AnimatorStateProperty)
                {
                    continue;
                }

                string ctrlName = rf.TargetObject != null ? rf.TargetObject.name : "Animator";
                if (!grouped.TryGetValue(ctrlName, out List<ParameterReference> list))
                {
                    list = new List<ParameterReference>();
                    grouped[ctrlName] = list;
                    order.Add(ctrlName);
                }
                list.Add(rf);
            }

            for (int g = 0; g < order.Count; g++)
            {
                string ctrlName = order[g];
                List<ParameterReference> refs = grouped[ctrlName];
                UnityEngine.Object targetObj = refs[0].TargetObject;
                string foldKey = p.EffectiveName + "::" + ctrlName;
                bool expanded = _animatorGroupFoldouts.TryGetValue(foldKey, out bool cur) && cur;

                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded = EditorGUILayout.Foldout(expanded, $"{ctrlName}（{refs.Count} 箇所）", true);
                    _animatorGroupFoldouts[foldKey] = expanded;
                    if (targetObj != null && GUILayout.Button("アセットを見る", GUILayout.Width(100f)))
                    {
                        PingAndSelect(targetObj);
                    }
                }

                if (expanded)
                {
                    int limit = Mathf.Min(refs.Count, 20);
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < limit; i++)
                    {
                        ParameterReference rf = refs[i];
                        string detail = string.IsNullOrEmpty(rf.Detail) ? string.Empty : $" ({rf.Detail})";
                        EditorGUILayout.LabelField($"・{rf.DisplayLabel}{detail}", EditorStyles.miniLabel);
                    }
                    if (refs.Count > limit)
                    {
                        EditorGUILayout.LabelField($"…ほか {refs.Count - limit} 箇所", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        private static void DrawReferenceCategoryHeader(string title)
        {
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        }

        private static void DrawReferenceRow(string label, UnityEngine.Object targetObj)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("・" + label, EditorStyles.miniLabel);
                if (targetObj != null)
                {
                    bool isSceneObj = targetObj is GameObject || targetObj is Component;
                    string btnLabel = isSceneObj ? "選択" : "アセットを見る";
                    float btnWidth = isSceneObj ? 56f : 100f;
                    if (GUILayout.Button(btnLabel, GUILayout.Width(btnWidth)))
                    {
                        PingAndSelect(targetObj);
                    }
                }
            }
        }

        private static void PingAndSelect(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (obj is Component comp)
            {
                Selection.activeObject = comp.gameObject;
                EditorGUIUtility.PingObject(comp.gameObject);
            }
            else if (obj is GameObject go)
            {
                Selection.activeObject = go;
                EditorGUIUtility.PingObject(go);
            }
            else
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

        private static string GetMenuControlTypeLabel(int controlType)
        {
            switch ((VRCExpressionsMenu.Control.ControlType)controlType)
            {
                case VRCExpressionsMenu.Control.ControlType.Button: return "ボタン";
                case VRCExpressionsMenu.Control.ControlType.Toggle: return "トグル";
                case VRCExpressionsMenu.Control.ControlType.SubMenu: return "サブメニュー";
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet: return "2軸パペット";
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet: return "4軸パペット";
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: return "スライダー";
                default: return string.Empty;
            }
        }

        private List<ParameterSnapshot> GetFilteredAndSortedParameters(ExParamManagerComponent comp)
        {
            if (_scanResult == null)
            {
                return new List<ParameterSnapshot>();
            }

            IEnumerable<ParameterSnapshot> query = _scanResult.Parameters;

            if (!string.IsNullOrEmpty(_selectedPrefabGroupKey))
            {
                query = query.Where(p => p.PrefabGroupKey == _selectedPrefabGroupKey);
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                query = query.Where(p =>
                    p.EffectiveName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || (!string.IsNullOrEmpty(p.PrimaryMenuLabel)
                        && p.PrimaryMenuLabel.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_typeFilterIndex == 1) query = query.Where(p => p.ValueKind == ParameterValueKind.Float);
            else if (_typeFilterIndex == 2) query = query.Where(p => p.ValueKind == ParameterValueKind.Int);
            else if (_typeFilterIndex == 3) query = query.Where(p => p.ValueKind == ParameterValueKind.Bool);

            switch (_quickTab)
            {
                case QuickFilterTab.Reducible:
                    query = query.Where(p => p.Suggestion.HasStrictIntRecommendation || p.Suggestion.IsRadialFloatReferenceCandidate);
                    break;
                case QuickFilterTab.UnusedCandidate:
                    query = query.Where(p => p.Suggestion.IsUnusedCandidate);
                    break;
                case QuickFilterTab.ChangedOnly:
                    query = query.Where(p => HasNonKeepRule(comp, p));
                    break;
                case QuickFilterTab.Protected:
                    query = query.Where(p => p.ProtectionFlags != ProtectionFlags.None);
                    break;
            }

            return ParameterSortHelper.Sort(query, IndexToSortMode(_sortModeIndex));
        }

        private void CountTabs(
            ExParamManagerComponent comp,
            out int totalCount,
            out int reducibleCount,
            out int unusedCount,
            out int changedCount,
            out int protectedCount,
            out int unappliedStrictIntCount,
            out int unappliedStrictIntSavings)
        {
            totalCount = 0;
            reducibleCount = 0;
            unusedCount = 0;
            changedCount = 0;
            protectedCount = 0;
            unappliedStrictIntCount = 0;
            unappliedStrictIntSavings = 0;

            if (_scanResult == null)
            {
                return;
            }

            totalCount = _scanResult.Parameters.Count;
            for (int i = 0; i < _scanResult.Parameters.Count; i++)
            {
                ParameterSnapshot p = _scanResult.Parameters[i];
                if (p.Suggestion.HasStrictIntRecommendation || p.Suggestion.IsRadialFloatReferenceCandidate)
                {
                    reducibleCount++;
                }
                if (p.Suggestion.IsUnusedCandidate)
                {
                    unusedCount++;
                }
                if (HasNonKeepRule(comp, p))
                {
                    changedCount++;
                }
                if (p.ProtectionFlags != ProtectionFlags.None)
                {
                    protectedCount++;
                }
                if (p.Suggestion.HasStrictIntRecommendation && !p.IsInternalParameter && !HasNonKeepRule(comp, p))
                {
                    unappliedStrictIntCount++;
                    unappliedStrictIntSavings += p.Suggestion.IntBitSavings;
                }
            }
        }

        private static bool HasNonKeepRule(ExParamManagerComponent comp, ParameterSnapshot p)
        {
            if (comp == null || p == null || p.IsInternalParameter)
            {
                return false;
            }

            ParameterRule r = comp.FindRule(p.EffectiveName, p.SourceComponent);
            return r != null && r.action != ParameterAction.Keep;
        }

        private static int CountChangedRules(ExParamManagerComponent comp)
        {
            if (comp == null || comp.rules == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < comp.rules.Count; i++)
            {
                if (comp.rules[i] != null && comp.rules[i].action != ParameterAction.Keep)
                {
                    count++;
                }
            }
            return count;
        }

        private static int ActionToPopupIndex(ParameterAction action, bool isBool)
        {
            switch (action)
            {
                case ParameterAction.Remove: return 1;
                case ParameterAction.MakeLocal: return 2;
                case ParameterAction.ResizeBitWidth: return isBool ? 0 : 3;
                default: return 0;
            }
        }

        private static ParameterAction PopupIndexToAction(int index, bool isBool)
        {
            switch (index)
            {
                case 1: return ParameterAction.Remove;
                case 2: return ParameterAction.MakeLocal;
                case 3: return isBool ? ParameterAction.Keep : ParameterAction.ResizeBitWidth;
                default: return ParameterAction.Keep;
            }
        }

        private static ParameterSortMode IndexToSortMode(int sortIndex)
        {
            switch (sortIndex)
            {
                case 1: return ParameterSortMode.NameAscending;
                case 2: return ParameterSortMode.ReferenceCountAscending;
                case 3: return ParameterSortMode.TypeOrder;
                case 0:
                default: return ParameterSortMode.BitCostDescending;
            }
        }

        private static int EstimateSingleAfterBit(ExParamManagerComponent comp, ParameterSnapshot p, ParameterRule rule)
        {
            if (comp == null || !comp.enabledOnBuild || rule == null || rule.disabled || rule.action == ParameterAction.Keep || p.IsInternalParameter)
            {
                return p.EffectiveBitCost;
            }

            return EstimateRuleAfterBit(p, rule);
        }

        private static int EstimateRuleAfterBit(ParameterSnapshot p, ParameterRule rule)
        {
            if (rule == null || rule.action == ParameterAction.Keep || p.IsInternalParameter)
            {
                return p.EffectiveBitCost;
            }

            switch (rule.action)
            {
                case ParameterAction.Remove:
                case ParameterAction.MakeLocal:
                    return 0;
                case ParameterAction.ResizeBitWidth:
                    if (!p.IsSynced || p.ValueKind == ParameterValueKind.Bool || !EnvironmentCapabilities.HasVrcSdk3106)
                    {
                        return p.EffectiveBitCost;
                    }
                    return rule.GetClampedBitWidth();
                default:
                    return p.EffectiveBitCost;
            }
        }
    }
}
