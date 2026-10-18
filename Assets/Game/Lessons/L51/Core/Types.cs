using System;

namespace Game.AssetSystem.Integration.L51
{
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        public string Value { get; }

        public AssetKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool Equals(AssetKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(AssetKey left, AssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AssetKey left, AssetKey right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct BundleId : IEquatable<BundleId>
    {
        public int Value { get; }

        public BundleId(int value)
        {
            Value = value;
        }

        public bool Equals(BundleId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is BundleId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(BundleId left, BundleId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BundleId left, BundleId right)
        {
            return !left.Equals(right);
        }
    }

    public enum AssetLoadStatus
    {
        Pending = 0,
        Succeeded = 1,
        Failed = 2,
        Canceled = 3
    }

    public enum AssetLoadErrorCode
    {
        None = 0,
        KeyNotFound = 1,
        TypeMismatch = 2,
        BundleLoadFailed = 3,
        AssetPayloadFailed = 4,
        ScopeDisposed = 5,
        Canceled = 6,
        InternalError = 7
    }

    public readonly struct AssetLoadError
    {
        public AssetLoadErrorCode Code { get; }
        public string Message { get; }

        public AssetLoadError(AssetLoadErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public readonly struct AssetLoadResult<T>
        where T : class
    {
        public AssetLoadStatus Status { get; }
        public AssetHandle<T> Handle { get; }
        public T Value { get; }
        public AssetLoadError Error { get; }
        public AssetKey Key { get; }
        public string Owner { get; }

        public AssetLoadResult(
            AssetLoadStatus status,
            AssetHandle<T> handle,
            T value,
            AssetLoadError error,
            AssetKey key,
            string owner)
        {
            Status = status;
            Handle = handle;
            Value = value;
            Error = error;
            Key = key;
            Owner = owner;
        }

        public bool IsSuccess
        {
            get { return Status == AssetLoadStatus.Succeeded; }
        }
    }

    public interface IReleasableAssetPayload
    {
        void ReleasePayload();
    }
}
