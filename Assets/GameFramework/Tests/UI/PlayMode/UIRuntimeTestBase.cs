using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Provider;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameFramework.UI.Tests
{
    public abstract class UIRuntimeTestBase
    {
        protected UIConfigSO Config;
        protected ControlledProvider Provider;
        protected AssetSystem Assets;
        protected UIManager UI;
        protected GameObject Owner;
        protected GameObject Prefab;

        [SetUp] public void SetUp()
        {
            Probe.Events.Clear(); Probe.AwakeAction = null; Probe.EnableAction = null; Probe.DisableAction = null;
            Probe.CaptureAction = null; ThrowingChild.FailEnable = false;
            Owner = new GameObject("UI Test Owner");
            Prefab = new GameObject("UI Test Prefab", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            Prefab.SetActive(false);
            var template = new GameObject("Template", typeof(RectTransform));
            template.transform.SetParent(Prefab.transform, false);
            var stat = new GameObject("StaticNode", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            stat.transform.SetParent(Prefab.transform, false);
            var nested = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
            nested.transform.SetParent(stat.transform, false);
            Provider = new ControlledProvider(Prefab);
            var entries = new List<ManifestAssetData>();
            foreach (var address in new[] { "A", "B", "C", "Popup", "Child", "Child2", "Item", "Top" })
                entries.Add(new ManifestAssetData { address = address, assetPath = "Assets/" + address + ".prefab" });
            Assets = AssetSystem.WithProvider(AssetManifest.FromJson(JsonUtility.ToJson(new AssetManifestFile {
                version = 1, bundles = new[] { new ManifestBundleData { name = "ui", hash = new string('0', 64) } }, assets = entries.ToArray()
            })), Provider);
            Config = ScriptableObject.CreateInstance<UIConfigSO>();
            Config.Views = new[] { Definition<A>("A"), Definition<B>("B"), Definition<C>("C"), Definition<Popup>("Popup", true),
                Definition<Child>("Child"), Definition<Child2>("Child2"), Definition<Top>("Top"), Definition<ThrowingChild>("Child2") };
            Config.Views[0].ChildList = new[] { Binding<Child>(), Binding<Child2>() };
            Config.Views[6].LayerType = LayerType.TopLayer;
        }
        protected void StartUI() => UI = new UIManager(Assets, Config, Owner.transform);
        protected static UIConfig Definition<T>(string address, bool popup = false) => new UIConfig {
            ViewType = typeof(T).AssemblyQualifiedName, PrefabAddress = address, IsPopup = popup };
        protected static UIChildConfig Binding<T>(string path = null, bool preload = false) => new UIChildConfig {
            ViewType = typeof(T).AssemblyQualifiedName, ResType = path == null ? ResType.Dynamic : ResType.Static,
            NodePath = path, LoadWithParent = preload };
        protected static async Task Cancelled(Task task)
        {
            try { await task; Assert.Fail("Expected cancellation."); }
            catch (OperationCanceledException) { }
        }
        protected static async Task Failed(Task task)
        {
            try { await task; Assert.Fail("Expected a failure."); }
            catch (InvalidOperationException) { }
        }
        protected static async UniTask WaitUntil(Func<bool> predicate)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!predicate())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("UI test timed out.");
                await UniTask.NextFrame();
            }
        }
        [UnityTearDown] public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            Probe.AwakeAction = null; Probe.EnableAction = null; Probe.DisableAction = null;
            Probe.CaptureAction = null; ThrowingChild.FailEnable = false;
            UI?.Dispose();
            Provider.CompleteAll();
            await Assets.ShutdownAsync();
            UnityEngine.Object.Destroy(Owner);
            UnityEngine.Object.Destroy(Prefab);
            UnityEngine.Object.Destroy(Config);
            await UniTask.NextFrame();
        });

        public sealed class ControlledProvider : IAssetProvider
        {
            private readonly GameObject _prefab;
            private readonly Dictionary<string, TaskCompletionSource<LoadedAsset>> _pending = new Dictionary<string, TaskCompletionSource<LoadedAsset>>();
            public readonly Dictionary<string, int> Loads = new Dictionary<string, int>();
            public readonly HashSet<string> Blocked = new HashSet<string>();
            public readonly HashSet<string> Failures = new HashSet<string>();
            public ControlledProvider(GameObject prefab) { _prefab = prefab; }
            public async UniTask<LoadedAsset> LoadAsync(AssetInfo info, Type type)
            {
                Loads[info.Address] = Loads.TryGetValue(info.Address, out var count) ? count + 1 : 1;
                if (Failures.Contains(info.Address)) throw new InvalidOperationException("Injected load failure");
                if (Blocked.Contains(info.Address))
                {
                    var source = new TaskCompletionSource<LoadedAsset>();
                    _pending[info.Address] = source;
                    return await source.Task;
                }
                return new LoadedAsset(_prefab);
            }
            public UniTask UnloadAsync(AssetInfo info, LoadedAsset loaded) => UniTask.CompletedTask;
            public void Complete(string address)
            {
                Blocked.Remove(address);
                if (_pending.TryGetValue(address, out var pending)) { _pending.Remove(address); pending.TrySetResult(new LoadedAsset(_prefab)); }
            }
            public void CompleteAll() { foreach (var address in new List<string>(_pending.Keys)) Complete(address); }
        }
    }

    public class Probe : UIGameView
    {
        public static readonly List<string> Events = new List<string>();
        public static Action<Probe> AwakeAction, EnableAction, DisableAction, CaptureAction;
        public int AwakeCount, StartCount, EnableCount, DisableCount, DestroyCount;
        public string Value;
        protected override void OnAwake() { AwakeCount++; Events.Add(GetType().Name + ".Awake"); AwakeAction?.Invoke(this); }
        protected override void OnStart(object[] args) { StartCount++; Events.Add(GetType().Name + ".Start"); }
        protected override void OnEnable() { EnableCount++; Events.Add(GetType().Name + ".Enable"); EnableAction?.Invoke(this); }
        protected override void OnDisable() { DisableCount++; Events.Add(GetType().Name + ".Disable"); DisableAction?.Invoke(this); }
        protected override void OnDestroy() { DestroyCount++; Events.Add(GetType().Name + ".Destroy"); }
        protected override object CaptureNavigationState() { CaptureAction?.Invoke(this); return Value; }
        protected override void RestoreNavigationState(object state) => Value = state as string;
        public void Exit() => CloseSelf();
    }
    public sealed class A : Probe { }
    public sealed class B : Probe { }
    public sealed class C : Probe { }
    public sealed class Popup : Probe { }
    public sealed class Top : Probe { }
    public sealed class Child : UISubView { }
    public sealed class Child2 : UISubView { }
    public sealed class ThrowingChild : UISubView
    {
        public static bool FailEnable;
        protected override void OnEnable() { if (FailEnable) throw new InvalidOperationException("Injected child lifecycle failure"); }
    }
    public sealed class Item : UIItem { }
}
