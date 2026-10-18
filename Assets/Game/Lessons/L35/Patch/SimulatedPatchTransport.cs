using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AssetSystem.Patch.L35
{
    public sealed class SimulatedPatchServer
    {
        private readonly Dictionary<string, byte[]> _files =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public bool SupportsRange { get; set; } = true;

        public int FailAfterBytesThisAttempt { get; set; } = -1;

        public void AddTextFile(string url, string text)
        {
            AddFile(url, Encoding.UTF8.GetBytes(text));
        }

        public void AddFile(string url, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("url must not be empty.", nameof(url));
            }

            _files[url] = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public bool TryGetFile(string url, out byte[] bytes)
        {
            return _files.TryGetValue(url, out bytes);
        }
    }

    public sealed class SimulatedPatchTransport : IPatchTransport
    {
        private readonly SimulatedPatchServer _server;

        public SimulatedPatchTransport(SimulatedPatchServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public Task<PatchDownloadResponse> DownloadAsync(
            string url,
            string partFilePath,
            long offsetBytes,
            long expectedTotalBytes,
            IProgress<PatchProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => DownloadSync(
                url,
                partFilePath,
                offsetBytes,
                expectedTotalBytes,
                progress,
                cancellationToken));
        }

        private PatchDownloadResponse DownloadSync(
            string url,
            string partFilePath,
            long offsetBytes,
            long expectedTotalBytes,
            IProgress<PatchProgress> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!_server.TryGetFile(url, out var payload))
                {
                    return PatchDownloadResponse.Failure(
                        PatchTransportErrorCode.ServerError,
                        0,
                        "Simulated file not found: " + url);
                }

                if (offsetBytes < 0)
                {
                    return PatchDownloadResponse.Failure(
                        PatchTransportErrorCode.IoError,
                        0,
                        "offsetBytes must be >= 0.");
                }

                if (offsetBytes > payload.Length)
                {
                    return PatchDownloadResponse.Failure(
                        PatchTransportErrorCode.IoError,
                        0,
                        "offsetBytes is greater than payload length.");
                }

                if (offsetBytes > 0 && !_server.SupportsRange)
                {
                    return PatchDownloadResponse.Failure(
                        PatchTransportErrorCode.RangeNotSupported,
                        0,
                        "Simulated server does not support Range.");
                }

                var directory = Path.GetDirectoryName(partFilePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(
                    partFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read))
                {
                    if (offsetBytes == 0)
                    {
                        stream.SetLength(0);
                    }
                    else
                    {
                        stream.Seek(offsetBytes, SeekOrigin.Begin);
                    }

                    var remaining = payload.Length - (int)offsetBytes;
                    var failAfter = _server.FailAfterBytesThisAttempt;
                    var willFail = failAfter >= 0 && failAfter < remaining;
                    var toWrite = willFail ? failAfter : remaining;
                    var written = 0;

                    while (written < toWrite)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var chunk = Math.Min(4096, toWrite - written);
                        stream.Write(
                            payload,
                            (int)offsetBytes + written,
                            chunk);

                        written += chunk;

                        progress?.Report(new PatchProgress(
                            PatchPhase.Downloading,
                            Path.GetFileName(partFilePath),
                            offsetBytes + written,
                            payload.Length,
                            "simulated download"));
                    }

                    stream.Flush(true);

                    if (willFail)
                    {
                        return PatchDownloadResponse.Failure(
                            PatchTransportErrorCode.Network,
                            written,
                            "Simulated network failure after " +
                            written + " bytes.");
                    }

                    if (expectedTotalBytes > 0 &&
                        offsetBytes + written != expectedTotalBytes)
                    {
                        return PatchDownloadResponse.Failure(
                            PatchTransportErrorCode.ServerError,
                            written,
                            "Downloaded size mismatch. expected=" +
                            expectedTotalBytes +
                            ", actual=" + (offsetBytes + written));
                    }

                    return PatchDownloadResponse.Succeeded(written);
                }
            }
            catch (OperationCanceledException)
            {
                return PatchDownloadResponse.Canceled();
            }
            catch (Exception exception)
            {
                return PatchDownloadResponse.Failure(
                    PatchTransportErrorCode.IoError,
                    0,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
