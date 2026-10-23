using System;
using System.Collections.Generic;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;

namespace Game.AssetSystem.Content
{
    /// <summary>
    /// One bundle as described by the active content manifest. This is a
    /// descriptor, not a runtime entry: nothing here is loaded or ref-counted.
    /// </summary>
    public sealed class RuntimeBundle
    {
        private readonly BundleId[] _directDependencies;

        public BundleId Id { get; }
        public string BundleGroupName { get; }
        public string FileName { get; }
        public string AbsolutePath { get; }
        public long SizeBytes { get; }
        public string Sha256Hex { get; }

        /// <summary>
        /// Bundles that must be loaded before this one. A bundle payload keeps
        /// references into its dependencies, so the runtime acquires a lease on
        /// each of them and releases those leases only after the payload is
        /// unloaded.
        /// </summary>
        public IReadOnlyList<BundleId> DirectDependencies
        {
            get { return _directDependencies; }
        }

        public RuntimeBundle(
            BundleId id,
            string bundleGroupName,
            string fileName,
            string absolutePath,
            long sizeBytes,
            string sha256Hex)
            : this(
                id,
                bundleGroupName,
                fileName,
                absolutePath,
                sizeBytes,
                sha256Hex,
                null)
        {
        }

        public RuntimeBundle(
            BundleId id,
            string bundleGroupName,
            string fileName,
            string absolutePath,
            long sizeBytes,
            string sha256Hex,
            BundleId[] directDependencies)
        {
            Id = id;
            BundleGroupName = bundleGroupName;
            FileName = fileName;
            AbsolutePath = absolutePath;
            SizeBytes = sizeBytes;
            Sha256Hex = sha256Hex;
            _directDependencies = directDependencies ?? Array.Empty<BundleId>();
        }
    }

    /// <summary>
    /// One asset as described by the active content manifest.
    /// </summary>
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

    /// <summary>
    /// Describes what the currently activated version contains.
    ///
    /// This type used to also carry a second BundleRuntime and a second
    /// AssetCache, which is how the project ended up with two answers to
    /// "how many refs does this bundle have". It now only answers one
    /// question: what does the active version look like. The runtimes are
    /// built from it through the catalogs below.
    /// </summary>
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

        /// <summary>Every bundle the active content declares, in no order.</summary>
        public IEnumerable<RuntimeBundle> AllBundles
        {
            get { return _bundles.Values; }
        }

        /// <summary>Every asset the active content declares, in no order.</summary>
        public IEnumerable<RuntimeAsset> AllAssets
        {
            get { return _assets.Values; }
        }

        /// <summary>
        /// Projection into the single bundle runtime's catalog. The group name
        /// is the bundle's identity for diagnostics; the file path is what the
        /// loader needs.
        /// </summary>
        public BundleCatalog CreateBundleCatalog()
        {
            var list = new List<BundleInfo>(_bundles.Count);

            foreach (var bundle in _bundles.Values)
            {
                var dependencies = bundle.DirectDependencies;
                var dependencyArray = dependencies == null ||
                                      dependencies.Count == 0
                    ? Array.Empty<BundleId>()
                    : new BundleId[dependencies.Count];

                for (var i = 0; i < dependencyArray.Length; i++)
                {
                    dependencyArray[i] = dependencies[i];
                }

                list.Add(new BundleInfo
                {
                    Id = bundle.Id,
                    Name = bundle.BundleGroupName,
                    DirectDependencies = dependencyArray,
                    SourcePath = bundle.AbsolutePath
                });
            }

            return new BundleCatalog(list);
        }

        /// <summary>Projection into the single asset cache's catalog.</summary>
        public AssetCatalog CreateAssetCatalog()
        {
            var list = new List<AssetEntry>(_assets.Count);

            foreach (var asset in _assets.Values)
            {
                list.Add(new AssetEntry
                {
                    Key = asset.Key,
                    BundleId = asset.BundleId,
                    InternalAssetPath = asset.InternalPath
                });
            }

            return new AssetCatalog(list);
        }
    }
}
