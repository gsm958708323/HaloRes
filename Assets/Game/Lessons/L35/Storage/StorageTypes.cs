using System;

namespace Game.AssetSystem.Storage.L35
{
    [Serializable]
    public sealed class BundleHashRecord
    {
        public string bundleFileName;
        public string sha256Hex;
    }

    [Serializable]
    public sealed class ActiveStateRecord
    {
        public int schemaVersion;
        public string contentVersion;
        public string activatedAtUtc;
        public BundleHashRecord[] bundles;
    }

    public readonly struct BundleHashExpectation
    {
        public string BundleFileName { get; }
        public string Sha256Hex { get; }

        public BundleHashExpectation(string bundleFileName, string sha256Hex)
        {
            BundleFileName = bundleFileName;
            Sha256Hex = sha256Hex;
        }
    }

    public enum StorageErrorCode
    {
        None,
        InvalidArgument,
        IoError,
        HashMismatch,
        BundleMissing,
        ActiveStateCorrupted,
        ActivationFailed
    }

    public readonly struct StorageError
    {
        public StorageErrorCode Code { get; }
        public string Message { get; }

        public StorageError(StorageErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public readonly struct BundleCommitResult
    {
        public bool Success { get; }
        public bool AlreadyPresent { get; }
        public string TargetPath { get; }
        public StorageError Error { get; }

        private BundleCommitResult(
            bool success,
            bool alreadyPresent,
            string targetPath,
            StorageError error)
        {
            Success = success;
            AlreadyPresent = alreadyPresent;
            TargetPath = targetPath;
            Error = error;
        }

        public static BundleCommitResult Committed(string targetPath)
        {
            return new BundleCommitResult(
                true,
                false,
                targetPath,
                default(StorageError));
        }

        public static BundleCommitResult AlreadyExists(string targetPath)
        {
            return new BundleCommitResult(
                true,
                true,
                targetPath,
                default(StorageError));
        }

        public static BundleCommitResult Failed(
            StorageError error,
            string targetPath)
        {
            return new BundleCommitResult(
                false,
                false,
                targetPath,
                error);
        }
    }

    public readonly struct ActivationResult
    {
        public bool Success { get; }
        public StorageError Error { get; }
        public string ActiveStatePath { get; }

        private ActivationResult(
            bool success,
            StorageError error,
            string activeStatePath)
        {
            Success = success;
            Error = error;
            ActiveStatePath = activeStatePath;
        }

        public static ActivationResult Succeeded(string activeStatePath)
        {
            return new ActivationResult(
                true,
                default(StorageError),
                activeStatePath);
        }

        public static ActivationResult Failed(
            StorageError error,
            string activeStatePath)
        {
            return new ActivationResult(
                false,
                error,
                activeStatePath);
        }
    }
}
