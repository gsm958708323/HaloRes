using System;
using System.Collections.Generic;
using GameFramework.Asset.Manifest;
using UnityEngine;

namespace GameFramework.UI
{
    [CreateAssetMenu(fileName = "UIConfig", menuName = "Game Framework/UI Configuration")]
    public sealed class UIConfigSO : ScriptableObject
    {
        public Vector2 ReferenceResolution = new Vector2(UIDefine.Width, UIDefine.Height);
        public UIConfig[] Views = Array.Empty<UIConfig>();

        internal Dictionary<Type, UIViewDefinition> CreateSnapshot(AssetManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (!(ReferenceResolution.x > 0) || !(ReferenceResolution.y > 0)
                || float.IsInfinity(ReferenceResolution.x) || float.IsInfinity(ReferenceResolution.y))
                throw new ArgumentException("UI reference resolution must be finite and positive.");
            var addresses = new Dictionary<string, AssetKind>(StringComparer.Ordinal);
            foreach (var asset in manifest.Assets) addresses.Add(asset.Address, asset.Kind);
            var result = new Dictionary<Type, UIViewDefinition>();
            foreach (var config in Views ?? Array.Empty<UIConfig>())
            {
                if (config == null) throw new ArgumentException("UI configuration contains a null entry.");
                var type = Resolve(config.ViewType);
                if (!typeof(UIGameView).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters
                    || type.GetConstructor(Type.EmptyTypes) == null)
                    throw new ArgumentException("UI type needs a public parameterless constructor: " + type);
                if (!Enum.IsDefined(typeof(LayerType), config.LayerType))
                    throw new ArgumentException("Invalid UI layer: " + config.LayerType);
                if (!string.IsNullOrEmpty(config.PrefabAddress)
                    && (!addresses.TryGetValue(config.PrefabAddress, out var kind) || kind != AssetKind.Asset))
                    throw new ArgumentException("UI prefab address is absent or is a scene: " + config.PrefabAddress);
                var children = new List<UIChildDefinition>();
                var childTypes = new HashSet<Type>();
                var nodePaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var binding in config.ChildList ?? Array.Empty<UIChildConfig>())
                {
                    if (binding == null) throw new ArgumentException("Null UI child binding.");
                    var child = Resolve(binding.ViewType);
                    if (!typeof(UISubView).IsAssignableFrom(child) || !childTypes.Add(child)
                        || !Enum.IsDefined(typeof(ResType), binding.ResType))
                        throw new ArgumentException("Invalid or duplicate UI child: " + binding.ViewType);
                    if (binding.ResType == ResType.Static)
                    {
                        var path = binding.NodePath;
                        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/") || path.EndsWith("/")
                            || path.Contains("\\") || path.Contains("//") || path == "." || path == ".."
                            || path.StartsWith("../") || path.StartsWith("./") || path.Contains("/../")
                            || path.Contains("/./") || path.EndsWith("/..") || path.EndsWith("/.") || !nodePaths.Add(path))
                            throw new ArgumentException("Static UI needs a unique relative NodePath: " + path);
                        foreach (var previous in children)
                            if (previous.ResType == ResType.Static && (path.StartsWith(previous.NodePath + "/", StringComparison.Ordinal)
                                || previous.NodePath.StartsWith(path + "/", StringComparison.Ordinal)))
                                throw new ArgumentException("Static sibling bindings must not overlap: " + path);
                    }
                    children.Add(new UIChildDefinition(child, binding));
                }
                if (!result.TryAdd(type, new UIViewDefinition(type, config, children.ToArray())))
                    throw new ArgumentException("Duplicate UI configuration: " + type);
            }
            var visiting = new HashSet<Type>();
            var visited = new HashSet<Type>();
            foreach (var type in result.Keys) ValidateChildren(type, result, visiting, visited);
            return result;
        }

        private static Type Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("UI type name is required.");
            return Type.GetType(name, false) ?? throw new ArgumentException("UI type was not found: " + name);
        }

        private static void ValidateChildren(Type type, Dictionary<Type, UIViewDefinition> definitions,
            HashSet<Type> visiting, HashSet<Type> visited)
        {
            if (visited.Contains(type)) return;
            if (!visiting.Add(type)) throw new ArgumentException("UI child configuration contains a cycle: " + type);
            foreach (var binding in definitions[type].Children)
            {
                var child = binding.ViewType;
                if (!definitions.ContainsKey(child)) throw new ArgumentException("Missing child UI configuration: " + child);
                if (binding.ResType == ResType.Dynamic && string.IsNullOrWhiteSpace(definitions[child].PrefabAddress))
                    throw new ArgumentException("Dynamic child needs a prefab address: " + child);
                ValidateChildren(child, definitions, visiting, visited);
            }
            visiting.Remove(type);
            visited.Add(type);
        }
    }
}
