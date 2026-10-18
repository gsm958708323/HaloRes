using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Infrastructure.L47;
using UnityEngine;

namespace Game.AssetSystem.Patch.L47
{
    public sealed class PatchActivationController
    {
        private readonly IActiveHandleCounter _handleCounter;

        public RuntimeContentContext ActiveContext { get; private set; }
        public int ActivationCount { get; private set; }

        public PatchActivationController(IActiveHandleCounter handleCounter)
        {
            _handleCounter = handleCounter;
        }

        public async Task<PatchActivationResult> ActivateAsync(
            PatchManifestDescriptor manifest,
            string bundleRoot,
            string expectedPlatformKey,
            CancellationToken cancellationToken = default)
        {
            var precheck = await Task.Run(() =>
                PreValidate(
                    manifest,
                    bundleRoot,
                    expectedPlatformKey,
                    cancellationToken)).ConfigureAwait(true);

            if (precheck != null)
            {
                precheck.ActiveVersionBefore =
                    ActiveContext == null ? null : ActiveContext.ContentVersion;
                precheck.ActiveVersionAfter = precheck.ActiveVersionBefore;
                return precheck;
            }

            if (!UnityMainThreadPump.IsMainThread)
            {
                var tcs = new TaskCompletionSource<PatchActivationResult>();
                UnityMainThreadPump.Post(() =>
                    tcs.TrySetResult(
                        CompleteOnMainThread(manifest, bundleRoot)));

                return await tcs.Task.ConfigureAwait(true);
            }

            return CompleteOnMainThread(manifest, bundleRoot);
        }

        private PatchActivationResult PreValidate(
            PatchManifestDescriptor manifest,
            string bundleRoot,
            string expectedPlatformKey,
            CancellationToken cancellationToken)
        {
            var result = new PatchActivationResult();

            if (cancellationToken.IsCancellationRequested)
            {
                result.Status = PatchActivationStatus.Canceled;
                result.ErrorCode = PatchActivationErrorCode.Canceled;
                result.Message = "Canceled before validation.";
                return result;
            }

            if (!ValidateManifestShape(manifest, out var shapeError))
            {
                result.Status = PatchActivationStatus.InvalidManifest;
                result.ErrorCode = PatchActivationErrorCode.InvalidManifest;
                result.Message = shapeError;
                return result;
            }

            if (!string.Equals(
                manifest.targetPlatformKey,
                expectedPlatformKey,
                StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.PlatformMismatch;
                result.ErrorCode = PatchActivationErrorCode.PlatformMismatch;
                result.Message = "Manifest platform " +
                    manifest.targetPlatformKey +
                    " != requested " +
                    expectedPlatformKey;
                return result;
            }

            if (string.IsNullOrEmpty(bundleRoot) || !Directory.Exists(bundleRoot))
            {
                result.Status = PatchActivationStatus.IoError;
                result.ErrorCode = PatchActivationErrorCode.InvalidBundleRoot;
                result.Message = "Bundle root invalid: " + bundleRoot;
                return result;
            }

            for (int i = 0; i < manifest.bundles.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = PatchActivationStatus.Canceled;
                    result.ErrorCode = PatchActivationErrorCode.Canceled;
                    result.Message = "Canceled mid-validation.";
                    return result;
                }

                var b = manifest.bundles[i];
                result.BundlesChecked++;

                var path = Path.Combine(bundleRoot, b.bundleFileName);

                if (!File.Exists(path))
                {
                    result.Status = PatchActivationStatus.BundleMissing;
                    result.ErrorCode = PatchActivationErrorCode.BundleMissing;
                    result.Message = "Missing bundle: " + path;
                    return result;
                }

                long size;

                try
                {
                    size = new FileInfo(path).Length;
                }
                catch (Exception exception)
                {
                    result.Status = PatchActivationStatus.IoError;
                    result.ErrorCode = PatchActivationErrorCode.IoError;
                    result.Message = "Failed to read size: " + exception.Message;
                    return result;
                }

                if (size != b.sizeBytes)
                {
                    result.Status = PatchActivationStatus.BundleSizeMismatch;
                    result.ErrorCode = PatchActivationErrorCode.BundleSizeMismatch;
                    result.Message = "Size mismatch for " + b.bundleFileName +
                        ": expected " + b.sizeBytes + ", actual " + size;
                    return result;
                }

                string hash;

                try
                {
                    hash = ComputeSha256Hex(path);
                }
                catch (Exception exception)
                {
                    result.Status = PatchActivationStatus.IoError;
                    result.ErrorCode = PatchActivationErrorCode.IoError;
                    result.Message = "Failed to hash: " + exception.Message;
                    return result;
                }

                if (!string.Equals(
                    hash,
                    b.sha256Hex,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = PatchActivationStatus.HashMismatch;
                    result.ErrorCode = PatchActivationErrorCode.HashMismatch;
                    result.Message = "Hash mismatch for " + b.bundleFileName;
                    return result;
                }
            }

            return null;
        }

        private PatchActivationResult CompleteOnMainThread(
            PatchManifestDescriptor manifest,
            string bundleRoot)
        {
            if (!UnityMainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "CompleteOnMainThread must run on the Unity main thread.");
            }

            var result = new PatchActivationResult
            {
                ActiveVersionBefore =
                    ActiveContext == null ? null : ActiveContext.ContentVersion,
                ActiveVersionAfter =
                    ActiveContext == null ? null : ActiveContext.ContentVersion
            };

            if (ActiveContext != null &&
                string.Equals(
                    ActiveContext.ContentVersion,
                    manifest.contentVersion,
                    StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.AlreadyActive;
                result.ErrorCode = PatchActivationErrorCode.None;
                result.Message = "Version " +
                    manifest.contentVersion +
                    " is already active.";
                return result;
            }

            var liveHandles = _handleCounter == null
                ? 0
                : _handleCounter.Count;

            if (liveHandles > 0)
            {
                result.Status = PatchActivationStatus.RejectedActiveHandles;
                result.ErrorCode = PatchActivationErrorCode.ActiveHandlesExist;
                result.Message = "Cannot activate while " +
                    liveHandles +
                    " live handle(s) still reference the active context.";
                return result;
            }

            var bundles = new List<RuntimeBundleEntry>();
            var groupToId =
                new Dictionary<string, RuntimeBundleId>(StringComparer.Ordinal);

            for (int i = 0; i < manifest.bundles.Length; i++)
            {
                var b = manifest.bundles[i];

                if (groupToId.ContainsKey(b.bundleGroupName))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.ErrorCode = PatchActivationErrorCode.InvalidManifest;
                    result.Message = "Duplicate bundle group in manifest: " +
                        b.bundleGroupName;
                    return result;
                }

                var id = new RuntimeBundleId(i);
                groupToId.Add(b.bundleGroupName, id);

                bundles.Add(new RuntimeBundleEntry
                {
                    Id = id,
                    BundleGroupName = b.bundleGroupName,
                    FileName = b.bundleFileName,
                    AbsolutePath = Path.Combine(bundleRoot, b.bundleFileName),
                    SizeBytes = b.sizeBytes,
                    Sha256Hex = b.sha256Hex
                });
            }

            var assets = new List<RuntimeAssetEntry>();

            for (int i = 0; i < manifest.assets.Length; i++)
            {
                var a = manifest.assets[i];

                if (!groupToId.TryGetValue(a.bundleGroupName, out var bundleId))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.ErrorCode = PatchActivationErrorCode.InvalidManifest;
                    result.Message = "Asset " + a.assetKey +
                        " references unknown bundle group " + a.bundleGroupName;
                    return result;
                }

                assets.Add(new RuntimeAssetEntry
                {
                    Key = new RuntimeAssetKey(a.assetKey),
                    BundleId = bundleId,
                    InternalAssetPath = a.internalAssetPath,
                    SourceBundleGroup = a.bundleGroupName
                });
            }

            var newContext = new RuntimeContentContext(
                manifest.contentVersion,
                manifest.targetPlatformKey,
                bundleRoot,
                assets,
                bundles);

            ActiveContext = newContext;
            ActivationCount++;

            result.Status = PatchActivationStatus.Succeeded;
            result.ErrorCode = PatchActivationErrorCode.None;
            result.Message = "Activated " +
                manifest.contentVersion +
                " with " +
                bundles.Count +
                " bundle(s) and " +
                assets.Count +
                " asset(s).";
            result.ActiveVersionAfter = manifest.contentVersion;

            Debug.Log(
                "[Activation] Swapped ActiveContext: " +
                (result.ActiveVersionBefore ?? "<none>") +
                " -> " +
                result.ActiveVersionAfter);

            return result;
        }

        private static bool ValidateManifestShape(
            PatchManifestDescriptor manifest,
            out string error)
        {
            error = null;

            if (manifest == null)
            {
                error = "Manifest is null.";
                return false;
            }

            if (manifest.schemaVersion != 1)
            {
                error = "Unsupported schemaVersion: " + manifest.schemaVersion;
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.contentVersion))
            {
                error = "Empty contentVersion.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.targetPlatformKey))
            {
                error = "Empty targetPlatformKey.";
                return false;
            }

            if (manifest.bundles == null || manifest.bundles.Length == 0)
            {
                error = "Manifest has no bundles.";
                return false;
            }

            if (manifest.assets == null || manifest.assets.Length == 0)
            {
                error = "Manifest has no assets.";
                return false;
            }

            var groups = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < manifest.bundles.Length; i++)
            {
                var b = manifest.bundles[i];

                if (b == null)
                {
                    error = "Null bundle descriptor at index " + i + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(b.bundleFileName))
                {
                    error = "Empty bundleFileName at index " + i + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(b.bundleGroupName))
                {
                    error = "Empty bundleGroupName at index " + i + ".";
                    return false;
                }

                if (!groups.Add(b.bundleGroupName))
                {
                    error = "Duplicate bundleGroupName: " + b.bundleGroupName;
                    return false;
                }

                if (b.sizeBytes <= 0)
                {
                    error = "Non-positive bundle size for " + b.bundleFileName;
                    return false;
                }

                if (string.IsNullOrEmpty(b.sha256Hex) || b.sha256Hex.Length != 64)
                {
                    error = "Invalid SHA256 for " + b.bundleFileName;
                    return false;
                }
            }

            for (int i = 0; i < manifest.assets.Length; i++)
            {
                var a = manifest.assets[i];

                if (a == null)
                {
                    error = "Null asset descriptor at index " + i + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(a.assetKey))
                {
                    error = "Empty assetKey at index " + i + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(a.bundleGroupName))
                {
                    error = "Empty asset bundleGroupName at index " + i + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(a.internalAssetPath))
                {
                    error = "Empty internalAssetPath for " + a.assetKey;
                    return false;
                }
            }

            return true;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                var bytes = sha.ComputeHash(stream);
                var builder = new StringBuilder(bytes.Length * 2);

                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
