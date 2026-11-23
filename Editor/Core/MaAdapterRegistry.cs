namespace Sebanne.ExParamManager.Editor
{
    /// <summary>
    /// Modular Avatar 連携アダプタの登録先。
    /// MA 導入環境では ModularAvatar アセンブリ側の [InitializeOnLoad] で自動登録される。
    /// </summary>
    public static class MaAdapterRegistry
    {
        public static IMaAdapter Current { get; private set; }

        public static bool IsAvailable => Current != null;

        public static void Register(IMaAdapter adapter)
        {
            Current = adapter;
        }
    }
}
