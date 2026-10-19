using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Integration;

namespace Game.AssetSystem.Release
{
    public sealed class GatedBundlePayloadFactory : IBundlePayloadFactory
    {
        private readonly IBundlePayloadFactory _inner;
        private readonly AssetLoadGate _gate;

        public GatedBundlePayloadFactory(
            IBundlePayloadFactory inner,
            AssetLoadGate gate)
        {
            _inner = inner ??
                     throw new ArgumentNullException(nameof(inner));
            _gate = gate ??
                    throw new ArgumentNullException(nameof(gate));
        }

        public Task<IBundlePayload> LoadAsync(
            RuntimeBundle bundle,
            CancellationToken cancellationToken)
        {
            return _gate.Enqueue(
                () => _inner.LoadAsync(bundle, cancellationToken),
                "Bundle:" + bundle.FileName);
        }
    }
}
