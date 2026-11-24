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

        public BitProjection(
            int beforeBits,
            int afterBits,
            int removeRuleCount,
            int makeLocalRuleCount,
            int resizeRuleCount,
            int skippedResizeOnOldSdkCount)
        {
            BeforeBits = beforeBits;
            AfterBits = afterBits;
            SavedBits = Mathf.Max(0, beforeBits - afterBits);
            RemoveRuleCount = removeRuleCount;
            MakeLocalRuleCount = makeLocalRuleCount;
            ResizeRuleCount = resizeRuleCount;
            SkippedResizeOnOldSdkCount = skippedResizeOnOldSdkCount;
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

            return new BitProjection(beforeBits, afterBits, removeCount, localCount, resizeCount, skippedOldSdk);
        }

        public static void DrawBitBar(BitProjection projection)
        {
            int maxCost = VRCExpressionParameters.MAX_PARAMETER_COST;
            float ratio = Mathf.Clamp01(projection.AfterBits / (float)maxCost);

            Rect rect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 1f));

            Color fillColor = projection.AfterBits > maxCost
                ? new Color(0.82f, 0.24f, 0.24f, 1f)
                : projection.AfterBits > 200
                    ? new Color(0.85f, 0.60f, 0.18f, 1f)
                    : new Color(0.24f, 0.65f, 0.38f, 1f);

            var fillRect = new Rect(rect.x + 1, rect.y + 1, Mathf.Max(0f, (rect.width - 2f) * ratio), rect.height - 2f);
            EditorGUI.DrawRect(fillRect, fillColor);

            string text = projection.SavedBits > 0
                ? $"同期 bit: {projection.BeforeBits} → {projection.AfterBits} / {maxCost} bit（-{projection.SavedBits} bit）"
                : $"同期 bit: {projection.AfterBits} / {maxCost} bit";

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            GUI.Label(rect, text, style);
        }
    }
}
