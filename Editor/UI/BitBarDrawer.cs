using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor
{
    public readonly struct BitProjection
    {
        public readonly int BeforeBits;
        public readonly int AfterBits;
        public readonly int SavedBits;
        public readonly int RemoveRuleCount;
        public readonly int MakeLocalRuleCount;
        public readonly int ResizeRuleCount;
        public readonly int SkippedResizeOnOldSdkCount;
        public readonly int DisabledRuleCount;
        public readonly bool IsGloballyDisabled;

        public BitProjection(
            int beforeBits,
            int afterBits,
            int removeRuleCount,
            int makeLocalRuleCount,
            int resizeRuleCount,
            int skippedResizeOnOldSdkCount,
            int disabledRuleCount = 0,
            bool isGloballyDisabled = false)
        {
            BeforeBits = beforeBits;
            AfterBits = afterBits;
            SavedBits = Mathf.Max(0, beforeBits - afterBits);
            RemoveRuleCount = removeRuleCount;
            MakeLocalRuleCount = makeLocalRuleCount;
            ResizeRuleCount = resizeRuleCount;
            SkippedResizeOnOldSdkCount = skippedResizeOnOldSdkCount;
            DisabledRuleCount = disabledRuleCount;
            IsGloballyDisabled = isGloballyDisabled;
        }
    }

    public static class BitBarDrawer
    {
        public static BitProjection CalculateProjection(
            AvatarScanResult scan,
            ExParamManagerComponent component,
            bool allowBitWidthResize)
        {
            if (scan == null)
            {
                return default;
            }

            int beforeBits = 0;
            int afterBits = 0;
            int removeCount = 0;
            int localCount = 0;
            int resizeCount = 0;
            int skippedOldSdk = 0;
            int disabledCount = 0;
            bool globallyDisabled = component != null && !component.enabledOnBuild;

            for (int i = 0; i < scan.Parameters.Count; i++)
            {
                ParameterSnapshot p = scan.Parameters[i];
                int cur = p.EffectiveBitCost;
                beforeBits += cur;

                ParameterRule rule = component != null
                    ? component.FindRule(p.EffectiveName, p.SourceComponent)
                    : null;

                if (rule == null || rule.action == ParameterAction.Keep || p.IsInternalParameter)
                {
                    afterBits += cur;
                    continue;
                }

                if (rule.disabled)
                {
                    disabledCount++;
                    afterBits += cur;
                    continue;
                }

                if (globallyDisabled)
                {
                    afterBits += cur;
                    continue;
                }

                switch (rule.action)
                {
                    case ParameterAction.Remove:
                        removeCount++;
                        break;

                    case ParameterAction.MakeLocal:
                        localCount++;
                        break;

                    case ParameterAction.ResizeBitWidth:
                        if (!p.IsSynced || p.ValueKind == ParameterValueKind.Bool)
                        {
                            afterBits += cur;
                        }
                        else if (!allowBitWidthResize)
                        {
                            skippedOldSdk++;
                            afterBits += cur;
                        }
                        else
                        {
                            resizeCount++;
                            afterBits += rule.GetClampedBitWidth();
                        }
                        break;

                    default:
                        afterBits += cur;
                        break;
                }
            }

            return new BitProjection(beforeBits, afterBits, removeCount, localCount, resizeCount, skippedOldSdk, disabledCount, globallyDisabled);
        }

        public static void DrawBitBar(BitProjection projection)
        {
            int maxCost = VRCExpressionParameters.MAX_PARAMETER_COST;
            float beforeRatio = Mathf.Clamp01(projection.BeforeBits / (float)maxCost);
            float afterRatio = Mathf.Clamp01(projection.AfterBits / (float)maxCost);

            Rect rect = GUILayoutUtility.GetRect(18, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.14f, 1f));

            if (projection.SavedBits > 0)
            {
                var beforeRect = new Rect(rect.x + 1, rect.y + 1, Mathf.Max(0f, (rect.width - 2f) * beforeRatio), rect.height - 2f);
                EditorGUI.DrawRect(beforeRect, new Color(0.35f, 0.72f, 0.48f, 0.28f));
            }

            Color fillColor = projection.AfterBits > maxCost
                ? new Color(0.82f, 0.24f, 0.24f, 1f)
                : projection.IsGloballyDisabled
                    ? new Color(0.42f, 0.44f, 0.48f, 1f)
                    : projection.AfterBits > 200
                        ? new Color(0.85f, 0.60f, 0.18f, 1f)
                        : new Color(0.24f, 0.65f, 0.38f, 1f);

            var fillRect = new Rect(rect.x + 1, rect.y + 1, Mathf.Max(0f, (rect.width - 2f) * afterRatio), rect.height - 2f);
            EditorGUI.DrawRect(fillRect, fillColor);

            string text;
            if (projection.AfterBits > maxCost)
            {
                int over = projection.AfterBits - maxCost;
                text = projection.SavedBits > 0
                    ? $"同期 bit: {projection.BeforeBits} → {projection.AfterBits} / {maxCost} bit（+{over} bit 超過）"
                    : $"同期 bit: {projection.AfterBits} / {maxCost} bit（+{over} bit 超過）";
            }
            else if (projection.SavedBits > 0)
            {
                int remaining = maxCost - projection.AfterBits;
                text = $"同期 bit: {projection.BeforeBits} → {projection.AfterBits} / {maxCost} bit（-{projection.SavedBits} bit 削減 / 残り {remaining} bit）";
            }
            else
            {
                int remaining = maxCost - projection.AfterBits;
                text = $"同期 bit: {projection.AfterBits} / {maxCost} bit（残り {remaining} bit）";
            }

            if (projection.IsGloballyDisabled)
            {
                text += "（全体一時オフ中）";
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            GUI.Label(rect, text, style);
        }
    }
}
