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
    }
}
