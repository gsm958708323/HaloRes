using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Storage;

namespace Game.AssetSystem.Patch
{
    public sealed class PatchApplyController
    {
        private readonly AssetFileStore _fileStore;
        private readonly PatchStagingStore _staging;
        private readonly IPatchTransport _transport;
        private readonly int _maxAttemptsPerBundle;

        public PatchApplyController(
            AssetFileStore fileStore,
            PatchStagingStore staging,
            IPatchTransport transport,
            int maxAttemptsPerBundle = 3)
        {
            _fileStore = fileStore ??
                         throw new ArgumentNullException(nameof(fileStore));
            _staging = staging ??
                       throw new ArgumentNullException(nameof(staging));
            _transport = transport ??
                         throw new ArgumentNullException(nameof(transport));

            if (maxAttemptsPerBundle < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxAttemptsPerBundle));
            }

            _maxAttemptsPerBundle = maxAttemptsPerBundle;
        }

        public async Task<PatchApplyResult> ApplyAsync(
            PatchManifest manifest,
            IProgress<PatchProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new PatchApplyResult();

            string activeBefore = null;

            if (_fileStore.TryLoadActiveState(out var activeState, out _))
            {
                activeBefore = activeState.contentVersion;
            }

            result.ActiveVersionBefore = activeBefore;
            result.ActiveVersionAfter = activeBefore;

            if (!ValidateManifest(manifest, out var validationError))
            {
                result.Status = PatchApplyStatus.Failed;
                result.ErrorCode = PatchApplyErrorCode.InvalidManifest;
                result.Message = validationError;
                progress?.Report(new PatchProgress(
                    PatchPhase.Failed,
                    null,
                    0,
                    0,
                    validationError));
                return result;
            }

            try
            {
                _staging.CleanupStaleParts(manifest);

                if (activeBefore == manifest.contentVersion)
                {
                    var allValid = true;

                    for (var i = 0; i < manifest.bundles.Length; i++)
                    {
                        var bundle = manifest.bundles[i];

                        if (!_fileStore.VerifyBundle(
                            manifest.contentVersion,
                            bundle.bundleFileName,
                            bundle.sha256Hex,
                            out _,
                            out _))
                        {
                            allValid = false;
                            break;
                        }
                    }

                    if (allValid)
                    {
                        result.Status = PatchApplyStatus.AlreadyActive;
                        result.ErrorCode = PatchApplyErrorCode.None;
                        result.Message =
                            "Target version is already active and valid.";
                        result.ActiveVersionAfter = activeBefore;
                        return result;
                    }

                    result.Status = PatchApplyStatus.Failed;
                    result.ErrorCode =
                        PatchApplyErrorCode.TargetVersionIsActiveButCorrupted;
                    result.Message =
                        "Target version is active but corrupted. " +
                        "This lesson refuses to modify active version in place.";
                    return result;
                }

                progress?.Report(new PatchProgress(
                    PatchPhase.Validating,
                    null,
                    0,
                    0,
                    "manifest validated"));

                var skipped = 0;
                var downloaded = 0;
                long resumeTotal = 0;

                for (var i = 0; i < manifest.bundles.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bundle = manifest.bundles[i];
                    var report = new PatchBundleReport
                    {
                        BundleFileName = bundle.bundleFileName
                    };

                    var targetPath = _fileStore.GetBundlePath(
                        manifest.contentVersion,
                        bundle.bundleFileName);

                    if (File.Exists(targetPath) &&
                        _fileStore.VerifyBundle(
                            manifest.contentVersion,
                            bundle.bundleFileName,
                            bundle.sha256Hex,
                            out _,
                            out _))
                    {
                        report.Skipped = true;
                        report.Committed = true;
                        skipped++;

                        _staging.DeletePartAndMeta(
                            manifest.contentVersion,
                            bundle.bundleFileName);

                        result.Bundles.Add(report);
                        continue;
                    }

                    if (File.Exists(targetPath))
                    {
                        // The target version is not active (guarded above),
                        // so a corrupted candidate file can be removed.
                        TryDeleteFile(targetPath);
                    }

                    if (!_staging.PreparePartForResume(
                        manifest,
                        bundle,
                        out var offset,
                        out var stageError))
                    {
                        report.Error = stageError;
                        result.Bundles.Add(report);
                        result.Status = PatchApplyStatus.Failed;
                        result.ErrorCode = PatchApplyErrorCode.IoError;
                        result.Message = stageError;
                        return result;
                    }

                    report.ResumeOffset = offset;
                    resumeTotal += offset;
                    var committed = false;

                    for (var attempt = 0;
                         attempt < _maxAttemptsPerBundle;
                         attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var partPath = _staging.GetPartPath(
                            manifest.contentVersion,
                            bundle.bundleFileName);

                        PatchDownloadResponse downloadResponse;

                        try
                        {
                            downloadResponse = await _transport.DownloadAsync(
                                bundle.url,
                                partPath,
                                offset,
                                bundle.sizeBytes,
                                progress,
                                cancellationToken).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            downloadResponse = PatchDownloadResponse.Failure(
                                PatchTransportErrorCode.IoError,
                                0,
                                exception.GetType().Name +
                                ": " + exception.Message);
                        }

                        report.BytesTransferredThisRun +=
                            downloadResponse.BytesWrittenThisRun;

                        if (!downloadResponse.Success)
                        {
                            offset = _staging.GetPartLength(
                                manifest.contentVersion,
                                bundle.bundleFileName);

                            if (downloadResponse.ErrorCode ==
                                PatchTransportErrorCode.RangeNotSupported &&
                                offset > 0)
                            {
                                report.RangeFallback = true;

                                _staging.DeletePartAndMeta(
                                    manifest.contentVersion,
                                    bundle.bundleFileName);

                                if (!_staging.PreparePartForResume(
                                    manifest,
                                    bundle,
                                    out offset,
                                    out stageError))
                                {
                                    report.Error = stageError;
                                    result.Bundles.Add(report);
                                    result.Status = PatchApplyStatus.Failed;
                                    result.ErrorCode =
                                        PatchApplyErrorCode.IoError;
                                    result.Message = stageError;
                                    return result;
                                }

                                continue;
                            }

                            if (attempt + 1 >= _maxAttemptsPerBundle)
                            {
                                report.Error = downloadResponse.Message;
                                result.Bundles.Add(report);

                                result.Status = PatchApplyStatus.Failed;
                                result.ErrorCode =
                                    PatchApplyErrorCode.DownloadFailed;
                                result.Message =
                                    "Bundle download failed: " +
                                    bundle.bundleFileName +
                                    " " + downloadResponse.Message;
                                return result;
                            }

                            await Task.Delay(
                                50 * (attempt + 1),
                                cancellationToken).ConfigureAwait(true);

                            continue;
                        }

                        progress?.Report(new PatchProgress(
                            PatchPhase.Verifying,
                            bundle.bundleFileName,
                            0,
                            bundle.sizeBytes,
                            "verifying downloaded bundle"));

                        if (!BundleHashVerifier.VerifySha256Hex(
                            partPath,
                            bundle.sha256Hex,
                            out _,
                            out var verifyError))
                        {
                            _staging.DeletePartAndMeta(
                                manifest.contentVersion,
                                bundle.bundleFileName);

                            if (attempt + 1 >= _maxAttemptsPerBundle)
                            {
                                report.Error = verifyError;
                                result.Bundles.Add(report);

                                result.Status = PatchApplyStatus.Failed;
                                result.ErrorCode =
                                    PatchApplyErrorCode.HashMismatch;
                                result.Message =
                                    "Hash mismatch after download: " +
                                    verifyError;
                                return result;
                            }

                            if (!_staging.PreparePartForResume(
                                manifest,
                                bundle,
                                out offset,
                                out stageError))
                            {
                                report.Error = stageError;
                                result.Bundles.Add(report);
                                result.Status = PatchApplyStatus.Failed;
                                result.ErrorCode =
                                    PatchApplyErrorCode.IoError;
                                result.Message = stageError;
                                return result;
                            }

                            continue;
                        }

                        var commit = _fileStore.CommitBundleFromTemp(
                            partPath,
                            manifest.contentVersion,
                            bundle.bundleFileName,
                            bundle.sha256Hex);

                        if (commit.Success)
                        {
                            _staging.DeleteMeta(
                                manifest.contentVersion,
                                bundle.bundleFileName);

                            report.Committed = true;
                            committed = true;
                            downloaded++;
                            break;
                        }

                        report.Error = commit.Error.ToString();

                        _staging.DeletePartAndMeta(
                            manifest.contentVersion,
                            bundle.bundleFileName);

                        if (commit.Error.Code !=
                            StorageErrorCode.HashMismatch ||
                            attempt + 1 >= _maxAttemptsPerBundle)
                        {
                            result.Bundles.Add(report);

                            result.Status = PatchApplyStatus.Failed;
                            result.ErrorCode =
                                PatchApplyErrorCode.CommitFailed;
                            result.Message =
                                "Commit failed: " + report.Error;
                            return result;
                        }

                        if (!_staging.PreparePartForResume(
                            manifest,
                            bundle,
                            out offset,
                            out stageError))
                        {
                            report.Error = stageError;
                            result.Bundles.Add(report);
                            result.Status = PatchApplyStatus.Failed;
                            result.ErrorCode = PatchApplyErrorCode.IoError;
                            result.Message = stageError;
                            return result;
                        }
                    }

                    if (!committed)
                    {
                        report.Error = "Exhausted download attempts.";
                        result.Bundles.Add(report);

                        result.Status = PatchApplyStatus.Failed;
                        result.ErrorCode = PatchApplyErrorCode.DownloadFailed;
                        result.Message =
                            "Bundle did not commit: " +
                            bundle.bundleFileName;
                        return result;
                    }

                    result.Bundles.Add(report);
                }

                progress?.Report(new PatchProgress(
                    PatchPhase.Activating,
                    null,
                    0,
                    0,
                    "activating " + manifest.contentVersion));

                var expectations =
                    new List<BundleHashExpectation>(manifest.bundles.Length);

                for (var i = 0; i < manifest.bundles.Length; i++)
                {
                    var bundle = manifest.bundles[i];

                    expectations.Add(new BundleHashExpectation(
                        bundle.bundleFileName,
                        bundle.sha256Hex));
                }

                var activation = _fileStore.ActivateVersion(
                    manifest.contentVersion,
                    expectations);

                if (!activation.Success)
                {
                    result.Status = PatchApplyStatus.Failed;
                    result.ErrorCode = PatchApplyErrorCode.ActivationFailed;
                    result.Message = activation.Error.ToString();
                    return result;
                }

                result.Status = PatchApplyStatus.Succeeded;
                result.ErrorCode = PatchApplyErrorCode.None;
                result.Message = "Patch applied successfully.";
                result.ActiveVersionAfter = manifest.contentVersion;
                result.BundlesSkipped = skipped;
                result.BundlesDownloaded = downloaded;
                result.ResumeOffsetTotal = resumeTotal;

                progress?.Report(new PatchProgress(
                    PatchPhase.Completed,
                    null,
                    0,
                    0,
                    "completed"));

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = PatchApplyStatus.Canceled;
                result.ErrorCode = PatchApplyErrorCode.Canceled;
                result.Message = "Patch application canceled.";
                result.ActiveVersionAfter = activeBefore;
                return result;
            }
        }

        private static bool ValidateManifest(
            PatchManifest manifest,
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
                error = "Unsupported manifest schemaVersion: " +
                        manifest.schemaVersion;
                return false;
            }

            if (!AssetFileStore.IsSimpleFileName(manifest.contentVersion))
            {
                error = "Invalid contentVersion: " +
                        manifest.contentVersion;
                return false;
            }

            if (manifest.bundles == null ||
                manifest.bundles.Length == 0)
            {
                error = "Manifest has no bundles.";
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.bundles.Length; i++)
            {
                var bundle = manifest.bundles[i];

                if (bundle == null)
                {
                    error = "Bundle descriptor at index " + i + " is null.";
                    return false;
                }

                if (!AssetFileStore.IsSimpleFileName(bundle.bundleFileName))
                {
                    error = "Invalid bundle file name: " +
                            bundle.bundleFileName;
                    return false;
                }

                if (!names.Add(bundle.bundleFileName))
                {
                    error = "Duplicate bundle file name: " +
                            bundle.bundleFileName;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bundle.url))
                {
                    error = "Bundle URL is empty: " +
                            bundle.bundleFileName;
                    return false;
                }

                if (bundle.sizeBytes <= 0)
                {
                    error = "Bundle size must be > 0: " +
                            bundle.bundleFileName;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bundle.sha256Hex) ||
                    bundle.sha256Hex.Length != 64)
                {
                    error = "Invalid SHA256 for bundle: " +
                            bundle.bundleFileName;
                    return false;
                }
            }

            return true;
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
