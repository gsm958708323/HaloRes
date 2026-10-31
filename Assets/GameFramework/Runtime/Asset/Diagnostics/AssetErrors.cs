using System;

namespace GameFramework.Asset.Diagnostics
{
    public enum AssetErrorCode
    {
        AddressNotFound,
        BundleFileMissing,
        BundleLoadFailed,
        AssetNotInBundle,
        TypeMismatch,
        SceneLoadFailed,
        ScopeDisposed,
        DownloadFailed,
        HashMismatch,
        VersionInvalid,
        MainThreadViolation
    }

    public sealed class AssetError
    {
        public AssetError(AssetErrorCode code, string address, string bundle, string detail)
        {
            Code = code;
            Address = address ?? string.Empty;
            Bundle = bundle ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public AssetErrorCode Code { get; }
        public string Address { get; }
        public string Bundle { get; }
        public string Detail { get; }

        public override string ToString()
        {
            var location = string.IsNullOrEmpty(Address) ? Bundle : Address + " (" + Bundle + ")";
            return Code + ": " + location + (string.IsNullOrEmpty(Detail) ? string.Empty : " - " + Detail);
        }
    }

    public sealed class AssetException : Exception
    {
        public AssetException(AssetError error)
            : base(error == null ? "Asset operation failed." : error.ToString())
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public AssetError Error { get; }
    }
}
