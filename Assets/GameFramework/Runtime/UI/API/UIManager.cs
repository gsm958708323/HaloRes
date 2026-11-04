using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFramework.Asset;
using UnityEngine;

namespace GameFramework.UI
{
    /// <summary>业务侧门面：公开导航与查询 API，不承载导航规则或 Unity 根节点创建。</summary>
    public sealed class UIManager : IDisposable
    {
        private readonly UIRuntime _runtime;

        public UIManager(AssetSystem assets, UIConfigSO configuration, Transform parent = null, Camera worldCamera = null)
        {
            _runtime = new UIRuntime(this, assets, configuration, parent, worldCamera);
        }

        public AssetSystem AssetSystem => _runtime.AssetSystem;
        public Transform Root => _runtime.Root;
        public bool IsDisposed => _runtime.IsDisposed;
        public int InstanceCount => _runtime.InstanceCount;
        internal UIRuntime Runtime => _runtime;

        public UIHandle<T> Open<T>(params object[] args) where T : UIGameView => _runtime.Open<T>(args);
        public UIHandle<T> Open<T>(object[] args, CancellationToken ct) where T : UIGameView => _runtime.Open<T>(args, ct);
        public Task<T> OpenAsync<T>(object[] args = null, CancellationToken ct = default) where T : UIGameView
            => _runtime.OpenAsync<T>(args, ct);
        public void Close(UIHandle handle) => _runtime.Close(handle);
        public Task CloseAsync(UIHandle handle) => _runtime.CloseAsync(handle);
        public void Close<T>() where T : UIGameView => _runtime.Close<T>();
        public Task CloseAsync<T>() where T : UIGameView => _runtime.CloseAsync<T>();
        public void Close(UIGameView view) => _runtime.Close(view);
        public Task PopAsync(LayerType layer) => _runtime.PopAsync(layer);
        public IReadOnlyList<UIHandle> GetHistory(LayerType layer) => _runtime.GetHistory(layer);
        public int GetLayerCount(LayerType layer) => _runtime.GetLayerCount(layer);
        public T FindView<T>() where T : UIGameView => _runtime.FindView<T>();
        public void Dispose() => _runtime.Dispose();
    }
}
