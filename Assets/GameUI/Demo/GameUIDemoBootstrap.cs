using GameFramework.Asset.Manifest;
using GameUI.Kernel;
using GameUI.Runtime;
using UnityEngine;

namespace GameUI.Demo
{
    public sealed class GameUIDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private UIRoot root;
        [SerializeField] private Camera worldCamera;
        private UIViewRegistry _registry;

        private void Start()
        {
            if (root == null) root = FindFirstObjectByType<UIRoot>();
            var manifest = BuildManifest();
            _registry = new UIViewRegistry();
            _registry.Register(Info("Home", typeof(DemoHomeView), UIMode.FullScreen, UILayer.Window, "UI/Demo/Home", () => new DemoHomeView(), true));
            _registry.Register(Info("Popup", typeof(DemoPopupView), UIMode.Popup, UILayer.Window, "UI/Demo/Popup", () => new DemoPopupView(), false));
            _registry.Register(Info("Overlay", typeof(DemoOverlayView), UIMode.Overlay, UILayer.Tip, "UI/Demo/Overlay", () => new DemoOverlayView(), false));
            UIBoot.Install(new UIBootConfig { Manifest = manifest, AssetMode = GameFramework.Asset.AssetSystemMode.Editor, Registry = _registry, Root = root, WorldCamera = worldCamera });
            UI.Open<DemoHomeView>();
        }

        private void OnDestroy() { UIBoot.Shutdown(); }

        private static UIViewInfo Info(string id, System.Type type, UIMode mode, UILayer layer, string path, System.Func<IUIViewLogic> factory, bool coversWorld)
        {
            return new UIViewInfo
            {
                Id = id, ViewType = type, Mode = mode, Layer = layer, PrefabPath = path, Factory = factory,
                CoversWorld = coversWorld, Visibility = UIOverlayVisibility.Always, BackBehavior = UIBackBehavior.Close,
                MaskStyle = mode == UIMode.Popup ? UIMaskStyle.Dim : UIMaskStyle.None, InstancePolicy = UIInstancePolicy.Single
            };
        }

        private static AssetManifest BuildManifest()
        {
            var file = new AssetManifestFile
            {
                version = 1,
                bundles = new[] { new ManifestBundleData { name = "editor", hash = new string('a', 64), size = 0 } },
                assets = new[]
                {
                    new ManifestAssetData { address = "UI/Demo/Home", assetPath = "Assets/GameUI/Demo/Generated/DemoHome.prefab", bundleIndex = 0 },
                    new ManifestAssetData { address = "UI/Demo/Popup", assetPath = "Assets/GameUI/Demo/Generated/DemoPopup.prefab", bundleIndex = 0 },
                    new ManifestAssetData { address = "UI/Demo/Overlay", assetPath = "Assets/GameUI/Demo/Generated/DemoOverlay.prefab", bundleIndex = 0 }
                }
            };
            return AssetManifest.FromJson(JsonUtility.ToJson(file));
        }
    }
}
