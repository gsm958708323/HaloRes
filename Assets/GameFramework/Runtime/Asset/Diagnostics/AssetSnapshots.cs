using System;
using System.Collections.Generic;

namespace GameFramework.Asset.Diagnostics
{
    public sealed class AssetSnapshot
    {
        internal AssetSnapshot(string address, string state, int referenceCount, IReadOnlyList<string> owners, string error)
        {
            Address = address;
            State = state;
            ReferenceCount = referenceCount;
            Owners = owners;
            Error = error;
        }

        public string Address { get; }
        public string State { get; }
        public int ReferenceCount { get; }
        public IReadOnlyList<string> Owners { get; }
        public string Error { get; }
    }

    public sealed class BundleSnapshot
    {
        internal BundleSnapshot(string name, string state, int referenceCount, long size, string hash, string error)
        {
            Name = name;
            State = state;
            ReferenceCount = referenceCount;
            Size = size;
            Hash = hash;
            Error = error;
        }

        public string Name { get; }
        public string State { get; }
        public int ReferenceCount { get; }
        public long Size { get; }
        public string Hash { get; }
        public string Error { get; }
    }

    public sealed class AssetSystemSnapshot
    {
        internal AssetSystemSnapshot(IReadOnlyList<AssetSnapshot> assets, IReadOnlyList<BundleSnapshot> bundles)
        {
            Assets = assets;
            Bundles = bundles;
            CapturedAtUtc = DateTime.UtcNow;
        }

        public DateTime CapturedAtUtc { get; }
        public IReadOnlyList<AssetSnapshot> Assets { get; }
        public IReadOnlyList<BundleSnapshot> Bundles { get; }
    }
}
