using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Provider;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameFramework.Asset.Tests.PlayMode
{
    public sealed class LifecycleTests
    {
        private AssetSystem _system;
        private ControlledProvider _provider;
        private GameObject _prefab;

        [SetUp]
        public void Setup()
        {
            _prefab = new GameObject("TestPrefab");
            _prefab.SetActive(false);
            _provider = new ControlledProvider();
            var file = new AssetManifestFile {
                version = 1, bundles = new[] { new ManifestBundleData { name = "test", hash = new string('a',64) } },
                assets = new[] { new ManifestAssetData { address = "Prefab", assetPath = "Assets/Test.prefab" } }
            };
            _system = AssetSystem.WithProvider(AssetManifest.FromJson(JsonUtility.ToJson(file)), _provider);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                _provider.Complete(_prefab);
                _provider.CompleteUnload();
                _provider.BlockUnload = false;
                _provider.FailUnload = false;
                await _system.ShutdownAsync();
                Object.Destroy(_prefab);
                await UniTask.Yield();
            });
        }

        [UnityTest]
        public IEnumerator DuplicateRequestsShareLoadButOwnSeparateLeases() => UniTask.ToCoroutine(async () =>
        {
            var a = _system.CreateScope("A"); var b = _system.CreateScope("B");
            var first = a.LoadAsync<GameObject>("Prefab").AsTask();
            var second = b.LoadAsync<GameObject>("Prefab").AsTask();
            Assert.That(_provider.LoadCount, Is.EqualTo(1));
            Assert.That(_system.Tracker.Capture().Assets.Single().ReferenceCount, Is.EqualTo(2));
            _provider.Complete(_prefab);
            Assert.That(await first, Is.SameAs(await second));
            a.Dispose(); _system.Tick(); Assert.That(_provider.UnloadCount, Is.Zero);
            b.Dispose(); _system.Tick(); Assert.That(_provider.UnloadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator CancellingOneWaiterDoesNotCancelAnother() => UniTask.ToCoroutine(async () =>
        {
            var a = _system.CreateScope("A"); var b = _system.CreateScope("B");
            using (var cts = new CancellationTokenSource())
            {
                var first = a.LoadAsync<GameObject>("Prefab", cts.Token).AsTask();
                var second = b.LoadAsync<GameObject>("Prefab").AsTask();
                cts.Cancel();
                await ExpectCancelled(first);
                _provider.Complete(_prefab);
                Assert.That(await second, Is.SameAs(_prefab));
                Assert.That(_system.Tracker.Capture().Assets.Single().ReferenceCount, Is.EqualTo(1));
            }
        });

        [UnityTest]
        public IEnumerator DisposedLoadingScopeReclaimsLateCompletion() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Gone");
            var request = scope.LoadAsync<GameObject>("Prefab").AsTask();
            scope.Dispose(); scope.Dispose();
            await ExpectCancelled(request);
            _system.Tick(); Assert.That(_system.Tracker.Capture().Assets.Single().State, Is.EqualTo("Loading"));
            _provider.Complete(_prefab);
            await Until(() => _system.Tracker.Capture().Assets.Single().State == "Loaded");
            _system.Tick();
            Assert.That(_system.IsIdle, Is.True);
            Assert.That(_provider.UnloadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator FailedLeaseIsReturnedAndSameScopeCanRetry() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Retry");
            var first = scope.LoadAsync<GameObject>("Prefab").AsTask();
            _provider.Fail();
            await ExpectAssetFailure(first, AssetErrorCode.AssetNotInBundle);
            Assert.That(_system.Tracker.Capture().Assets.Single().ReferenceCount, Is.Zero);
            _system.Tick();
            Assert.That(_provider.UnloadCount, Is.Zero);
            _provider.Reset();
            var second = scope.LoadAsync<GameObject>("Prefab").AsTask();
            _provider.Complete(_prefab);
            Assert.That(await second, Is.SameAs(_prefab));
            Assert.That(_provider.LoadCount, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator SameFrameReacquireRevivesWithoutProviderReload() => UniTask.ToCoroutine(async () =>
        {
            var a = _system.CreateScope("A"); _provider.Complete(_prefab);
            await a.LoadAsync<GameObject>("Prefab"); a.Dispose();
            var b = _system.CreateScope("B"); await b.LoadAsync<GameObject>("Prefab");
            _system.Tick();
            Assert.That(_provider.LoadCount, Is.EqualTo(1));
            Assert.That(_provider.UnloadCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator InstanceTransferAndDestroyKeepAssetsAlive() => UniTask.ToCoroutine(async () =>
        {
            var a = _system.CreateScope("A"); var b = _system.CreateScope("B");
            _provider.Complete(_prefab);
            var instance = await a.InstantiateAsync("Prefab");
            a.TransferInstance(instance, b); a.Dispose(); _system.Tick();
            Assert.That(instance != null, Is.True);
            Assert.That(_system.Tracker.CreateLeakReport(), Does.Contain("B#"));
            b.ReleaseInstance(instance); _system.Tick();
            Assert.That(_provider.UnloadCount, Is.Zero, "Destroy has not yet completed.");
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            await UniTask.NextFrame();
            _system.Tick();
            Assert.That(instance == null, Is.True);
            Assert.That(_system.IsIdle, Is.True);
        });

        [UnityTest]
        public IEnumerator ParentDisposesChildrenIdempotently() => UniTask.ToCoroutine(async () =>
        {
            var parent = _system.CreateScope("Parent");
            var first = parent.CreateChild("First"); var second = parent.CreateChild("Second");
            _provider.Complete(_prefab);
            await first.LoadAsync<GameObject>("Prefab"); await second.LoadAsync<GameObject>("Prefab");
            first.Dispose(); parent.Dispose(); second.Dispose();
            _system.Tick(); Assert.That(_system.IsIdle, Is.True);
            Assert.That(_provider.UnloadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator TypeMismatchAndNullReleaseCannotStealLease() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Types"); _provider.Complete(_prefab);
            await scope.LoadAsync<GameObject>("Prefab");
            await ExpectAssetFailure(scope.LoadAsync<Texture2D>("Prefab").AsTask(), AssetErrorCode.TypeMismatch);
            Assert.Throws<ArgumentNullException>(() => scope.Release(null));
            Assert.Throws<ArgumentNullException>(() => scope.ReleaseInstance(null));
            Assert.That(_system.Tracker.Capture().Assets.Single().ReferenceCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator UnloadingHandleIsNotReused() => UniTask.ToCoroutine(async () =>
        {
            _provider.BlockUnload = true;
            var first = _system.CreateScope("First"); _provider.Complete(_prefab);
            await first.LoadAsync<GameObject>("Prefab"); first.Dispose(); _system.Tick();
            _provider.Reset();
            var second = _system.CreateScope("Second");
            var request = second.LoadAsync<GameObject>("Prefab").AsTask();
            _provider.Complete(_prefab); await request;
            Assert.That(_provider.LoadCount, Is.EqualTo(2));
            Assert.That(_system.Tracker.Capture().Assets.Count, Is.EqualTo(2));
            _provider.CompleteUnload();
            await Until(() => _system.Tracker.Capture().Assets.Count == 1);
        });

        [UnityTest]
        public IEnumerator FailedUnloadStaysObservableAndCanRecover() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("UnloadRetry"); _provider.Complete(_prefab);
            await scope.LoadAsync<GameObject>("Prefab");
            _provider.FailUnload = true;
            scope.Dispose(); _system.Tick();
            var failed = _system.Tracker.Capture().Assets.Single();
            Assert.That(failed.State, Is.EqualTo("UnloadFailed"));
            Assert.That(failed.Error, Does.Contain("Injected unload failure"));
            Assert.That(_system.IsIdle, Is.False);
            _provider.FailUnload = false;
            _system.Tick();
            Assert.That(_system.IsIdle, Is.True);
        });

        [UnityTest]
        public IEnumerator InvalidAddressAndDisposedScopeNeverStartProviderWork() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Invalid");
            await ExpectAssetFailure(scope.LoadAsync<GameObject>("Missing").AsTask(), AssetErrorCode.AddressNotFound);
            scope.Dispose();
            await ExpectAssetFailure(scope.LoadAsync<GameObject>("Prefab").AsTask(), AssetErrorCode.ScopeDisposed);
            Assert.That(_provider.LoadCount, Is.Zero);
            Assert.That(_system.Tracker.Capture().Assets, Is.Empty);
        });

        [UnityTest]
        public IEnumerator ScopeDisposedByPrefabAwakeCannotLeakNewInstance() => UniTask.ToCoroutine(async () =>
        {
            _prefab.AddComponent<DisposeScopeOnAwake>();
            _prefab.SetActive(true);
            var scope = _system.CreateScope("Awake");
            _provider.Complete(_prefab);
            DisposeScopeOnAwake.Target = scope;
            try { await ExpectCancelled(scope.InstantiateAsync("Prefab").AsTask()); }
            finally { DisposeScopeOnAwake.Target = null; }
            _system.Tick();
            Assert.That(_provider.UnloadCount, Is.Zero, "New instance must be destroyed before asset unload.");
            await UniTask.NextFrame(); _system.Tick();
            Assert.That(GameObject.Find("TestPrefab(Clone)"), Is.Null);
            Assert.That(_system.IsIdle, Is.True);
        });

        [UnityTest]
        public IEnumerator BackgroundThreadCallsAreRejected() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Thread");
            var error = await Task.Run(() => {
                try { scope.CreateChild("Illegal"); return null; }
                catch (AssetException failure) { return failure; }
            });
            Assert.That(error.Error.Code, Is.EqualTo(AssetErrorCode.MainThreadViolation));
        });

        internal static async UniTask Until(Func<bool> condition)
        {
            var timeout = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > timeout) Assert.Fail("Timed out waiting for lifecycle completion.");
                await UniTask.Yield();
            }
        }

        internal static async UniTask ExpectCancelled(Task request)
        {
            try { await request; Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException) { }
        }

        internal static async UniTask ExpectAssetFailure(Task request, AssetErrorCode code)
        {
            try { await request; Assert.Fail("Expected AssetException: " + code); }
            catch (AssetException error) { Assert.That(error.Error.Code, Is.EqualTo(code)); }
        }

        private sealed class ControlledProvider : IAssetProvider
        {
            private TaskCompletionSource<LoadedAsset> _load = new TaskCompletionSource<LoadedAsset>();
            private readonly TaskCompletionSource<bool> _unload = new TaskCompletionSource<bool>();
            internal int LoadCount;
            internal int UnloadCount;
            internal bool BlockUnload;
            internal bool FailUnload;
            public async UniTask<LoadedAsset> LoadAsync(AssetInfo info, Type type) { LoadCount++; return await _load.Task; }
            public async UniTask UnloadAsync(AssetInfo info, LoadedAsset loaded)
            {
                UnloadCount++;
                if (FailUnload) throw new InvalidOperationException("Injected unload failure");
                if (BlockUnload) await _unload.Task;
            }
            internal void Complete(GameObject prefab) => _load.TrySetResult(new LoadedAsset(prefab));
            internal void Fail() => _load.TrySetException(new AssetException(new AssetError(AssetErrorCode.AssetNotInBundle, "Prefab", "test", "Injected failure")));
            internal void Reset() { _load = new TaskCompletionSource<LoadedAsset>(); }
            internal void CompleteUnload() => _unload.TrySetResult(true);
        }
    }
}
