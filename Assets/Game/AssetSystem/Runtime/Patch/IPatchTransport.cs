using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AssetSystem.Patch
{
    public enum PatchTransportErrorCode
    {
        None,
        Network,
        RangeNotSupported,
        IoError,
        Canceled,
        ServerError
    }

    public readonly struct PatchDownloadResponse
    {
        public bool Success { get; }
        public long BytesWrittenThisRun { get; }
        public PatchTransportErrorCode ErrorCode { get; }
        public string Message { get; }

        private PatchDownloadResponse(
            bool success,
            long bytesWrittenThisRun,
            PatchTransportErrorCode errorCode,
            string message)
        {
            Success = success;
            BytesWrittenThisRun = bytesWrittenThisRun;
            ErrorCode = errorCode;
            Message = message;
        }

        public static PatchDownloadResponse Succeeded(long bytesWrittenThisRun)
        {
            return new PatchDownloadResponse(
                true,
                bytesWrittenThisRun,
                PatchTransportErrorCode.None,
                null);
        }

        public static PatchDownloadResponse Failure(
            PatchTransportErrorCode errorCode,
            long bytesWrittenThisRun,
            string message)
        {
            return new PatchDownloadResponse(
                false,
                bytesWrittenThisRun,
                errorCode,
                message);
        }

        public static PatchDownloadResponse Canceled()
        {
            return new PatchDownloadResponse(
                false,
                0,
                PatchTransportErrorCode.Canceled,
                "Canceled.");
        }
    }

    public interface IPatchTransport
    {
        Task<PatchDownloadResponse> DownloadAsync(
            string url,
            string partFilePath,
            long offsetBytes,
            long expectedTotalBytes,
            IProgress<PatchProgress> progress,
            CancellationToken cancellationToken);
    }
}
