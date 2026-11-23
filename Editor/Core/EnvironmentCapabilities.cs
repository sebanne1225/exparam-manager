using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// 実行環境の SDK / NDMF バージョン定義および Dynamics DLL 参照状態を返す静的ヘルパ。
    /// </summary>
    public static class EnvironmentCapabilities
    {
        public static bool HasVrcSdk3106
        {
            get
            {
#if HAS_VRCSDK3_AVATARS_3_10_6
                return true;
#else
                return false;
#endif
            }
        }

        public static bool HasNdmf115
        {
            get
            {
#if HAS_NDMF_1_15
                return true;
#else
                return false;
#endif
            }
        }

        public static bool SupportsDynamicsTypes =>
            typeof(VRCPhysBone) != null && typeof(VRCContactReceiver) != null;
    }
}
