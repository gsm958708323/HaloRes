using System;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework.Asset.Samples
{
    internal static class AssetPlayerSmokeTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--asset-smoke-result");
            if (Application.isEditor || index < 0 || index + 1 >= args.Length) return;
            Application.runInBackground = true;
            RunAsync(args[index + 1]).Forget(Debug.LogException);
        }

        private static async UniTask RunAsync(string resultPath)
        {
            var result = new SmokeResult();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
            {
                try
                {
                    var ct = timeout.Token;
                    var launcher = UnityEngine.Object.FindFirstObjectByType<AssetDemoLauncher>();
                    if (launcher == null) throw new InvalidOperationException("Demo launcher was not found.");
                    await UniTask.WaitUntil(() => launcher.System != null && !launcher.IsBusy, cancellationToken: ct);
                    result.cityBundles = launcher.System.Tracker.Capture().Bundles.Count;
                    if (result.cityBundles != 2) throw new InvalidOperationException("Expected two bundles in the main city.");
                    await launcher.EnterBattleAsync().AttachExternalCancellation(ct);
                    var battle = launcher.System.Tracker.Capture();
                    result.battleBundles = battle.Bundles.Count;
                    result.commonReferences = battle.Bundles.Single(bundle => bundle.Name == "ui_common").ReferenceCount;
                    result.heroReferences = battle.Bundles.Single(bundle => bundle.Name == "hero_001").ReferenceCount;
                    if (result.battleBundles != 6 || result.commonReferences != 2 || result.heroReferences != 2)
                        throw new InvalidOperationException("Battle dependency ownership is incorrect.");
                    launcher.ReturnToCity();
                    await UniTask.WaitUntil(() => launcher.System.Tracker.Capture().Bundles.Count == 2, cancellationToken: ct);
                    result.returnedCityBundles = launcher.System.Tracker.Capture().Bundles.Count;
                    var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                    if (renderers.Length != 1 || renderers[0].sharedMaterial == null)
                        throw new InvalidOperationException("The remaining main city instance lost its material.");
                    await launcher.System.ShutdownAsync(ct);
                    result.finalAssets = launcher.System.Tracker.Capture().Assets.Count;
                    result.finalBundles = launcher.System.Tracker.Capture().Bundles.Count;
                    result.passed = launcher.System.IsIdle;
                }
                catch (Exception error) { result.error = error.ToString(); }
            }
            var path = Path.GetFullPath(resultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            Debug.Log(result.passed ? "ASSET_SMOKE_PASS" : "ASSET_SMOKE_FAIL: " + result.error);
            Application.Quit(result.passed ? 0 : 1);
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public bool passed;
            public int cityBundles;
            public int battleBundles;
            public int commonReferences;
            public int heroReferences;
            public int returnedCityBundles;
            public int finalAssets;
            public int finalBundles;
            public string error;
        }
    }
}
