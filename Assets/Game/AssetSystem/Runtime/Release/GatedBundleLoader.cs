using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles;

namespace Game.AssetSystem.Release
{
    /// <summary>
    /// Puts bundle payload loads through <see cref="AssetLoadGate"/> so at most
    /// N bundles start per frame. Wraps any <see cref="IBundleLoader"/>, so the
    /// budget applies to the real loader and the simulated one alike.
    /// </summary>
    public sealed class GatedBundleLoader : IBundleLoader
    {
        private readonly IBundleLoader _inner;
        private readonly AssetLoadGate _gate;

        public GatedBundleLoader(
            IBundleLoader inner,
            AssetLoadGate gate)
        {
            _inner = inner ??
                     throw new ArgumentNullException(nameof(inner));
            _gate = gate ??
                    throw new ArgumentNullException(nameof(gate));
        }

        public Task<IBundlePayload> LoadAsync(
            BundleInfo bundle,
            CancellationToken cancellationToken)
        {
            return _gate.Enqueue(
                () => _inner.LoadAsync(bundle, cancellationToken),
                "Bundle:" + (bundle == null ? "<null>" : bundle.Name));
        }
    }
}
