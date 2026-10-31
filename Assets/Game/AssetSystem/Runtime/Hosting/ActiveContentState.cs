using System;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Storage;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// What the client remembers between runs: the activated manifest plus the
    /// folder its bundles live in. Written only after a successful activation,
    /// so a failed switch never leaves the client pointing at half content.
    ///
    /// Field names are the on-disk contract — <c>active.json</c> written by an
    /// older build must still load, so do not rename them.
    ///
    /// <see cref="bundles"/> duplicates what the manifest already carries, but
    /// it is what lets <see cref="AssetFileStore.TryLoadActiveState"/> and the
    /// patch pipeline read the same file without depending on the manifest
    /// shape. <see cref="bundleRoot"/> has the same role. Keep both in sync
    /// with the manifest when writing.
    /// </summary>
    [Serializable]
    public sealed class ActiveContentState
    {
        public int schemaVersion;
        public string contentVersion;
        public string activatedAtUtc;
        public string bundleRoot;
        public BundleHashRecord[] bundles;
        public PatchManifest manifest;
    }
}
