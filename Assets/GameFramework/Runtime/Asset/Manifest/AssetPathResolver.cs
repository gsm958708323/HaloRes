using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Patch;

namespace GameFramework.Asset.Manifest
{
    public sealed class AssetPathResolver
    {
        public AssetPathResolver(string persistentRoot, string packagedRoot = null)
        {
            if (string.IsNullOrWhiteSpace(persistentRoot)) throw new ArgumentException("Persistent root is required.", nameof(persistentRoot));
            PersistentRoot = Path.GetFullPath(persistentRoot);
            PackagedRoot = packagedRoot;
        }

        public string PersistentRoot { get; }
        public string PackagedRoot { get; }
        public string BundlePath(BundleInfo info) => Path.Combine(PersistentRoot, "bundles", info.Hash + ".bundle");

        internal async UniTask<string> ResolveVerifiedAsync(BundleInfo info, CancellationToken ct = default)
        {
            var local = BundlePath(info);
            if (await FileIntegrity.VerifyAsync(local, info.Size, info.Hash, ct)) return local;
            if (string.IsNullOrWhiteSpace(PackagedRoot))
            {
                var code = File.Exists(local) ? AssetErrorCode.HashMismatch : AssetErrorCode.BundleFileMissing;
                throw new AssetException(new AssetError(code, null, info.Name, local + " expected SHA-256=" + info.Hash));
            }

            var source = FileDownloader.Combine(PackagedRoot, "bundles/" + info.Hash + ".bundle");
            var temporary = local + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await new FileDownloader().DownloadAsync(source, temporary, ct);
                if (!await FileIntegrity.VerifyAsync(temporary, info.Size, info.Hash, ct))
                    throw new AssetException(new AssetError(AssetErrorCode.HashMismatch, null, info.Name, "Packaged bundle verification failed: " + source));
                if (File.Exists(local)) File.Delete(local);
                File.Move(temporary, local);
                return local;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
