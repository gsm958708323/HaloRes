using System;
using System.Collections.Generic;

namespace Game.AssetSystem.Patch.L47
{
    public readonly struct RuntimeAssetKey : IEquatable<RuntimeAssetKey>
    {
        public string Value { get; }

        public RuntimeAssetKey(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool Equals(RuntimeAssetKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeAssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(RuntimeAssetKey left, RuntimeAssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RuntimeAssetKey left, RuntimeAssetKey right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct RuntimeBundleId : IEquatable<RuntimeBundleId>
    {
        public int Value { get; }

        public RuntimeBundleId(int value)
        {
            Value = value;
        }

        public bool Equals(RuntimeBundleId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeBundleId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(RuntimeBundleId left, RuntimeBundleId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RuntimeBundleId left, RuntimeBundleId right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class RuntimeAssetEntry
    {
        public RuntimeAssetKey Key;
        public RuntimeBundleId BundleId;
        public string InternalAssetPath;
        public string SourceBundleGroup;
    }

    public sealed class RuntimeBundleEntry
    {
        public RuntimeBundleId Id;
        public string BundleGroupName;
        public string FileName;
        public string AbsolutePath;
        public long SizeBytes;
        public string Sha256Hex;
    }

    public sealed class RuntimeContentContext
    {
        public string ContentVersion { get; }
        public string PlatformKey { get; }
        public string BundleRoot { get; }

        private readonly Dictionary<RuntimeAssetKey, RuntimeAssetEntry> _assets;
        private readonly Dictionary<RuntimeBundleId, RuntimeBundleEntry> _bundles;

        public RuntimeContentContext(
            string contentVersion,
            string platformKey,
            string bundleRoot,
            IEnumerable<RuntimeAssetEntry> assets,
            IEnumerable<RuntimeBundleEntry> bundles)
        {
            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                throw new ArgumentException("contentVersion must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(platformKey))
            {
                throw new ArgumentException("platformKey must not be empty.");
            }

            ContentVersion = contentVersion;
            PlatformKey = platformKey;
            BundleRoot = bundleRoot ?? string.Empty;

            _assets = new Dictionary<RuntimeAssetKey, RuntimeAssetEntry>();
            _bundles = new Dictionary<RuntimeBundleId, RuntimeBundleEntry>();

            if (assets != null)
            {
                foreach (var a in assets)
                {
                    if (a != null) _assets[a.Key] = a;
                }
            }

            if (bundles != null)
            {
                foreach (var b in bundles)
                {
                    if (b != null) _bundles[b.Id] = b;
                }
            }
        }

        public bool TryGetAsset(RuntimeAssetKey key, out RuntimeAssetEntry entry)
        {
            return _assets.TryGetValue(key, out entry);
        }

        public bool TryGetBundle(RuntimeBundleId id, out RuntimeBundleEntry entry)
        {
            return _bundles.TryGetValue(id, out entry);
        }

        public IEnumerable<RuntimeBundleEntry> AllBundles
        {
            get { return _bundles.Values; }
        }

        public int AssetCount
        {
            get { return _assets.Count; }
        }

        public int BundleCount
        {
            get { return _bundles.Count; }
        }
    }
}
