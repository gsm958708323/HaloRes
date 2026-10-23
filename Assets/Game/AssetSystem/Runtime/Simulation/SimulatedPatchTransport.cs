using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Patch;

namespace Game.AssetSystem.Simulation
{
    /// <summary>
    /// In-memory CDN. Keyed by URL, so it can back any transport that speaks
    /// <see cref="IPatchTransport"/>.
    /// </summary>
    public sealed class SimulatedCdnServer
    {
        private readonly Dictionary<string, byte[]> _files =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>When false, a request with offset &gt; 0 is rejected.</summary>
        public bool SupportsRange { get; set; } = true;

        /// <summary>
        /// Stop this attempt after N bytes and report a network failure.
        /// -1 disables the interruption.
        /// </summary>
        public int FailAfterBytesThisAttempt { get; set; } = -1;

        public void AddFile(string url, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("url must not be empty.", nameof(url));
            }

            _files[url] = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public void AddTextFile(string url, string text)
        {
            AddFile(url, Encoding.UTF8.GetBytes(text));
        }

        public bool TryGetFile(string url, out byte[] bytes)
        {
            return _files.TryGetValue(url, out bytes);
        }
    }

    /// <summary>
    /// The one simulated patch transport.
    ///
    /// Before this existed there were three copies (lesson, tests, integration
    /// demo), each with its own return type and its own notion of how to
    /// interrupt. They all sat on the same <see cref="IPatchTransport"/> seam,
    /// so they are now one class with two knobs:
    /// <see cref="SimulatedCdnServer.FailAfterBytesThisAttempt"/> for a
    /// per-attempt interruption and <see cref="TransferBudget"/> for a global
    /// one (used to stop a whole download at a percentage).
    /// </summary>
    public sealed class SimulatedPatchTransport : IPatchTransport
    {
        private readonly SimulatedCdnServer _server;

        public SimulatedPatchTransport(SimulatedCdnServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Remaining bytes this transport instance may transfer. -1 means
        /// unlimited. Used by demos that interrupt a multi-bundle download at a
        /// global percentage.
        /// </summary>
        public long TransferBudget = -1;

        /// <summary>Bytes transferred by this transport so far.</summary>
        public long Transferred { get; private set; }

        /// <summary>Called with the cumulative byte count after each attempt.</summary>
        public Action<long> Progress;

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
                    var cap = ResolveCap(remaining);
                    var willFail = cap < remaining;
                    var toWrite = willFail ? cap : remaining;
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

                        if (progress != null)
                        {
                            progress.Report(new PatchProgress(
                                PatchPhase.Downloading,
                                Path.GetFileName(partFilePath),
                                offsetBytes + written,
                                payload.Length,
                                "simulated download"));
                        }
                    }

                    stream.Flush(true);

                    AccountFor(written);

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

        /// <summary>
        /// How many bytes this attempt is allowed to write before the simulated
        /// network drops. Takes the tighter of the per-attempt and global knobs.
        /// </summary>
        private int ResolveCap(int remaining)
        {
            var cap = remaining;

            var perAttempt = _server.FailAfterBytesThisAttempt;

            if (perAttempt >= 0 && perAttempt < cap)
            {
                cap = perAttempt;
            }

            if (TransferBudget >= 0)
            {
                if (TransferBudget == 0)
                {
                    return 0;
                }

                if (TransferBudget < cap)
                {
                    cap = (int)Math.Min(int.MaxValue, TransferBudget);
                }
            }

            return cap;
        }

        private void AccountFor(int written)
        {
            Transferred += written;

            if (TransferBudget >= 0)
            {
                TransferBudget = Math.Max(0, TransferBudget - written);
            }

            var handler = Progress;

            if (handler != null)
            {
                handler(Transferred);
            }
        }
    }
}
