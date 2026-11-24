using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using UnityEngine;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// NDMF Build Console（ErrorReport）へ日本語固定文字列を出力する SimpleError 派生。
    /// </summary>
    internal sealed class ExParamNdmfError : SimpleError
    {
        private static readonly Localizer EmptyLocalizer =
            new Localizer("en-US", () => new List<(string, Func<string, string>)>());

        private readonly ErrorSeverity _severity;
        private readonly string _title;
        private readonly string _details;

        public ExParamNdmfError(ErrorSeverity severity, string title, string details)
        {
            _severity = severity;
            _title = title ?? string.Empty;
            _details = details ?? string.Empty;
        }

        public override Localizer Localizer => EmptyLocalizer;
        public override string TitleKey => "exparam-manager.ndmf.console";
        public override ErrorSeverity Severity => _severity;
        public override string FormatTitle() => _title;
        public override string FormatDetails() => string.IsNullOrEmpty(_details) ? null : _details;
        public override string FormatHint() => null;

        public static void SafeReport(
            BuildContext context,
            ErrorSeverity severity,
            string title,
            string details,
            params UnityEngine.Object[] references)
        {
            var err = new ExParamNdmfError(severity, title, details);
            context?.GetState<ExParamBuildState>().RecordedReports.Add((severity, title));

            if (references != null)
            {
                for (int i = 0; i < references.Length; i++)
                {
                    if (references[i] != null)
                    {
                        err.AddReference(ObjectRegistry.GetReference(references[i]));
                    }
                }
            }

            ErrorReport.ReportError(err);
        }
    }
}
