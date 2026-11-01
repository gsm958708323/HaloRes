namespace GameUI.Runtime
{
    internal interface IUINodeChild
    {
        void HostOpen();
        void HostVisibilityChanged(bool visible);
        void HostRefresh();
        void HostClose();
        void HostDestroy();
        bool HostBack();
    }
}
