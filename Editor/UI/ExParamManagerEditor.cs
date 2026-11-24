using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.ExParamManager.Editor
{
    [CustomEditor(typeof(ExParamManagerComponent))]
    public sealed class ExParamManagerEditor : UnityEditor.Editor
    {
        private bool _showAdvanced;
        private AvatarScanResult _cachedScan;
        private int _cachedInstanceId;

        public override void OnInspectorGUI()
        {
            var comp = (ExParamManagerComponent)target;
            if (comp == null)
            {
                return;
            }

            VRCAvatarDescriptor descriptor = comp.GetComponentInParent<VRCAvatarDescriptor>();
            if (_cachedScan == null || _cachedInstanceId != comp.GetInstanceID())
            {
                _cachedInstanceId = comp.GetInstanceID();
                _cachedScan = descriptor != null ? AvatarScanner.Scan(descriptor) : new AvatarScanResult();
            }

            EditorGUI.BeginChangeCheck();
            bool newEnabled = EditorGUILayout.Toggle("ビルド時に適用 (Enabled On Build)", comp.enabledOnBuild);
            if (EditorGUI.EndChangeCheck())
            {
                RecordEdit(comp, "Toggle ExParam Manager Enabled");
                comp.enabledOnBuild = newEnabled;
            }

            // 警告バナー（必要な場合のみ）
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("親階層に VRCAvatarDescriptor が見つかりません。アバター配下に配置してください。", MessageType.Error);
            }
            else
            {
                ExParamManagerComponent[] all = descriptor.GetComponentsInChildren<ExParamManagerComponent>(true);
                if (all.Length > 1)
                {
                    EditorGUILayout.HelpBox($"同じアバター内に ExParam Manager が {all.Length} 個配置されています。ビルド安全停止を防ぐため 1 つだけ残してください。", MessageType.Error);
                }

                if (comp.gameObject != descriptor.gameObject)
                {
                    EditorGUILayout.HelpBox("アバタールート直下への配置を推奨します（このままでも動作はします）。", MessageType.Warning);
                }

                if (_cachedScan.HasSyncParameterSequence && !comp.allowOverrideWithSyncSequence)
                {
                    EditorGUILayout.HelpBox("ModularAvatarSyncParameterSequence を検出したため、同期列を変える操作はビルド時に安全ブロックされます。", MessageType.Warning);
                }
            }

            BitProjection projection = BitBarDrawer.CalculateProjection(_cachedScan, comp, EnvironmentCapabilities.HasVrcSdk3106);
            BitBarDrawer.DrawBitBar(projection);

            EditorGUILayout.LabelField(
                $"ルール内訳: 削除 {projection.RemoveRuleCount} 件 / 自分だけ(非同期) {projection.MakeLocalRuleCount} 件 / bit 幅縮小 {projection.ResizeRuleCount} 件",
                EditorStyles.miniLabel);

            if (projection.ResizeRuleCount > 0)
            {
                EditorGUILayout.LabelField(
                    EnvironmentCapabilities.HasVrcSdk3106
                        ? "※ bit 幅縮小は 2 クライアント同期未検証の実験的機能です。"
                        : "※ 現在の SDK（3.10.5 以下）では bit 幅縮小はスキップされます。",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("ExParam Manager ウィンドウを開く", GUILayout.Height(28f)))
                {
                    ExParamManagerWindow.OpenFor(descriptor);
                }

                if (GUILayout.Button("更新", GUILayout.Width(68f), GUILayout.Height(28f)))
                {
                    _cachedScan = descriptor != null ? AvatarScanner.Scan(descriptor) : new AvatarScanResult();
                }
            }

            EditorGUILayout.Space(4f);
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "詳細設定", true);
            if (_showAdvanced)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    bool prune = EditorGUILayout.Toggle("今回空になった SubMenu を隠す", comp.pruneEmptySubMenus);
                    bool allowSyncOverride = EditorGUILayout.Toggle("SyncParameterSequence 上書き許可", comp.allowOverrideWithSyncSequence);
                    bool verbose = EditorGUILayout.Toggle("詳細ログを出力", comp.verboseLogging);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordEdit(comp, "Edit ExParam Manager Settings");
                        comp.pruneEmptySubMenus = prune;
                        comp.allowOverrideWithSyncSequence = allowSyncOverride;
                        comp.verboseLogging = verbose;
                    }
                }
            }
        }

        internal static void RecordEdit(ExParamManagerComponent comp, string undoName)
        {
            Undo.RecordObject(comp, undoName);
            EditorUtility.SetDirty(comp);
            PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
        }
    }
}
