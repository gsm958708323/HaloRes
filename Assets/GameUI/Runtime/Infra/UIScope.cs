using System;
using System.Collections.Generic;
using System.Threading;

namespace GameUI.Runtime
{
    public sealed class UIScope : IDisposable
    {
        private readonly List<Action> _disposables = new List<Action>();
        private CancellationTokenSource _lifetime;
        private bool _disposed;

        public CancellationToken Token
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(UIScope));
                if (_lifetime == null) _lifetime = new CancellationTokenSource();
                return _lifetime.Token;
            }
        }
        public bool IsDisposed { get { return _disposed; } }
        public int RegistrationCount { get { return _disposables.Count; } }

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
            if (_lifetime != null)
                try { _lifetime.Cancel(); } catch (Exception error) { UnityEngine.Debug.LogException(error); }
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                try { _disposables[i](); }
                catch (Exception error) { UnityEngine.Debug.LogException(error); }
            }
            _disposables.Clear();
            _lifetime?.Dispose();
            _lifetime = null;
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
