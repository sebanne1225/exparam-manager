using System;
using UnityEngine;

namespace Sebanne.ExParamManager
{
    /// <summary>
    /// パラメータ 1 件ごとの削減ルール定義。
    /// 発生源の主キーは <see cref="sourceComponent"/> とし、NDMF クローンや MA 階層変更に追従する。
    /// </summary>
    [Serializable]
    public sealed class ParameterRule
    {
        public const int MinBitWidth = 2;
        public const int MaxBitWidth = 8;

        public string parameterName = string.Empty;
        public Component sourceComponent;
        public string sourceObjectPath = string.Empty;
        public bool isInternalParameter;
        public string originalName = string.Empty;
        public ParameterAction action = ParameterAction.Keep;
        public int targetBitWidth = MaxBitWidth;
        public bool hideMenuControlsOnRemove = true;
        public bool disabled;

        public bool IsEnabled => !disabled;
        public bool IsActive => !disabled && action != ParameterAction.Keep;

        /// <summary>
        /// 有効範囲（2..8 bit）にクランプした目標 bit 幅を返す。
        /// </summary>
        public int GetClampedBitWidth()
        {
            return Mathf.Clamp(targetBitWidth, MinBitWidth, MaxBitWidth);
        }

        /// <summary>
        /// Pass 1 での BuildState 退避用に全フィールドを複製する。
        /// </summary>
        public ParameterRule Clone()
        {
            return new ParameterRule
            {
                parameterName = parameterName,
                sourceComponent = sourceComponent,
                sourceObjectPath = sourceObjectPath,
                isInternalParameter = isInternalParameter,
                originalName = originalName,
                action = action,
                targetBitWidth = targetBitWidth,
                hideMenuControlsOnRemove = hideMenuControlsOnRemove,
                disabled = disabled,
            };
        }
    }
}
