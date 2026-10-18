using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Patch.L35
{
    [Serializable]
    public sealed class PatchBundleDescriptor
    {
        public string bundleFileName;
        public string url;
        public long sizeBytes;
        public string sha256Hex;
    }

    [Serializable]
    public sealed class PatchManifest
    {
        public int schemaVersion;
        public string contentVersion;
        public PatchBundleDescriptor[] bundles;
    }

    public enum PatchApplyStatus
    {
        Succeeded,
        AlreadyActive,
        Failed,
        Canceled
    }

    public enum PatchApplyErrorCode
    {
        None,
        InvalidManifest,
        ActiveStateUnavailable,
        TargetVersionIsActiveButCorrupted,
        DownloadFailed,
        HashMismatch,
        CommitFailed,
        ActivationFailed,
        IoError,
        Canceled
    }

    public enum PatchPhase
    {
        Validating,
        Downloading,
        Verifying,
        Activating,
        Completed,
        Failed
    }

    public readonly struct PatchProgress
    {
        public PatchPhase Phase { get; }
        public string BundleFileName { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public string Message { get; }

        public PatchProgress(
            PatchPhase phase,
            string bundleFileName,
            long downloadedBytes,
            long totalBytes,
            string message)
        {
            Phase = phase;
            BundleFileName = bundleFileName;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Message = message;
        }
    }

    public sealed class PatchBundleReport
    {
        public string BundleFileName;
        public long ResumeOffset;
        public long BytesTransferredThisRun;
        public bool RangeFallback;
        public bool Skipped;
        public bool Committed;
        public string Error;
    }

    public sealed class PatchApplyResult
    {
        public PatchApplyStatus Status;
        public PatchApplyErrorCode ErrorCode;
        public string Message;
        public string ActiveVersionBefore;
        public string ActiveVersionAfter;
        public int BundlesSkipped;
        public int BundlesDownloaded;
        public long ResumeOffsetTotal;
        public List<PatchBundleReport> Bundles = new List<PatchBundleReport>();

        public bool Success
        {
            get
            {
                return Status == PatchApplyStatus.Succeeded ||
                       Status == PatchApplyStatus.AlreadyActive;
            }
        }
    }
}
