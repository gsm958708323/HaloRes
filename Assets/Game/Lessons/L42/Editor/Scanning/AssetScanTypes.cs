using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Editor.Scanning.L42
{
    public enum AssetScanErrorCode
    {
        None = 0,
        RuleSetNull = 1,
        RuleNull = 2,
        RuleNameEmpty = 3,
        DuplicateRuleName = 4,
        InvalidSourcePathPrefix = 5,
        DuplicateRulePrefix = 6,
        InvalidKeyPrefix = 7,
        BundleGroupNameEmpty = 8,
        InvalidBundleGroupName = 9,
        OverrideNull = 10,
        DuplicateOverride = 11,
        OverrideOutsideRule = 12,
        InvalidOverrideKey = 13,
        NoRuleMatched = 14,
        KeyGenerationFailed = 15,
        InvalidKeyFormat = 16,
        DuplicateKey = 17,
        ScanRootMissing = 18
    }

    public enum AssetScanWarningCode
    {
        None = 0,
        RuleMatchedNothing = 1,
        AssetGuidMissing = 2
    }

    [Serializable]
    public sealed class AssetScanError
    {
        public AssetScanErrorCode Code;
        public string SourcePath;
        public string AssetKey;
        public string Message;
    }

    [Serializable]
    public sealed class AssetScanWarning
    {
        public AssetScanWarningCode Code;
        public string SourcePath;
        public string AssetKey;
        public string Message;
    }

    [Serializable]
    public sealed class AssetBindingRecord
    {
        public string SourcePath;
        public string AssetGuid;
        public string AssetKey;
        public string BundleGroupName;
        public string RuleName;
        public bool IsExplicitKey;
    }

    [Serializable]
    public sealed class AssetBundleGroupRecord
    {
        public string BundleGroupName;
        public List<string> AssetKeys = new List<string>();
        public List<string> SourcePaths = new List<string>();
    }

    [Serializable]
    public sealed class AssetScanReport
    {
        public string GeneratedAtUtc;
        public List<string> ScanRoots = new List<string>();
        public int ScannedAssetCount;
        public List<AssetBindingRecord> Bindings = new List<AssetBindingRecord>();
        public List<AssetBundleGroupRecord> Groups = new List<AssetBundleGroupRecord>();
        public List<AssetScanError> Errors = new List<AssetScanError>();
        public List<AssetScanWarning> Warnings = new List<AssetScanWarning>();

        public bool HasFatalErrors
        {
            get { return Errors != null && Errors.Count > 0; }
        }
    }
}
