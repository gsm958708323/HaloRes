using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameUI.Kernel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    [Serializable]
    public sealed class UIBootConfig
    {
        public AssetSystem System;
        public UIViewRegistry Registry;
        public UIRoot Root;
        public Camera WorldCamera;
        public int PoolCapacity = 32;
        public Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        public UIScreenFitMode ScreenFitMode = UIScreenFitMode.Expand;
    }

    public static class UIBoot
    {
        private static AssetSystem _assetSystem;
        private static UIViewPool _pool;
        private static UIKernel _kernel;
        private static UIViewRegistry _registry;
        private static GameObject _ownedRoot;
        private static EventSystem _ownedEventSystem;
        private static UIWorldHudSystem _worldHuds;
        private static bool _shuttingDown;

        public static UIKernel Kernel { get { return _kernel; } }
        public static UIRoot Root { get; private set; }
        public static UICameraRig Camera { get; private set; }
        public static UIDragCoordinator Drags { get; private set; }

        public static void Install(UIBootConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Shutdown();
            if (_shuttingDown) throw new InvalidOperationException("Wait for UI shutdown to finish before installing from a navigation callback.");
            UIMessageHub.CaptureMainThread();
            if (config.Registry == null) throw new InvalidOperationException("UIBootConfig.Registry is required.");
            if (config.System == null) throw new InvalidOperationException("UIBootConfig.System is required.");
            try { InstallCore(config); }
            catch { Shutdown(); throw; }
        }

        private static void InstallCore(UIBootConfig config)
        {
            _assetSystem = config.System;
            Root = config.Root != null ? config.Root : FindOrCreateRoot();
            Root.ConfigureScreen(config.ReferenceResolution, config.ScreenFitMode);
            _registry = config.Registry;
            Camera = new UICameraRig(Root);
            _pool = new UIViewPool(new AssetSystemUIAssetService(_assetSystem), info => Root.GetLayerRoot(info.Layer), config.PoolCapacity);
            _kernel = new UIKernel(config.Registry, _pool, new UIPresentationApplier(Root, Camera), new UnityUIClock(), new UILog());
            Drags = new UIDragCoordinator();
            Root.Install(_kernel, _pool);
            EnsureEventSystem();
            Camera.Register(config.WorldCamera != null ? config.WorldCamera : FindWorldCamera());
            _worldHuds = new UIWorldHudSystem(Root, Camera);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.lowMemory += OnLowMemory;
            UI.Bind(_kernel, Camera, _pool, _registry, Drags);
        }

        public static void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            if (_kernel != null && !_kernel.IsDisposed)
            {
                _kernel.Disposed += FinishShutdown;
                try { _kernel.Dispose(); }
                catch { FinishShutdown(); throw; }
            }
            else FinishShutdown();
        }

        private static void FinishShutdown()
        {
            if (_kernel != null) _kernel.Disposed -= FinishShutdown;
            try { ShutdownCore(); }
            finally { _shuttingDown = false; }
        }

        private static void ShutdownCore()
        {
            _pool?.Dispose();
            Drags?.CancelAll();
            UI.Unbind();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.lowMemory -= OnLowMemory;
            Camera?.Dispose();
            _worldHuds?.Dispose();
            _worldHuds = null;
            UIModelStage.ClearPool();
            _assetSystem?.Tick();
            if (_ownedEventSystem != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_ownedEventSystem.gameObject);
                else UnityEngine.Object.DestroyImmediate(_ownedEventSystem.gameObject);
            }
            if (_ownedRoot != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_ownedRoot);
                else UnityEngine.Object.DestroyImmediate(_ownedRoot);
            }
            _kernel = null;
            _pool = null;
            _assetSystem = null;
            _registry = null;
            Root = null;
            Camera = null;
            Drags = null;
            _ownedRoot = null;
            _ownedEventSystem = null;
        }

        public static async UniTask PreloadKeepAlive(CancellationToken ct = default)
        {
            if (_registry == null || _pool == null) throw new InvalidOperationException("Install UI first.");
            foreach (var info in _registry.All)
            {
                ct.ThrowIfCancellationRequested();
                if (info.KeepAlive) await _pool.Preload(info, ct);
            }
        }

        private static UIRoot FindOrCreateRoot()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<UIRoot>();
            if (existing != null) return existing;
            _ownedRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            UnityEngine.Object.DontDestroyOnLoad(_ownedRoot);
            return _ownedRoot.GetComponent<UIRoot>();
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
            _ownedEventSystem = go.GetComponent<EventSystem>();
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private static Camera FindWorldCamera()
        {
            if (UnityEngine.Camera.main != null) return UnityEngine.Camera.main;
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
                if (cameras[i].isActiveAndEnabled && cameras[i] != Camera?.UICamera) return cameras[i];
            return null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Camera == null) return;
            var world = FindWorldCamera();
            if (world != null && world != Camera.UICamera) Camera.Register(world);
        }

        internal static void TickWorldHuds() { _worldHuds?.Tick(); }
        private static void OnLowMemory() { _pool?.Trim(); UIModelStage.ClearPool(); }
    }
}
