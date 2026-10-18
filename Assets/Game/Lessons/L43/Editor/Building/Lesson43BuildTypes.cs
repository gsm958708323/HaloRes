using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Building.L43
{
    public enum Lesson43BuildErrorCode
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
    public sealed class Lesson43AssetBinding
    {
        public string sourcePath;
        public string assetKey;
        public string bundleGroupName;
    }

    [Serializable]
    public sealed class Lesson43AssetRecord
    {
        public string assetKey;
        public string sourcePath;
        public string internalAssetPath;
        public string bundleGroupName;
    }

    [Serializable]
    public sealed class Lesson43BundleRecord
    {
        public string bundleGroupName;
        public string bundleFileName;
        public long sizeBytes;
        public string sha256Hex;
    }

    [Serializable]
    public sealed class Lesson43PlatformManifest
    {
        public int schemaVersion;
        public string contentVersion;
        public string targetPlatformKey;
        public string buildTarget;
        public string generatedAtUtc;

        public List<Lesson43BundleRecord> bundles =
            new List<Lesson43BundleRecord>();

        public List<Lesson43AssetRecord> assets =
            new List<Lesson43AssetRecord>();
    }

    [Serializable]
    public sealed class Lesson43BuildError
    {
        public Lesson43BuildErrorCode Code;
        public string SourcePath;
        public string AssetKey;
        public string Message;
    }

    [Serializable]
    public sealed class Lesson43BuildResult
    {
        public bool Success;
        public string ContentVersion;
        public string TargetPlatformKey;
        public string OutputDirectory;
        public Lesson43PlatformManifest Manifest;

        public List<Lesson43BundleRecord> Bundles =
            new List<Lesson43BundleRecord>();

        public List<Lesson43BuildError> Errors =
            new List<Lesson43BuildError>();
    }

    [Serializable]
    public sealed class Lesson43ValidationResult
    {
        public bool Success;
        public int AssetsChecked;
        public int BundlesLoaded;

        public List<Lesson43BuildError> Errors =
            new List<Lesson43BuildError>();
    }
}
