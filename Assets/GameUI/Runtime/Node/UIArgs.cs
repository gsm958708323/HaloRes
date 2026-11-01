using GameUI.Kernel;

namespace GameUI.Runtime
{
    public abstract class UIArgs<TView> : GameUI.Kernel.UIArgs where TView : UIView { }
    public abstract class UIArgs<TView, TResult> : UIArgs<TView> where TView : UIView { }

    public sealed class UIViewAttribute : System.Attribute
    {
        public UIMode Mode = UIMode.FullScreen;
        public UILayer Layer = UILayer.Window;
        public bool CoversWorld = true;
        public string Context;
        public UIOverlayVisibility Visibility = UIOverlayVisibility.Always;
        public string ContextTag;
        public UIBackBehavior Back = UIBackBehavior.Ignore;
        public UIMaskStyle Mask = UIMaskStyle.None;
        public bool CloseOnMaskClick;
        public bool KeepAlive;
        public UIInstancePolicy Instance = UIInstancePolicy.Single;
        public string PrefabPath;
    }
}
