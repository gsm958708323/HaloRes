using System;
using Game.AssetSystem.Patch;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// What the client remembers between runs: the activated manifest plus the
    /// folder its bundles live in. Written only after a successful activation,
    /// so a failed switch never leaves the client pointing at half content.
    ///
    /// Field names are the on-disk contract — <c>active.json</c> written by an
    /// older build must still load, so do not rename them.
    /// </summary>
    [Serializable]
    public sealed class ActiveContentState
    {
        public int schemaVersion;
        public string contentVersion;
        public string bundleRoot;
        public PatchManifest manifest;
    }
}
