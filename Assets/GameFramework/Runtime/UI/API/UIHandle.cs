using System;
using System.Threading.Tasks;

namespace GameFramework.UI
{
    public enum UINavigationState { Loading, Ready, Open, Closed, Failed }

    /// <summary>一次打开访问的公开句柄；重复访问可共享同一视图实例。</summary>
    public class UIHandle
    {
        private readonly TaskCompletionSource<UIGameView> _opened = new TaskCompletionSource<UIGameView>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal UIRuntime Owner;
        internal UIHandle(UIRuntime owner, long id, Type type) { Owner = owner; Id = id; ViewType = type; }
        public long Id { get; }
        public Type ViewType { get; }
        public UINavigationState State { get; internal set; }
        public Task<UIGameView> Opened => _opened.Task;
        public Task Closed => _closed.Task;
        public bool IsClosed => State == UINavigationState.Closed || State == UINavigationState.Failed;
        public void Close() => Owner?.Close(this);
        internal void CompleteOpen(UIGameView view) => _opened.TrySetResult(view);
        internal void CompleteClose(Exception error = null)
        {
            State = error == null ? UINavigationState.Closed : UINavigationState.Failed;
            if (error == null || error is OperationCanceledException) _opened.TrySetCanceled();
            else _opened.TrySetException(error);
            Owner = null;
            _closed.TrySetResult(true);
        }
    }

    public sealed class UIHandle<T> : UIHandle where T : UIGameView
    {
        internal UIHandle(UIRuntime owner, long id) : base(owner, id, typeof(T)) { Opened = Cast(base.Opened); }
        public new Task<T> Opened { get; }
        private static async Task<T> Cast(Task<UIGameView> task) => (T)await task;
    }
}
