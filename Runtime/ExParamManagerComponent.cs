using System.Collections.Generic;
#if HAS_NDMF_EDITOR_ONLY
using nadena.dev.ndmf;
#endif
#if HAS_VRCSDK_BASE
using VRC.SDKBase;
#endif
using UnityEngine;

namespace Sebanne.ExParamManager
{
    /// <summary>
    /// アバターに 1 つ配置する ExParam Manager の非破壊設定コンポーネント。
    /// エディタ専用コンポーネント（IEditorOnly / INDMFEditorOnly）として、ビルド時・アップロード検証対象から自動除外される。
    /// </summary>
    [AddComponentMenu("ExParam Manager/ExParam Manager")]
    [DisallowMultipleComponent]
    public sealed class ExParamManagerComponent : MonoBehaviour
#if HAS_NDMF_EDITOR_ONLY
        , INDMFEditorOnly
#elif HAS_VRCSDK_BASE
        , IEditorOnly
#endif
    {
        public bool enabledOnBuild = true;
        public bool pruneEmptySubMenus = true;
        public bool allowOverrideWithSyncSequence;
        public bool verboseLogging;
        public List<ParameterRule> rules = new List<ParameterRule>();

        /// <summary>
        /// <paramref name="sourceComponent"/> を優先キーとし、一致しなければ通常パラメータ名でルールを検索する。
        /// </summary>
        public ParameterRule FindRule(string effectiveName, Component sourceComponent = null)
        {
            if (rules == null)
            {
                return null;
            }

            if (sourceComponent != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    ParameterRule rule = rules[i];
                    if (rule != null && rule.sourceComponent == sourceComponent && rule.parameterName == effectiveName)
                    {
                        return rule;
                    }
                }

                for (int i = 0; i < rules.Count; i++)
                {
                    ParameterRule rule = rules[i];
                    if (rule != null && rule.sourceComponent == sourceComponent)
                    {
                        return rule;
                    }
                }
            }

            if (!string.IsNullOrEmpty(effectiveName))
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    ParameterRule rule = rules[i];
                    if (rule == null || rule.isInternalParameter || rule.parameterName != effectiveName)
                    {
                        continue;
                    }

                    // sourceComponent 指定時は、別コンポーネント所有のルールへ誤フォールバックしない
                    if (sourceComponent == null || rule.sourceComponent == null)
                    {
                        return rule;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 既存ルールを返すか、無ければ新規作成して <see cref="rules"/> へ追加する。
        /// </summary>
        public ParameterRule GetOrCreateRule(
            string effectiveName,
            Component sourceComponent = null,
            string sourceObjectPath = "",
            bool isInternal = false,
            string originalName = "")
        {
            if (rules == null)
            {
                rules = new List<ParameterRule>();
            }

            ParameterRule existing = FindRule(effectiveName, sourceComponent);
            if (existing != null)
            {
                return existing;
            }

            var created = new ParameterRule
            {
                parameterName = effectiveName ?? string.Empty,
                sourceComponent = sourceComponent,
                sourceObjectPath = sourceObjectPath ?? string.Empty,
                isInternalParameter = isInternal,
                originalName = originalName ?? string.Empty,
            };
            rules.Add(created);
            return created;
        }

        private void OnValidate()
        {
            if (rules == null)
            {
                rules = new List<ParameterRule>();
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                ParameterRule rule = rules[i];
                if (rule != null)
                {
                    rule.targetBitWidth = rule.GetClampedBitWidth();
                }
            }
        }
    }
}
