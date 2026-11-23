namespace Sebanne.ExParamManager
{
    /// <summary>
    /// ビルド時に各 Expression Parameter へ適用する非破壊操作種別。
    /// </summary>
    public enum ParameterAction
    {
        Keep = 0,
        Remove = 1,
        MakeLocal = 2,
        ResizeBitWidth = 3,
    }
}
