using GameUI.Components;
using GameUI.Kernel;
using GameUI.Runtime;

namespace GameUI.Demo
{
    [UIView(Mode=UIMode.FullScreen,CoversWorld=true,Context="Home",KeepAlive=true,PrefabPath="UI/Demo/Home")]
    public sealed partial class DemoHomeView : UIView
    {
        protected override void OnCreate()
        {
            _OpenPopup.onClick.AddListener(OpenPopup); InstanceScope.Add(()=>_OpenPopup.onClick.RemoveListener(OpenPopup));
            _OpenOverlay.onClick.AddListener(OpenOverlay); InstanceScope.Add(()=>_OpenOverlay.onClick.RemoveListener(OpenOverlay));
            _OpenWidgets.onClick.AddListener(OpenWidgets); InstanceScope.Add(()=>_OpenWidgets.onClick.RemoveListener(OpenWidgets));
            _OpenSystem.onClick.AddListener(OpenSystem); InstanceScope.Add(()=>_OpenSystem.onClick.RemoveListener(OpenSystem));
            _OpenConfirm.onClick.AddListener(OpenConfirm); InstanceScope.Add(()=>_OpenConfirm.onClick.RemoveListener(OpenConfirm));
        }
        private void OpenPopup(){UI.Open<DemoPopupView>();}
        private void OpenOverlay(){UI.Open<DemoOverlayView>();}
        private void OpenWidgets(){UI.Open<DemoWidgetsView>();}
        private void OpenSystem(){UI.Open<DemoSystemView>();}
        private void OpenConfirm(){UI.Open<DemoConfirmView,bool>(new DemoConfirmArgs{Message="Confirm action"});}
    }

    [UIView(Mode=UIMode.Popup,CoversWorld=false,Mask=UIMaskStyle.Dim,CloseOnMaskClick=true,KeepAlive=true,PrefabPath="UI/Demo/Popup")]
    public sealed partial class DemoPopupView : UIView
    {
        protected override void OnCreate(){_ClosePopup.onClick.AddListener(CloseSelf);InstanceScope.Add(()=>_ClosePopup.onClick.RemoveListener(CloseSelf));}
    }

    [UIView(Mode=UIMode.Overlay,Layer=UILayer.Tip,CoversWorld=false,Back=UIBackBehavior.Close,PrefabPath="UI/Demo/Overlay")]
    public sealed partial class DemoOverlayView : UIView
    {
        protected override void OnCreate(){_CloseOverlay.onClick.AddListener(CloseSelf);InstanceScope.Add(()=>_CloseOverlay.onClick.RemoveListener(CloseSelf));}
    }

    [UIView(Mode=UIMode.Overlay,Layer=UILayer.System,CoversWorld=false,PrefabPath="UI/Demo/System")]
    public sealed partial class DemoSystemView : UIView
    {
        protected override void OnCreate(){_CloseSystem.onClick.AddListener(CloseSelf);InstanceScope.Add(()=>_CloseSystem.onClick.RemoveListener(CloseSelf));}
    }

    public sealed class DemoConfirmArgs : UIArgs<DemoConfirmView,bool> { public string Message; }
    [UIView(Mode=UIMode.Popup,CoversWorld=false,Mask=UIMaskStyle.Dim,PrefabPath="UI/Demo/Confirm")]
    public sealed partial class DemoConfirmView : UIView<DemoConfirmArgs,bool>
    {
        protected override void OnCreate(){_Confirm.onClick.AddListener(Confirm);InstanceScope.Add(()=>_Confirm.onClick.RemoveListener(Confirm));}
        protected override void OnOpen(DemoConfirmArgs args){_Message.text=args?.Message ?? "Confirm";}
        private void Confirm(){Finish(true);}
    }

    [UIView(Mode=UIMode.FullScreen,CoversWorld=true,Context="Widgets",PrefabPath="UI/Demo/Widgets")]
    public sealed partial class DemoWidgetsView : UIView
    {
        private UILoopList<DemoListItem,string> _items;
        protected override void OnCreate()
        {
            _items=new UILoopList<DemoListItem,string>(this,_List,_ItemTemplate,()=>new DemoListItem());
            _CloseWidgets.onClick.AddListener(CloseSelf);InstanceScope.Add(()=>_CloseWidgets.onClick.RemoveListener(CloseSelf));
        }
        protected override void OnOpen()
        {
            var data=new string[1000];for(var i=0;i<data.Length;i++)data[i]="Item " + i;
            _items.SetItems(data);
            CreateModelStage(_Model);
        }
    }
    public sealed class DemoListItem : UIItem<string>
    {
        private UnityEngine.UI.Text _label;
        protected override void OnCreate(){_label=GameObject.GetComponentInChildren<UnityEngine.UI.Text>(true);}
        protected override void OnBind(string data){_label.text=data;}
    }
}
