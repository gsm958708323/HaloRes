using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Manifest.L42
{
    [Serializable]
    public sealed class ContentManifestAssetRecord
    {
        public string assetKey;
        public int bundleId;
        public string bundleGroupName;
        public string internalAssetPath;
        public string sourcePath;
    }

    [Serializable]
    public sealed class ContentManifestBundleRecord
    {
        public int bundleId;
        public string bundleGroupName;
        public List<int> directDependencies = new List<int>();
    }

    [Serializable]
    public sealed class ContentManifestRecord
    {
        public int schemaVersion;
        public string contentVersion;
        public string generatedAtUtc;
        public List<ContentManifestAssetRecord> assets = new List<ContentManifestAssetRecord>();
        public List<ContentManifestBundleRecord> bundles = new List<ContentManifestBundleRecord>();
    }
}
