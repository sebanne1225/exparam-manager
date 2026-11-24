using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.ExParamManager.Editor
{
    public enum ParameterValueKind
    {
        Int = 0,
        Float = 1,
        Bool = 2,
    }

    public enum ParameterSignednessKind
    {
        Unsigned = 0,
        Signed = 1,
    }

    [Flags]
    public enum ProtectionFlags
    {
        None = 0,
        BuiltIn = 1 << 0,
        SdkDefaultTemplate = 1 << 1,
        ExternalPluginManaged = 1 << 2,
        HasParameterDriverWrite = 1 << 3,
        HasPhysBoneOrContact = 1 << 4,
        UsedInMultiAxisControl = 1 << 5,
        InternalParameter = 1 << 6,
        ExternalInputSuspected = 1 << 7,
    }

    public enum ReferenceKind
    {
        ExpressionsMenuAsset = 0,
        MaMenuItem = 1,
        MaMenuInstaller = 2,
        AnimatorTransition = 3,
        AnimatorAnyStateTransition = 4,
        AnimatorBlendTree = 5,
        AnimatorStateProperty = 6,
        ParameterDriverWrite = 7,
        ParameterDriverCopySource = 8,
        PhysBone = 9,
        ContactReceiver = 10,
        MaReactive = 11,
    }

    public enum ParameterSortMode
    {
        BitCostDescending = 0,
        NameAscending = 1,
        TypeOrder = 2,
        ReferenceCountAscending = 3,
    }

    public sealed class ParameterReference
    {
        public ReferenceKind Kind;
        public string DisplayLabel = string.Empty;
        public string Detail = string.Empty;
        public UnityEngine.Object TargetObject;
        public string MenuControlName = string.Empty;
        public string MenuItemLabel = string.Empty;
        public int MenuControlType;
        public float? MenuWriteValue;
        public bool IsExtractMenuObject;
        public bool HasBrokenMultiAxisPeer;
    }

    public readonly struct AnimatorConditionObservation
    {
        public readonly int ConditionMode;
        public readonly float Threshold;

        public AnimatorConditionObservation(int conditionMode, float threshold)
        {
            ConditionMode = conditionMode;
            Threshold = threshold;
        }
    }

    public sealed class SuggestionInfo
    {
        public bool HasStrictIntRecommendation;
        public int RecommendedIntBitWidth = 8;
        public int IntBitSavings;
        public int ObservedMinInt;
        public int ObservedMaxInt;
        public bool HasConditionalIntEstimate;
        public int ConditionalEstimatedIntBitWidth = 8;
        public string IntReason = string.Empty;

        public bool IsRadialFloatReferenceCandidate;
        public int RadialFloat4BitSavings;
        public int RadialFloat5BitSavings;
        public string FloatReason = string.Empty;

        public bool IsUnusedCandidate;
    }

    public sealed class ParameterSnapshot
    {
        public string EffectiveName = string.Empty;
        public string OriginalName = string.Empty;
        public ParameterValueKind ValueKind = ParameterValueKind.Bool;
        public bool IsSynced = true;
        public bool Saved = true;
        public float DefaultValue;
        public int CurrentBitWidth = 1;
        public ParameterSignednessKind Signedness = ParameterSignednessKind.Unsigned;
        public bool IsSignednessKnown = true;

        public bool IsInternalParameter;
        public bool IsExternalPluginManaged;
        public string SourcePluginName = string.Empty;
        public string SourcePluginQualifiedName = string.Empty;
        public Component SourceComponent;
        public string SourceObjectPath = string.Empty;
        public string PrefabGroupKey = string.Empty;
        public string PrefabGroupDisplayName = string.Empty;
        public UnityEngine.Object PrefabGroupRootObject;

        public bool IsInBaseExpressionParameters;
        public bool IsFromNdmfProvider;
        public string PrimaryMenuLabel = string.Empty;

        public readonly List<ParameterReference> References = new List<ParameterReference>();
        public readonly List<float> MenuWriteValues = new List<float>();
        public readonly List<AnimatorConditionObservation> AnimatorConditions = new List<AnimatorConditionObservation>();

        public int MenuRefCount;
        public int AnimatorRefCount;
        public int DriverRefCount;
        public int DynamicsRefCount;
        public int ReactiveRefCount;

        public bool HasMenuWriter;
        public bool IsRadialOnlyFloat;
        public bool IsUsedInMultiAxis;
        public bool HasParameterDriverWrite;
        public bool HasParameterDriverRead;
        public bool HasPhysBoneOrContact;
        public bool HasMaReactive;

        public ProtectionFlags ProtectionFlags = ProtectionFlags.None;
        public readonly List<string> ProtectionReasons = new List<string>();
        public SuggestionInfo Suggestion = new SuggestionInfo();

        public int EffectiveBitCost => IsSynced
            ? (ValueKind == ParameterValueKind.Bool ? 1 : Mathf.Clamp(CurrentBitWidth, 2, 8))
            : 0;
    }

    public sealed class PrefabBitGroup
    {
        public string GroupKey = string.Empty;
        public string DisplayName = string.Empty;
        public UnityEngine.Object RootObject;
        public int TotalSyncedBits;
        public int SyncedParameterCount;
        public int TotalParameterCount;
    }

    public sealed class AvatarScanResult
    {
        public GameObject AvatarRoot;
        public VRCAvatarDescriptor Descriptor;
        public bool HasSyncParameterSequence;
        public int TotalSyncedBits;
        public int TotalParameterCount;
        public readonly List<ParameterSnapshot> Parameters = new List<ParameterSnapshot>();
        public readonly List<PrefabBitGroup> PrefabGroups = new List<PrefabBitGroup>();
        public readonly List<string> Warnings = new List<string>();
    }

    public static class ParameterSortHelper
    {
        public static List<ParameterSnapshot> Sort(IEnumerable<ParameterSnapshot> source, ParameterSortMode mode)
        {
            if (source == null)
            {
                return new List<ParameterSnapshot>();
            }

            switch (mode)
            {
                case ParameterSortMode.NameAscending:
                    return source
                        .OrderBy(p => p.EffectiveName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                case ParameterSortMode.TypeOrder:
                    return source
                        .OrderBy(p => GetTypeRank(p.ValueKind))
                        .ThenByDescending(p => p.EffectiveBitCost)
                        .ThenBy(p => p.EffectiveName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                case ParameterSortMode.ReferenceCountAscending:
                    return source
                        .OrderBy(p => p.References.Count)
                        .ThenByDescending(p => p.EffectiveBitCost)
                        .ThenBy(p => p.EffectiveName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                case ParameterSortMode.BitCostDescending:
                default:
                    return source
                        .OrderByDescending(p => p.EffectiveBitCost)
                        .ThenBy(p => GetTypeRank(p.ValueKind))
                        .ThenBy(p => p.EffectiveName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
        }

        private static int GetTypeRank(ParameterValueKind kind)
        {
            switch (kind)
            {
                case ParameterValueKind.Float: return 0;
                case ParameterValueKind.Int: return 1;
                case ParameterValueKind.Bool: return 2;
                default: return 3;
            }
        }
    }
}
