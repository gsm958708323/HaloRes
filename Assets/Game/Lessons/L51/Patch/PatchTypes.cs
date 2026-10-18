using System;

namespace Game.AssetSystem.Integration.L51
{
    [Serializable]
    public sealed class PatchBundleDescriptor
    {
        public string BundleGroupName;
        public string FileName;
        public long SizeBytes;
        public string Sha256Hex;
    }

    [Serializable]
    public sealed class PatchAssetDescriptor
    {
        public string AssetKey;
        public string BundleGroupName;
        public string InternalPath;
    }

    [Serializable]
    public sealed class PatchManifest
    {
        public int SchemaVersion;
        public string ContentVersion;
        public string PlatformKey;
        public PatchBundleDescriptor[] Bundles;
        public PatchAssetDescriptor[] Assets;
    }

    public enum PatchApplyStatus
    {
        Succeeded = 0,
        Failed = 1,
        Canceled = 2
    }

    public sealed class PatchApplyResult
    {
        public PatchApplyStatus Status;
        public string Message;
        public int BundlesCommitted;
        public int BundlesSkipped;
        public long ResumeBytesTotal;
        public string ContentVersion;
        public string BundleRoot;
    }

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
        RejectedUnsafePoint = 8,
        Canceled = 9
    }

    public sealed class PatchActivationResult
    {
        public PatchActivationStatus Status;
        public string Message;
        public string ActiveVersionBefore;
        public string ActiveVersionAfter;
        public int BundlesChecked;
    }
}
