using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// ExParam Manager の 2 パス NDMF ビルド処理（Pass 1: Transforming 退避 / Pass 2: Optimizing 適用）。
    /// </summary>
    public static class ExParamBuildProcessor
    {
        public static void RunPass1(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null)
            {
                return;
            }

            ExParamBuildState state = context.GetState<ExParamBuildState>();
            state.ShouldRun = false;
            state.AbortedByDuplicateComponents = false;
            state.ResolvedRules.Clear();
            state.SkippedInternalRuleCount = 0;

            ExParamManagerComponent[] components =
                context.AvatarRootObject.GetComponentsInChildren<ExParamManagerComponent>(true);

            if (components == null || components.Length == 0)
            {
                return;
            }

            if (components.Length > 1)
            {
                state.AbortedByDuplicateComponents = true;
                ExParamNdmfError.SafeReport(
                    context,
                    ErrorSeverity.Error,
                    "ExParam Manager コンポーネントがアバター内に複数配置されています",
                    $"検出数: {components.Length} 個。安全のためビルド適用を停止しました。アバター内に 1 つだけ残してください。",
                    components.Cast<UnityEngine.Object>().ToArray());
                return;
            }

            ExParamManagerComponent comp = components[0];
            if (!comp.enabledOnBuild)
            {
                return;
            }

            state.ShouldRun = true;
            state.PruneEmptySubMenus = comp.pruneEmptySubMenus;
            state.AllowOverrideWithSyncSequence = comp.allowOverrideWithSyncSequence;
            state.VerboseLogging = comp.verboseLogging;
            state.HasSyncParameterSequence =
                MaAdapterRegistry.Current?.HasSyncParameterSequence(context.AvatarRootObject) ?? false;

            if (comp.rules != null)
            {
                for (int i = 0; i < comp.rules.Count; i++)
                {
                    ParameterRule rule = comp.rules[i];
                    if (rule == null || rule.action == ParameterAction.Keep)
                    {
                        continue;
                    }

                    // 初版は internalParameter の変更を適用スキップ（v1.1 解禁予定）
                    if (rule.isInternalParameter)
                    {
                        state.SkippedInternalRuleCount++;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(rule.parameterName))
                    {
                        state.ResolvedRules.Add(rule.Clone());
                    }
                }
            }
        }

        public static void RunPass2(BuildContext context)
        {
            if (context == null || context.AvatarRootObject == null)
            {
                return;
            }

            ExParamBuildState state = context.GetState<ExParamBuildState>();
            if (state.AbortedByDuplicateComponents || !state.ShouldRun)
            {
                return;
            }

            VRCAvatarDescriptor descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return;
            }

            if (descriptor.expressionParameters == null)
            {
                ExParamNdmfError.SafeReport(
                    context,
                    ErrorSeverity.NonFatal,
                    "Expression Parameters が未設定のため ExParam Manager をスキップしました",
                    "VRCAvatarDescriptor.expressionParameters が null です。",
                    descriptor);
                return;
            }

            if (state.HasSyncParameterSequence && !state.AllowOverrideWithSyncSequence && state.ResolvedRules.Count > 0)
            {
                ExParamNdmfError.SafeReport(
                    context,
                    ErrorSeverity.NonFatal,
                    "ModularAvatarSyncParameterSequence 検出のため同期列変更を安全ブロックしました",
                    "SyncParameterSequence は Transforming 時点で同期順序を記録するため、Optimizing での同期列変更は PC/Quest 同期ずれの原因になります。変更を許可する場合はコンポーネントの Allow Override With Sync Sequence を有効にしてください。",
                    descriptor);
                return;
            }

            VRCExpressionParameters origExpParams = descriptor.expressionParameters;
            VRCExpressionParameters expParams = EnsureMutableExpressionParameters(context, descriptor, origExpParams);

            int beforeBits = expParams.CalcTotalCost();
            var ruleByName = new Dictionary<string, ParameterRule>(StringComparer.Ordinal);
            for (int i = 0; i < state.ResolvedRules.Count; i++)
            {
                ParameterRule r = state.ResolvedRules[i];
                ruleByName[r.parameterName] = r;
            }

            var removedParams = new Dictionary<string, float>(StringComparer.Ordinal);
            var removedForMenu = new HashSet<string>(StringComparer.Ordinal);
            var keptParams = new List<VRCExpressionParameters.Parameter>();

            int removedCount = 0;
            int madeLocalCount = 0;
            int resizedCount = 0;
            int skippedResizeOldSdkCount = 0;

            VRCExpressionParameters.Parameter[] currentParams = expParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();
            for (int i = 0; i < currentParams.Length; i++)
            {
                VRCExpressionParameters.Parameter p = currentParams[i];
                if (p == null)
                {
                    continue;
                }

                if (!ruleByName.TryGetValue(p.name, out ParameterRule rule))
                {
                    keptParams.Add(p);
                    continue;
                }

                switch (rule.action)
                {
                    case ParameterAction.Remove:
                        removedCount++;
                        removedParams[p.name] = p.defaultValue;
                        if (rule.hideMenuControlsOnRemove)
                        {
                            removedForMenu.Add(p.name);
                        }
                        break;

                    case ParameterAction.MakeLocal:
                        p.networkSynced = false;
                        madeLocalCount++;
                        keptParams.Add(p);
                        break;

                    case ParameterAction.ResizeBitWidth:
                        if (p.valueType != VRCExpressionParameters.ValueType.Bool)
                        {
#if HAS_VRCSDK3_AVATARS_3_10_6
                            int clampedWidth = rule.GetClampedBitWidth();
                            p.bitWidth = (byte)clampedWidth;
                            p.defaultValue = ClampDefaultValueForSdk3106(
                                p.defaultValue,
                                p.valueType,
                                clampedWidth,
                                p.flippedRepresentation);
                            resizedCount++;
#else
                            skippedResizeOldSdkCount++;
#endif
                        }
                        keptParams.Add(p);
                        break;

                    default:
                        keptParams.Add(p);
                        break;
                }
            }

            expParams.parameters = keptParams.ToArray();

            // 削除対象の最終 defaultValue を全 Playable Layer コントローラ初期値へ書き戻す
            if (removedParams.Count > 0)
            {
                WriteBackAnimatorDefaults(context, descriptor, removedParams);
            }

            // 循環安全・非破壊メニュー項目除去
            var brokenRemainingControls = new List<string>();
            int removedMenuControlCount = 0;
            if (removedForMenu.Count > 0 && descriptor.expressionsMenu != null)
            {
                var menuClones = new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
                var visitedMenus = new HashSet<VRCExpressionsMenu>();
                VRCExpressionsMenu updatedRoot = ProcessMenuRecursive(
                    context,
                    descriptor.expressionsMenu,
                    0,
                    removedForMenu,
                    state.PruneEmptySubMenus,
                    menuClones,
                    visitedMenus,
                    brokenRemainingControls,
                    ref removedMenuControlCount,
                    out bool rootChanged);
                if (rootChanged && updatedRoot != null)
                {
                    descriptor.expressionsMenu = updatedRoot;
                }
            }

            int afterBits = expParams.CalcTotalCost();

            // ErrorReport 出力
            ReportBuildSummary(
                context,
                descriptor,
                beforeBits,
                afterBits,
                removedCount,
                madeLocalCount,
                resizedCount,
                skippedResizeOldSdkCount,
                removedMenuControlCount,
                state.SkippedInternalRuleCount,
                brokenRemainingControls);
        }

        private static VRCExpressionParameters EnsureMutableExpressionParameters(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            VRCExpressionParameters original)
        {
            if (context.IsTemporaryAsset(original))
            {
                return original;
            }

            VRCExpressionParameters cloned = UnityEngine.Object.Instantiate(original);
            cloned.name = original.name + " (ExParamClone)";
            SaveGeneratedAsset(context, cloned);
            TryRegisterReplacedAsset(context, original, cloned);
            descriptor.expressionParameters = cloned;
            return cloned;
        }

        private static void WriteBackAnimatorDefaults(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            Dictionary<string, float> removedDefaults)
        {
            var processedControllers = new HashSet<AnimatorController>();

            WriteBackLayerArray(context, descriptor.baseAnimationLayers, removedDefaults, processedControllers);
            WriteBackLayerArray(context, descriptor.specialAnimationLayers, removedDefaults, processedControllers);
        }

        private static void WriteBackLayerArray(
            BuildContext context,
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            Dictionary<string, float> removedDefaults,
            HashSet<AnimatorController> processed)
        {
            if (layers == null)
            {
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                RuntimeAnimatorController rawCtrl = layers[i].animatorController;
                if (rawCtrl == null)
                {
                    continue;
                }

                AnimatorController origCtrl = ReferenceScanner.UnwrapController(rawCtrl);
                if (origCtrl == null || !processed.Add(origCtrl))
                {
                    continue;
                }

                AnimatorControllerParameter[] existingParams = origCtrl.parameters;
                bool needsWrite = false;
                for (int p = 0; p < existingParams.Length; p++)
                {
                    if (removedDefaults.ContainsKey(existingParams[p].name))
                    {
                        needsWrite = true;
                        break;
                    }
                }

                if (!needsWrite)
                {
                    continue;
                }

                AnimatorController mutableCtrl = origCtrl;
                if (!context.IsTemporaryAsset(origCtrl))
                {
                    mutableCtrl = UnityEngine.Object.Instantiate(origCtrl);
                    mutableCtrl.name = origCtrl.name + " (ExParamClone)";
                    SaveGeneratedAsset(context, mutableCtrl);
                    TryRegisterReplacedAsset(context, origCtrl, mutableCtrl);

                    if (rawCtrl is AnimatorOverrideController origAoc)
                    {
                        AnimatorOverrideController clonedAoc = context.IsTemporaryAsset(origAoc)
                            ? origAoc
                            : UnityEngine.Object.Instantiate(origAoc);
                        if (!ReferenceEquals(clonedAoc, origAoc))
                        {
                            clonedAoc.name = origAoc.name + " (ExParamClone)";
                            SaveGeneratedAsset(context, clonedAoc);
                            TryRegisterReplacedAsset(context, origAoc, clonedAoc);
                        }
                        clonedAoc.runtimeAnimatorController = mutableCtrl;
                        layers[i].animatorController = clonedAoc;
                    }
                    else
                    {
                        layers[i].animatorController = mutableCtrl;
                    }
                }

                AnimatorControllerParameter[] parameters = mutableCtrl.parameters;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (!removedDefaults.TryGetValue(parameters[p].name, out float defVal))
                    {
                        continue;
                    }

                    switch (parameters[p].type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            parameters[p].defaultBool = defVal > 0.5f;
                            break;
                        case AnimatorControllerParameterType.Int:
                            parameters[p].defaultInt = Mathf.RoundToInt(defVal);
                            break;
                        case AnimatorControllerParameterType.Float:
                            parameters[p].defaultFloat = defVal;
                            break;
                    }
                }

                mutableCtrl.parameters = parameters;
            }
        }

        private static VRCExpressionsMenu ProcessMenuRecursive(
            BuildContext context,
            VRCExpressionsMenu menu,
            int depth,
            HashSet<string> removedForMenu,
            bool pruneEmptySubMenus,
            Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> clones,
            HashSet<VRCExpressionsMenu> recursionStack,
            List<string> brokenControls,
            ref int removedCount,
            out bool changed)
        {
            changed = false;
            if (menu == null || depth > 16)
            {
                return menu;
            }

            if (clones.TryGetValue(menu, out VRCExpressionsMenu alreadyCloned))
            {
                changed = !ReferenceEquals(alreadyCloned, menu);
                return alreadyCloned;
            }

            if (!recursionStack.Add(menu))
            {
                return menu;
            }

            List<VRCExpressionsMenu.Control> controls = menu.controls ?? new List<VRCExpressionsMenu.Control>();
            var newControls = new List<VRCExpressionsMenu.Control>(controls.Count);
            bool anyChange = false;

            for (int i = 0; i < controls.Count; i++)
            {
                VRCExpressionsMenu.Control origCtrl = controls[i];
                if (origCtrl == null)
                {
                    continue;
                }

                // SubMenu 再帰判定
                if (origCtrl.type == VRCExpressionsMenu.Control.ControlType.SubMenu && origCtrl.subMenu != null)
                {
                    int subOriginalCount = origCtrl.subMenu.controls != null ? origCtrl.subMenu.controls.Count : 0;
                    VRCExpressionsMenu processedSub = ProcessMenuRecursive(
                        context,
                        origCtrl.subMenu,
                        depth + 1,
                        removedForMenu,
                        pruneEmptySubMenus,
                        clones,
                        recursionStack,
                        brokenControls,
                        ref removedCount,
                        out bool subChanged);

                    if (subChanged)
                    {
                        anyChange = true;
                    }

                    int subAfterCount = processedSub != null && processedSub.controls != null ? processedSub.controls.Count : 0;
                    // 今回の除去処理により 0 件になった SubMenu のみ隠す（元から空の SubMenu は残す）
                    if (pruneEmptySubMenus && subOriginalCount > 0 && subAfterCount == 0)
                    {
                        anyChange = true;
                        removedCount++;
                        continue;
                    }

                    VRCExpressionsMenu.Control ctrlToKeep = CloneControl(origCtrl);
                    ctrlToKeep.subMenu = processedSub;
                    newControls.Add(ctrlToKeep);
                    continue;
                }

                bool mainRemoved = origCtrl.parameter != null
                    && !string.IsNullOrEmpty(origCtrl.parameter.name)
                    && removedForMenu.Contains(origCtrl.parameter.name);

                bool isMultiAxis = origCtrl.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet
                    || origCtrl.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet;

                if (origCtrl.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                {
                    bool radialSubRemoved = origCtrl.subParameters != null
                        && origCtrl.subParameters.Any(sp => sp != null && !string.IsNullOrEmpty(sp.name) && removedForMenu.Contains(sp.name));
                    if (mainRemoved || radialSubRemoved)
                    {
                        anyChange = true;
                        removedCount++;
                        continue;
                    }
                }
                else if (isMultiAxis)
                {
                    VRCExpressionsMenu.Control clonedCtrl = CloneControl(origCtrl);
                    if (mainRemoved)
                    {
                        clonedCtrl.parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty };
                        anyChange = true;
                    }

                    int nonEmptyAxesBefore = 0;
                    int nonEmptyAxesAfter = 0;
                    bool clearedAnyAxis = false;

                    if (clonedCtrl.subParameters != null)
                    {
                        for (int s = 0; s < clonedCtrl.subParameters.Length; s++)
                        {
                            string spName = clonedCtrl.subParameters[s]?.name;
                            if (!string.IsNullOrEmpty(spName))
                            {
                                nonEmptyAxesBefore++;
                                if (removedForMenu.Contains(spName))
                                {
                                    clonedCtrl.subParameters[s] = new VRCExpressionsMenu.Control.Parameter { name = string.Empty };
                                    clearedAnyAxis = true;
                                    anyChange = true;
                                }
                                else
                                {
                                    nonEmptyAxesAfter++;
                                }
                            }
                        }
                    }

                    if (clearedAnyAxis && nonEmptyAxesBefore > 0 && nonEmptyAxesAfter == 0)
                    {
                        removedCount++;
                        continue;
                    }

                    if (clearedAnyAxis && nonEmptyAxesAfter > 0)
                    {
                        brokenControls.Add(menu.name + " / " + origCtrl.name);
                    }

                    newControls.Add(clonedCtrl);
                    continue;
                }
                else
                {
                    if (mainRemoved)
                    {
                        anyChange = true;
                        removedCount++;
                        continue;
                    }
                }

                newControls.Add(CloneControl(origCtrl));
            }

            recursionStack.Remove(menu);

            if (!anyChange)
            {
                clones[menu] = menu;
                changed = false;
                return menu;
            }

            VRCExpressionsMenu targetMenu = menu;
            if (!context.IsTemporaryAsset(menu))
            {
                targetMenu = UnityEngine.Object.Instantiate(menu);
                targetMenu.name = menu.name + " (ExParamClone)";
                SaveGeneratedAsset(context, targetMenu);
                TryRegisterReplacedAsset(context, menu, targetMenu);
            }

            targetMenu.controls = newControls;
            clones[menu] = targetMenu;
            changed = true;
            return targetMenu;
        }

        private static VRCExpressionsMenu.Control CloneControl(VRCExpressionsMenu.Control src)
        {
            return new VRCExpressionsMenu.Control
            {
                name = src.name,
                icon = src.icon,
                type = src.type,
                parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = src.parameter != null ? src.parameter.name : string.Empty,
                },
                value = src.value,
                style = src.style,
                subMenu = src.subMenu,
                subParameters = src.subParameters == null
                    ? Array.Empty<VRCExpressionsMenu.Control.Parameter>()
                    : src.subParameters
                        .Select(sp => new VRCExpressionsMenu.Control.Parameter { name = sp != null ? sp.name : string.Empty })
                        .ToArray(),
                labels = src.labels == null
                    ? Array.Empty<VRCExpressionsMenu.Control.Label>()
                    : src.labels
                        .Select(l => new VRCExpressionsMenu.Control.Label { name = l.name, icon = l.icon })
                        .ToArray(),
            };
        }

        public static float ClampDefaultValueForSdk3106(
            float value,
            VRCExpressionParameters.ValueType valueType,
            int bitWidth,
            bool flippedRepresentation)
        {
            if (valueType == VRCExpressionParameters.ValueType.Float)
            {
                return flippedRepresentation ? Mathf.Clamp01(value) : Mathf.Clamp(value, -1f, 1f);
            }

            if (valueType == VRCExpressionParameters.ValueType.Int)
            {
                int minVal = flippedRepresentation ? -(1 << (bitWidth - 1)) : 0;
                int maxVal = flippedRepresentation ? (1 << (bitWidth - 1)) - 1 : (1 << bitWidth) - 1;
                return Mathf.Clamp(Mathf.RoundToInt(value), minVal, maxVal);
            }

            return value;
        }

        private static void ReportBuildSummary(
            BuildContext context,
            VRCAvatarDescriptor descriptor,
            int beforeBits,
            int afterBits,
            int removedCount,
            int madeLocalCount,
            int resizedCount,
            int skippedResizeOldSdkCount,
            int removedMenuControlCount,
            int skippedInternalCount,
            List<string> brokenRemainingControls)
        {
            string details =
                $"消費 bit: {beforeBits} → {afterBits} / 256 bit\n"
                + $"削除: {removedCount} 件 / 非同期化: {madeLocalCount} 件 / bit 幅縮小: {resizedCount} 件 / メニュー項目非表示: {removedMenuControlCount} 件";

            if (resizedCount > 0)
            {
                details += "\n注記: bit 幅縮小（ResizeBitWidth）は 2 クライアント同期未検証の実験的機能です。";
            }

            if (skippedResizeOldSdkCount > 0)
            {
                details += $"\n注記: VRChat SDK 3.10.5 以下の環境のため {skippedResizeOldSdkCount} 件の bit 幅縮小をスキップしました。";
            }

            if (skippedInternalCount > 0)
            {
                details += $"\n注記: internalParameter {skippedInternalCount} 件は初版仕様により変更をスキップしました（v1.1 解禁予定）。";
            }

            if (removedCount + madeLocalCount + resizedCount > 0 || skippedResizeOldSdkCount > 0 || skippedInternalCount > 0)
            {
                ExParamNdmfError.SafeReport(
                    context,
                    ErrorSeverity.Information,
                    "ExParam Manager ビルド適用サマリ",
                    details,
                    descriptor);
            }

            if (brokenRemainingControls.Count > 0)
            {
                ExParamNdmfError.SafeReport(
                    context,
                    ErrorSeverity.NonFatal,
                    "一部パラメータ削除により不完全になったメニュー項目があります",
                    "対象メニュー項目（削除された軸の参照のみクリアしました）:\n- " + string.Join("\n- ", brokenRemainingControls),
                    descriptor.expressionsMenu);
            }

            if (afterBits > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                bool hasVrcFuryCompressor = HasVrcFuryParameterCompressor();
                ExParamNdmfError.SafeReport(
                    context,
                    hasVrcFuryCompressor ? ErrorSeverity.NonFatal : ErrorSeverity.Error,
                    $"Expression Parameters 合計が上限 256 bit を超過しています（{afterBits} / 256 bit）",
                    hasVrcFuryCompressor
                        ? "後段の VRCFury ParameterCompressor により圧縮される見込みですが、ビルド結果の bit 数を必ず確認してください。"
                        : "後段に VRCFury ParameterCompressor が見つかりません。パラメータをさらに削減してください。",
                    descriptor.expressionParameters);
            }
        }

        private static bool HasVrcFuryParameterCompressor()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetType("VF.Hooks.ParameterCompressorHook", false) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SaveGeneratedAsset(BuildContext context, UnityEngine.Object asset)
        {
            if (asset == null || context.AssetContainer == null)
            {
                return;
            }

            if (AssetDatabase.Contains(context.AssetContainer) && !EditorUtility.IsPersistent(asset))
            {
                AssetDatabase.AddObjectToAsset(asset, context.AssetContainer);
            }
        }

        private static void TryRegisterReplacedAsset(BuildContext context, UnityEngine.Object oldObj, UnityEngine.Object newObj)
        {
            if (oldObj == null || newObj == null)
            {
                return;
            }

            try
            {
                if (ObjectRegistry.ActiveRegistry != null)
                {
                    ObjectRegistry.RegisterReplacedObject(oldObj, newObj);
                }
                else if (context.ObjectRegistry is IObjectRegistry reg)
                {
                    reg.RegisterReplacedObject(oldObj, newObj);
                }
            }
            catch (ArgumentException)
            {
                // 既に別パス等で置換登録済みのオブジェクトは安全にスキップする
            }
        }
    }
}
