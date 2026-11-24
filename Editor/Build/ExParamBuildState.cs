using System.Collections.Generic;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// Pass 1（Transforming）から Pass 2（Optimizing）へ引き渡す退避状態。
    /// <see cref="ExParamManagerComponent"/> が INDMFEditorOnly で破棄された後も安全に動作する。
    /// </summary>
    public sealed class ExParamBuildState
    {
        public bool ShouldRun;
        public bool AbortedByDuplicateComponents;
        public bool PruneEmptySubMenus = true;
        public bool AllowOverrideWithSyncSequence;
        public bool VerboseLogging;
        public bool HasSyncParameterSequence;
        public int SkippedInternalRuleCount;
        public readonly List<ParameterRule> ResolvedRules = new List<ParameterRule>();
        public readonly List<(nadena.dev.ndmf.ErrorSeverity Severity, string Title)> RecordedReports =
            new List<(nadena.dev.ndmf.ErrorSeverity, string)>();
    }
}
