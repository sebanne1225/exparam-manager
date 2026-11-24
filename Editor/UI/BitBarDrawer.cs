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
        public readonly int BeforeSyncedParamCount;
        public readonly int AfterSyncedParamCount;

        public BitProjection(
            int beforeBits,
            int afterBits,
            int removeRuleCount,
            int makeLocalRuleCount,
            int resizeRuleCount,
            int skippedResizeOnOldSdkCount,
            int disabledRuleCount = 0,
            bool isGloballyDisabled = false,
            int beforeSyncedParamCount = 0,
            int afterSyncedParamCount = 0)
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
            BeforeSyncedParamCount = beforeSyncedParamCount;
            AfterSyncedParamCount = afterSyncedParamCount;
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
            int beforeSyncedCount = 0;
            int afterSyncedCount = 0;
            bool globallyDisabled = component != null && !component.enabledOnBuild;

            for (int i = 0; i < scan.Parameters.Count; i++)
            {
                ParameterSnapshot p = scan.Parameters[i];
                int cur = p.EffectiveBitCost;
                beforeBits += cur;
                if (p.IsSynced)
                {
                    beforeSyncedCount++;
                }

                ParameterRule rule = component != null
                    ? component.FindRule(p.EffectiveName, p.SourceComponent)
                    : null;

                if (rule == null || rule.action == ParameterAction.Keep || p.IsInternalParameter)
                {
                    afterBits += cur;
                    if (p.IsSynced)
                    {
                        afterSyncedCount++;
                    }
                    continue;
                }

                if (rule.disabled)
                {
                    disabledCount++;
                    afterBits += cur;
                    if (p.IsSynced)
                    {
                        afterSyncedCount++;
                    }
                    continue;
                }

                if (globallyDisabled)
                {
                    afterBits += cur;
                    if (p.IsSynced)
                    {
                        afterSyncedCount++;
                    }
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
                        if (p.IsSynced)
                        {
                            afterSyncedCount++;
                        }
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
                        if (p.IsSynced)
                        {
                            afterSyncedCount++;
                        }
                        break;
                }
            }

            return new BitProjection(
                beforeBits,
                afterBits,
                removeCount,
                localCount,
                resizeCount,
                skippedOldSdk,
                disabledCount,
                globallyDisabled,
                beforeSyncedCount,
                afterSyncedCount);
        }

        private static readonly Color[] GroupPalette =
        {
            new Color(0.32f, 0.56f, 0.86f, 1f), // 0: アバター本体（スチールブルー）
            new Color(0.28f, 0.72f, 0.46f, 1f), // 1: エメラルド
            new Color(0.90f, 0.56f, 0.22f, 1f), // 2: オレンジ
            new Color(0.64f, 0.44f, 0.86f, 1f), // 3: パープル
            new Color(0.24f, 0.72f, 0.82f, 1f), // 4: シアン
            new Color(0.86f, 0.40f, 0.58f, 1f), // 5: ローズ
            new Color(0.58f, 0.76f, 0.26f, 1f), // 6: ライム
            new Color(0.88f, 0.74f, 0.24f, 1f), // 7: アンバー
            new Color(0.36f, 0.46f, 0.88f, 1f), // 8: インディゴ
            new Color(0.88f, 0.42f, 0.34f, 1f), // 9: コーラル
            new Color(0.22f, 0.66f, 0.62f, 1f), // 10: ティール
            new Color(0.76f, 0.52f, 0.82f, 1f), // 11: ラベンダー
        };

        public static Color GetGroupColor(int colorIndex)
        {
            int idx = ((colorIndex % GroupPalette.Length) + GroupPalette.Length) % GroupPalette.Length;
            return GroupPalette[idx];
        }

        public static bool DrawPrefabGroupRatioBar(
            IReadOnlyList<PrefabBitGroup> orderedGroups,
            int totalSyncedBits,
            string selectedGroupKey,
            out string clickedGroupKey,
            out UnityEngine.Object clickedRootObject)
        {
            clickedGroupKey = string.Empty;
            clickedRootObject = null;

            Rect rect = GUILayoutUtility.GetRect(18f, 16f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.14f, 1f));

            if (orderedGroups == null || orderedGroups.Count == 0 || totalSyncedBits <= 0)
            {
                return false;
            }

            int maxCost = VRCExpressionParameters.MAX_PARAMETER_COST;
            int denom = Mathf.Max(maxCost, totalSyncedBits);
            float innerX = rect.x + 1f;
            float innerY = rect.y + 1f;
            float innerW = Mathf.Max(0f, rect.width - 2f);
            float innerH = Mathf.Max(0f, rect.height - 2f);

            var segLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };

            bool hasSelection = !string.IsNullOrEmpty(selectedGroupKey);
            bool clicked = false;
            float cursorX = innerX;

            for (int i = 0; i < orderedGroups.Count; i++)
            {
                PrefabBitGroup g = orderedGroups[i];
                if (g == null || g.TotalSyncedBits <= 0)
                {
                    continue;
                }

                float segW = innerW * (g.TotalSyncedBits / (float)denom);
                if (segW < 2f)
                {
                    segW = 2f;
                }
                if (cursorX + segW > innerX + innerW)
                {
                    segW = Mathf.Max(0f, innerX + innerW - cursorX);
                }
                if (segW <= 0f)
                {
                    break;
                }

                var segRect = new Rect(cursorX, innerY, segW, innerH);
                Color baseCol = GetGroupColor(g.ColorIndex);
                bool isSelected = hasSelection && selectedGroupKey == g.GroupKey;
                Color drawCol = (hasSelection && !isSelected)
                    ? new Color(baseCol.r, baseCol.g, baseCol.b, 0.35f)
                    : baseCol;

                EditorGUI.DrawRect(segRect, drawCol);
                if (segW > 3f)
                {
                    EditorGUI.DrawRect(new Rect(segRect.xMax - 1f, segRect.y, 1f, segRect.height), new Color(0f, 0f, 0f, 0.35f));
                }

                if (isSelected)
                {
                    DrawRectOutline(segRect, Color.white);
                }

                if (segW >= 30f)
                {
                    GUI.Label(segRect, $"{g.TotalSyncedBits}b", segLabelStyle);
                }

                float pctUsed = totalSyncedBits > 0 ? (g.TotalSyncedBits * 100f / totalSyncedBits) : 0f;
                float pctMax = g.TotalSyncedBits * 100f / maxCost;
                string tip = $"{g.DisplayName}\n{g.TotalSyncedBits} bit（使用分の {pctUsed:0.0}% / {maxCost} bit 枠の {pctMax:0.0}%）\nクリックで絞り込み＆Hierarchy 表示";
                if (GUI.Button(segRect, new GUIContent(string.Empty, tip), GUIStyle.none))
                {
                    clicked = true;
                    clickedGroupKey = g.GroupKey;
                    clickedRootObject = g.RootObject;
                }

                cursorX += segW;
            }

            if (totalSyncedBits < maxCost && cursorX < innerX + innerW)
            {
                var emptyRect = new Rect(cursorX, innerY, innerX + innerW - cursorX, innerH);
                int freeBits = maxCost - totalSyncedBits;
                GUI.Label(emptyRect, new GUIContent(string.Empty, $"空き容量: {freeBits} bit / {maxCost} bit"));
            }

            return clicked;
        }

        private static void DrawRectOutline(Rect r, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), color);
        }

        public static void DrawBitBar(BitProjection projection)
        {
            int maxCost = VRCExpressionParameters.MAX_PARAMETER_COST;
            float beforeRatio = Mathf.Clamp01(projection.BeforeBits / (float)maxCost);
            float afterRatio = Mathf.Clamp01(projection.AfterBits / (float)maxCost);

            Rect rect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
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

            bool compact = rect.width < 360f;
            string text;
            if (projection.AfterBits > maxCost)
            {
                int over = projection.AfterBits - maxCost;
                if (compact)
                {
                    text = $"{projection.AfterBits}/{maxCost}b (+{over}b)";
                }
                else
                {
                    text = projection.SavedBits > 0
                        ? $"同期 bit: {projection.BeforeBits} → {projection.AfterBits} / {maxCost} bit（+{over} bit 超過）"
                        : $"同期 bit: {projection.AfterBits} / {maxCost} bit（+{over} bit 超過）";
                }
            }
            else if (projection.SavedBits > 0)
            {
                int remaining = maxCost - projection.AfterBits;
                text = compact
                    ? $"{projection.BeforeBits}→{projection.AfterBits}/{maxCost}b (-{projection.SavedBits}b)"
                    : $"同期 bit: {projection.BeforeBits} → {projection.AfterBits} / {maxCost} bit（-{projection.SavedBits} bit 削減 / 残り {remaining} bit）";
            }
            else
            {
                int remaining = maxCost - projection.AfterBits;
                text = compact
                    ? $"{projection.AfterBits}/{maxCost}b (残{remaining}b)"
                    : $"同期 bit: {projection.AfterBits} / {maxCost} bit（残り {remaining} bit）";
            }

            if (projection.IsGloballyDisabled)
            {
                text += compact ? " [オフ]" : "（全体一時オフ中）";
            }

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            GUI.Label(rect, text, style);
        }

        public static void DrawParameterBitBar(int beforeBit, int afterBit, int maxScaleBit, bool isPaused)
        {
            int safeMax = Mathf.Max(1, maxScaleBit);
            float beforeRatio = Mathf.Clamp01(beforeBit / (float)safeMax);
            float afterRatio = Mathf.Clamp01(afterBit / (float)safeMax);

            Rect rect = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.14f, 1f));

            int saved = Mathf.Max(0, beforeBit - afterBit);
            if (!isPaused && saved > 0)
            {
                var beforeRect = new Rect(rect.x + 1, rect.y + 1, Mathf.Max(0f, (rect.width - 2f) * beforeRatio), rect.height - 2f);
                EditorGUI.DrawRect(beforeRect, new Color(0.35f, 0.72f, 0.48f, 0.28f));
            }

            Color fillColor = isPaused
                ? new Color(0.42f, 0.44f, 0.48f, 1f)
                : saved > 0
                    ? new Color(0.24f, 0.65f, 0.38f, 1f)
                    : new Color(0.34f, 0.50f, 0.68f, 1f);

            var fillRect = new Rect(rect.x + 1, rect.y + 1, Mathf.Max(0f, (rect.width - 2f) * afterRatio), rect.height - 2f);
            EditorGUI.DrawRect(fillRect, fillColor);

            bool compact = rect.width < 260f;
            string text = isPaused
                ? (compact ? $"{beforeBit}b (一時オフ)" : $"{beforeBit} bit（一時オフ中）")
                : saved > 0
                    ? (compact ? $"{beforeBit}→{afterBit}b (-{saved}b)" : $"{beforeBit} bit → {afterBit} bit（-{saved} bit 削減）")
                    : (compact ? $"{beforeBit}b (そのまま)" : $"{beforeBit} bit（そのまま）");

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            GUI.Label(rect, text, style);
        }
    }
}
