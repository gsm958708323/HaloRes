using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using UnityEngine.Networking;

namespace GameFramework.Asset.Patch
{
    public sealed class FileDownloader
    {
        public FileDownloader(int timeoutSeconds = 30)
        {
            if (timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            TimeoutSeconds = timeoutSeconds;
        }

        public int TimeoutSeconds { get; }

        public async UniTask<byte[]> ReadAsync(string uri, CancellationToken ct = default)
        {
            using (var request = UnityWebRequest.Get(ToUri(uri)))
            {
                request.timeout = TimeoutSeconds;
                await SendAsync(request, ct);
                return request.downloadHandler.data;
            }
        }

        public async UniTask DownloadAsync(string uri, string destination, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
            try
            {
                using (var request = UnityWebRequest.Get(ToUri(uri)))
                {
                    request.downloadHandler = new DownloadHandlerFile(destination) { removeFileOnAbort = true };
                    request.timeout = TimeoutSeconds;
                    await SendAsync(request, ct);
                }
            }
            catch
            {
                if (File.Exists(destination)) File.Delete(destination);
                throw;
            }
        }

        private static async UniTask SendAsync(UnityWebRequest request, CancellationToken ct)
        {
            try { await request.SendWebRequest().ToUniTask(cancellationToken: ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                throw new AssetException(new AssetError(AssetErrorCode.DownloadFailed, null, null,
                    request.url + ": " + error.Message));
            }
        }

        public static string ToUri(string path)
        {
            if (path.Contains("://") || path.StartsWith("jar:", StringComparison.Ordinal)) return path;
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        public static string Combine(string root, string relative)
        {
            return root.TrimEnd('/', '\\') + "/" + relative.Replace('\\', '/');
        }
    }
}
