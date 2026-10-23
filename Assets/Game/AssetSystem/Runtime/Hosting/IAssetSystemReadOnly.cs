using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Memory;

namespace Game.AssetSystem.Hosting
{
    /// <summary>
    /// The read-only half of <see cref="AssetSystemFacade"/>: what a HUD, a
    /// loading screen or a debug panel is allowed to look at. Nothing here
    /// mutates runtime state.
    /// </summary>
    public interface IAssetSystemReadOnly
    {
        bool Ready { get; }

        string ActiveVersion { get; }

        int GatePendingCount { get; }

        MemoryPressureLevel Pressure { get; }

        AssetRuntimeDiagnostics Diagnostics { get; }
    }
}
