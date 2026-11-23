using UnityEngine;

namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// Modular Avatar 任意連携を疎結合に保つためのアダプタインタフェース。
    /// </summary>
    public interface IMaAdapter
    {
        /// <summary>
        /// アバター配下に ModularAvatarSyncParameterSequence が存在するか判定する。
        /// </summary>
        bool HasSyncParameterSequence(GameObject avatarRoot);
    }
}
