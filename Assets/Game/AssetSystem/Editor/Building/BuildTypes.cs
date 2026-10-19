using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Building
{
    public enum BuildErrorCode
    {
        None = 0,
        InvalidContentVersion = 1,
        InvalidBinding = 2,
        DuplicateSourcePath = 3,
        DuplicateAssetKey = 4,
        BuildPipelineFailed = 5,
        BuildManifestNull = 6,
        ExpectedBundleNotBuilt = 7,
        BundleFileMissing = 8,
        HashComputeFailed = 9,
        HashMismatch = 10,
        SizeMismatch = 11,
        InternalAssetNameMissing = 12,
        BundleLoadFailed = 13,
        AssetLoadFailed = 14,
        PlatformMismatch = 15,
        ManifestNull = 16,
        ExpectedAssetMissing = 17,
        IoError = 18
    }

    [Serializable]
    public sealed class AssetBinding
    {
        public string sourcePath;
        public string assetKey;
        public string bundleGroupName;
    }

    [Serializable]
    public sealed class AssetRecord
    {
        public string assetKey;
        public string sourcePath;
        public string internalAssetPath;
        public string bundleGroupName;
    }

    [Serializable]
    public sealed class BundleRecord
    {
        public string bundleGroupName;
        public string bundleFileName;
        public long sizeBytes;
        public string sha256Hex;
    }

    [Serializable]
    public sealed class PlatformManifest
    {
        public int schemaVersion;
        public string contentVersion;
        public string targetPlatformKey;
        public string buildTarget;
        public string generatedAtUtc;

        public List<BundleRecord> bundles =
            new List<BundleRecord>();

        public List<AssetRecord> assets =
            new List<AssetRecord>();
    }

    [Serializable]
    public sealed class BuildError
    {
        public BuildErrorCode Code;
        public string SourcePath;
        public string AssetKey;
        public string Message;
    }

    [Serializable]
    public sealed class BuildResult
    {
        public bool Success;
        public string ContentVersion;
        public string TargetPlatformKey;
        public string OutputDirectory;
        public PlatformManifest Manifest;

        public List<BundleRecord> Bundles =
            new List<BundleRecord>();

        public List<BuildError> Errors =
            new List<BuildError>();
    }

    [Serializable]
    public sealed class ValidationResult
    {
        public bool Success;
        public int AssetsChecked;
        public int BundlesLoaded;

        public List<BuildError> Errors =
            new List<BuildError>();
    }
}
