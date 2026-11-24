using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.ExParamManager.Editor
{
    public sealed class MaMenuItemInfo
    {
        public Component Component;
        public GameObject GameObject;
        public string DisplayName = string.Empty;
        public int ControlType;
        public string ParameterName = string.Empty;
        public float Value;
        public readonly List<string> SubParameters = new List<string>();
        public VRCExpressionsMenu SubMenuAsset;
        public bool IsExtractMenuObject;
    }

    public sealed class MaMenuInstallerInfo
    {
        public Component Component;
        public GameObject GameObject;
        public VRCExpressionsMenu MenuToAppend;
        public VRCExpressionsMenu InstallTargetMenu;
    }

    public sealed class MaMergeAnimatorInfo
    {
        public Component Component;
        public GameObject GameObject;
        public RuntimeAnimatorController Animator;
    }

    public sealed class MaReactiveInfo
    {
        public Component Component;
        public GameObject GameObject;
        public string ComponentTypeName = string.Empty;
        public string LinkedMenuParameterName = string.Empty;
    }

    /// <summary>
    /// Modular Avatar 任意連携を疎結合に保つためのアダプタインタフェース。
    /// </summary>
    public interface IMaAdapter
    {
        bool HasSyncParameterSequence(GameObject avatarRoot);
        IReadOnlyList<MaMenuItemInfo> CollectMenuItems(GameObject avatarRoot);
        IReadOnlyList<MaMenuInstallerInfo> CollectMenuInstallers(GameObject avatarRoot);
        IReadOnlyList<MaMergeAnimatorInfo> CollectMergeAnimators(GameObject avatarRoot);
        IReadOnlyList<MaReactiveInfo> CollectReactiveComponents(GameObject avatarRoot);
    }
}
