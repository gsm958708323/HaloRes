using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Integration
{
    public sealed class PatchDownloadService
    {
        private readonly PatchStagingStore _staging;
        private readonly string _contentRoot;

        public PatchDownloadService(
            PatchStagingStore staging,
            string contentRoot)
        {
            _staging = staging ??
                       throw new ArgumentNullException(nameof(staging));

            if (string.IsNullOrWhiteSpace(contentRoot))
            {
                throw new ArgumentException(
                    "contentRoot must not be empty.",
                    nameof(contentRoot));
            }

            _contentRoot = Path.GetFullPath(contentRoot);
            Directory.CreateDirectory(_contentRoot);
        }

        public string ContentRoot
        {
            get { return _contentRoot; }
        }

        public string GetVersionRoot(string contentVersion)
        {
            return Path.Combine(_contentRoot, contentVersion);
        }

        public async Task<PatchApplyResult> ApplyAsync(
            PatchManifest manifest,
            SimulatedCdnServer server,
            IContentTransport transport,
            CancellationToken cancellationToken)
        {
            MainThreadPump.EnsureMainThread(nameof(ApplyAsync));

            var result = new PatchApplyResult
            {
                ContentVersion = manifest == null
                    ? null
                    : manifest.ContentVersion,
                BundleRoot = manifest == null ||
                             !Game.AssetSystem.Storage.PathSafety.IsSimpleFileName(
                                 manifest.ContentVersion)
                    ? null
                    : GetVersionRoot(manifest.ContentVersion)
            };

            if (manifest == null ||
                manifest.Bundles == null ||
                manifest.Bundles.Length == 0 ||
                manifest.Assets == null ||
                manifest.Assets.Length == 0)
            {
                result.Status = PatchApplyStatus.Failed;
                result.Message = "Invalid manifest shape.";
                return result;
            }

            if (!Game.AssetSystem.Storage.PathSafety.IsSimpleFileName(
                manifest.ContentVersion))
            {
                result.Status = PatchApplyStatus.Failed;
                result.Message = "Invalid content version: " +
                                 (manifest.ContentVersion ?? "<null>");
                return result;
            }

            var versionRoot = GetVersionRoot(manifest.ContentVersion);
            Directory.CreateDirectory(versionRoot);

            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = PatchApplyStatus.Canceled;
                    result.Message = "Canceled before processing bundle " +
                                     i + ".";
                    return result;
                }

                var bundle = manifest.Bundles[i];

                if (!Game.AssetSystem.Storage.PathSafety.TryResolveChild(
                    versionRoot,
                    bundle.FileName,
                    out var targetPath,
                    out var pathError))
                {
                    result.Status = PatchApplyStatus.Failed;
                    result.Message = "Invalid bundle file name '" +
                                     bundle.FileName + "': " + pathError;
                    return result;
                }

                if (File.Exists(targetPath) &&
                    FileMatches(
                        targetPath,
                        bundle.SizeBytes,
                        bundle.Sha256Hex))
                {
                    // 已经存在且正确：清理可能残留的 .part 和 .meta
                    _staging.DeletePart(
                        manifest.ContentVersion,
                        bundle.FileName);

                    result.BundlesSkipped++;
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    // 候选目录里存在损坏文件：删除后从 staging 重新下载
                    File.Delete(targetPath);
                }

                if (!_staging.PrepareForResume(
                    manifest,
                    bundle,
                    out var offset,
                    out var stagingError))
                {
                    result.Status = PatchApplyStatus.Failed;
                    result.Message = "Staging prepare failed: " +
                                     stagingError;
                    return result;
                }

                result.ResumeBytesTotal += offset;

                var partPath = _staging.GetPartPath(
                    manifest.ContentVersion,
                    bundle.FileName);

                var attempt = await transport.DownloadAsync(
                    server,
                    bundle.FileName,
                    partPath,
                    offset,
                    bundle.SizeBytes,
                    cancellationToken);

                if (attempt.RangeUnsupported && offset > 0)
                {
                    _staging.DeletePart(
                        manifest.ContentVersion,
                        bundle.FileName);

                    if (!_staging.PrepareForResume(
                        manifest,
                        bundle,
                        out offset,
                        out stagingError))
                    {
                        result.Status = PatchApplyStatus.Failed;
                        result.Message = "Staging retry prepare failed: " +
                                         stagingError;
                        return result;
                    }

                    partPath = _staging.GetPartPath(
                        manifest.ContentVersion,
                        bundle.FileName);

                    attempt = await transport.DownloadAsync(
                        server,
                        bundle.FileName,
                        partPath,
                        offset,
                        bundle.SizeBytes,
                        cancellationToken);
                }

                if (!attempt.Success)
                {
                    result.Status = cancellationToken.IsCancellationRequested
                        ? PatchApplyStatus.Canceled
                        : PatchApplyStatus.Failed;

                    result.Message = "Download failed for " +
                                     bundle.FileName +
                                     ": " + attempt.Error;
                    return result;
                }

                if (!FileMatches(
                    partPath,
                    bundle.SizeBytes,
                    bundle.Sha256Hex))
                {
                    _staging.DeletePart(
                        manifest.ContentVersion,
                        bundle.FileName);

                    result.Status = PatchApplyStatus.Failed;
                    result.Message = "Hash mismatch after download for " +
                                     bundle.FileName;
                    return result;
                }

                Directory.CreateDirectory(versionRoot);

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(partPath, targetPath);

                // 只删 meta；part 已经被 move 到 target
                _staging.DeletePart(
                    manifest.ContentVersion,
                    bundle.FileName);

                result.BundlesCommitted++;
            }

            result.Status = PatchApplyStatus.Succeeded;
            result.Message = "Patch download completed.";
            return result;
        }

        private static bool FileMatches(
            string path,
            long sizeBytes,
            string expectedSha)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (new FileInfo(path).Length != sizeBytes)
            {
                return false;
            }

            using (var sha = SHA256.Create())
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan))
            {
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);

                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return string.Equals(
                    builder.ToString(),
                    expectedSha,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
