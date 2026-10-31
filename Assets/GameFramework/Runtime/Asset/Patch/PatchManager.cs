using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Core;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;

namespace GameFramework.Asset.Patch
{
    public readonly struct PatchProgress
    {
        public PatchProgress(int completed, int total, long completedBytes, long totalBytes)
        { CompletedFiles = completed; TotalFiles = total; CompletedBytes = completedBytes; TotalBytes = totalBytes; }
        public int CompletedFiles { get; }
        public int TotalFiles { get; }
        public long CompletedBytes { get; }
        public long TotalBytes { get; }
    }

    // Updates run before AssetSystem initialization. Content-addressed files never overwrite active content.
    public sealed class PatchManager
    {
        private readonly string _root;
        private readonly string _platform;
        private readonly FileDownloader _downloader;
        private bool _running;

        public PatchManager(string persistentRoot, string platform, FileDownloader downloader = null)
        {
            _root = Path.GetFullPath(persistentRoot);
            _platform = platform;
            _downloader = downloader ?? new FileDownloader();
        }

        public async UniTask<AssetManifest> RunAsync(string sourceRoot, IProgress<PatchProgress> progress = null, CancellationToken ct = default)
        {
            MainThreadGuard.Assert();
            if (_running) throw new InvalidOperationException("A patch is already running.");
            _running = true;
            // A process-scoped file lock also prevents two PatchManager instances from racing activation.
            try
            {
                Directory.CreateDirectory(_root);
                using (var patchLock = new FileStream(Path.Combine(_root, ".patch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    return await RunLockedAsync(sourceRoot, progress, ct);
            }
            finally { _running = false; }
        }

        private async UniTask<AssetManifest> RunLockedAsync(string sourceRoot, IProgress<PatchProgress> progress, CancellationToken ct)
        {
            var versionBytes = await _downloader.ReadAsync(FileDownloader.Combine(sourceRoot, "version.json"), ct);
            var versionJson = Encoding.UTF8.GetString(versionBytes);
            var version = VersionFile.Parse(versionJson, _platform);
            var currentPath = Path.Combine(_root, "version.json");
            if (File.Exists(currentPath))
            {
                var current = VersionFile.Parse(File.ReadAllText(currentPath), _platform);
                if (version.version < current.version) throw Invalid("Version downgrade refused.");
                if (version.version == current.version && version.manifestHash != current.manifestHash)
                    throw Invalid("A published version number cannot identify two different manifests.");
            }
            var manifestBytes = await _downloader.ReadAsync(FileDownloader.Combine(sourceRoot, "manifests/" + version.manifestHash + ".json"), ct);
            if (manifestBytes.LongLength != version.manifestSize || FileIntegrity.Hash(manifestBytes) != version.manifestHash)
                throw new AssetException(new AssetError(AssetErrorCode.HashMismatch, null, null, "Remote manifest failed SHA-256/size validation."));
            var manifest = AssetManifest.FromJson(Encoding.UTF8.GetString(manifestBytes));
            if (manifest.Version != version.version) throw Invalid("Version and manifest versions differ.");

            var resolver = new AssetPathResolver(_root);
            var missing = new System.Collections.Generic.List<BundleInfo>();
            long totalBytes = 0;
            foreach (var bundle in manifest.Bundles)
            {
                if (await FileIntegrity.VerifyAsync(resolver.BundlePath(bundle), bundle.Size, bundle.Hash, ct)) continue;
                missing.Add(bundle);
                totalBytes += bundle.Size;
            }
            long completedBytes = 0;
            progress?.Report(new PatchProgress(0, missing.Count, 0, totalBytes));
            for (var i = 0; i < missing.Count; i++)
            {
                var bundle = missing[i];
                var destination = resolver.BundlePath(bundle);
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    await _downloader.DownloadAsync(FileDownloader.Combine(sourceRoot, "bundles/" + bundle.Hash + ".bundle"), temporary, ct);
                    if (!await FileIntegrity.VerifyAsync(temporary, bundle.Size, bundle.Hash, ct))
                        throw new AssetException(new AssetError(AssetErrorCode.HashMismatch, null, bundle.Name, "Downloaded bundle failed SHA-256/size validation."));
                    if (File.Exists(destination)) File.Delete(destination);
                    File.Move(temporary, destination);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                completedBytes += bundle.Size;
                progress?.Report(new PatchProgress(i + 1, missing.Count, completedBytes, totalBytes));
            }
            ct.ThrowIfCancellationRequested();
            var manifests = Path.Combine(_root, "manifests");
            Directory.CreateDirectory(manifests);
            var manifestPath = Path.Combine(manifests, version.manifestHash + ".json");
            if (!await FileIntegrity.VerifyAsync(manifestPath, version.manifestSize, version.manifestHash, ct))
                AtomicWriteBytes(manifestPath, manifestBytes);
            ct.ThrowIfCancellationRequested();
            AtomicWrite(currentPath, versionJson);
            return manifest;
        }

        public async UniTask<AssetManifest> LoadInstalledAsync(CancellationToken ct = default)
        {
            var version = VersionFile.Parse(File.ReadAllText(Path.Combine(_root, "version.json")), _platform);
            var manifestPath = Path.Combine(_root, "manifests", version.manifestHash + ".json");
            if (!await FileIntegrity.VerifyAsync(manifestPath, version.manifestSize, version.manifestHash, ct))
                throw new AssetException(new AssetError(AssetErrorCode.HashMismatch, null, null, "Installed manifest is corrupt."));
            var manifest = AssetManifest.FromFile(manifestPath);
            if (manifest.Version != version.version) throw Invalid("Installed version and manifest differ.");
            var paths = new AssetPathResolver(_root);
            foreach (var bundle in manifest.Bundles)
                if (!await FileIntegrity.VerifyAsync(paths.BundlePath(bundle), bundle.Size, bundle.Hash, ct))
                    throw new AssetException(new AssetError(AssetErrorCode.HashMismatch, null, bundle.Name, "Installed bundle is corrupt."));
            return manifest;
        }

        private static void AtomicWrite(string path, string text)
        {
            AtomicWriteBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        private static void AtomicWriteBytes(string path, byte[] bytes)
        {
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static AssetException Invalid(string detail) => new AssetException(new AssetError(AssetErrorCode.VersionInvalid, null, null, detail));
    }
}
