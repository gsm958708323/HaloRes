using System;

namespace Game.AssetSystem.Patch.L47
{
    public enum PatchActivationStatus
    {
        Succeeded = 0,
        AlreadyActive = 1,
        PlatformMismatch = 2,
        BundleMissing = 3,
        BundleSizeMismatch = 4,
        HashMismatch = 5,
        InvalidManifest = 6,
        RejectedActiveHandles = 7,
        IoError = 8,
        Canceled = 9,
        InternalError = 10
    }

    public enum PatchActivationErrorCode
    {
        None = 0,
        InvalidManifest = 1,
        PlatformMismatch = 2,
        BundleMissing = 3,
        BundleSizeMismatch = 4,
        HashMismatch = 5,
        InvalidBundleRoot = 6,
        ActiveHandlesExist = 7,
        IoError = 8,
        Canceled = 9,
        InternalError = 10
    }

    [Serializable]
    public sealed class PatchBundleDescriptor
    {
        // 物理文件名，例如 "bundle_core_ab12cd34.bundle"。
        public string bundleFileName;

        // 逻辑 Bundle 组名，多个 Asset 可映射到同一组。
        public string bundleGroupName;

        public long sizeBytes;
        public string sha256Hex;
    }

    [Serializable]
    public sealed class PatchAssetDescriptor
    {
        public string assetKey;
        public string bundleGroupName;
        public string internalAssetPath;
    }

    [Serializable]
    public sealed class PatchManifestDescriptor
    {
        public int schemaVersion;
        public string contentVersion;
        public string targetPlatformKey;
        public PatchBundleDescriptor[] bundles;
        public PatchAssetDescriptor[] assets;
    }

    public sealed class PatchActivationResult
    {
        public PatchActivationStatus Status;
        public PatchActivationErrorCode ErrorCode;
        public string Message;
        public string ActiveVersionBefore;
        public string ActiveVersionAfter;
        public int BundlesChecked;
    }

    public interface IActiveHandleCounter
    {
        int Count { get; }
    }
}
