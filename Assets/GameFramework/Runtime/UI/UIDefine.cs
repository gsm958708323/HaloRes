namespace GameFramework.UI
{
    public static class UIDefine
    {
        public const int Width = 1920;
        public const int Height = 1080;
    }

    public enum LayerType
    {
        SceneLayer = 1000,
        BackgroundLayer = 2000,
        NormalLayer = 3000,
        InfoLayer = 4000,
        TopLayer = 5000,
        TipsLayer = 6000,
        BlackMaskLayer = 7000
    }

    public enum ResType { Dynamic, Static }
    public enum UIState { None, Loading, Awake, Start, Enable, Disable, Destroy, Release }
}
