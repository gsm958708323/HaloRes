using System;
using System.IO;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using UnityEngine;

namespace GameFramework.UI.Samples
{
    public static class UIPlayerSmokeTest
    {
        [Serializable] private sealed class Result
        {
            public bool passed;
            public bool reusedB;
            public bool returnedInOrder;
            public bool resourcesReleased;
            public string error;
        }
        public static void RunIfRequested(UIManager ui, AssetSystem assets)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--ui-smoke-result") Run(ui, assets, Path.GetFullPath(args[i + 1])).Forget(Debug.LogException);
        }
        private static async UniTask Run(UIManager ui, AssetSystem assets, string path)
        {
            var result = new Result();
            string loggedError = null;
            Application.LogCallback capture = (message, stack, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedError = message;
            };
            Application.logMessageReceived += capture;
            try
            {
                var a = ui.FindView<FullView1>();
                var first = ui.Open<FullView2>(); var b = await first.Opened;
                var c = ui.Open<ChildView2>(); await c.Opened;
                var second = ui.Open<FullView2>();
                result.reusedB = ReferenceEquals(b, await second.Opened) && first.Id != second.Id;
                await ui.PopAsync(LayerType.NormalLayer);
                var returnedC = (await c.Opened).IsVisible;
                await ui.PopAsync(LayerType.NormalLayer);
                var returnedB = b.IsVisible && b.ActiveNavigation == first;
                await ui.PopAsync(LayerType.NormalLayer);
                result.returnedInOrder = returnedC && returnedB && a.IsVisible;
                var child = await a.OpenOneChildUIAsync<ChildView1>();
                if (!child.IsVisible) throw new InvalidOperationException("Sample child did not open.");
                var popup = ui.Open<TipsView1>(); await popup.Opened;
                if (!a.IsVisible) throw new InvalidOperationException("Popup hid its background.");
                await ui.CloseAsync(popup);
                ui.Dispose();
                var deadline = Time.realtimeSinceStartup + 20;
                while (!assets.IsIdle && Time.realtimeSinceStartup < deadline)
                {
                    await UniTask.NextFrame(); assets.Tick();
                }
                result.resourcesReleased = assets.IsIdle;
                result.passed = result.reusedB && result.returnedInOrder && result.resourcesReleased && loggedError == null;
                result.error = loggedError ?? "";
            }
            catch (Exception error) { result.error = error.ToString(); }
            finally
            {
                Application.logMessageReceived -= capture;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(result, true));
            }
            if (!Application.isEditor) Application.Quit(result.passed ? 0 : 1);
        }
    }
}
