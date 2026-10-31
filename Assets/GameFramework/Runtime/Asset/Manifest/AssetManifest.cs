using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GameFramework.Asset.Patch;

namespace GameFramework.Asset.Manifest
{
    public enum AssetKind
    {
        Asset = 0,
        Scene = 1
    }

    [Serializable]
    public sealed class AssetManifestFile
    {
        public int version;
        public ManifestBundleData[] bundles = Array.Empty<ManifestBundleData>();
        public ManifestAssetData[] assets = Array.Empty<ManifestAssetData>();
    }

    [Serializable]
    public sealed class ManifestBundleData
    {
        public string name;
        public string hash;
        public long size;
        public int[] dependencies = Array.Empty<int>();
    }

    [Serializable]
    public sealed class ManifestAssetData
    {
        public string address;
        public string assetPath;
        public int bundleIndex;
        public AssetKind kind;
    }

    public sealed class AssetInfo
    {
        internal AssetInfo(string address, string assetPath, int bundleIndex, AssetKind kind)
        {
            Address = address;
            AssetPath = assetPath;
            BundleIndex = bundleIndex;
            Kind = kind;
        }

        public string Address { get; }
        public string AssetPath { get; }
        public int BundleIndex { get; }
        public AssetKind Kind { get; }
    }

    public sealed class BundleInfo
    {
        internal BundleInfo(string name, string hash, long size, int[] dependencies)
        {
            Name = name;
            Hash = hash;
            Size = size;
            Dependencies = Array.AsReadOnly((int[])(dependencies ?? Array.Empty<int>()).Clone());
        }

        public string Name { get; }
        public string Hash { get; }
        public long Size { get; }
        public IReadOnlyList<int> Dependencies { get; }
    }

    public sealed class AssetManifest
    {
        private readonly Dictionary<string, AssetInfo> _assetsByAddress;
        private readonly BundleInfo[] _bundles;
        private readonly AssetInfo[] _assets;
        private readonly IReadOnlyList<BundleInfo> _bundleView;
        private readonly IReadOnlyList<AssetInfo> _assetView;

        private AssetManifest(int version, BundleInfo[] bundles, AssetInfo[] assets)
        {
            if (version < 1) throw new ArgumentException("Manifest version must be positive.", nameof(version));
            Version = version;
            _bundles = bundles;
            _assets = assets;
            _bundleView = Array.AsReadOnly(bundles);
            _assetView = Array.AsReadOnly(assets);
            _assetsByAddress = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);

            for (var i = 0; i < _assets.Length; i++)
            {
                var asset = _assets[i];
                if (string.IsNullOrWhiteSpace(asset.Address) || asset.Address.Trim() != asset.Address)
                {
                    throw new ArgumentException("Manifest contains an empty asset address.", nameof(assets));
                }

                if (string.IsNullOrEmpty(asset.AssetPath) || !asset.AssetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    || asset.AssetPath.Contains("\\") || asset.AssetPath.Contains("//") || asset.AssetPath.Contains("/../")
                    || asset.AssetPath.Contains("/./") || asset.AssetPath.EndsWith("/..", StringComparison.Ordinal)
                    || !Enum.IsDefined(typeof(AssetKind), asset.Kind)
                    || (asset.Kind == AssetKind.Scene) != asset.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Invalid asset path or kind: " + asset.Address, nameof(assets));

                if (!_assetsByAddress.TryAdd(asset.Address, asset))
                {
                    throw new ArgumentException("Manifest contains duplicate asset address: " + asset.Address, nameof(assets));
                }

                if (asset.BundleIndex < 0 || asset.BundleIndex >= _bundles.Length)
                {
                    throw new ArgumentException("Asset references an invalid bundle index: " + asset.Address, nameof(assets));
                }
            }

            var bundleNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _bundles.Length; i++)
            {
                var bundle = _bundles[i];
                if (!bundleNames.Add(bundle.Name) || !FileIntegrity.IsHash(bundle.Hash) || bundle.Size < 0)
                    throw new ArgumentException("Invalid bundle name, hash or size: " + bundle.Name, nameof(bundles));
                foreach (var character in bundle.Name)
                    if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '_' || character == '-'))
                        throw new ArgumentException("Unsafe bundle name: " + bundle.Name, nameof(bundles));
                var dependencies = _bundles[i].Dependencies;
                var unique = new HashSet<int>();
                for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
                {
                    var dependency = dependencies[dependencyIndex];
                    if (dependency < 0 || dependency >= _bundles.Length || dependency == i || !unique.Add(dependency))
                    {
                        throw new ArgumentException("Bundle contains an invalid dependency index: " + _bundles[i].Name, nameof(bundles));
                    }
                }
            }

            // Every dependency list is a complete closure, including cycles but excluding itself.
            for (var i = 0; i < _bundles.Length; i++)
            {
                var closure = new HashSet<int>(_bundles[i].Dependencies);
                foreach (var dependency in _bundles[i].Dependencies)
                    foreach (var transitive in _bundles[dependency].Dependencies)
                        if (transitive != i && !closure.Contains(transitive))
                            throw new ArgumentException("Dependency closure is incomplete: " + _bundles[i].Name, nameof(bundles));
            }
        }

        public int Version { get; }
        public IReadOnlyList<BundleInfo> Bundles => _bundleView;
        public IReadOnlyList<AssetInfo> Assets => _assetView;

        public static AssetManifest FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Manifest JSON is empty.", nameof(json));
            }

            var file = JsonUtility.FromJson<AssetManifestFile>(json);
            if (file == null)
            {
                throw new ArgumentException("Manifest JSON could not be parsed.", nameof(json));
            }

            var bundleData = file.bundles ?? Array.Empty<ManifestBundleData>();
            var assetData = file.assets ?? Array.Empty<ManifestAssetData>();
            var bundles = new BundleInfo[bundleData.Length];
            var assets = new AssetInfo[assetData.Length];

            for (var i = 0; i < bundleData.Length; i++)
            {
                var data = bundleData[i] ?? throw new ArgumentException("Manifest contains a null bundle entry.", nameof(json));
                if (string.IsNullOrWhiteSpace(data.name))
                {
                    throw new ArgumentException("Manifest contains a bundle without a name.", nameof(json));
                }

                bundles[i] = new BundleInfo(data.name, data.hash ?? string.Empty, data.size, data.dependencies);
            }

            for (var i = 0; i < assetData.Length; i++)
            {
                var data = assetData[i] ?? throw new ArgumentException("Manifest contains a null asset entry.", nameof(json));
                assets[i] = new AssetInfo(data.address, data.assetPath, data.bundleIndex, data.kind);
            }

            return new AssetManifest(file.version, bundles, assets);
        }

        public static AssetManifest FromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Asset manifest file was not found.", path);
            }

            return FromJson(File.ReadAllText(path));
        }

        public string ToJson(bool prettyPrint = true)
        {
            var file = new AssetManifestFile
            {
                version = Version,
                bundles = new ManifestBundleData[_bundles.Length],
                assets = new ManifestAssetData[_assets.Length]
            };

            for (var i = 0; i < _bundles.Length; i++)
            {
                var bundle = _bundles[i];
                var dependencies = new int[bundle.Dependencies.Count];
                for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
                {
                    dependencies[dependencyIndex] = bundle.Dependencies[dependencyIndex];
                }

                file.bundles[i] = new ManifestBundleData
                {
                    name = bundle.Name,
                    hash = bundle.Hash,
                    size = bundle.Size,
                    dependencies = dependencies
                };
            }

            for (var i = 0; i < _assets.Length; i++)
            {
                var asset = _assets[i];
                file.assets[i] = new ManifestAssetData
                {
                    address = asset.Address,
                    assetPath = asset.AssetPath,
                    bundleIndex = asset.BundleIndex,
                    kind = asset.Kind
                };
            }

            return JsonUtility.ToJson(file, prettyPrint);
        }

        public bool TryGetAsset(string address, out AssetInfo info)
        {
            if (address == null) { info = null; return false; }
            return _assetsByAddress.TryGetValue(address, out info);
        }

        public AssetInfo GetAsset(string address)
        {
            if (!TryGetAsset(address, out var info))
            {
                throw new KeyNotFoundException("Asset address was not found: " + address);
            }

            return info;
        }

        public BundleInfo GetBundle(int index)
        {
            if (index < 0 || index >= _bundles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _bundles[index];
        }
    }
}
