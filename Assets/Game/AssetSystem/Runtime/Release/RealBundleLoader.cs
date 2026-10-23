using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Core;
using UnityEngine;

namespace Game.AssetSystem.Release
{
    /// <summary>
    /// Loads a real <see cref="AssetBundle"/> from the path the catalog carries.
    ///
    /// This used to be <c>RealBundlePayloadFactory</c> taking an
    /// <c>Integration.RuntimeBundle</c>; it now implements the same
    /// <see cref="IBundleLoader"/> seam the simulated loader implements, so
    /// production and demos differ only in which loader is injected.
    /// </summary>
    public sealed class RealBundleLoader : IBundleLoader
    {
        public Task<IBundlePayload> LoadAsync(
            BundleInfo bundle,
            CancellationToken cancellationToken)
        {
            MainThreadPump.EnsureMainThread(
                nameof(RealBundleLoader) + "." + nameof(LoadAsync));

            if (cancellationToken.IsCancellationRequested)
            {
                var canceled = new TaskCompletionSource<IBundlePayload>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                canceled.SetException(new OperationCanceledException());
                return canceled.Task;
            }

            if (bundle == null)
            {
                var nullBundle = new TaskCompletionSource<IBundlePayload>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                nullBundle.SetException(new ArgumentNullException(nameof(bundle)));
                return nullBundle.Task;
            }

            if (string.IsNullOrEmpty(bundle.SourcePath))
            {
                var noPath = new TaskCompletionSource<IBundlePayload>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                noPath.SetException(new InvalidOperationException(
                    "Bundle has no SourcePath: " + bundle.Name));
                return noPath.Task;
            }

            if (!File.Exists(bundle.SourcePath))
            {
                var missing = new TaskCompletionSource<IBundlePayload>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                missing.SetException(new FileNotFoundException(
                    "Bundle file not found: " + bundle.SourcePath,
                    bundle.SourcePath));
                return missing.Task;
            }

            var loaded = new TaskCompletionSource<IBundlePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var assetBundle = AssetBundle.LoadFromFile(bundle.SourcePath);

                if (assetBundle == null)
                {
                    loaded.SetException(new InvalidOperationException(
                        "AssetBundle.LoadFromFile returned null. path=" +
                        bundle.SourcePath));
                }
                else
                {
                    loaded.SetResult(new RealBundlePayload(assetBundle));
                }
            }
            catch (Exception exception)
            {
                loaded.SetException(exception);
            }

            return loaded.Task;
        }
    }
}
