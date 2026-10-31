using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using GameFramework.Asset.Scope;
using UnityEngine;

namespace GameFramework.Asset.Samples
{
    public sealed class AssetDemoLauncher : MonoBehaviour
    {
        [SerializeField] private AssetSystemMode mode = AssetSystemMode.Editor;
        [SerializeField] private TextAsset editorManifest;
        [SerializeField] private string remoteRoot;
        [SerializeField] private bool allowOffline = true;
        private AssetScope _city;
        private AssetScope _battle;
        private CancellationTokenSource _lifetime;
        private AssetSystemDriver _driver;
        private bool _changing;
        private Vector2 _diagnosticsScroll;

        public AssetSystem System => _driver == null ? null : _driver.System;
        public string Status { get; private set; } = "Starting";
        public bool IsBusy => _changing;
        public bool InBattle => _battle != null && !_battle.IsDisposed;
        public AssetSystemMode Mode { get => mode; set => mode = value; }

        private void Start()
        {
            _lifetime = new CancellationTokenSource();
            InitializeAsync().Forget(ReportError);
        }

        private async UniTask InitializeAsync()
        {
            _changing = true;
            try
            {
                AssetManifest manifest;
                AssetPathResolver paths = null;
                var selectedMode = Application.isEditor ? mode : AssetSystemMode.Bundle;
                if (selectedMode == AssetSystemMode.Editor)
                {
                    if (editorManifest == null) throw new InvalidOperationException("Editor manifest is not assigned.");
                    manifest = AssetManifest.FromJson(editorManifest.text);
                }
                else
                {
                    var persistent = Path.Combine(Application.persistentDataPath, "GameFrameworkAsset", AssetPlatform.Current);
                    var packaged = Path.Combine(Application.streamingAssetsPath, "GameFrameworkAsset", AssetPlatform.Current);
                    var patch = new PatchManager(persistent, AssetPlatform.Current);
                    var source = string.IsNullOrWhiteSpace(remoteRoot) ? packaged : remoteRoot;
                    Status = "Checking content";
                    try { manifest = await patch.RunAsync(source, new Progress<PatchProgress>(p => Status = "Downloading " + p.CompletedFiles + "/" + p.TotalFiles), _lifetime.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch when (allowOffline) { manifest = await patch.LoadInstalledAsync(_lifetime.Token); }
                    paths = new AssetPathResolver(persistent, packaged);
                }
                _lifetime.Token.ThrowIfCancellationRequested();
                _driver = gameObject.AddComponent<AssetSystemDriver>();
                _driver.Bind(AssetSystem.Initialize(selectedMode, manifest, paths));
                _city = System.CreateScope("MainCity");
                var city = await _city.InstantiateAsync("UI/MainCityPanel", transform, _lifetime.Token);
                city.transform.localPosition = new Vector3(-2.5f, 0f, 0f);
                Status = "Main City";
            }
            finally { _changing = false; }
        }

        public void EnterBattle() { EnterBattleAsync().Forget(ReportError); }
        public async UniTask EnterBattleAsync()
        {
            if (System == null || _changing || InBattle) return;
            _changing = true;
            var battle = _battle = System.CreateScope("Battle");
            Status = "Loading battle";
            try
            {
                var sceneLoad = battle.LoadSceneAsync("Scene/Battle_01", _lifetime.Token);
                var panelLoad = battle.InstantiateAsync("UI/BattlePanel", transform, _lifetime.Token);
                var hudLoad = battle.InstantiateAsync("Hero/Hero001_Hud", transform, _lifetime.Token);
                var results = await UniTask.WhenAll(sceneLoad, panelLoad, hudLoad);
                results.Item2.transform.localPosition = new Vector3(2.5f, 0f, 0f);
                results.Item3.transform.localPosition = new Vector3(2.5f, 2f, 0f);
                Status = "Battle";
            }
            catch (OperationCanceledException)
            {
                battle.Dispose();
                if (_battle == battle) _battle = null;
                Status = "Main City";
            }
            catch { battle.Dispose(); if (_battle == battle) _battle = null; throw; }
            finally { _changing = false; }
        }

        public void ReturnToCity()
        {
            _battle?.Dispose();
            _battle = null;
            Status = "Main City";
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
            _battle?.Dispose();
            _city?.Dispose();
            _lifetime?.Dispose();
        }

        private void OnGUI()
        {
            var width = Mathf.Min(680, Screen.width - 32);
            GUILayout.BeginArea(new Rect(16, 16, width, Screen.height - 32));
            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 16;
            GUILayout.Label("Asset Lifecycle Lab");
            GUILayout.Label(Status, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.BeginHorizontal();
            GUI.enabled = System != null && !_changing && !InBattle;
            if (GUILayout.Button("Enter Battle", GUILayout.Height(34))) EnterBattle();
            GUI.enabled = System != null && InBattle;
            if (GUILayout.Button("Return to City", GUILayout.Height(34))) ReturnToCity();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (System != null)
            {
                _diagnosticsScroll = GUILayout.BeginScrollView(_diagnosticsScroll);
                var snapshot = System.Tracker.Capture();
                GUILayout.Label("Assets " + snapshot.Assets.Count + "    Bundles " + snapshot.Bundles.Count);
                foreach (var asset in snapshot.Assets) GUILayout.Label(asset.Address + "    " + asset.State + "    ref=" + asset.ReferenceCount);
                foreach (var bundle in snapshot.Bundles) GUILayout.Label(bundle.Name + "    " + bundle.State + "    ref=" + bundle.ReferenceCount);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
    }
}
