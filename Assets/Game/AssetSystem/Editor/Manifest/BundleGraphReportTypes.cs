using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Manifest
{
    public enum BundleGraphErrorCode
    {
        None = 0,
        ScanReportNull = 1,
        ScanReportHasErrors = 2,
        NoBindings = 3,
        InvalidContentVersion = 4,
        DuplicateSourcePath = 5,
        DuplicateAssetKey = 6,
        DuplicateBundleGroup = 7,
        UnmappedDependency = 8,
        DependencyQueryFailed = 9,
        CircularBundleDependency = 10,
        InvalidManifest = 11
    }

    public enum BundleGraphWarningCode
    {
        None = 0,
        CrossGroupDependency = 1
    }

    [Serializable]
    public sealed class BundleGraphError
    {
        public BundleGraphErrorCode Code;
        public string AssetKey;
        public string SourcePath;
        public string DependencyPath;
        public string Message;
    }

    [Serializable]
    public sealed class BundleGraphWarning
    {
        public BundleGraphWarningCode Code;
        public string AssetKey;
        public string Message;
    }

    [Serializable]
    public sealed class BundleDependencyEdgeRecord
    {
        public int FromBundleId;
        public string FromBundleGroupName;
        public int ToBundleId;
        public string ToBundleGroupName;
        public string FromAssetKey;
        public string DependencyPath;
    }

    [Serializable]
    public sealed class BundleGraphBuildResult
    {
        public bool Success;
        public ContentManifestRecord Manifest;
        public int AssetCount;
        public int BundleCount;
        public List<BundleGraphError> Errors = new List<BundleGraphError>();
        public List<BundleGraphWarning> Warnings = new List<BundleGraphWarning>();
        public List<BundleDependencyEdgeRecord> Edges = new List<BundleDependencyEdgeRecord>();
    }
}
