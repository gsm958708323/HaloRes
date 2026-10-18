using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Integration.L51;
using UnityEngine;

namespace Game.AssetSystem.Integration.Release.L52
{
    public sealed class RealBundlePayloadFactory : IBundlePayloadFactory
    {
        public Task<IBundlePayload> LoadAsync(
            RuntimeBundle bundle,
            CancellationToken cancellationToken)
        {
            IntegrationMainThreadPump.EnsureMainThread(
                nameof(RealBundlePayloadFactory) + "." + nameof(LoadAsync));

            if (cancellationToken.IsCancellationRequested)
            {
                var canceled = new TaskCompletionSource<IBundlePayload>();
                canceled.SetException(new OperationCanceledException());
                return canceled.Task;
            }

            if (bundle == null)
            {
                var tcs = new TaskCompletionSource<IBundlePayload>();
                tcs.SetException(new ArgumentNullException(nameof(bundle)));
                return tcs.Task;
            }

            if (!File.Exists(bundle.AbsolutePath))
            {
                var tcs = new TaskCompletionSource<IBundlePayload>();
                tcs.SetException(new FileNotFoundException(
                    "Bundle file not found: " + bundle.AbsolutePath,
                    bundle.AbsolutePath));
                return tcs.Task;
            }

            var loaded = new TaskCompletionSource<IBundlePayload>();

            try
            {
                var assetBundle = AssetBundle.LoadFromFile(bundle.AbsolutePath);

                if (assetBundle == null)
                {
                    loaded.SetException(new InvalidOperationException(
                        "AssetBundle.LoadFromFile returned null. path=" +
                        bundle.AbsolutePath));
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
