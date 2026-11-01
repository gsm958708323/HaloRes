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
        private readonly Func<UIViewInfo, Transform> _parentFor;
        private readonly int _capacity;
        private readonly List<UGUIViewInstance> _free = new List<UGUIViewInstance>();
        private readonly HashSet<UGUIViewInstance> _active = new HashSet<UGUIViewInstance>();
        private readonly HashSet<PoolLoadOp> _loading = new HashSet<PoolLoadOp>();
        private readonly List<PoolLoadOp> _handoffs = new List<PoolLoadOp>();
        private long _useSequence;
        private bool _disposed;

        public UIViewPool(IUIAssetService assets, Func<UIViewInfo, Transform> parentFor, int capacity = 32)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _parentFor = parentFor ?? throw new ArgumentNullException(nameof(parentFor));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public IUILoadOp<IUIViewInstance> Acquire(UIViewInfo info)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIViewPool));
            for (var i = _free.Count - 1; i >= 0; i--)
            {
                var cached = _free[i];
                if (!string.Equals(cached.ViewId, info.Id, StringComparison.Ordinal)) continue;
                _free.RemoveAt(i);
                _active.Add(cached);
                cached.MarkUsed(++_useSequence);
                cached.PrepareForPool();
                return new ImmediateLoadOp(cached);
            }

            var op = new PoolLoadOp(this, info);
            if (info.IsSingle)
            {
                var exiting = FindExiting(info.Id);
                if (exiting != null)
                {
                    _handoffs.Add(op);
                    exiting.RequestHandoff();
                    return op;
                }
            }

            StartBuild(op);
            return op;
        }

        public async UniTask Preload(UIViewInfo info, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIViewPool));
            if (HasInstance(info.Id)) return;

            var load = Acquire(info);
            try
            {
                while (!load.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                if (load.Error != null) throw load.Error;
                if (load.Result == null) throw new OperationCanceledException(cancellationToken);
                Return(load.Result, true);
            }
            catch
            {
                load.Cancel();
                if (load.IsDone && load.Result != null) Return(load.Result, false);
                throw;
            }
        }

        public void Return(IUIViewInstance instance, bool reusable)
        {
            var concrete = instance as UGUIViewInstance;
            if (concrete == null) return;
            _active.Remove(concrete);

            if (!_disposed && reusable)
            {
                var handoff = FindHandoff(concrete.ViewId);
                if (handoff != null)
                {
                    if (TryHandoff(concrete, handoff)) return;
                }
            }

            if (_disposed || !reusable || (!_free.Contains(concrete) && !concrete.KeepAlive && _capacity == 0))
            {
                concrete.DestroyForPool();
                return;
            }

            concrete.PrepareForPool();
            if (!_free.Contains(concrete))
            {
                concrete.MarkUsed(++_useSequence);
                _free.Add(concrete);
            }
            TrimToCapacity();
        }

        public void Trim()
        {
            for (var i = _free.Count - 1; i >= 0; i--)
            {
                if (_free[i].KeepAlive) continue;
                var instance = _free[i];
                _free.RemoveAt(i);
                DestroySafely(instance);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var load in new List<PoolLoadOp>(_loading)) load.Cancel();
            foreach (var load in new List<PoolLoadOp>(_handoffs)) load.Cancel();
            foreach (var instance in _free) DestroySafely(instance);
            foreach (var instance in new List<UGUIViewInstance>(_active)) DestroySafely(instance);
            _free.Clear();
            _active.Clear();
            _handoffs.Clear();
        }

        private static void DestroySafely(UGUIViewInstance instance)
        {
            try { instance.DestroyForPool(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private bool HasInstance(string viewId)
        {
            for (var i = 0; i < _free.Count; i++) if (_free[i].ViewId == viewId) return true;
            foreach (var instance in _active) if (instance.ViewId == viewId) return true;
            return false;
        }

        private UGUIViewInstance FindExiting(string viewId)
        {
            foreach (var instance in _active)
                if (instance.ViewId == viewId && instance.IsExiting && !instance.HandoffRequested) return instance;
            return null;
        }

        private PoolLoadOp FindHandoff(string viewId)
        {
            for (var i = 0; i < _handoffs.Count; i++)
                if (!_handoffs[i].IsDone && _handoffs[i].Info.Id == viewId) return _handoffs[i];
            return null;
        }

        private bool TryHandoff(UGUIViewInstance instance, PoolLoadOp load)
        {
            try
            {
                instance.PrepareForPool();
                _active.Add(instance);
                _handoffs.Remove(load);
                load.Complete(instance);
                return true;
            }
            catch (Exception error)
            {
                _handoffs.Remove(load);
                load.Fail(error);
                instance.DestroyForPool();
                return true;
            }
        }

        private void StartBuild(PoolLoadOp load)
        {
            _loading.Add(load);
            load.StartBuild();
        }

        private void Finish(PoolLoadOp load)
        {
            _loading.Remove(load);
            _handoffs.Remove(load);
        }

        private void CancelHandoff(PoolLoadOp load)
        {
            _handoffs.Remove(load);
            if (FindHandoff(load.Info.Id) != null) return;
            foreach (var instance in _active)
                if (instance.ViewId == load.Info.Id) instance.CancelHandoffRequest();
        }

        private void TrimToCapacity()
        {
            var nonKeepAlive = 0;
            for (var i = 0; i < _free.Count; i++) if (!_free[i].KeepAlive) nonKeepAlive++;
            while (nonKeepAlive > _capacity)
            {
                var oldestIndex = -1;
                var oldestUse = long.MaxValue;
                for (var i = 0; i < _free.Count; i++)
                {
                    var candidate = _free[i];
                    if (candidate.KeepAlive || candidate.LastUsed >= oldestUse) continue;
                    oldestIndex = i;
                    oldestUse = candidate.LastUsed;
                }
                if (oldestIndex < 0) return;
                var evicted = _free[oldestIndex];
                _free.RemoveAt(oldestIndex);
                evicted.DestroyForPool();
                nonKeepAlive--;
            }
        }

        private async UniTask<UGUIViewInstance> BuildAsync(UIViewInfo info, CancellationToken cancellationToken)
        {
            var load = _assets.LoadPrefab(info.PrefabPath);
            IUIAssetHandle handle = null;
            GameObject instanceObject = null;
            IUIViewLogic logic = null;
            try
            {
                while (!load.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (load.Error != null) throw load.Error;
                handle = load.Result;
                if (handle == null || !(handle.Asset is GameObject prefab)) throw new InvalidOperationException("UI prefab is not a GameObject: " + info.PrefabPath);
                var parent = _parentFor(info);
                instanceObject = UnityEngine.Object.Instantiate(prefab, parent);
                SetLayerRecursively(instanceObject.transform, UIRoot.UIRenderLayer);
                instanceObject.name = prefab.name + "[UI]";
                logic = info.Factory();
                if (logic == null) throw new InvalidOperationException("UIView factory returned null: " + info.Id);
                if (logic is UIElement element) element.Initialize(instanceObject);
                var instance = new UGUIViewInstance(info.Id, instanceObject, logic, handle, info.KeepAlive);
                instance.MarkUsed(++_useSequence);
                _active.Add(instance);
                return instance;
            }
            catch
            {
                load.Cancel();
                if (instanceObject != null)
                {
                    try
                    {
                        if (logic is UINode node) node.HostDestroy();
                        else if (logic is UIElement element) element.DestroyElement();
                    }
                    catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                    if (Application.isPlaying) UnityEngine.Object.Destroy(instanceObject);
                    else UnityEngine.Object.DestroyImmediate(instanceObject);
                }
                if (handle != null)
                {
                    if (instanceObject != null && Application.isPlaying) ReleaseAssetAfterDestroy(instanceObject, handle).Forget();
                    else handle.Release();
                }
                else if (load.IsDone && load.Result != null) load.Result.Release();
                throw;
            }
        }

        private static async UniTaskVoid ReleaseAssetAfterDestroy(GameObject instance, IUIAssetHandle handle)
        {
            while (instance != null) await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            handle.Release();
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (var i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private sealed class PoolLoadOp : IUILoadOp<IUIViewInstance>
        {
            private readonly UIViewPool _owner;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private IUIViewInstance _result;
            private Exception _error;
            private bool _done;

            internal PoolLoadOp(UIViewPool owner, UIViewInfo info) { _owner = owner; Info = info; }
            internal UIViewInfo Info { get; }
            public bool IsDone { get { return _done; } }
            public IUIViewInstance Result { get { return _result; } }
            public Exception Error { get { return _error; } }

            public void Cancel()
            {
                if (_done) return;
                _cts.Cancel();
                _owner.CancelHandoff(this);
                if (!_owner._loading.Contains(this)) Finish(null, null);
            }

            internal void StartBuild() { RunBuild().Forget(); }

            internal void Complete(UGUIViewInstance instance)
            {
                if (_cts.IsCancellationRequested)
                {
                    _owner.Return(instance, false);
                    Finish(null, null);
                    return;
                }
                Finish(instance, null);
            }

            internal void Fail(Exception error) { Finish(null, error); }

            private async UniTaskVoid RunBuild()
            {
                try
                {
                    var instance = await _owner.BuildAsync(Info, _cts.Token);
                    if (_cts.IsCancellationRequested) { _owner.Return(instance, false); Finish(null, null); }
                    else Finish(instance, null);
                }
                catch (OperationCanceledException) { Finish(null, null); }
                catch (Exception error) { Finish(null, error); }
            }

            private void Finish(IUIViewInstance result, Exception error)
            {
                if (_done) return;
                _result = result;
                _error = error;
                _done = true;
                _owner.Finish(this);
                _cts.Dispose();
            }
        }

        private sealed class ImmediateLoadOp : IUILoadOp<IUIViewInstance>
        {
            internal ImmediateLoadOp(IUIViewInstance instance) { Result = instance; }
            public bool IsDone { get { return true; } }
            public IUIViewInstance Result { get; }
            public Exception Error { get { return null; } }
            public void Cancel() { }
        }
    }
}
