using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameUI.Kernel;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIViewPool : IUIViewSource, IDisposable
    {
        private readonly IUIAssetService _assets;
        private readonly Transform _parent;
        private readonly Dictionary<string, Stack<UGUIViewInstance>> _free = new Dictionary<string, Stack<UGUIViewInstance>>(StringComparer.Ordinal);
        private readonly HashSet<UGUIViewInstance> _active = new HashSet<UGUIViewInstance>();
        private bool _disposed;

        public UIViewPool(IUIAssetService assets, Transform parent)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _parent = parent;
        }

        public IUILoadOp<IUIViewInstance> Acquire(UIViewInfo info)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIViewPool));
            Stack<UGUIViewInstance> pool;
            if (_free.TryGetValue(info.Id, out pool) && pool.Count > 0)
            {
                var cached = pool.Pop();
                _active.Add(cached);
                cached.PrepareForPool();
                return new ImmediateLoadOp<IUIViewInstance>(cached);
            }

            var op = new PoolLoadOp(this, info);
            op.Start();
            return op;
        }

        public void Return(IUIViewInstance instance, bool reusable)
        {
            var concrete = instance as UGUIViewInstance;
            if (concrete == null) return;
            _active.Remove(concrete);
            if (_disposed || !reusable)
            {
                concrete.DestroyForPool();
                return;
            }
            concrete.PrepareForPool();
            Stack<UGUIViewInstance> pool;
            if (!_free.TryGetValue(FindId(concrete), out pool))
            {
                pool = new Stack<UGUIViewInstance>();
                _free.Add(FindId(concrete), pool);
            }
            pool.Push(concrete);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pair in _free) while (pair.Value.Count > 0) pair.Value.Pop().DestroyForPool();
            foreach (var active in new List<UGUIViewInstance>(_active)) active.DestroyForPool();
            _free.Clear();
            _active.Clear();
        }

        private string FindId(UGUIViewInstance instance) { return instance.ViewId; }

        private async UniTask<UGUIViewInstance> BuildAsync(UIViewInfo info, CancellationToken ct)
        {
            var load = _assets.LoadPrefab(info.PrefabPath);
            try
            {
                while (!load.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                if (load.Error != null) throw load.Error;
                var handle = load.Result;
                if (handle == null || !(handle.Asset is GameObject prefab)) throw new InvalidOperationException("UI prefab is not a GameObject: " + info.PrefabPath);
                var instanceObject = UnityEngine.Object.Instantiate(prefab, _parent);
                instanceObject.name = prefab.name + "[UI]";
                var logic = info.Factory();
                if (logic == null) throw new InvalidOperationException("UIView factory returned null: " + info.Id);
                if (logic is UIElement element) element.Initialize(instanceObject);
                var instance = new UGUIViewInstance(info.Id, instanceObject, logic, handle);
                _active.Add(instance);
                return instance;
            }
            catch
            {
                load.Cancel();
                if (load.IsDone && load.Result != null) load.Result.Release();
                throw;
            }
        }

        private sealed class PoolLoadOp : IUILoadOp<IUIViewInstance>
        {
            private readonly UIViewPool _owner;
            private readonly UIViewInfo _info;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private IUIViewInstance _result;
            private Exception _error;
            private bool _done;

            internal PoolLoadOp(UIViewPool owner, UIViewInfo info) { _owner = owner; _info = info; }
            public bool IsDone { get { return _done; } }
            public IUIViewInstance Result { get { return _result; } }
            public Exception Error { get { return _error; } }
            public void Cancel() { if (!_done) _cts.Cancel(); }
            internal void Start() { Run().Forget(); }

            private async UniTaskVoid Run()
            {
                try { _result = await _owner.BuildAsync(_info, _cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception error) { _error = error; }
                finally { _done = true; _cts.Dispose(); }
            }
        }

        private sealed class ImmediateLoadOp<T> : IUILoadOp<T>
        {
            public ImmediateLoadOp(T result) { Result = result; }
            public bool IsDone { get { return true; } }
            public T Result { get; }
            public Exception Error { get { return null; } }
            public void Cancel() { }
        }
    }
}
