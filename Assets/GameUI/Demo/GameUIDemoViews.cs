using GameUI.Kernel;
using GameUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Demo
{
    [UIView(Mode = UIMode.FullScreen, CoversWorld = true, Context = "Home", PrefabPath = "UI/Demo/Home")]
    public sealed class DemoHomeView : UIView
    {
        protected override void OnCreate()
        {
            var button = GameObject.transform.Find("OpenPopup")?.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(OpenPopup);
            var overlay = GameObject.transform.Find("OpenOverlay")?.GetComponent<Button>();
            if (overlay != null) overlay.onClick.AddListener(OpenOverlay);
        }

        private void OpenPopup() { UI.Open<DemoPopupView>(UIOpenPolicy.Push); }
        private void OpenOverlay() { UI.Open<DemoOverlayView>(UIOpenPolicy.Push); }

        protected override void OnOpen() { Invalidate(); }
    }

    [UIView(Mode = UIMode.Popup, CoversWorld = false, Mask = UIMaskStyle.Dim, CloseOnMaskClick = true, PrefabPath = "UI/Demo/Popup")]
    public sealed class DemoPopupView : UIView
    {
        protected override void OnCreate()
        {
            var close = GameObject.transform.Find("ClosePopup")?.GetComponent<Button>();
            if (close != null) close.onClick.AddListener(CloseSelf);
        }
    }

    [UIView(Mode = UIMode.Overlay, Layer = UILayer.Tip, Visibility = UIOverlayVisibility.Always, Back = UIBackBehavior.Close, PrefabPath = "UI/Demo/Overlay")]
    public sealed class DemoOverlayView : UIView
    {
        protected override void OnCreate()
        {
            var close = GameObject.transform.Find("CloseOverlay")?.GetComponent<Button>();
            if (close != null) close.onClick.AddListener(CloseSelf);
        }
    }
}
