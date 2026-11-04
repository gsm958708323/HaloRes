using System;
using System.Collections.Generic;
using GameFramework.Asset;
using GameFramework.Asset.Scope;

namespace GameFramework.UI
{
    /// <summary>根视图的唯一实例表；一条导航记录持有一份引用，最后一份释放时销毁视图作用域。</summary>
    internal sealed class UIViewInstanceStore : IDisposable
    {
        private readonly Dictionary<Type, UIViewRecord> _records = new Dictionary<Type, UIViewRecord>();
        private readonly AssetScope _scope;
        private bool _disposed;

        internal UIViewInstanceStore(AssetSystem assets) => _scope = assets.CreateScope("UI");
        internal IReadOnlyCollection<UIViewRecord> Records => _records.Values;
        internal UIViewRecord Find(Type type) => _records.TryGetValue(type, out var record) ? record : null;

        internal UIViewRecord Retain(Type type, Func<AssetScope, UIGameView> create)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIViewInstanceStore));
            if (_records.TryGetValue(type, out var existing))
            {
                existing.References++;
                return existing;
            }

            var viewScope = _scope.CreateChild(type.Name);
            try
            {
                var record = new UIViewRecord { ViewType = type, View = create(viewScope), References = 1 };
                _records.Add(type, record);
                return record;
            }
            catch
            {
                viewScope.Dispose();
                throw;
            }
        }

        internal void Release(Type type)
        {
            if (!_records.TryGetValue(type, out var record)) return;
            if (record.References <= 0) throw new InvalidOperationException("UI instance reference count underflow: " + type);
            record.References--;
            if (record.References != 0) return;
            _records.Remove(type);
            record.View.Release();
        }

        /// <summary>先从查找表解绑，再回收实例，避免 Destroy 回调重入时再次取得即将释放的实例。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var records = new List<UIViewRecord>(_records.Values);
            _records.Clear();
            foreach (var record in records) UIView.Cleanup(record.View.Release);
            _scope.Dispose();
        }
    }
}
