using System;
using System.IO;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Core;
using UnityEngine;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// Drop-in MonoBehaviour for game scenes.
    ///
    /// Awake creates the facade (and makes sure the main-thread pump exists),
    /// Start initialises it, Update ticks it. A game that wants more control
    /// can skip this component entirely and drive
    /// <see cref="AssetSystemFacade"/> itself — the component adds no
    /// behaviour the facade does not have.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssetSystemHost : MonoBehaviour
    {
        [Header("Client")]
        [Tooltip("Absolute client folder. Empty = persistentDataPath/<folder name>.")]
        [SerializeField] private string clientRoot = string.Empty;

        [Tooltip("Folder the base version is installed from on first run.")]
        [SerializeField] private string baseContentRoot = string.Empty;

        [SerializeField] private string baseVersion = "v1";

        [SerializeField] private string manifestFileName = "lesson43-manifest.json";

        [Header("Runtime")]
        [SerializeField] private int maxStartsPerFrame = 2;

        [SerializeField] private int maxConcurrentBundleLoads = 4;

        [SerializeField] private bool useLoadGate = true;

        [Tooltip("Uncheck to call InitializeAsync() yourself.")]
        [SerializeField] private bool initializeOnStart = true;

        public AssetSystemFacade Facade { get; private set; }

        /// <summary>
        /// Raised once the facade is ready (or failed). Handlers receive the
        /// facade so they can start loading without holding a reference to the
        /// component.
        /// </summary>
        public event Action<AssetSystemFacade> Initialized;

        private void Awake()
        {
            MainThreadPump.EnsureExists();

            Facade = new AssetSystemFacade(BuildOptions());
        }

        private async void Start()
        {
            if (!initializeOnStart || Facade == null)
            {
                return;
            }

            try
            {
                if (!await Facade.InitializeAsync())
                {
                    Debug.LogError(
                        "[AssetSystemHost] init failed: " + Facade.InitError);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            var handler = Initialized;

            if (handler != null)
            {
                handler(Facade);
            }
        }

        private void Update()
        {
            if (Facade != null && Facade.Ready)
            {
                Facade.Tick();
            }
        }

        private void OnDestroy()
        {
            if (Facade != null)
            {
                Facade.ReleaseAllScopes();
                Facade = null;
            }
        }

        /// <summary>
        /// Convenience for game code: one call, one scope, one handle.
        /// </summary>
        public AssetLoadTask<T> LoadAsync<T>(string key, string scopeName)
            where T : class
        {
            if (Facade == null)
            {
                throw new InvalidOperationException(
                    "AssetSystemHost has no facade yet.");
            }

            return Facade.LoadAsync<T>(key, scopeName);
        }

        private AssetSystemOptions BuildOptions()
        {
            var root = clientRoot;

            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Application.persistentDataPath,
                    "AssetSystemContent");
            }

            return new AssetSystemOptions
            {
                ClientRoot = Path.GetFullPath(root),
                BaseContentRoot = string.IsNullOrWhiteSpace(baseContentRoot)
                    ? null
                    : Path.GetFullPath(baseContentRoot),
                BaseVersion = baseVersion,
                ManifestFileName = manifestFileName,
                MaxStartsPerFrame = Math.Max(1, maxStartsPerFrame),
                MaxConcurrentBundleLoads = Math.Max(1, maxConcurrentBundleLoads),
                UseLoadGate = useLoadGate
            };
        }
    }
}
