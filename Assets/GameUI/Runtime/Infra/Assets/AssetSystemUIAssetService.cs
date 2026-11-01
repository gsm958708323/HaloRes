using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Scope;
using GameUI.Kernel;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class AssetSystemUIAssetService : IUIAssetService
    {
        private readonly AssetSystem _system;

        public AssetSystemUIAssetService(AssetSystem system)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
        }

        public IUILoadOp<IUIAssetHandle> LoadPrefab(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("UI prefab path is required.", nameof(path));
            var scope = _system.CreateScope("UI/" + path);
            return new AssetLoadOp<IUIAssetHandle>(async ct =>
            {
                try
                {
                    var prefab = await scope.LoadAsync<GameObject>(path, ct);
                    return new AssetHandle(scope, prefab);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            });
        }

        private sealed class AssetHandle : IUIAssetHandle
        {
            private AssetScope _scope;
            public AssetHandle(AssetScope scope, UnityEngine.Object asset) { _scope = scope; Asset = asset; }
            public UnityEngine.Object Asset { get; private set; }

            public void Release()
            {
                var scope = _scope;
                _scope = null;
                if (scope == null) return;
                scope.Dispose();
                Asset = null;
            }
        }

        private sealed class AssetLoadOp<T> : IUILoadOp<T>
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private bool _done;
            private T _result;
            private Exception _error;

            public AssetLoadOp(Func<CancellationToken, UniTask<T>> factory) { Run(factory).Forget(); }
            public bool IsDone { get { return _done; } }
            public T Result { get { return _result; } }
            public Exception Error { get { return _error; } }

            public void Cancel() { if (!_done) _cts.Cancel(); }

            private async UniTaskVoid Run(Func<CancellationToken, UniTask<T>> factory)
            {
                try
                {
                    _result = await factory(_cts.Token);
                    if (_cts.IsCancellationRequested)
                    {
                        var releasable = _result as IUIAssetHandle;
                        releasable?.Release();
                        _result = default(T);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { _error = error; }
                finally
                {
                    _done = true;
                    _cts.Dispose();
                }
            }
        }
    }
}
