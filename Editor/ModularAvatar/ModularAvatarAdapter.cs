using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace Sebanne.ExParamManager.Editor.ModularAvatar
{
    /// <summary>
    /// Modular Avatar (>= 1.11.2) 向けの <see cref="IMaAdapter"/> 実装。
    /// </summary>
    [InitializeOnLoad]
    public sealed class ModularAvatarAdapter : IMaAdapter
    {
        static ModularAvatarAdapter()
        {
            MaAdapterRegistry.Register(new ModularAvatarAdapter());
        }

        public bool HasSyncParameterSequence(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return false;
            }

            return avatarRoot.GetComponentInChildren<ModularAvatarSyncParameterSequence>(true) != null;
        }

        public IReadOnlyList<MaMenuItemInfo> CollectMenuItems(GameObject avatarRoot)
        {
            var results = new List<MaMenuItemInfo>();
            if (avatarRoot == null)
            {
                return results;
            }

            ModularAvatarMenuItem[] items = avatarRoot.GetComponentsInChildren<ModularAvatarMenuItem>(true);
            for (int i = 0; i < items.Length; i++)
            {
                ModularAvatarMenuItem item = items[i];
                if (item == null)
                {
                    continue;
                }

                bool isExtract = item.GetComponentInParent<ModularAvatarMenuGroup>() != null
                    || item.GetComponent<ModularAvatarMenuGroup>() != null;

                var info = new MaMenuItemInfo
                {
                    Component = item,
                    GameObject = item.gameObject,
                    DisplayName = string.IsNullOrEmpty(item.label) ? item.gameObject.name : item.label,
                    IsExtractMenuObject = isExtract,
                };

                if (item.Control != null)
                {
                    info.ControlType = (int)item.Control.type;
                    info.ParameterName = item.Control.parameter != null ? (item.Control.parameter.name ?? string.Empty) : string.Empty;
                    info.Value = item.Control.value;
                    info.SubMenuAsset = item.Control.subMenu;

                    if (item.Control.subParameters != null)
                    {
                        for (int s = 0; s < item.Control.subParameters.Length; s++)
                        {
                            string sp = item.Control.subParameters[s]?.name;
                            if (!string.IsNullOrEmpty(sp))
                            {
                                info.SubParameters.Add(sp);
                            }
                        }
                    }
                }

                results.Add(info);
            }

            return results;
        }

        public IReadOnlyList<MaMenuInstallerInfo> CollectMenuInstallers(GameObject avatarRoot)
        {
            var results = new List<MaMenuInstallerInfo>();
            if (avatarRoot == null)
            {
                return results;
            }

            ModularAvatarMenuInstaller[] installers = avatarRoot.GetComponentsInChildren<ModularAvatarMenuInstaller>(true);
            for (int i = 0; i < installers.Length; i++)
            {
                ModularAvatarMenuInstaller inst = installers[i];
                if (inst == null)
                {
                    continue;
                }

                results.Add(new MaMenuInstallerInfo
                {
                    Component = inst,
                    GameObject = inst.gameObject,
                    MenuToAppend = inst.menuToAppend,
                    InstallTargetMenu = inst.installTargetMenu,
                });
            }

            return results;
        }

        public IReadOnlyList<MaMergeAnimatorInfo> CollectMergeAnimators(GameObject avatarRoot)
        {
            var results = new List<MaMergeAnimatorInfo>();
            if (avatarRoot == null)
            {
                return results;
            }

            ModularAvatarMergeAnimator[] mergers = avatarRoot.GetComponentsInChildren<ModularAvatarMergeAnimator>(true);
            for (int i = 0; i < mergers.Length; i++)
            {
                ModularAvatarMergeAnimator merger = mergers[i];
                if (merger == null || merger.animator == null)
                {
                    continue;
                }

                results.Add(new MaMergeAnimatorInfo
                {
                    Component = merger,
                    GameObject = merger.gameObject,
                    Animator = merger.animator,
                });
            }

            return results;
        }

        public IReadOnlyList<MaReactiveInfo> CollectReactiveComponents(GameObject avatarRoot)
        {
            var results = new List<MaReactiveInfo>();
            if (avatarRoot == null)
            {
                return results;
            }

            ReactiveComponent[] reactives = avatarRoot.GetComponentsInChildren<ReactiveComponent>(true);
            for (int i = 0; i < reactives.Length; i++)
            {
                ReactiveComponent rc = reactives[i];
                if (rc == null)
                {
                    continue;
                }

                ModularAvatarMenuItem linkedMenu = rc.GetComponent<ModularAvatarMenuItem>()
                    ?? rc.GetComponentInParent<ModularAvatarMenuItem>();
                string paramName = linkedMenu != null && linkedMenu.Control != null && linkedMenu.Control.parameter != null
                    ? (linkedMenu.Control.parameter.name ?? string.Empty)
                    : string.Empty;

                results.Add(new MaReactiveInfo
                {
                    Component = rc,
                    GameObject = rc.gameObject,
                    ComponentTypeName = rc.GetType().Name,
                    LinkedMenuParameterName = paramName,
                });
            }

            return results;
        }
    }
}
