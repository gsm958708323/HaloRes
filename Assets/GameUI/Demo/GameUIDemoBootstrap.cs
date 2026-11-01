using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using GameUI.Generated;
using GameUI.Runtime;
using UnityEngine;

namespace GameUI.Demo
{
    public sealed class GameUIDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private UIRoot root;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private AssetSystemMode mode=AssetSystemMode.Editor;
        private CancellationTokenSource _lifetime;
        public AssetSystem System { get; private set; }
        public AssetSystemMode Mode { get=>mode; set=>mode=value; }

        private void Start()
        {
            _lifetime=new CancellationTokenSource();
            GameUIDemoSmokeTest.AttachIfRequested(gameObject);
            Initialize().Forget(error=>{if(!(error is OperationCanceledException))Debug.LogException(error);});
        }
        private async UniTask Initialize()
        {
            var selected=Application.isEditor?mode:AssetSystemMode.Bundle;
            AssetManifest manifest;
            AssetPathResolver paths=null;
            if(selected==AssetSystemMode.Editor) manifest=BuildManifest();
            else
            {
                var packaged=Path.Combine(Application.streamingAssetsPath,"GameUIAsset",PlatformName);
                var persistent=Path.Combine(Application.persistentDataPath,"GameUIAsset",PlatformName);
                manifest=await new PatchManager(persistent,PlatformName).RunAsync(packaged,null,_lifetime.Token);
                paths=new AssetPathResolver(persistent,packaged);
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            System=AssetSystem.Initialize(selected,manifest,paths);
            var driver=gameObject.AddComponent<AssetSystemDriver>();driver.Bind(System);
            if(root==null)root=FindFirstObjectByType<UIRoot>();
            UIBoot.Install(new UIBootConfig{System=System,Registry=GameUI_DemoRegistry.Create(),Root=root,WorldCamera=worldCamera});
            await UIBoot.PreloadKeepAlive(_lifetime.Token);
            UI.Open<DemoHomeView>();
        }
        private void OnDestroy()
        {
            _lifetime?.Cancel();UIBoot.Shutdown();_lifetime?.Dispose();_lifetime=null;
        }
        private static AssetManifest BuildManifest()
        {
            var names=new[]{"Home","Popup","Overlay","System","Confirm","Widgets"};
            var assets=new ManifestAssetData[names.Length];
            for(var i=0;i<names.Length;i++)assets[i]=new ManifestAssetData
                {address="UI/Demo/"+names[i],assetPath="Assets/GameUI/Demo/Generated/Demo"+names[i]+".prefab",bundleIndex=0};
            return AssetManifest.FromJson(JsonUtility.ToJson(new AssetManifestFile{version=1,
                bundles=new[]{new ManifestBundleData{name="editor",hash=new string('a',64),size=0}},assets=assets}));
        }
        public static string PlatformName
        {
            get
            {
                switch(Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:case RuntimePlatform.WindowsPlayer:return "Windows";
                    case RuntimePlatform.OSXEditor:case RuntimePlatform.OSXPlayer:return "OSX";
                    case RuntimePlatform.LinuxEditor:case RuntimePlatform.LinuxPlayer:return "Linux";
                    case RuntimePlatform.Android:return "Android";
                    case RuntimePlatform.IPhonePlayer:return "iOS";
                    default:throw new PlatformNotSupportedException(Application.platform.ToString());
                }
            }
        }
    }
}
