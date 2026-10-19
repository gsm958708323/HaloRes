using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AssetSystem.Integration
{
    public sealed class SimulatedCdnServer
    {
        private readonly Dictionary<string, byte[]> _files =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public void Put(string fileName, byte[] payload)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException(
                    "fileName must not be empty.",
                    nameof(fileName));
            }

            _files[fileName] = payload ??
                throw new ArgumentNullException(nameof(payload));
        }

        public bool TryGet(string fileName, out byte[] payload)
        {
            return _files.TryGetValue(fileName, out payload);
        }
    }

    public struct DownloadAttempt
    {
        public bool Success;
        public long BytesWritten;
        public bool RangeUnsupported;
        public string Error;

        public static DownloadAttempt Ok(long written)
        {
            return new DownloadAttempt
            {
                Success = true,
                BytesWritten = written
            };
        }

        public static DownloadAttempt Fail(string error, long written = 0)
        {
            return new DownloadAttempt
            {
                Success = false,
                BytesWritten = written,
                Error = error
            };
        }

        public static DownloadAttempt RangeNotSupported()
        {
            return new DownloadAttempt
            {
                Success = false,
                RangeUnsupported = true,
                Error = "Server does not support Range."
            };
        }
    }

    public interface IContentTransport
    {
        Task<DownloadAttempt> DownloadAsync(
            SimulatedCdnServer server,
            string fileName,
            string partPath,
            long offset,
            long expectedSize,
            CancellationToken cancellationToken);
    }

    public sealed class SimulatedContentTransport : IContentTransport
    {
        public bool ServerSupportsRange = true;
        public int FailAfterBytesThisAttempt = -1;

        public Task<DownloadAttempt> DownloadAsync(
            SimulatedCdnServer server,
            string fileName,
            string partPath,
            long offset,
            long expectedSize,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => DownloadWorker(
                server,
                fileName,
                partPath,
                offset,
                expectedSize,
                cancellationToken));
        }

        private DownloadAttempt DownloadWorker(
            SimulatedCdnServer server,
            string fileName,
            string partPath,
            long offset,
            long expectedSize,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!server.TryGet(fileName, out var payload))
                {
                    return DownloadAttempt.Fail(
                        "Server file not found: " + fileName);
                }

                if (payload.Length != expectedSize)
                {
                    return DownloadAttempt.Fail(
                        "Server size mismatch: expected " +
                        expectedSize + ", actual " + payload.Length);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return DownloadAttempt.Fail("Canceled.");
                }

                if (offset > 0 && !ServerSupportsRange)
                {
                    return DownloadAttempt.RangeNotSupported();
                }

                if (offset > payload.Length)
                {
                    return DownloadAttempt.Fail(
                        "Offset beyond payload length.");
                }

                var directory = Path.GetDirectoryName(partPath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(
                    partPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read))
                {
                    if (offset == 0)
                    {
                        stream.SetLength(0);
                    }
                    else
                    {
                        stream.Seek(offset, SeekOrigin.Begin);
                    }

                    var remaining = payload.Length - (int)offset;
                    var failAfter = FailAfterBytesThisAttempt;
                    var willFail = failAfter >= 0 && failAfter < remaining;
                    var toWrite = willFail ? failAfter : remaining;
                    var written = 0;

                    while (written < toWrite)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return DownloadAttempt.Fail("Canceled.", written);
                        }

                        var chunk = Math.Min(4096, toWrite - written);
                        stream.Write(payload, (int)offset + written, chunk);
                        written += chunk;
                    }

                    stream.Flush(true);

                    if (willFail)
                    {
                        return DownloadAttempt.Fail(
                            "Simulated network failure after " +
                            written + " bytes",
                            written);
                    }

                    return DownloadAttempt.Ok(written);
                }
            }
            catch (Exception exception)
            {
                return DownloadAttempt.Fail(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
