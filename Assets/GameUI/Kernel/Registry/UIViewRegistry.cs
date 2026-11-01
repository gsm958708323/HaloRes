using System;
using System.Collections.Generic;

namespace GameUI.Kernel
{
    public sealed class UIViewRegistry : IUIViewRegistry
    {
        private readonly Dictionary<Type, UIViewInfo> _byType = new Dictionary<Type, UIViewInfo>();
        private readonly Dictionary<string, UIViewInfo> _byId = new Dictionary<string, UIViewInfo>(StringComparer.Ordinal);

        public UIViewRegistry(IEnumerable<UIViewInfo> views)
        {
            if (views == null) throw new ArgumentNullException(nameof(views));
            foreach (var view in views) Register(view);
        }

        public UIViewRegistry() { }

        public void Register(UIViewInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            info.Validate();
            if (_byId.ContainsKey(info.Id)) throw new InvalidOperationException("Duplicate UIView id: " + info.Id);
            if (_byType.ContainsKey(info.ViewType)) throw new InvalidOperationException("Duplicate UIView type: " + info.ViewType.FullName);
            _byId.Add(info.Id, info);
            _byType.Add(info.ViewType, info);
        }

        public UIViewInfo Get(Type viewType)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));
            UIViewInfo info;
            if (!_byType.TryGetValue(viewType, out info)) throw new KeyNotFoundException("UIView is not registered: " + viewType.FullName);
            return info;
        }

        public UIViewInfo Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("UIView id is required.", nameof(id));
            UIViewInfo info;
            if (!_byId.TryGetValue(id, out info)) throw new KeyNotFoundException("UIView is not registered: " + id);
            return info;
        }

        public IReadOnlyCollection<UIViewInfo> All { get { return _byId.Values; } }
    }
}
