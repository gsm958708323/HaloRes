using System;
using System.Collections.Generic;
using System.Linq;
using GameFramework.Asset.Manifest;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Asset.Editor.Build
{
    public static class ManifestGenerator
    {
        public static ManifestAssetData[] CollectAssets(AssetBuildProfile profile, IReadOnlyList<string> bundles)
        {
            if (profile == null || profile.version < 1 || profile.entries == null || profile.entries.Length == 0)
                throw new ArgumentException("A build profile requires a positive version and at least one entry.");
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < bundles.Count; i++) indices.Add(bundles[i], i);
            var assets = new List<ManifestAssetData>();
            foreach (var entry in profile.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.address) || !addresses.Add(entry.address))
                    throw new ArgumentException("Addresses must be nonempty and unique.");
                if (entry.asset == null) throw new ArgumentException("No asset assigned to " + entry.address);
                var path = AssetDatabase.GetAssetPath(entry.asset);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("/Resources/") || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(path))
                    throw new ArgumentException("Unsupported asset path: " + path);
                if (!paths.Add(path)) throw new ArgumentException("An asset may have only one address: " + path);
                if (!indices.TryGetValue(entry.bundle, out var bundleIndex)) throw new ArgumentException("Unknown bundle: " + entry.bundle);
                assets.Add(new ManifestAssetData { address = entry.address, assetPath = path, bundleIndex = bundleIndex,
                    kind = entry.asset is SceneAsset ? AssetKind.Scene : AssetKind.Asset });
            }
            return assets.OrderBy(asset => asset.address, StringComparer.Ordinal).ToArray();
        }

        public static string[] BundleNames(AssetBuildProfile profile)
        {
            if (profile == null || profile.entries == null) throw new ArgumentNullException(nameof(profile));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in profile.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.bundle) || entry.bundle.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '_' || c == '-')))
                    throw new ArgumentException("Bundle names must use lowercase letters, digits, underscores or hyphens.");
                names.Add(entry.bundle);
            }
            return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        public static int[] FlattenDependencies(int root, IReadOnlyList<int[]> directDependencies)
        {
            if (root < 0 || root >= directDependencies.Count) throw new ArgumentOutOfRangeException(nameof(root));
            var visited = new HashSet<int> { root };
            var pending = new Stack<int>(directDependencies[root]);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current < 0 || current >= directDependencies.Count) throw new ArgumentException("Invalid dependency index.");
                if (!visited.Add(current)) continue;
                foreach (var dependency in directDependencies[current]) pending.Push(dependency);
            }
            visited.Remove(root);
            return visited.OrderBy(index => index).ToArray();
        }

        public static string EditorManifest(AssetBuildProfile profile)
        {
            var names = BundleNames(profile);
            var assets = CollectAssets(profile, names);
            var bundles = names.Select(name => new ManifestBundleData { name = name, hash = new string('0', 64), size = 0 }).ToArray();
            var json = JsonUtility.ToJson(new AssetManifestFile { version = profile.version, bundles = bundles, assets = assets }, true);
            AssetManifest.FromJson(json);
            return json;
        }
    }
}
