using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.ExParamManager.Editor
{
    public sealed class ExParamManagerWindow : EditorWindow
    {
        private enum StateFilter
        {
            All = 0,
            HasRule = 1,
            StrictIntRecommended = 2,
            RadialFloatReference = 3,
            UnusedCandidate = 4,
            Protected = 5,
            InternalReadOnly = 6,
        }

        private VRCAvatarDescriptor _targetAvatar;
        private AvatarScanResult _scanResult;
        private bool _needsRescan;
        private string _searchText = string.Empty;
        private int _typeFilterIndex; // 0=All, 1=Int, 2=Float, 3=Bool
        private StateFilter _stateFilter = StateFilter.All;
        private ParameterSortMode _sortMode = ParameterSortMode.BitCostDescending;
        private string _selectedPrefabGroupKey = string.Empty;
        private bool _showPrefabGroups = true;

        private string _selectedParamKey = string.Empty;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        [MenuItem("Tools/Sebanne/ExParam Manager")]
        public static void Open()
        {
            var win = GetWindow<ExParamManagerWindow>("ExParam Manager");
            win.minSize = new Vector2(820f, 500f);
            win.Show();
        }

        public static void OpenFor(VRCAvatarDescriptor descriptor)
        {
            var win = GetWindow<ExParamManagerWindow>("ExParam Manager");
            win.minSize = new Vector2(820f, 500f);
            if (descriptor != null)
            {
                win._targetAvatar = descriptor;
                win.RunScan();
            }
            win.Show();
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedo;

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
        }

        private void OnHierarchyChanged()
        {
            if (_targetAvatar != null)
            {
                _needsRescan = true;
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
            _needsRescan = false;
            _scanResult = _targetAvatar != null ? AvatarScanner.Scan(_targetAvatar) : null;
        }

        private void OnGUI()
        {
            DrawTopBar();

            if (_targetAvatar == null)
            {
                EditorGUILayout.HelpBox("上の欄で対象の VRCAvatarDescriptor を選択してください。", MessageType.Info);
                return;
            }

            if (_scanResult == null)
            {
                RunScan();
            }

            ExParamManagerComponent comp = _targetAvatar.GetComponentInChildren<ExParamManagerComponent>(true);
            DrawFilterAndPrefabSection(comp);

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                DrawLeftTable(comp);
                DrawRightDetailPanel(comp);
            }
        }

        private void DrawTopBar()
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
                        RunScan();
                    }

                    Color prevBg = GUI.backgroundColor;
                    if (_needsRescan)
                    {
                        GUI.backgroundColor = new Color(1f, 0.78f, 0.25f, 1f);
                    }

                    if (GUILayout.Button(_needsRescan ? "要再スキャン" : "再スキャン", GUILayout.Width(104f)))
                    {
                        RunScan();
                    }
                    GUI.backgroundColor = prevBg;
                }

                if (_targetAvatar != null)
                {
                    ExParamManagerComponent comp = _targetAvatar.GetComponentInChildren<ExParamManagerComponent>(true);
                    if (comp == null)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("このアバターには ExParam Manager コンポーネントが未追加です（閲覧のみ可能）。", EditorStyles.miniLabel);
                            if (GUILayout.Button("ルートに追加", GUILayout.Width(104f)))
                            {
                                Undo.AddComponent<ExParamManagerComponent>(_targetAvatar.gameObject);
                                RunScan();
                            }
                        }
                    }

                    if (_scanResult != null)
                    {
                        BitProjection proj = BitBarDrawer.CalculateProjection(_scanResult, comp, EnvironmentCapabilities.HasVrcSdk3106);
                        BitBarDrawer.DrawBitBar(proj);

                        if (_scanResult.HasSyncParameterSequence && (comp == null || !comp.allowOverrideWithSyncSequence))
                        {
                            EditorGUILayout.HelpBox("ModularAvatarSyncParameterSequence を検出したため、同期列変更はビルド時に安全ブロックされます。", MessageType.Warning);
                        }
                    }
                }

                EditorGUILayout.LabelField(
                    EnvironmentCapabilities.HasVrcSdk3106
                        ? "※ Edit Mode 解析のため VRCFury 等 IParameterProvider 未実装コンポーネントの動的パラメータはビルド時に加算されます。bit 幅縮小は実験的機能です。"
                        : "※ SDK 3.10.5 以下環境のため bit 幅縮小は適用されません（可視化・逆引き・削除・非同期化のみ有効）。",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawFilterAndPrefabSection(ExParamManagerComponent comp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _searchText = EditorGUILayout.TextField("検索", _searchText, GUILayout.MinWidth(180f));
                    _typeFilterIndex = EditorGUILayout.Popup(_typeFilterIndex, new[] { "全型", "Int", "Float", "Bool" }, GUILayout.Width(76f));
                    _stateFilter = (StateFilter)EditorGUILayout.EnumPopup(_stateFilter, GUILayout.Width(160f));
                    _sortMode = (ParameterSortMode)EditorGUILayout.EnumPopup(_sortMode, GUILayout.Width(140f));
                }

                if (_scanResult != null && _scanResult.PrefabGroups.Count > 0)
                {
                    _showPrefabGroups = EditorGUILayout.Foldout(_showPrefabGroups, $"Prefab / サブツリー別 bit 集計（{_scanResult.PrefabGroups.Count} グループ）", true);
                    if (_showPrefabGroups)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Toggle(string.IsNullOrEmpty(_selectedPrefabGroupKey), "すべて", EditorStyles.miniButton, GUILayout.Width(60f)))
                            {
                                _selectedPrefabGroupKey = string.Empty;
                            }

                            int maxChips = Mathf.Min(_scanResult.PrefabGroups.Count, 8);
                            for (int i = 0; i < maxChips; i++)
                            {
                                PrefabBitGroup g = _scanResult.PrefabGroups[i];
                                bool active = _selectedPrefabGroupKey == g.GroupKey;
                                string label = $"{g.DisplayName} ({g.TotalSyncedBits}b)";
                                if (GUILayout.Toggle(active, label, EditorStyles.miniButton))
                                {
                                    _selectedPrefabGroupKey = g.GroupKey;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void DrawLeftTable(ExParamManagerComponent comp)
        {
            List<ParameterSnapshot> filtered = GetFilteredAndSortedParameters(comp);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.56f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField($"パラメータ一覧（{filtered.Count} 件）", EditorStyles.boldLabel);

                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                for (int i = 0; i < filtered.Count; i++)
                {
                    ParameterSnapshot p = filtered[i];
                    ParameterRule rule = comp != null ? comp.FindRule(p.EffectiveName, p.SourceComponent) : null;
                    bool isSelected = _selectedParamKey == p.EffectiveName;

                    using (new EditorGUILayout.HorizontalScope(isSelected ? EditorStyles.selectionRect : EditorStyles.helpBox))
                    {
                        if (GUILayout.Button(p.EffectiveName, EditorStyles.label, GUILayout.MinWidth(130f)))
                        {
                            _selectedParamKey = p.EffectiveName;
                        }

                        GUILayout.Label(p.ValueKind.ToString(), EditorStyles.miniLabel, GUILayout.Width(40f));

                        int afterBit = EstimateSingleAfterBit(p, rule);
                        string bitText = p.EffectiveBitCost != afterBit
                            ? $"{p.EffectiveBitCost}→{afterBit}b"
                            : $"{p.EffectiveBitCost}b";
                        GUILayout.Label(bitText, EditorStyles.miniLabel, GUILayout.Width(48f));

                        GUILayout.Label($"参照{p.References.Count}", EditorStyles.miniLabel, GUILayout.Width(44f));

                        string badge = BuildShortBadge(p);
                        if (!string.IsNullOrEmpty(badge))
                        {
                            GUILayout.Label(badge, EditorStyles.miniLabel, GUILayout.Width(84f));
                        }

                        DrawInlineActionSelector(comp, p, rule);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawInlineActionSelector(ExParamManagerComponent comp, ParameterSnapshot p, ParameterRule rule)
        {
            if (p.IsInternalParameter)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.Button("v1.1予定", EditorStyles.miniButton, GUILayout.Width(96f));
                }
                return;
            }

            if (comp == null)
            {
                GUILayout.Label(rule != null ? rule.action.ToString() : "Keep", EditorStyles.miniLabel, GUILayout.Width(96f));
                return;
            }

            ParameterAction curAction = rule != null ? rule.action : ParameterAction.Keep;
            EditorGUI.BeginChangeCheck();
            ParameterAction nextAction = (ParameterAction)EditorGUILayout.EnumPopup(curAction, GUILayout.Width(96f));
            if (EditorGUI.EndChangeCheck())
            {
                ExParamManagerEditor.RecordEdit(comp, "Change ExParam Rule Action");
                ParameterRule targetRule = comp.GetOrCreateRule(
                    p.EffectiveName,
                    p.SourceComponent,
                    p.SourceObjectPath,
                    p.IsInternalParameter,
                    p.OriginalName);
                targetRule.action = nextAction;
                if (nextAction == ParameterAction.ResizeBitWidth && p.Suggestion.HasStrictIntRecommendation)
                {
                    targetRule.targetBitWidth = p.Suggestion.RecommendedIntBitWidth;
                }
                else if (nextAction == ParameterAction.ResizeBitWidth && p.Suggestion.IsRadialFloatReferenceCandidate)
                {
                    targetRule.targetBitWidth = 4;
                }
            }
        }

        private void DrawRightDetailPanel(ExParamManagerComponent comp)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("詳細・逆引き", EditorStyles.boldLabel);
                ParameterSnapshot p = _scanResult?.Parameters.FirstOrDefault(x => x.EffectiveName == _selectedParamKey);
                if (p == null)
                {
                    EditorGUILayout.LabelField("左の一覧からパラメータを選択すると詳細と参照先を表示します。", EditorStyles.miniLabel);
                    return;
                }

                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                ParameterRule rule = comp != null ? comp.FindRule(p.EffectiveName, p.SourceComponent) : null;

                EditorGUILayout.LabelField("パラメータ名", p.EffectiveName);
                EditorGUILayout.LabelField("型 / 同期 / 保存", $"{p.ValueKind} / Synced={p.IsSynced} / Saved={p.Saved} / 既定値={p.DefaultValue}");
                EditorGUILayout.LabelField("bit 幅 / 符号表現", $"{p.CurrentBitWidth} bit / {p.Signedness}");

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("発生源コンポーネント", p.SourceComponent, typeof(Component), true);
                }

                if (p.ProtectionReasons.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox("保護・注意理由:\n- " + string.Join("\n- ", p.ProtectionReasons), MessageType.Warning);
                }

                // 厳格 Int 推奨 / Radial Float 試算
                if (p.Suggestion.HasStrictIntRecommendation || p.Suggestion.HasConditionalIntEstimate)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField(p.Suggestion.IntReason, EditorStyles.wordWrappedMiniLabel);
                    if (comp != null && p.Suggestion.HasStrictIntRecommendation && EnvironmentCapabilities.HasVrcSdk3106)
                    {
                        if (GUILayout.Button($"推奨 {p.Suggestion.RecommendedIntBitWidth} bit を適用（実験）"))
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Apply Recommended Int BitWidth");
                            ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                            r.action = ParameterAction.ResizeBitWidth;
                            r.targetBitWidth = p.Suggestion.RecommendedIntBitWidth;
                        }
                    }
                }

                if (p.Suggestion.IsRadialFloatReferenceCandidate)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField(p.Suggestion.FloatReason, EditorStyles.wordWrappedMiniLabel);
                    if (comp != null && EnvironmentCapabilities.HasVrcSdk3106)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("4 bit 縮小（実験）"))
                            {
                                ExParamManagerEditor.RecordEdit(comp, "Apply Radial Float 4 bit");
                                ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                                r.action = ParameterAction.ResizeBitWidth;
                                r.targetBitWidth = 4;
                            }
                            if (GUILayout.Button("5 bit 縮小（実験）"))
                            {
                                ExParamManagerEditor.RecordEdit(comp, "Apply Radial Float 5 bit");
                                ParameterRule r = comp.GetOrCreateRule(p.EffectiveName, p.SourceComponent, p.SourceObjectPath);
                                r.action = ParameterAction.ResizeBitWidth;
                                r.targetBitWidth = 5;
                            }
                        }
                    }
                }

                // ルール個別編集
                if (comp != null && rule != null && !p.IsInternalParameter)
                {
                    EditorGUILayout.Space(6f);
                    if (rule.action == ParameterAction.ResizeBitWidth)
                    {
                        EditorGUI.BeginChangeCheck();
                        int bw = EditorGUILayout.IntSlider("目標 bit 幅（実験）", rule.GetClampedBitWidth(), 2, 8);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Adjust Target BitWidth");
                            rule.targetBitWidth = bw;
                        }
                    }
                    else if (rule.action == ParameterAction.Remove)
                    {
                        EditorGUI.BeginChangeCheck();
                        bool hideMenu = EditorGUILayout.Toggle("専用メニュー項目も隠す", rule.hideMenuControlsOnRemove);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ExParamManagerEditor.RecordEdit(comp, "Toggle Hide Menu On Remove");
                            rule.hideMenuControlsOnRemove = hideMenu;
                        }
                        if (p.Saved)
                        {
                            EditorGUILayout.LabelField("※ 削除時は Saved（VRChat アバター間の状態保存）が失われます。既定値は Animator 初期値へ書き戻されます。", EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                }

                // 逆引き参照一覧
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField($"逆引き参照（{p.References.Count} 件）", EditorStyles.boldLabel);
                for (int i = 0; i < p.References.Count; i++)
                {
                    ParameterReference rf = p.References[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"[{rf.Kind}] {rf.DisplayLabel}", EditorStyles.miniLabel);
                        if (rf.TargetObject != null)
                        {
                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUILayout.ObjectField(rf.TargetObject, typeof(UnityEngine.Object), true, GUILayout.Width(130f));
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
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
                query = query.Where(p => p.EffectiveName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_typeFilterIndex == 1) query = query.Where(p => p.ValueKind == ParameterValueKind.Int);
            else if (_typeFilterIndex == 2) query = query.Where(p => p.ValueKind == ParameterValueKind.Float);
            else if (_typeFilterIndex == 3) query = query.Where(p => p.ValueKind == ParameterValueKind.Bool);

            switch (_stateFilter)
            {
                case StateFilter.HasRule:
                    query = query.Where(p =>
                    {
                        ParameterRule r = comp != null ? comp.FindRule(p.EffectiveName, p.SourceComponent) : null;
                        return r != null && r.action != ParameterAction.Keep;
                    });
                    break;
                case StateFilter.StrictIntRecommended:
                    query = query.Where(p => p.Suggestion.HasStrictIntRecommendation);
                    break;
                case StateFilter.RadialFloatReference:
                    query = query.Where(p => p.Suggestion.IsRadialFloatReferenceCandidate);
                    break;
                case StateFilter.UnusedCandidate:
                    query = query.Where(p => p.Suggestion.IsUnusedCandidate);
                    break;
                case StateFilter.Protected:
                    query = query.Where(p => p.ProtectionFlags != ProtectionFlags.None);
                    break;
                case StateFilter.InternalReadOnly:
                    query = query.Where(p => p.IsInternalParameter);
                    break;
            }

            return ParameterSortHelper.Sort(query, _sortMode);
        }

        private static int EstimateSingleAfterBit(ParameterSnapshot p, ParameterRule rule)
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

        private static string BuildShortBadge(ParameterSnapshot p)
        {
            if (p.IsInternalParameter) return "[internal]";
            if (p.Suggestion.HasStrictIntRecommendation) return $"[推奨 {p.Suggestion.RecommendedIntBitWidth}b]";
            if (p.Suggestion.IsRadialFloatReferenceCandidate) return "[Radial参考]";
            if (p.Suggestion.IsUnusedCandidate) return "[未使用候補]";
            if (p.ProtectionFlags != ProtectionFlags.None) return "[保護注意]";
            return string.Empty;
        }
    }
}
