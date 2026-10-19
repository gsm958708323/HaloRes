using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Integration
{
    public sealed class RuntimeBundle
    {
        public BundleId Id { get; }
        public string BundleGroupName { get; }
        public string FileName { get; }
        public string AbsolutePath { get; }
        public long SizeBytes { get; }
        public string Sha256Hex { get; }

        public RuntimeBundle(
            BundleId id,
            string bundleGroupName,
            string fileName,
            string absolutePath,
            long sizeBytes,
            string sha256Hex)
        {
            Id = id;
            BundleGroupName = bundleGroupName;
            FileName = fileName;
            AbsolutePath = absolutePath;
            SizeBytes = sizeBytes;
            Sha256Hex = sha256Hex;
        }
    }

    public sealed class RuntimeAsset
    {
        public AssetKey Key { get; }
        public BundleId BundleId { get; }
        public string InternalPath { get; }

        public RuntimeAsset(AssetKey key, BundleId bundleId, string internalPath)
        {
            Key = key;
            BundleId = bundleId;
            InternalPath = internalPath;
        }
    }

    public sealed class ActiveContentContext
    {
        private readonly Dictionary<AssetKey, RuntimeAsset> _assets;
        private readonly Dictionary<BundleId, RuntimeBundle> _bundles;

        public string ContentVersion { get; }
        public string PlatformKey { get; }
        public string BundleRoot { get; }

        public int AssetCount
        {
            get { return _assets.Count; }
        }

        public int BundleCount
        {
            get { return _bundles.Count; }
        }

        public ActiveContentContext(
            string contentVersion,
            string platformKey,
            string bundleRoot,
            IEnumerable<RuntimeAsset> assets,
            IEnumerable<RuntimeBundle> bundles)
        {
            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                throw new ArgumentException(
                    "contentVersion must not be empty.",
                    nameof(contentVersion));
            }

            if (string.IsNullOrWhiteSpace(platformKey))
            {
                throw new ArgumentException(
                    "platformKey must not be empty.",
                    nameof(platformKey));
            }

            ContentVersion = contentVersion;
            PlatformKey = platformKey;
            BundleRoot = bundleRoot ?? string.Empty;
            _assets = new Dictionary<AssetKey, RuntimeAsset>();
            _bundles = new Dictionary<BundleId, RuntimeBundle>();

            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset != null)
                    {
                        _assets[asset.Key] = asset;
                    }
                }
            }

            if (bundles != null)
            {
                foreach (var bundle in bundles)
                {
                    if (bundle != null)
                    {
                        _bundles[bundle.Id] = bundle;
                    }
                }
            }
        }

        public bool TryGetAsset(AssetKey key, out RuntimeAsset asset)
        {
            return _assets.TryGetValue(key, out asset);
        }

        public bool TryGetBundle(BundleId id, out RuntimeBundle bundle)
        {
            return _bundles.TryGetValue(id, out bundle);
        }
    }
}
