using System;
using System.IO;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// Everything a game has to tell the asset system about itself: where the
    /// client folder lives, which version ships with the build, and how much
    /// bundle loading may cost per frame.
    ///
    /// Nothing here mentions a demo. The full-feature demo fills the same
    /// fields from its own layout contract, so the framework path and the demo
    /// path are the same code with different numbers.
    /// </summary>
    public sealed class AssetSystemOptions
    {
        /// <summary>
        /// Client folder. Content, staging and state live underneath it, so a
        /// test can point one run at a throwaway folder.
        /// </summary>
        public string ClientRoot;

        /// <summary>
        /// Folder the base version is installed from on first run: a local
        /// build output folder today, a CDN mirror in a real deployment.
        /// </summary>
        public string BaseContentRoot;

        /// <summary>Build-target key written into the manifest.</summary>
        public string PlatformKey = PlatformKeys.Current;

        /// <summary>Version installed when the client has no active state yet.</summary>
        public string BaseVersion = "v1";

        public string ManifestFileName = "lesson43-manifest.json";

        public string ContentFolderName = "content";
        public string StagingFolderName = "staging";
        public string StateFolderName = "state";
        public string ActiveStateFileName = "active.json";

        /// <summary>Schema tag written into <c>active.json</c>.</summary>
        public int ActiveStateSchemaVersion = 1;

        /// <summary>How many bundle payload loads may start per frame.</summary>
        public int MaxStartsPerFrame = 2;

        /// <summary>How many bundle payload loads may be in flight at once.</summary>
        public int MaxConcurrentBundleLoads = 4;

        /// <summary>
        /// When true the runtime loads bundles through
        /// <c>GatedBundleLoader</c>, so the per-frame budget applies. Turn it
        /// off only when the caller drives the loader itself.
        /// </summary>
        public bool UseLoadGate = true;

        public string ContentRoot
        {
            get { return Path.Combine(ClientRoot, ContentFolderName); }
        }

        public string StagingRoot
        {
            get { return Path.Combine(ClientRoot, StagingFolderName); }
        }

        public string StateRoot
        {
            get { return Path.Combine(ClientRoot, StateFolderName); }
        }

        public string ActiveStatePath
        {
            get { return Path.Combine(StateRoot, ActiveStateFileName); }
        }

        /// <summary>
        /// Fails fast on the two fields that cannot be guessed. A relative
        /// client root would silently resolve against the process directory,
        /// which differs between editor, player and tests.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ClientRoot))
            {
                throw new ArgumentException(
                    "AssetSystemOptions.ClientRoot is required.");
            }

            if (string.IsNullOrWhiteSpace(PlatformKey))
            {
                throw new ArgumentException(
                    "AssetSystemOptions.PlatformKey is required.");
            }

            // BaseVersion is only needed when there is something to install
            // from. A client that always downloads its first version has no
            // base content root and no base version either.
            if (!string.IsNullOrWhiteSpace(BaseContentRoot) &&
                string.IsNullOrWhiteSpace(BaseVersion))
            {
                throw new ArgumentException(
                    "AssetSystemOptions.BaseVersion is required when " +
                    "BaseContentRoot is set.");
            }

            if (string.IsNullOrWhiteSpace(ManifestFileName))
            {
                throw new ArgumentException(
                    "AssetSystemOptions.ManifestFileName is required.");
            }

            if (!Path.IsPathRooted(ClientRoot))
            {
                throw new ArgumentException(
                    "AssetSystemOptions.ClientRoot must be absolute: " +
                    ClientRoot);
            }
        }
    }
}
