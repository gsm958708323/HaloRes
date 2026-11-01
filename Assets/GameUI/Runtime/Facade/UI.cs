using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameUI.Kernel;

namespace GameUI.Runtime
{
    public static class UI
    {
        private static UIKernel _kernel;
        private static UICameraRig _camera;

        public static UICameraRig Camera { get { return _camera; } }
        public static UIInputGate Input { get { return RequireKernel().Input; } }
        public static event Action<UIChange> Changed;
        public static event Action BackAtRoot;

        internal static void Bind(UIKernel kernel, UICameraRig camera)
        {
            Unbind();
            _kernel = kernel;
            _camera = camera;
            _kernel.Changed += OnChanged;
            _kernel.BackAtRoot += OnBackAtRoot;
        }

        internal static void Unbind()
        {
            if (_kernel != null)
            {
                _kernel.Changed -= OnChanged;
                _kernel.BackAtRoot -= OnBackAtRoot;
            }
            _kernel = null;
            _camera = null;
        }

        public static UIHandle Open<TView>(UIOpenPolicy policy = UIOpenPolicy.Push) where TView : UIView
        { return RequireKernel().Navigator.Open(typeof(TView), null, policy); }

        public static UIHandle Open<TView>(UIArgs<TView> args, UIOpenPolicy policy = UIOpenPolicy.Push) where TView : UIView
        { return RequireKernel().Navigator.Open(typeof(TView), args, policy); }

        public static UIHandle<TResult> Open<TView, TResult>(UIArgs<TView, TResult> args) where TView : UIView
        {
            return new UIHandle<TResult>(RequireKernel().Navigator.Open(typeof(TView), args, UIOpenPolicy.Push));
        }

        public static UIHandle OpenById(string id, GameUI.Kernel.UIArgs args = null, UIOpenPolicy policy = UIOpenPolicy.Push)
        { return RequireKernel().Navigator.OpenById(id, args, policy); }

        public static bool Back() { return RequireKernel().Navigator.Back(); }
        public static bool IsOpen<TView>() where TView : UIView { return RequireKernel().Navigator.IsOpen(typeof(TView)); }
        public static IDisposable Batch() { return RequireKernel().Navigator.Batch(); }
        public static string Dump() { return RequireKernel().Dump(); }

        public static async UniTask Preload<TView>() where TView : UIView
        {
            var handle = RequireKernel().Navigator.Open(typeof(TView), null, UIOpenPolicy.Push);
            handle.Close();
            await handle.WaitClosed();
        }

        private static UIKernel RequireKernel()
        {
            if (_kernel == null) throw new InvalidOperationException("UIBoot.Install must be called first.");
            return _kernel;
        }

        private static void OnChanged(UIChange change) { Changed?.Invoke(change); }
        private static void OnBackAtRoot() { BackAtRoot?.Invoke(); }
    }
}
