using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameUI.Kernel
{
    public readonly struct UIHandle
    {
        private readonly UIKernel _kernel;
        private readonly UIEntry _entry;

        internal UIHandle(UIKernel kernel, UIEntry entry) { _kernel = kernel; _entry = entry; }

        public int EntryId { get { return _entry == null ? 0 : _entry.Id; } }
        public bool IsAlive { get { return _entry != null && _entry.Act.Phase != UIPhase.Closed; } }
        public UIPhase Phase { get { return _entry == null ? UIPhase.Closed : _entry.Act.Phase; } }

        public UniTask<UIOpenResult> WaitOpened(CancellationToken ct = default)
        {
            return _entry == null ? UniTask.FromResult(new UIOpenResult(false, UICloseReason.Cancelled))
                : _entry.Opened.Task.AttachExternalCancellation(ct);
        }

        public UniTask<UICloseReason> WaitClosed(CancellationToken ct = default)
        {
            return _entry == null ? UniTask.FromResult(UICloseReason.Cancelled) : _entry.Closed.Task.AttachExternalCancellation(ct);
        }

        internal UniTask<UIResultBox> WaitResult(CancellationToken ct)
        {
            return _entry == null ? UniTask.FromResult(new UIResultBox(false, null, UICloseReason.Cancelled))
                : _entry.Result.Task.AttachExternalCancellation(ct);
        }

        public void Close() { _kernel?.Navigator.Close(EntryId); }
    }

    public readonly struct UIHandle<TResult>
    {
        public UIHandle(UIHandle raw) { Raw = raw; }
        public UIHandle Raw { get; }

        public async UniTask<UIResult<TResult>> WaitResult(CancellationToken ct = default)
        {
            var result = await Raw.WaitResult(ct);
            return new UIResult<TResult>(result.HasValue, result.HasValue ? (TResult)result.Value : default, result.Reason);
        }
    }
}
