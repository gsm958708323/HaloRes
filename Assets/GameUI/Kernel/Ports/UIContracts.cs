using System;
using System.Collections.Generic;

namespace GameUI.Kernel
{
    public enum UIMode { FullScreen, Popup, Overlay }
    public enum UILayer { Background, Window, Info, Guide, Top, Tip, Drag, System, Blocker, Letterbox }
    public enum UIOverlayVisibility { Always, WhenWorldVisible, WithContext }
    public enum UIBackBehavior { Ignore, Consume, Close }
    public enum UIMaskStyle { None, Transparent, Dim }
    public enum UIInstancePolicy { Single, Multiple }
    public enum UIOpenPolicy { Push, Replace, Reset }
    public enum UIPhase { Pending, Loading, Hidden, Entering, Shown, Exiting, Closed }
    public enum UITarget { Absent, Hidden, Shown }
    public enum UICloseReason { Normal, Cancelled, Failed, Replaced, Reset }
    public enum UIExitIntent { Normal, Replaced, Reset }
    public enum UILogLevel { Debug, Info, Warning, Error }

    public abstract class UIArgs { }

    public sealed class UIViewInfo
    {
        public string Id;
        public Type ViewType;
        public Type ArgsType;
        public Type ResultType;
        public UIMode Mode;
        public UILayer Layer;
        public bool CoversWorld;
        public string Context;
        public UIOverlayVisibility Visibility;
        public string ContextTag;
        public UIBackBehavior BackBehavior;
        public UIMaskStyle MaskStyle;
        public bool CloseOnMaskClick;
        public bool KeepAlive;
        public UIInstancePolicy InstancePolicy;
        public string PrefabPath;
        public Func<IUIViewLogic> Factory;

        public bool IsOverlay { get { return Mode == UIMode.Overlay; } }
        public bool IsSingle { get { return InstancePolicy != UIInstancePolicy.Multiple; } }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new InvalidOperationException("UIViewInfo.Id is required.");
            if (ViewType == null) throw new InvalidOperationException("UIViewInfo.ViewType is required for " + Id + ".");
            if (Factory == null) throw new InvalidOperationException("UIViewInfo.Factory is required for " + Id + ".");
            if (IsOverlay && Layer == UILayer.Window) throw new InvalidOperationException("Overlay " + Id + " must specify a non-Window layer.");
            if (!IsOverlay && Layer != UILayer.Window) throw new InvalidOperationException("Navigation view " + Id + " must use the Window layer.");
        }
    }

    public interface IUIViewRegistry
    {
        UIViewInfo Get(Type viewType);
        UIViewInfo Get(string id);
    }

    public interface IUILoadOp<T>
    {
        bool IsDone { get; }
        T Result { get; }
        Exception Error { get; }
        void Cancel();
    }

    public interface IUIViewSource
    {
        IUILoadOp<IUIViewInstance> Acquire(UIViewInfo info);
        void Return(IUIViewInstance instance, bool reusable);
    }

    public interface IUIViewInstance
    {
        IUIViewLogic Logic { get; }
        bool HandoffRequested { get; }
        void SetBaseOrder(int baseOrder);
        void SetVisible(bool on);
        void BeginEnter();
        void BeginExit();
        bool IsTransitionDone { get; }
        void CompleteTransition();
    }

    public interface IUIViewLogic
    {
        void BindSession(IUINavigator navigator, int entryId);
        void Open(UIArgs args);
        void Resume();
        void Pause();
        void Close();
        bool Back();
        void ContextChanged(string context);
    }

    public interface IUIPresentationSink
    {
        void Apply(in UIPresentation presentation);
    }

    public interface IUIClock
    {
        double Now { get; }
    }

    public interface IUILogSink
    {
        void Log(UILogLevel level, string message);
    }

    public readonly struct UIResultBox
    {
        public UIResultBox(bool hasValue, object value, UICloseReason reason)
        {
            HasValue = hasValue;
            Value = value;
            Reason = reason;
        }

        public bool HasValue { get; }
        public object Value { get; }
        public UICloseReason Reason { get; }
    }

    public readonly struct UIResult<T>
    {
        public UIResult(bool hasValue, T value, UICloseReason reason)
        {
            HasValue = hasValue;
            Value = value;
            Reason = reason;
        }

        public bool HasValue { get; }
        public T Value { get; }
        public UICloseReason Reason { get; }
    }

    public readonly struct UIOpenResult
    {
        public UIOpenResult(bool succeeded, UICloseReason reason)
        {
            Succeeded = succeeded;
            Reason = reason;
        }

        public bool Succeeded { get; }
        public UICloseReason Reason { get; }
    }

    public readonly struct UIPresentation
    {
        public UIPresentation(
            bool worldCovered,
            string topContext,
            bool inputLocked,
            bool loadingIndicator,
            bool maskVisible,
            int maskOrder,
            UIMaskStyle maskStyle,
            int maskOwnerId,
            bool systemOverlayVisible,
            int systemOverlayOrder)
        {
            WorldCovered = worldCovered;
            TopContext = topContext;
            InputLocked = inputLocked;
            LoadingIndicator = loadingIndicator;
            MaskVisible = maskVisible;
            MaskOrder = maskOrder;
            MaskStyle = maskStyle;
            MaskOwnerId = maskOwnerId;
            SystemOverlayVisible = systemOverlayVisible;
            SystemOverlayOrder = systemOverlayOrder;
        }

        public bool WorldCovered { get; }
        public string TopContext { get; }
        public bool InputLocked { get; }
        public bool LoadingIndicator { get; }
        public bool MaskVisible { get; }
        public int MaskOrder { get; }
        public UIMaskStyle MaskStyle { get; }
        public int MaskOwnerId { get; }
        public bool SystemOverlayVisible { get; }
        public int SystemOverlayOrder { get; }
    }
}
