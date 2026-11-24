using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// NDMF ParameterInfo と VRCAvatarDescriptor.expressionParameters を統合し、
    /// パラメータ一覧と Prefab/サブツリー別 bit 集計を収集する。
    /// </summary>
    public static class ParameterSnapshotCollector
    {
        private const string ModularAvatarPluginQualifiedName = "nadena.dev.modular-avatar";
        private const string ExParamManagerPluginQualifiedName = "com.sebanne.exparam-manager";

        public static void Collect(VRCAvatarDescriptor descriptor, AvatarScanResult result)
        {
            if (descriptor == null || result == null)
            {
                return;
            }

            GameObject avatarRoot = descriptor.gameObject;
            var byName = new Dictionary<string, ParameterSnapshot>(StringComparer.Ordinal);
            var baseParamLookup = BuildBaseParameterLookup(descriptor.expressionParameters);

            IEnumerable<ProvidedParameter> providedList = ParameterInfo.ForUI.GetParametersForObject(avatarRoot);
            foreach (ProvidedParameter provided in providedList)
            {
                if (provided == null || provided.Namespace != ParameterNamespace.Animator || provided.IsAnimatorOnly)
                {
                    continue;
                }

                string effectiveName = provided.EffectiveName;
                if (string.IsNullOrEmpty(effectiveName))
                {
                    continue;
                }

                if (!byName.TryGetValue(effectiveName, out ParameterSnapshot snap))
                {
                    snap = CreateSnapshotFromProvided(provided, avatarRoot, descriptor, baseParamLookup);
                    byName[effectiveName] = snap;
                    result.Parameters.Add(snap);
                }
                else
                {
                    MergeProvidedInfo(snap, provided);
                }
            }

            // ParameterInfo に含まれない expressionParameters 項目があれば補完マージする
            if (descriptor.expressionParameters != null && descriptor.expressionParameters.parameters != null)
            {
                foreach (VRCExpressionParameters.Parameter p in descriptor.expressionParameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || byName.ContainsKey(p.name))
                    {
                        continue;
                    }

                    var snap = CreateSnapshotFromBaseParameter(p, descriptor);
                    byName[p.name] = snap;
                    result.Parameters.Add(snap);
                }
            }

            BuildPrefabGroups(result, avatarRoot);
        }

        private static Dictionary<string, VRCExpressionParameters.Parameter> BuildBaseParameterLookup(
            VRCExpressionParameters expParams)
        {
            var dict = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            if (expParams == null || expParams.parameters == null)
            {
                return dict;
            }

            for (int i = 0; i < expParams.parameters.Length; i++)
            {
                VRCExpressionParameters.Parameter p = expParams.parameters[i];
                if (p != null && !string.IsNullOrEmpty(p.name) && !dict.ContainsKey(p.name))
                {
                    dict[p.name] = p;
                }
            }

            return dict;
        }

        private static ParameterSnapshot CreateSnapshotFromProvided(
            ProvidedParameter provided,
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            Dictionary<string, VRCExpressionParameters.Parameter> baseLookup)
        {
            ParameterValueKind valueKind = ConvertValueKind(provided.ParameterType);
            bool hasBase = baseLookup.TryGetValue(provided.EffectiveName, out VRCExpressionParameters.Parameter baseParam);

            int bitWidth = valueKind == ParameterValueKind.Bool ? 1 : 8;
            ParameterSignednessKind signedness = valueKind == ParameterValueKind.Float
                ? ParameterSignednessKind.Signed
                : ParameterSignednessKind.Unsigned;
            bool signednessKnown = true;

#if HAS_NDMF_1_15
            if (valueKind != ParameterValueKind.Bool && provided.NumericBitWidth.HasValue)
            {
                bitWidth = Mathf.Clamp(provided.NumericBitWidth.Value, 2, 8);
            }

            signedness = provided.Signedness == ParameterSignedness.Signed
                ? ParameterSignednessKind.Signed
                : ParameterSignednessKind.Unsigned;
#endif

#if HAS_VRCSDK3_AVATARS_3_10_6
            // SDK 3.10.6 と NDMF < 1.15.0 の混在時や未設定(bitWidth==0)も含め、本体 expressionParameters 側の実値で補完
            if (hasBase && valueKind != ParameterValueKind.Bool)
            {
                bitWidth = (baseParam.bitWidth >= 2 && baseParam.bitWidth <= 8) ? baseParam.bitWidth : 8;
                signedness = ResolveSdk3106Signedness(valueKind, baseParam.flippedRepresentation);
            }
#endif

            Component srcComp = provided.Source;
            bool isFromDescriptor = srcComp == descriptor;
            (string groupKey, string groupDisplay, GameObject groupRoot) = ResolvePrefabGroup(avatarRoot, srcComp);

            string pluginQName = provided.Plugin != null ? (provided.Plugin.QualifiedName ?? string.Empty) : string.Empty;
            string pluginDispName = provided.Plugin != null ? (provided.Plugin.DisplayName ?? pluginQName) : string.Empty;
            bool isExternalPlugin = !string.IsNullOrEmpty(pluginQName)
                && !string.Equals(pluginQName, ModularAvatarPluginQualifiedName, StringComparison.Ordinal)
                && !string.Equals(pluginQName, ExParamManagerPluginQualifiedName, StringComparison.Ordinal)
                && !pluginQName.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal);

            return new ParameterSnapshot
            {
                EffectiveName = provided.EffectiveName,
                OriginalName = provided.OriginalName ?? provided.EffectiveName,
                ValueKind = valueKind,
                IsSynced = provided.WantSynced,
                Saved = hasBase ? baseParam.saved : true,
                DefaultValue = provided.DefaultValue ?? (hasBase ? baseParam.defaultValue : 0f),
                CurrentBitWidth = bitWidth,
                Signedness = signedness,
                IsSignednessKnown = signednessKnown,
                IsInternalParameter = provided.IsHidden,
                IsExternalPluginManaged = isExternalPlugin,
                SourcePluginName = pluginDispName,
                SourcePluginQualifiedName = pluginQName,
                SourceComponent = srcComp,
                SourceObjectPath = GetRelativePath(avatarRoot, srcComp),
                PrefabGroupKey = groupKey,
                PrefabGroupDisplayName = groupDisplay,
                PrefabGroupRootObject = groupRoot,
                IsInBaseExpressionParameters = hasBase || isFromDescriptor,
                IsFromNdmfProvider = true,
            };
        }

        private static void MergeProvidedInfo(ParameterSnapshot existing, ProvidedParameter provided)
        {
            if (provided.WantSynced)
            {
                existing.IsSynced = true;
            }

            if (!provided.IsHidden)
            {
                existing.IsInternalParameter = false;
            }

            if (existing.SourceComponent == null && provided.Source != null)
            {
                existing.SourceComponent = provided.Source;
            }
        }

        private static ParameterSnapshot CreateSnapshotFromBaseParameter(
            VRCExpressionParameters.Parameter p,
            VRCAvatarDescriptor descriptor)
        {
            ParameterValueKind kind = ConvertVrcValueType(p.valueType);
            int bitWidth = kind == ParameterValueKind.Bool ? 1 : 8;
            ParameterSignednessKind signedness = kind == ParameterValueKind.Float
                ? ParameterSignednessKind.Signed
                : ParameterSignednessKind.Unsigned;

#if HAS_VRCSDK3_AVATARS_3_10_6
            if (kind != ParameterValueKind.Bool)
            {
                if (p.bitWidth >= 2 && p.bitWidth <= 8)
                {
                    bitWidth = p.bitWidth;
                }

                signedness = ResolveSdk3106Signedness(kind, p.flippedRepresentation);
            }
#endif

            return new ParameterSnapshot
            {
                EffectiveName = p.name,
                OriginalName = p.name,
                ValueKind = kind,
                IsSynced = p.networkSynced,
                Saved = p.saved,
                DefaultValue = p.defaultValue,
                CurrentBitWidth = bitWidth,
                Signedness = signedness,
                IsSignednessKnown = true,
                IsInternalParameter = false,
                IsExternalPluginManaged = false,
                SourceComponent = descriptor,
                SourceObjectPath = string.Empty,
                PrefabGroupKey = "root",
                PrefabGroupDisplayName = descriptor.gameObject.name + " (アバター本体)",
                PrefabGroupRootObject = descriptor.gameObject,
                IsInBaseExpressionParameters = true,
                IsFromNdmfProvider = false,
            };
        }

        public static void BuildPrefabGroups(AvatarScanResult result, GameObject avatarRoot)
        {
            var groups = new Dictionary<string, PrefabBitGroup>(StringComparer.Ordinal);
            int totalBits = 0;

            for (int i = 0; i < result.Parameters.Count; i++)
            {
                ParameterSnapshot p = result.Parameters[i];
                int cost = p.EffectiveBitCost;
                totalBits += cost;

                string key = string.IsNullOrEmpty(p.PrefabGroupKey) ? "root" : p.PrefabGroupKey;
                if (!groups.TryGetValue(key, out PrefabBitGroup grp))
                {
                    UnityEngine.Object rootObj = p.PrefabGroupRootObject != null
                        ? p.PrefabGroupRootObject
                        : avatarRoot;
                    grp = new PrefabBitGroup
                    {
                        GroupKey = key,
                        DisplayName = string.IsNullOrEmpty(p.PrefabGroupDisplayName) ? avatarRoot.name : p.PrefabGroupDisplayName,
                        RootObject = rootObj,
                    };
                    groups[key] = grp;
                }

                grp.TotalParameterCount++;
                if (p.IsSynced)
                {
                    grp.SyncedParameterCount++;
                    grp.TotalSyncedBits += cost;
                }
            }

            result.TotalSyncedBits = totalBits;
            result.TotalParameterCount = result.Parameters.Count;

            Transform avatarRootTransform = avatarRoot != null ? avatarRoot.transform : null;
            var entries = new List<KeyValuePair<PrefabBitGroup, int[]>>(groups.Count);
            foreach (PrefabBitGroup g in groups.Values)
            {
                entries.Add(new KeyValuePair<PrefabBitGroup, int[]>(
                    g,
                    BuildHierarchyPath(avatarRootTransform, g.GroupKey, g.RootObject)));
            }

            entries.Sort((a, b) =>
            {
                int cmp = CompareHierarchyPaths(a.Value, b.Value);
                if (cmp != 0) return cmp;
                cmp = b.Key.TotalSyncedBits.CompareTo(a.Key.TotalSyncedBits);
                if (cmp != 0) return cmp;
                return StringComparer.OrdinalIgnoreCase.Compare(a.Key.DisplayName, b.Key.DisplayName);
            });

            for (int i = 0; i < entries.Count; i++)
            {
                PrefabBitGroup grp = entries[i].Key;
                grp.HierarchyOrder = i;
                grp.ColorIndex = i;
                result.PrefabGroups.Add(grp);
            }
        }

        private static int[] BuildHierarchyPath(Transform avatarRootTransform, string groupKey, UnityEngine.Object groupRootObj)
        {
            if (groupKey == "root" || avatarRootTransform == null)
            {
                return Array.Empty<int>();
            }

            Transform targetTransform = null;
            if (groupRootObj is GameObject go)
            {
                targetTransform = go.transform;
            }
            else if (groupRootObj is Component comp)
            {
                targetTransform = comp.transform;
            }

            if (targetTransform == null)
            {
                return new[] { int.MaxValue };
            }

            if (targetTransform == avatarRootTransform)
            {
                return Array.Empty<int>();
            }

            if (!targetTransform.IsChildOf(avatarRootTransform))
            {
                return new[] { int.MaxValue };
            }

            var indices = new List<int>();
            Transform cur = targetTransform;
            while (cur != null && cur != avatarRootTransform)
            {
                indices.Add(cur.GetSiblingIndex());
                cur = cur.parent;
            }

            indices.Reverse();
            return indices.ToArray();
        }

        private static int CompareHierarchyPaths(int[] x, int[] y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int minLen = Math.Min(x.Length, y.Length);
            for (int i = 0; i < minLen; i++)
            {
                int cmp = x[i].CompareTo(y[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return x.Length.CompareTo(y.Length);
        }

        public static (string key, string display, GameObject rootObj) ResolvePrefabGroup(GameObject avatarRoot, Component src)
        {
            if (src == null || src.gameObject == avatarRoot)
            {
                return ("root", avatarRoot.name + " (アバター本体)", avatarRoot);
            }

            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(src.gameObject);
            if (prefabRoot != null && prefabRoot != avatarRoot && prefabRoot.transform.IsChildOf(avatarRoot.transform))
            {
                return ("prefab:" + prefabRoot.GetInstanceID(), prefabRoot.name, prefabRoot);
            }

            Transform topChild = src.transform;
            while (topChild.parent != null && topChild.parent != avatarRoot.transform)
            {
                topChild = topChild.parent;
            }

            return ("sub:" + topChild.gameObject.GetInstanceID(), topChild.gameObject.name, topChild.gameObject);
        }

        private static string GetRelativePath(GameObject avatarRoot, Component comp)
        {
            if (avatarRoot == null || comp == null)
            {
                return string.Empty;
            }

            if (comp.gameObject == avatarRoot)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            Transform cur = comp.transform;
            while (cur != null && cur != avatarRoot.transform)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }

            return string.Join("/", stack);
        }

        private static ParameterValueKind ConvertValueKind(AnimatorControllerParameterType? type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Int:
                    return ParameterValueKind.Int;
                case AnimatorControllerParameterType.Float:
                    return ParameterValueKind.Float;
                default:
                    return ParameterValueKind.Bool;
            }
        }

        private static ParameterValueKind ConvertVrcValueType(VRCExpressionParameters.ValueType valueType)
        {
            switch (valueType)
            {
                case VRCExpressionParameters.ValueType.Int:
                    return ParameterValueKind.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return ParameterValueKind.Float;
                default:
                    return ParameterValueKind.Bool;
            }
        }

        private static ParameterSignednessKind ResolveSdk3106Signedness(ParameterValueKind kind, bool flippedRepresentation)
        {
            if (kind == ParameterValueKind.Float)
            {
                return flippedRepresentation ? ParameterSignednessKind.Unsigned : ParameterSignednessKind.Signed;
            }

            return flippedRepresentation ? ParameterSignednessKind.Signed : ParameterSignednessKind.Unsigned;
        }
    }
}
