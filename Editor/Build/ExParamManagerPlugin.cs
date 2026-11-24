using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(Sebanne.ExParamManager.Editor.ExParamManagerPlugin))]

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// ExParam Manager の NDMF プラグイン定義。
    /// Pass 1（Transforming）で設定とルールを退避し、Pass 2（Optimizing）で非破壊削減を行う。
    /// </summary>
    public sealed class ExParamManagerPlugin : Plugin<ExParamManagerPlugin>
    {
        public override string DisplayName => "ExParam Manager";
        public override string QualifiedName => "com.sebanne.exparam-manager";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .BeforePlugin("nadena.dev.modular-avatar.late-transform-stages")
                .Run("ExParam Manager: Capture Build State", ExParamBuildProcessor.RunPass1);

            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("ExParam Manager: Apply Parameter Rules", ExParamBuildProcessor.RunPass2);
        }
    }
}
