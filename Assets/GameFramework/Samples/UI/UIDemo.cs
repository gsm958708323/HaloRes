using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GameFramework.UI.Samples
{
    /// <summary>The sample's application entry point: owns AssetSystem, ticks it, and shuts UI down first.</summary>
    public sealed class UIDemo : MonoBehaviour
    {
        [SerializeField] private AssetSystemMode mode = AssetSystemMode.Editor;
        [SerializeField] private TextAsset editorManifest;
        private CancellationTokenSource _lifetime;
        public AssetSystem System { get; private set; }
        public UIManager UI { get; private set; }
        public string Status { get; private set; } = "Starting";
        public AssetSystemMode Mode { get => mode; set => mode = value; }

        private void Start()
        {
            _lifetime = new CancellationTokenSource();
            InitializeAsync().Forget(ReportError);
        }

        private async UniTask InitializeAsync()
        {
            var selectedMode = Application.isEditor ? mode : AssetSystemMode.Bundle;
            AssetManifest manifest;
            AssetPathResolver paths = null;
            if (selectedMode == AssetSystemMode.Editor)
            {
                if (editorManifest == null) throw new InvalidOperationException("Assign the UI sample's EditorManifest.");
                manifest = AssetManifest.FromJson(editorManifest.text);
            }
            else
            {
                var persistent = Path.Combine(Application.persistentDataPath, "GameFrameworkUI", PlatformName);
                var packaged = Path.Combine(Application.streamingAssetsPath, "GameFrameworkUI", PlatformName);
                manifest = await new PatchManager(persistent, PlatformName).RunAsync(packaged, ct: _lifetime.Token);
                paths = new AssetPathResolver(persistent, packaged);
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            System = AssetSystem.Initialize(selectedMode, manifest, paths);
            using (var scope = System.CreateScope("UIConfiguration"))
            {
                var config = await scope.LoadAsync<UIConfigSO>("UI/Config", _lifetime.Token);
                UI = new UIManager(System, config, transform, Camera.main);
            }
            await UI.OpenAsync<FullView1>(ct: _lifetime.Token);
            Status = selectedMode + " / Ready";
            UIPlayerSmokeTest.RunIfRequested(UI, System);
        }

        private void Update()
        {
            if (System != null && !System.IsClosed) System.Tick();
            if (UI == null || UI.IsDisposed) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.qKey.wasPressedThisFrame) UI.Open<FullView1>();
            if (keyboard.wKey.wasPressedThisFrame) UI.Open<TipsView1>();
            if (keyboard.eKey.wasPressedThisFrame) UI.Open<FullView2>();
            if (keyboard.rKey.wasPressedThisFrame) UI.Open<ChildView2>();
            if (keyboard.aKey.wasPressedThisFrame) UI.Open<FullView2_Top>();
            if (keyboard.sKey.wasPressedThisFrame) UI.Open<TipsView2>();
#else
            if (Input.GetKeyDown(KeyCode.Q)) UI.Open<FullView1>();
            if (Input.GetKeyDown(KeyCode.W)) UI.Open<TipsView1>();
            if (Input.GetKeyDown(KeyCode.E)) UI.Open<FullView2>();
            if (Input.GetKeyDown(KeyCode.R)) UI.Open<ChildView2>();
            if (Input.GetKeyDown(KeyCode.A)) UI.Open<FullView2_Top>();
            if (Input.GetKeyDown(KeyCode.S)) UI.Open<TipsView2>();
#endif
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(720, Screen.width - 32), 148), GUI.skin.box);
            GUILayout.Label("HaloFrame UI / " + Status);
            if (UI != null && !UI.IsDisposed)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Q FullView1")) UI.Open<FullView1>();
                if (GUILayout.Button("W TipsView1")) UI.Open<TipsView1>();
                if (GUILayout.Button("E FullView2")) UI.Open<FullView2>();
                if (GUILayout.Button("R ChildView2")) UI.Open<ChildView2>();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("A Top fullscreen")) UI.Open<FullView2_Top>();
                if (GUILayout.Button("S Top popup")) UI.Open<TipsView2>();
                if (GUILayout.Button("Back Normal")) Observe(UI.PopAsync(LayerType.NormalLayer));
                if (GUILayout.Button("Back Top")) Observe(UI.PopAsync(LayerType.TopLayer));
                GUILayout.EndHorizontal();
                GUILayout.Label("Normal: " + UI.GetLayerCount(LayerType.NormalLayer)
                    + "    Top: " + UI.GetLayerCount(LayerType.TopLayer));
            }
            GUILayout.EndArea();
        }

        private async void Observe(Task task)
        {
            try { await task; }
            catch (Exception error) { ReportError(error); }
        }

        private void ReportError(Exception error)
        {
            if (error is OperationCanceledException) return;
            Status = error.Message;
            Debug.LogException(error);
        }

        private void OnDestroy()
        {
            _lifetime?.Cancel();
            UI?.Dispose();
            if (System != null) System.ShutdownAsync().Forget(Debug.LogException);
            _lifetime?.Dispose();
        }

        public static string PlatformName
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                    case RuntimePlatform.WindowsPlayer: return "Windows";
                    case RuntimePlatform.OSXEditor:
                    case RuntimePlatform.OSXPlayer: return "OSX";
                    case RuntimePlatform.LinuxEditor:
                    case RuntimePlatform.LinuxPlayer: return "Linux";
                    case RuntimePlatform.Android: return "Android";
                    case RuntimePlatform.IPhonePlayer: return "iOS";
                    default: throw new PlatformNotSupportedException("Unsupported platform: " + Application.platform);
                }
            }
        }
    }
}
