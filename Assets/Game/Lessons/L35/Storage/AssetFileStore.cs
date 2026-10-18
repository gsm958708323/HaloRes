using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.AssetSystem.Storage.L35
{
    public sealed class AssetFileStore
    {
        public const int ActiveStateSchemaVersion = 1;

        public string RootDirectory { get; }
        public string ContentDirectory { get; }
        public string StateDirectory { get; }
        public string ActiveStatePath { get; }
        public string ActiveStateTempPath { get; }
        public string ActiveStateBackupPath { get; }

        public AssetFileStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException(
                    "rootDirectory must not be empty.",
                    nameof(rootDirectory));
            }

            RootDirectory = Path.GetFullPath(rootDirectory);
            ContentDirectory = Path.Combine(RootDirectory, "content");
            StateDirectory = Path.Combine(RootDirectory, "state");
            ActiveStatePath = Path.Combine(StateDirectory, "active.json");
            ActiveStateTempPath = Path.Combine(StateDirectory, "active.json.tmp");
            ActiveStateBackupPath = Path.Combine(StateDirectory, "active.json.bak");
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(ContentDirectory);
            Directory.CreateDirectory(StateDirectory);
        }

        public static bool IsSimpleFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value == "." || value == "..")
            {
                return false;
            }

            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
            {
                return false;
            }

            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            return true;
        }

        public string GetBundlePath(
            string contentVersion,
            string bundleFileName)
        {
            if (!IsSimpleFileName(contentVersion))
            {
                throw new ArgumentException(
                    "contentVersion must be a simple directory name.",
                    nameof(contentVersion));
            }

            if (!IsSimpleFileName(bundleFileName))
            {
                throw new ArgumentException(
                    "bundleFileName must be a simple file name.",
                    nameof(bundleFileName));
            }

            return Path.Combine(
                ContentDirectory,
                contentVersion,
                bundleFileName);
        }

        public bool VerifyBundle(
            string contentVersion,
            string bundleFileName,
            string expectedSha256Hex,
            out string actualHex,
            out string error)
        {
            var path = GetBundlePath(contentVersion, bundleFileName);

            if (!File.Exists(path))
            {
                actualHex = null;
                error = "Bundle file is missing: " + path;
                return false;
            }

            return BundleHashVerifier.VerifySha256Hex(
                path,
                expectedSha256Hex,
                out actualHex,
                out error);
        }

        public BundleCommitResult CommitBundleFromTemp(
            string tempFilePath,
            string contentVersion,
            string bundleFileName,
            string expectedSha256Hex)
        {
            string targetPath = null;

            if (string.IsNullOrWhiteSpace(tempFilePath))
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "tempFilePath is empty."),
                    targetPath);
            }

            if (!IsSimpleFileName(contentVersion))
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "Invalid contentVersion: " + contentVersion),
                    targetPath);
            }

            if (!IsSimpleFileName(bundleFileName))
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "Invalid bundleFileName: " + bundleFileName),
                    targetPath);
            }

            if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "expectedSha256Hex is empty."),
                    targetPath);
            }

            EnsureDirectories();

            if (!File.Exists(tempFilePath))
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.IoError,
                        "Temp file does not exist: " + tempFilePath),
                    targetPath);
            }

            var versionDirectory = Path.Combine(
                ContentDirectory,
                contentVersion);

            targetPath = Path.Combine(versionDirectory, bundleFileName);

            try
            {
                if (!BundleHashVerifier.VerifySha256Hex(
                    tempFilePath,
                    expectedSha256Hex,
                    out var tempActualHash,
                    out var tempHashError))
                {
                    TryDeleteFile(tempFilePath);

                    return BundleCommitResult.Failed(
                        new StorageError(
                            StorageErrorCode.HashMismatch,
                            "Temp bundle hash verification failed. actual=" +
                            tempActualHash + ", error=" + tempHashError),
                        targetPath);
                }

                Directory.CreateDirectory(versionDirectory);

                if (File.Exists(targetPath) &&
                    BundleHashVerifier.VerifySha256Hex(
                        targetPath,
                        expectedSha256Hex,
                        out var existingActualHash,
                        out var existingHashError))
                {
                    TryDeleteFile(tempFilePath);
                    return BundleCommitResult.AlreadyExists(targetPath);
                }

                MoveWithReplace(tempFilePath, targetPath);

                if (!BundleHashVerifier.VerifySha256Hex(
                    targetPath,
                    expectedSha256Hex,
                    out var finalActualHash,
                    out var finalHashError))
                {
                    return BundleCommitResult.Failed(
                        new StorageError(
                            StorageErrorCode.IoError,
                            "Final bundle hash verification failed. actual=" +
                            finalActualHash + ", error=" + finalHashError),
                        targetPath);
                }

                return BundleCommitResult.Committed(targetPath);
            }
            catch (Exception exception)
            {
                return BundleCommitResult.Failed(
                    new StorageError(
                        StorageErrorCode.IoError,
                        exception.GetType().Name + ": " + exception.Message),
                    targetPath);
            }
        }

        public ActivationResult ActivateVersion(
            string contentVersion,
            IReadOnlyList<BundleHashExpectation> expectedBundles)
        {
            if (!IsSimpleFileName(contentVersion))
            {
                return ActivationResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "Invalid contentVersion: " + contentVersion),
                    ActiveStatePath);
            }

            if (expectedBundles == null || expectedBundles.Count == 0)
            {
                return ActivationResult.Failed(
                    new StorageError(
                        StorageErrorCode.InvalidArgument,
                        "expectedBundles is null or empty."),
                    ActiveStatePath);
            }

            EnsureDirectories();

            var versionDirectory = Path.Combine(
                ContentDirectory,
                contentVersion);

            if (!Directory.Exists(versionDirectory))
            {
                return ActivationResult.Failed(
                    new StorageError(
                        StorageErrorCode.BundleMissing,
                        "Version directory does not exist: " + versionDirectory),
                    ActiveStatePath);
            }

            var records = new BundleHashRecord[expectedBundles.Count];

            for (var i = 0; i < expectedBundles.Count; i++)
            {
                var expectation = expectedBundles[i];

                if (!IsSimpleFileName(expectation.BundleFileName))
                {
                    return ActivationResult.Failed(
                        new StorageError(
                            StorageErrorCode.InvalidArgument,
                            "Invalid bundle file name: " +
                            expectation.BundleFileName),
                        ActiveStatePath);
                }

                if (string.IsNullOrWhiteSpace(expectation.Sha256Hex))
                {
                    return ActivationResult.Failed(
                        new StorageError(
                            StorageErrorCode.InvalidArgument,
                            "Expected hash is empty for bundle: " +
                            expectation.BundleFileName),
                        ActiveStatePath);
                }

                var bundlePath = Path.Combine(
                    versionDirectory,
                    expectation.BundleFileName);

                if (!File.Exists(bundlePath))
                {
                    return ActivationResult.Failed(
                        new StorageError(
                            StorageErrorCode.BundleMissing,
                            "Bundle file is missing: " + bundlePath),
                        ActiveStatePath);
                }

                if (!BundleHashVerifier.VerifySha256Hex(
                    bundlePath,
                    expectation.Sha256Hex,
                    out var actualHash,
                    out var hashError))
                {
                    return ActivationResult.Failed(
                        new StorageError(
                            StorageErrorCode.HashMismatch,
                            "Bundle hash verification failed before activation. file=" +
                            bundlePath + ", error=" + hashError),
                        ActiveStatePath);
                }

                records[i] = new BundleHashRecord
                {
                    bundleFileName = expectation.BundleFileName,
                    sha256Hex = actualHash
                };
            }

            var record = new ActiveStateRecord
            {
                schemaVersion = ActiveStateSchemaVersion,
                contentVersion = contentVersion,
                activatedAtUtc = DateTime.UtcNow.ToString("o"),
                bundles = records
            };

            var json = JsonUtility.ToJson(record, true);

            try
            {
                WriteAllTextWithFlush(ActiveStateTempPath, json);
                MoveWithReplace(ActiveStateTempPath, ActiveStatePath);
                TryDeleteFile(ActiveStateBackupPath);

                return ActivationResult.Succeeded(ActiveStatePath);
            }
            catch (Exception exception)
            {
                TryDeleteFile(ActiveStateTempPath);

                return ActivationResult.Failed(
                    new StorageError(
                        StorageErrorCode.ActivationFailed,
                        exception.GetType().Name + ": " + exception.Message),
                    ActiveStatePath);
            }
        }

        public bool TryLoadActiveState(
            out ActiveStateRecord state,
            out StorageError error)
        {
            if (!File.Exists(ActiveStatePath) &&
                File.Exists(ActiveStateBackupPath))
            {
                try
                {
                    File.Copy(ActiveStateBackupPath, ActiveStatePath, true);
                }
                catch
                {
                    // Best effort. The read below decides the outcome.
                }
            }

            if (TryReadActiveStateFile(ActiveStatePath, out state, out error))
            {
                return true;
            }

            if (File.Exists(ActiveStateBackupPath) &&
                TryReadActiveStateFile(ActiveStateBackupPath, out state, out error))
            {
                try
                {
                    File.Copy(ActiveStateBackupPath, ActiveStatePath, true);
                }
                catch
                {
                    // Best effort.
                }

                return true;
            }

            state = null;
            return false;
        }

        private static bool TryReadActiveStateFile(
            string path,
            out ActiveStateRecord state,
            out StorageError error)
        {
            state = null;
            error = default(StorageError);

            if (!File.Exists(path))
            {
                error = new StorageError(
                    StorageErrorCode.ActiveStateCorrupted,
                    "Active state does not exist: " + path);
                return false;
            }

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                state = JsonUtility.FromJson<ActiveStateRecord>(json);

                if (state == null ||
                    state.schemaVersion != ActiveStateSchemaVersion)
                {
                    error = new StorageError(
                        StorageErrorCode.ActiveStateCorrupted,
                        "Active state schema is invalid. path=" + path);
                    state = null;
                    return false;
                }

                if (!IsSimpleFileName(state.contentVersion))
                {
                    error = new StorageError(
                        StorageErrorCode.ActiveStateCorrupted,
                        "Active state contentVersion is invalid: " +
                        state.contentVersion);
                    state = null;
                    return false;
                }

                if (state.bundles == null || state.bundles.Length == 0)
                {
                    error = new StorageError(
                        StorageErrorCode.ActiveStateCorrupted,
                        "Active state has no bundles.");
                    state = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = new StorageError(
                    StorageErrorCode.ActiveStateCorrupted,
                    exception.GetType().Name + ": " + exception.Message);
                state = null;
                return false;
            }
        }

        private static void WriteAllTextWithFlush(
            string path,
            string text)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false)))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static void MoveWithReplace(
            string sourcePath,
            string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(sourcePath, destinationPath);
                return;
            }

            var backupPath = destinationPath + ".bak";

            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(sourcePath, destinationPath, backupPath);

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Fall through to the portable sequence.
            }
            catch (IOException)
            {
                // Fall through to the portable sequence.
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(destinationPath, backupPath);

            try
            {
                File.Move(sourcePath, destinationPath);
            }
            catch
            {
                if (!File.Exists(destinationPath) &&
                    File.Exists(backupPath))
                {
                    File.Move(backupPath, destinationPath);
                }

                throw;
            }

            TryDeleteFile(backupPath);
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
