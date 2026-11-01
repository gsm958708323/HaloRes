namespace GameUI.Runtime
{
    public interface IUINodeChild
    {
        void HostOpen();
        void HostVisibilityChanged(bool visible);
        void HostRefresh();
        void HostClose();
        void HostDestroy();
        bool HostBack();
    }
}
