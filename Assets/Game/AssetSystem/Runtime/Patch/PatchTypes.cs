using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Patch
{
    /// <summary>
    /// One bundle in a patch manifest.
    ///
    /// This is the single descriptor shape. The project used to have two:
    /// a camelCase one for the download lesson and a PascalCase one for the
    /// integration layer, differing in whether they carried a URL and whether
    /// they knew about dependencies. Anything that read one could not read the
    /// other, so the same manifest could mean two different things.
    /// </summary>
    [Serializable]
    public sealed class PatchBundleDescriptor
    {
        public string BundleGroupName;
        public string FileName;
        public long SizeBytes;
        public string Sha256Hex;

        /// <summary>
        /// Download URL. Required by <see cref="IPatchTransport"/>; a locally
        /// installed manifest may leave it empty when no download is planned.
        /// </summary>
        public string Url;

        /// <summary>
        /// Bundle group names that must be loaded before this bundle. Optional:
        /// manifests written before dependency tracking leave it null.
        /// </summary>
        public string[] Dependencies;
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
        AlreadyActive = 1,
        Failed = 2,
        Canceled = 3
    }

    public enum PatchApplyErrorCode
    {
        None = 0,
        InvalidManifest = 1,
        ActiveStateUnavailable = 2,
        TargetVersionIsActiveButCorrupted = 3,
        DownloadFailed = 4,
        HashMismatch = 5,
        CommitFailed = 6,
        ActivationFailed = 7,
        IoError = 8,
        Canceled = 9
    }

    public enum PatchPhase
    {
        Validating = 0,
        Downloading = 1,
        Verifying = 2,
        Activating = 3,
        Completed = 4,
        Failed = 5
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

        public string ContentVersion;
        public string BundleRoot;

        public string ActiveVersionBefore;
        public string ActiveVersionAfter;

        public int BundlesSkipped;
        public int BundlesDownloaded;
        public int BundlesCommitted;
        public long ResumeOffsetTotal;

        public List<PatchBundleReport> Bundles = new List<PatchBundleReport>();

        /// <summary>
        /// Alias for <see cref="ResumeOffsetTotal"/>. The integration layer used
        /// this name before the two result types were merged.
        /// </summary>
        public long ResumeBytesTotal
        {
            get { return ResumeOffsetTotal; }
        }

        public bool Success
        {
            get
            {
                return Status == PatchApplyStatus.Succeeded ||
                       Status == PatchApplyStatus.AlreadyActive;
            }
        }
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
