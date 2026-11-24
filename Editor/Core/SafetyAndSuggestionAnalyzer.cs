using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// 保護警告フラグ判定、厳格 Int 推奨 bit 幅、Radial 専用 Float 参考フィルタ、未使用候補判定を行う。
    /// </summary>
    public static class SafetyAndSuggestionAnalyzer
    {
        private static readonly HashSet<string> BuiltInParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureLeftWeight",
            "GestureRight", "GestureRightWeight", "AngularY", "VelocityX",
            "VelocityY", "VelocityZ", "VelocityMagnitude", "Upright", "Grounded",
            "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation",
            "Earmuffs", "IsOnFriendsList", "AvatarVersion", "ScaleModified",
            "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
            "EyeHeightAsPercent", "IsAnimatorEnabled", "PreviewMode",
        };

        private static readonly HashSet<string> SdkDefaultTemplateParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "VRCEmote", "VRCFaceBlendH", "VRCFaceBlendV",
        };

        // T0.5 横断実測を反映した外部入力（OSC / FT）および汎用共通ギミック保護プレフィックス辞書
        private static readonly string[] ExternalOrCommonGimmickPrefixes =
        {
            "FT/",
            "v2/",
            "QPro/",
            "FaceTracking/",
            "Toggle/EyeTracking",
            "Toggle/LipTracking",
            "Go/",
            "VF",
            "VRCFury",
            "VirtualLens",
            "VL2_",
            "SPS",
            "DPS",
            "AudioLink",
            "AL_",
            "QV",
        };

        public static void Analyze(AvatarScanResult result)
        {
            if (result == null)
            {
                return;
            }

            for (int i = 0; i < result.Parameters.Count; i++)
            {
                ParameterSnapshot p = result.Parameters[i];
                EvaluateProtectionFlags(p);
                EvaluateSuggestions(p);
            }
        }

        private static void EvaluateProtectionFlags(ParameterSnapshot p)
        {
            p.ProtectionFlags = ProtectionFlags.None;
            p.ProtectionReasons.Clear();

            string name = p.EffectiveName ?? string.Empty;

            if (BuiltInParameters.Contains(name))
            {
                p.ProtectionFlags |= ProtectionFlags.BuiltIn;
                p.ProtectionReasons.Add("VRChat ランタイム組み込みパラメータです");
            }

            if (SdkDefaultTemplateParameters.Contains(name))
            {
                p.ProtectionFlags |= ProtectionFlags.SdkDefaultTemplate;
                p.ProtectionReasons.Add("VRChat SDK 既定テンプレート由来パラメータです");
            }

            if (p.IsExternalPluginManaged)
            {
                p.ProtectionFlags |= ProtectionFlags.ExternalPluginManaged;
                p.ProtectionReasons.Add("他 NDMF プラグイン（" + p.SourcePluginName + "）が供給しています");
            }

            if (p.HasParameterDriverWrite || p.HasParameterDriverRead)
            {
                p.ProtectionFlags |= ProtectionFlags.HasParameterDriverWrite;
                p.ProtectionReasons.Add("VRCAvatarParameterDriver で読み書きされています");
            }

            if (p.HasPhysBoneOrContact)
            {
                p.ProtectionFlags |= ProtectionFlags.HasPhysBoneOrContact;
                p.ProtectionReasons.Add("PhysBone または ContactReceiver から参照されています");
            }

            if (p.IsUsedInMultiAxis)
            {
                p.ProtectionFlags |= ProtectionFlags.UsedInMultiAxisControl;
                p.ProtectionReasons.Add("TwoAxis / FourAxis Puppet で他パラメータと同居しています");
            }

            if (p.IsInternalParameter)
            {
                p.ProtectionFlags |= ProtectionFlags.InternalParameter;
                p.ProtectionReasons.Add("MA internalParameter（初版は読み取り専用・v1.1 解禁予定）");
            }

            if (HasExternalOrCommonPrefix(name) || (p.ValueKind == ParameterValueKind.Int && p.IsSynced && !p.HasMenuWriter && !p.HasParameterDriverWrite))
            {
                p.ProtectionFlags |= ProtectionFlags.ExternalInputSuspected;
                p.ProtectionReasons.Add(HasExternalOrCommonPrefix(name)
                    ? "外部入力（OSC/FT）または共通ギミック由来のプレフィックスに一致します"
                    : "書込元 0 件の同期 Int（外部入力・OSC 等の可能性）です");
            }
        }

        private static bool HasExternalOrCommonPrefix(string name)
        {
            for (int i = 0; i < ExternalOrCommonGimmickPrefixes.Length; i++)
            {
                if (name.StartsWith(ExternalOrCommonGimmickPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EvaluateSuggestions(ParameterSnapshot p)
        {
            var s = new SuggestionInfo();
            p.Suggestion = s;

            // 未使用候補バッジ
            bool hasProtectionBlockingUnused = (p.ProtectionFlags & (
                ProtectionFlags.BuiltIn
                | ProtectionFlags.SdkDefaultTemplate
                | ProtectionFlags.ExternalPluginManaged
                | ProtectionFlags.ExternalInputSuspected
                | ProtectionFlags.InternalParameter)) != ProtectionFlags.None;

            s.IsUnusedCandidate = p.References.Count == 0 && !hasProtectionBlockingUnused;

            // 厳格 Int 推奨
            if (p.ValueKind == ParameterValueKind.Int && p.IsSynced && p.CurrentBitWidth > 2)
            {
                var observed = new List<int>();
                for (int i = 0; i < p.MenuWriteValues.Count; i++)
                {
                    observed.Add(Mathf.RoundToInt(p.MenuWriteValues[i]));
                }

                for (int i = 0; i < p.AnimatorConditions.Count; i++)
                {
                    AnimatorConditionObservation obs = p.AnimatorConditions[i];
                    int t = Mathf.RoundToInt(obs.Threshold);
                    // Unity AnimatorConditionMode: 3 = Greater, 4 = Less
                    if (obs.ConditionMode == 3)
                    {
                        observed.Add(t + 1);
                    }
                    else if (obs.ConditionMode == 4)
                    {
                        observed.Add(t - 1);
                    }
                    else
                    {
                        observed.Add(t);
                    }
                }

                observed.Add(Mathf.RoundToInt(p.DefaultValue));

                int minObs = observed[0];
                int maxObs = observed[0];
                for (int i = 1; i < observed.Count; i++)
                {
                    if (observed[i] < minObs) minObs = observed[i];
                    if (observed[i] > maxObs) maxObs = observed[i];
                }

                s.ObservedMinInt = minObs;
                s.ObservedMaxInt = maxObs;

                int minSafeWidth = ComputeMinIntBitWidth(minObs, maxObs, p.Signedness == ParameterSignednessKind.Signed);
                bool canShrink = minSafeWidth < p.CurrentBitWidth;

                if (p.ProtectionFlags == ProtectionFlags.None && p.HasMenuWriter && p.IsSignednessKnown && canShrink)
                {
                    s.HasStrictIntRecommendation = true;
                    s.RecommendedIntBitWidth = minSafeWidth;
                    s.IntBitSavings = p.CurrentBitWidth - minSafeWidth;
                    s.IntReason = $"観測値 [{minObs}..{maxObs}] ({p.Signedness}) は {minSafeWidth} bit で表現可能（{s.IntBitSavings} bit 節約）";
                }
                else if (canShrink)
                {
                    s.HasConditionalIntEstimate = true;
                    s.ConditionalEstimatedIntBitWidth = minSafeWidth;
                    s.IntReason = $"条件付き試算: [{minObs}..{maxObs}] ({p.Signedness}) → {minSafeWidth} bit（保護/書込条件あり）";
                }
            }

            // Radial 専用 Float 参考フィルタ
            if (p.ValueKind == ParameterValueKind.Float
                && p.IsSynced
                && p.CurrentBitWidth > 4
                && p.IsRadialOnlyFloat
                && p.ProtectionFlags == ProtectionFlags.None)
            {
                s.IsRadialFloatReferenceCandidate = true;
                s.RadialFloat4BitSavings = p.CurrentBitWidth - 4;
                s.RadialFloat5BitSavings = Mathf.Max(0, p.CurrentBitWidth - 5);
                s.FloatReason = $"Radial Puppet 専用 Float（4 bit 化で {s.RadialFloat4BitSavings} bit、5 bit 化で {s.RadialFloat5BitSavings} bit 削減見込み）";
            }
        }

        public static int ComputeMinIntBitWidth(int minValue, int maxValue, bool isSigned)
        {
            for (int w = 2; w <= 8; w++)
            {
                int minCap = isSigned ? -(1 << (w - 1)) : 0;
                int maxCap = isSigned ? (1 << (w - 1)) - 1 : (1 << w) - 1;
                if (minValue >= minCap && maxValue <= maxCap)
                {
                    return w;
                }
            }

            return 8;
        }
    }

    public static class AvatarScanner
    {
        public static AvatarScanResult Scan(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return new AvatarScanResult();
            }

            var result = new AvatarScanResult
            {
                AvatarRoot = descriptor.gameObject,
                Descriptor = descriptor,
                HasSyncParameterSequence = MaAdapterRegistry.Current?.HasSyncParameterSequence(descriptor.gameObject) ?? false,
            };

            if (descriptor.expressionParameters == null)
            {
                result.Warnings.Add("VRCAvatarDescriptor に Expression Parameters が未設定です。");
            }

            ParameterSnapshotCollector.Collect(descriptor, result);
            ReferenceScanner.Scan(descriptor, result);
            SafetyAndSuggestionAnalyzer.Analyze(result);

            return result;
        }
    }
}
