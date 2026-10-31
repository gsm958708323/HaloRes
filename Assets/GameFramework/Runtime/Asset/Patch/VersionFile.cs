using System;
using GameFramework.Asset.Diagnostics;
using UnityEngine;

namespace GameFramework.Asset.Patch
{
    [Serializable]
    public sealed class VersionFile
    {
        public int version;
        public string platform;
        public string manifestHash;
        public long manifestSize;

        public static VersionFile Parse(string json, string expectedPlatform)
        {
            VersionFile file;
            try { file = JsonUtility.FromJson<VersionFile>(json); }
            catch (Exception error) { throw Invalid(error.Message); }
            if (file == null || file.version < 1 || file.manifestSize < 1 || !FileIntegrity.IsHash(file.manifestHash))
                throw Invalid("Invalid version, manifest size or SHA-256.");
            if (file.platform != expectedPlatform) throw Invalid("Expected platform " + expectedPlatform + ", received " + file.platform);
            return file;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);
        private static AssetException Invalid(string detail) => new AssetException(new AssetError(AssetErrorCode.VersionInvalid, null, null, detail));
    }
}
