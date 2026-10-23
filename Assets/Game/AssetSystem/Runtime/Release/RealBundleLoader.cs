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
    ///
    /// The load itself is asynchronous. <c>AssetBundle.LoadFromFile</c> blocks
    /// the frame for as long as the read takes — tens of milliseconds per
    /// bundle on a cold file cache — and the gate only limited how many of
    /// those spikes a frame could contain. <c>LoadFromFileAsync</c> plus a
    /// per-frame poll moves the read off the frame entirely:
    ///
    /// <list type="bullet">
    /// <item>the gate still decides how many loads may <em>start</em> per frame;</item>
    /// <item>this loader decides how long each one may take to <em>finish</em>.</item>
    /// </list>
    ///
    /// Asset-level <c>LoadAsset</c> stays synchronous — that is the normal
    /// Unity usage and out of scope here.
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
                return Failed(new OperationCanceledException());
            }

            if (bundle == null)
            {
                return Failed(new ArgumentNullException(nameof(bundle)));
            }

            if (string.IsNullOrEmpty(bundle.SourcePath))
            {
                return Failed(new InvalidOperationException(
                    "Bundle has no SourcePath: " + bundle.Name));
            }

            if (!File.Exists(bundle.SourcePath))
            {
                return Failed(new FileNotFoundException(
                    "Bundle file not found: " + bundle.SourcePath,
                    bundle.SourcePath));
            }

            var completion = new TaskCompletionSource<IBundlePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            AssetBundleCreateRequest request;

            try
            {
                request = AssetBundle.LoadFromFileAsync(bundle.SourcePath);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                return completion.Task;
            }

            if (request == null)
            {
                completion.TrySetException(new InvalidOperationException(
                    "AssetBundle.LoadFromFileAsync returned null. path=" +
                    bundle.SourcePath));
                return completion.Task;
            }

            var state = new LoadState(
                request,
                bundle.SourcePath,
                completion,
                cancellationToken);

            // First look happens next frame: the request cannot be done in the
            // frame it was started in, and reading isDone here would only burn
            // a pump slot.
            MainThreadPump.PostNextFrame(state.Poll);

            return completion.Task;
        }

        private static Task<IBundlePayload> Failed(Exception exception)
        {
            var completion = new TaskCompletionSource<IBundlePayload>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            completion.SetException(exception);
            return completion.Task;
        }

        /// <summary>
        /// One in-flight <c>LoadFromFileAsync</c>. An object rather than a
        /// closure chain so the polling loop allocates nothing per frame.
        /// </summary>
        private sealed class LoadState
        {
            private readonly AssetBundleCreateRequest _request;
            private readonly string _path;
            private readonly TaskCompletionSource<IBundlePayload> _completion;
            private readonly CancellationToken _cancellationToken;

            private bool _abandoned;

            public LoadState(
                AssetBundleCreateRequest request,
                string path,
                TaskCompletionSource<IBundlePayload> completion,
                CancellationToken cancellationToken)
            {
                _request = request;
                _path = path;
                _completion = completion;
                _cancellationToken = cancellationToken;
            }

            public void Poll()
            {
                if (_abandoned)
                {
                    PollAbandoned();
                    return;
                }

                if (_completion.Task.IsCompleted)
                {
                    return;
                }

                if (_cancellationToken.IsCancellationRequested)
                {
                    // Unity offers no cancel for this request. The caller is
                    // told "canceled" at once; polling continues only so the
                    // bundle that eventually arrives can be unloaded instead of
                    // leaking its file mapping.
                    _abandoned = true;
                    _completion.TrySetCanceled();
                    PollAbandoned();
                    return;
                }

                if (!_request.isDone)
                {
                    MainThreadPump.PostNextFrame(Poll);
                    return;
                }

                var bundle = _request.assetBundle;

                if (bundle == null)
                {
                    _completion.TrySetException(new InvalidOperationException(
                        "AssetBundle.LoadFromFileAsync finished with a null " +
                        "bundle. path=" + _path));
                    return;
                }

                _completion.TrySetResult(new RealBundlePayload(bundle));
            }

            private void PollAbandoned()
            {
                if (!_request.isDone)
                {
                    MainThreadPump.PostNextFrame(PollAbandoned);
                    return;
                }

                var bundle = _request.assetBundle;

                if (bundle != null)
                {
                    // Nobody owns this payload, so drop the file mapping now
                    // rather than leaving it to the GC.
                    bundle.Unload(true);
                }
            }
        }
    }
}
