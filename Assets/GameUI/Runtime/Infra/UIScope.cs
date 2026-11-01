using System;
using System.Collections.Generic;
using System.Threading;

namespace GameUI.Runtime
{
    public sealed class UIScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<Action> _disposables = new List<Action>();
        private bool _disposed;

        public CancellationToken Token { get { return _lifetime.Token; } }
        public bool IsDisposed { get { return _disposed; } }

        public T Add<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            Add(disposable.Dispose);
            return disposable;
        }

        public void Add(Action onDispose)
        {
            if (onDispose == null) throw new ArgumentNullException(nameof(onDispose));
            if (_disposed) throw new ObjectDisposedException(nameof(UIScope));
            _disposables.Add(onDispose);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                try { _disposables[i](); }
                catch (Exception error) { UnityEngine.Debug.LogException(error); }
            }
            _disposables.Clear();
            _lifetime.Dispose();
        }
    }

    internal sealed class UIScopeCycle
    {
        private readonly string _name;
        public UIScopeCycle(string name) { _name = name; }
        public UIScope Current { get; private set; }

        public void Begin()
        {
            if (Current != null) throw new InvalidOperationException("Scope cycle is already active: " + _name);
            Current = new UIScope();
        }

        public void End()
        {
            var current = Current;
            Current = null;
            current?.Dispose();
        }

        public UIScope RequireCurrent()
        {
            if (Current == null) throw new InvalidOperationException("Scope cycle is not active: " + _name);
            return Current;
        }
    }
}
