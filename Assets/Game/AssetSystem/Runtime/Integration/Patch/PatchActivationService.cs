using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Integration
{
    public sealed class PatchActivationService
    {
        public ActiveContentContext ActiveContext { get; private set; }
        public int ActivationCount { get; private set; }

        public async Task<PatchActivationResult> ActivateAsync(
            PatchManifest manifest,
            string bundleRoot,
            string expectedPlatform,
            int liveHandles,
            bool safePoint,
            CancellationToken cancellationToken = default)
        {
            var before = ActiveContext == null
                ? null
                : ActiveContext.ContentVersion;

            var result = new PatchActivationResult
            {
                ActiveVersionBefore = before,
                ActiveVersionAfter = before
            };

            if (manifest == null ||
                manifest.Bundles == null ||
                manifest.Bundles.Length == 0 ||
                manifest.Assets == null ||
                manifest.Assets.Length == 0)
            {
                result.Status = PatchActivationStatus.InvalidManifest;
                result.Message = "Invalid manifest shape.";
                return result;
            }

            if (!string.Equals(
                manifest.PlatformKey,
                expectedPlatform,
                StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.PlatformMismatch;
                result.Message = "Manifest platform " +
                                 manifest.PlatformKey +
                                 " != expected platform " +
                                 expectedPlatform;
                return result;
            }

            var validation = await Task.Run(
                () => ValidateFiles(
                    manifest,
                    bundleRoot,
                    cancellationToken));

            result.BundlesChecked = validation.BundlesChecked;

            if (validation.Canceled)
            {
                result.Status = PatchActivationStatus.Canceled;
                result.Message = "Canceled during validation.";
                return result;
            }

            if (validation.ErrorStatus !=
                PatchActivationStatus.Succeeded)
            {
                result.Status = validation.ErrorStatus;
                result.Message = validation.Message;
                return result;
            }

            if (ActiveContext != null &&
                string.Equals(
                    ActiveContext.ContentVersion,
                    manifest.ContentVersion,
                    StringComparison.Ordinal))
            {
                result.Status = PatchActivationStatus.AlreadyActive;
                result.Message = "Version " +
                                 manifest.ContentVersion +
                                 " is already active and valid.";
                return result;
            }

            if (liveHandles > 0)
            {
                result.Status = PatchActivationStatus.RejectedActiveHandles;
                result.Message = "Cannot activate while " +
                                 liveHandles +
                                 " live handle(s) exist.";
                return result;
            }

            if (!safePoint)
            {
                result.Status = PatchActivationStatus.RejectedUnsafePoint;
                result.Message = "Cannot activate outside a safe point.";
                return result;
            }

            if (!MainThreadPump.IsMainThread)
            {
                var tcs = new TaskCompletionSource<PatchActivationResult>();
                MainThreadPump.Post(
                    () => tcs.TrySetResult(
                        Swap(manifest, bundleRoot, result)));

                return await tcs.Task;
            }

            return Swap(manifest, bundleRoot, result);
        }

        private PatchActivationResult Swap(
            PatchManifest manifest,
            string bundleRoot,
            PatchActivationResult result)
        {
            MainThreadPump.EnsureMainThread(nameof(Swap));

            var bundles = new List<RuntimeBundle>();
            var groupToId =
                new Dictionary<string, BundleId>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var b = manifest.Bundles[i];

                if (groupToId.ContainsKey(b.BundleGroupName))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.Message = "Duplicate bundle group: " +
                                     b.BundleGroupName;
                    return result;
                }

                var id = new BundleId(i);
                groupToId[b.BundleGroupName] = id;

                bundles.Add(new RuntimeBundle(
                    id,
                    b.BundleGroupName,
                    b.FileName,
                    Path.Combine(bundleRoot, b.FileName),
                    b.SizeBytes,
                    b.Sha256Hex));
            }

            var assets = new List<RuntimeAsset>();

            for (var i = 0; i < manifest.Assets.Length; i++)
            {
                var a = manifest.Assets[i];

                if (!groupToId.TryGetValue(
                    a.BundleGroupName,
                    out var bundleId))
                {
                    result.Status = PatchActivationStatus.InvalidManifest;
                    result.Message = "Unknown bundle group " +
                                     a.BundleGroupName +
                                     " for asset " + a.AssetKey;
                    return result;
                }

                assets.Add(new RuntimeAsset(
                    new AssetKey(a.AssetKey),
                    bundleId,
                    a.InternalPath));
            }

            ActiveContext = new ActiveContentContext(
                manifest.ContentVersion,
                manifest.PlatformKey,
                bundleRoot,
                assets,
                bundles);

            ActivationCount++;

            result.Status = PatchActivationStatus.Succeeded;
            result.ActiveVersionAfter = manifest.ContentVersion;
            result.Message = "Activated " +
                             manifest.ContentVersion +
                             " (" + bundles.Count + " bundles, " +
                             assets.Count + " assets).";

            Debug.Log(
                "[Activation] " +
                (result.ActiveVersionBefore ?? "<none>") +
                " -> " +
                result.ActiveVersionAfter);

            return result;
        }

        private struct ValidationReport
        {
            public int BundlesChecked;
            public bool Canceled;
            public PatchActivationStatus ErrorStatus;
            public string Message;
        }

        private ValidationReport ValidateFiles(
            PatchManifest manifest,
            string bundleRoot,
            CancellationToken cancellationToken)
        {
            var report = new ValidationReport
            {
                ErrorStatus = PatchActivationStatus.Succeeded
            };

            if (string.IsNullOrEmpty(bundleRoot) ||
                !Directory.Exists(bundleRoot))
            {
                report.ErrorStatus = PatchActivationStatus.BundleMissing;
                report.Message = "Bundle root missing: " + bundleRoot;
                return report;
            }

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    report.Canceled = true;
                    return report;
                }

                var b = manifest.Bundles[i];
                report.BundlesChecked++;

                var path = Path.Combine(bundleRoot, b.FileName);

                if (!File.Exists(path))
                {
                    report.ErrorStatus = PatchActivationStatus.BundleMissing;
                    report.Message = "Missing bundle: " + path;
                    return report;
                }

                var size = new FileInfo(path).Length;

                if (size != b.SizeBytes)
                {
                    report.ErrorStatus = PatchActivationStatus.BundleSizeMismatch;
                    report.Message = "Size mismatch for " +
                                     b.FileName +
                                     ": expected " + b.SizeBytes +
                                     ", actual " + size;
                    return report;
                }

                string hash;

                try
                {
                    using (var sha = SHA256.Create())
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        64 * 1024,
                        FileOptions.SequentialScan))
                    {
                        var bytes = sha.ComputeHash(stream);
                        var sb = new StringBuilder(bytes.Length * 2);

                        for (var k = 0; k < bytes.Length; k++)
                        {
                            sb.Append(bytes[k].ToString("x2"));
                        }

                        hash = sb.ToString();
                    }
                }
                catch (Exception exception)
                {
                    report.ErrorStatus =
                        PatchActivationStatus.InvalidManifest;
                    report.Message = "Failed to hash " +
                                     b.FileName + ": " +
                                     exception.Message;
                    return report;
                }

                if (!string.Equals(
                    hash,
                    b.Sha256Hex,
                    StringComparison.OrdinalIgnoreCase))
                {
                    report.ErrorStatus = PatchActivationStatus.HashMismatch;
                    report.Message = "Hash mismatch for " + b.FileName;
                    return report;
                }
            }

            return report;
        }
    }
}
