using System;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameUI.Kernel;
using UnityEngine;

namespace GameUI.Runtime
{
    [Serializable]
    public sealed class UIBootConfig
    {
        public AssetManifest Manifest;
        public AssetSystemMode AssetMode = AssetSystemMode.Editor;
        public AssetPathResolver Paths;
        public UIViewRegistry Registry;
        public UIRoot Root;
        public Camera WorldCamera;
    }

    public static class UIBoot
    {
        private static AssetSystem _assetSystem;
        private static AssetSystemUIAssetService _assetService;
        private static UIViewPool _pool;
        private static UIKernel _kernel;

        public static UIKernel Kernel { get { return _kernel; } }
        public static UIRoot Root { get; private set; }
        public static UICameraRig Camera { get; private set; }

        public static void Install(UIBootConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Shutdown();
            if (config.Registry == null) throw new InvalidOperationException("UIBootConfig.Registry is required.");
            if (config.Root == null) throw new InvalidOperationException("UIBootConfig.Root is required.");
            _assetSystem = AssetSystem.Initialize(config.AssetMode, config.Manifest, config.Paths);
            _assetService = new AssetSystemUIAssetService(_assetSystem);
            _pool = new UIViewPool(_assetService, config.Root.WindowRoot != null ? config.Root.WindowRoot : config.Root.transform);
            Camera = new UICameraRig();
            Camera.Register(config.WorldCamera);
            var sink = new UIPresentationApplier(config.Root, Camera);
            _kernel = new UIKernel(config.Registry, _pool, sink, new UnityUIClock(), new UILog());
            Root = config.Root;
            Root.Install(_kernel, _pool);
            UI.Bind(_kernel, Camera);
        }

        public static void Shutdown()
        {
            UI.Unbind();
            _kernel?.Dispose();
            _pool?.Dispose();
            _assetService = null;
            if (_assetSystem != null)
            {
                _assetSystem.Dispose();
                _assetSystem.Tick();
            }
            _kernel = null;
            _pool = null;
            _assetSystem = null;
            Root = null;
            Camera = null;
        }
    }
}
