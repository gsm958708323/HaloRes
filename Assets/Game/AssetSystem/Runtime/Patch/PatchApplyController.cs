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
        private readonly bool _activateAfterApply;

        public PatchApplyController(
            AssetFileStore fileStore,
            PatchStagingStore staging,
            IPatchTransport transport,
            int maxAttemptsPerBundle = 3,
            bool activateAfterApply = true)
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
            _activateAfterApply = activateAfterApply;
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

                if (activeBefore == manifest.ContentVersion)
                {
                    var allValid = true;

                    for (var i = 0; i < manifest.Bundles.Length; i++)
                    {
                        var bundle = manifest.Bundles[i];

                        if (!_fileStore.VerifyBundle(
                            manifest.ContentVersion,
                            bundle.FileName,
                            bundle.Sha256Hex,
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

                for (var i = 0; i < manifest.Bundles.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bundle = manifest.Bundles[i];
                    var report = new PatchBundleReport
                    {
                        BundleFileName = bundle.FileName
                    };

                    var targetPath = _fileStore.GetBundlePath(
                        manifest.ContentVersion,
                        bundle.FileName);

                    if (File.Exists(targetPath) &&
                        _fileStore.VerifyBundle(
                            manifest.ContentVersion,
                            bundle.FileName,
                            bundle.Sha256Hex,
                            out _,
                            out _))
                    {
                        report.Skipped = true;
                        report.Committed = true;
                        skipped++;

                        _staging.DeletePartAndMeta(
                            manifest.ContentVersion,
                            bundle.FileName);

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
                            manifest.ContentVersion,
                            bundle.FileName);

                        PatchDownloadResponse downloadResponse;

                        try
                        {
                            downloadResponse = await _transport.DownloadAsync(
                                bundle.Url,
                                partPath,
                                offset,
                                bundle.SizeBytes,
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
                                manifest.ContentVersion,
                                bundle.FileName);

                            if (downloadResponse.ErrorCode ==
                                PatchTransportErrorCode.RangeNotSupported &&
                                offset > 0)
                            {
                                report.RangeFallback = true;

                                _staging.DeletePartAndMeta(
                                    manifest.ContentVersion,
                                    bundle.FileName);

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
                                    bundle.FileName +
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
                            bundle.FileName,
                            0,
                            bundle.SizeBytes,
                            "verifying downloaded bundle"));

                        if (!BundleHashVerifier.VerifySha256Hex(
                            partPath,
                            bundle.Sha256Hex,
                            out _,
                            out var verifyError))
                        {
                            _staging.DeletePartAndMeta(
                                manifest.ContentVersion,
                                bundle.FileName);

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
                            manifest.ContentVersion,
                            bundle.FileName,
                            bundle.Sha256Hex);

                        if (commit.Success)
                        {
                            _staging.DeleteMeta(
                                manifest.ContentVersion,
                                bundle.FileName);

                            report.Committed = true;
                            committed = true;
                            downloaded++;
                            break;
                        }

                        report.Error = commit.Error.ToString();

                        _staging.DeletePartAndMeta(
                            manifest.ContentVersion,
                            bundle.FileName);

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
                            bundle.FileName;
                        return result;
                    }

                    result.Bundles.Add(report);
                }

                progress?.Report(new PatchProgress(
                    PatchPhase.Activating,
                    null,
                    0,
                    0,
                    "activating " + manifest.ContentVersion));

                // The file-store pointer swap is a storage primitive, not a
                // second activation policy. Callers that own a live runtime
                // (FullFeature, FullChain) pass activateAfterApply: false and
                // let PatchActivationService be the only thing that decides
                // whether a version may become active.
                if (_activateAfterApply)
                {
                    var expectations =
                        new List<BundleHashExpectation>(manifest.Bundles.Length);

                    for (var i = 0; i < manifest.Bundles.Length; i++)
                    {
                        var bundle = manifest.Bundles[i];

                        expectations.Add(new BundleHashExpectation(
                            bundle.FileName,
                            bundle.Sha256Hex));
                    }

                    var activation = _fileStore.ActivateVersion(
                        manifest.ContentVersion,
                        expectations);

                    if (!activation.Success)
                    {
                        result.Status = PatchApplyStatus.Failed;
                        result.ErrorCode = PatchApplyErrorCode.ActivationFailed;
                        result.Message = activation.Error.ToString();
                        return result;
                    }

                    result.ActiveVersionAfter = manifest.ContentVersion;
                }

                result.ContentVersion = manifest.ContentVersion;
                result.BundleRoot = Path.Combine(
                    _fileStore.ContentDirectory,
                    manifest.ContentVersion);
                result.Status = PatchApplyStatus.Succeeded;
                result.ErrorCode = PatchApplyErrorCode.None;
                result.Message = _activateAfterApply
                    ? "Patch applied and activated."
                    : "Patch applied; activation left to the caller.";
                result.BundlesSkipped = skipped;
                result.BundlesDownloaded = downloaded;
                result.BundlesCommitted = downloaded;
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

            if (manifest.SchemaVersion != 1)
            {
                error = "Unsupported manifest schemaVersion: " +
                        manifest.SchemaVersion;
                return false;
            }

            if (!AssetFileStore.IsSimpleFileName(manifest.ContentVersion))
            {
                error = "Invalid contentVersion: " +
                        manifest.ContentVersion;
                return false;
            }

            if (manifest.Bundles == null ||
                manifest.Bundles.Length == 0)
            {
                error = "Manifest has no bundles.";
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                var bundle = manifest.Bundles[i];

                if (bundle == null)
                {
                    error = "Bundle descriptor at index " + i + " is null.";
                    return false;
                }

                if (!AssetFileStore.IsSimpleFileName(bundle.FileName))
                {
                    error = "Invalid bundle file name: " +
                            bundle.FileName;
                    return false;
                }

                if (!names.Add(bundle.FileName))
                {
                    error = "Duplicate bundle file name: " +
                            bundle.FileName;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bundle.Url))
                {
                    error = "Bundle URL is empty: " +
                            bundle.FileName;
                    return false;
                }

                if (bundle.SizeBytes <= 0)
                {
                    error = "Bundle size must be > 0: " +
                            bundle.FileName;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(bundle.Sha256Hex) ||
                    bundle.Sha256Hex.Length != 64)
                {
                    error = "Invalid SHA256 for bundle: " +
                            bundle.FileName;
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
