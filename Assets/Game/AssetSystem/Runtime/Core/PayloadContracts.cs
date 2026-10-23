namespace Game.AssetSystem.Core
{
    /// <summary>
    /// A payload that owns resources and must be released when nobody ends up
    /// receiving it (canceled waiter, abandoned worker result).
    ///
    /// It lives in Core because every layer needs to release abandoned
    /// payloads without knowing what they are: the scheduler, the shared-load
    /// operation and the caches all hit the same path.
    /// </summary>
    public interface IReleasableAssetPayload
    {
        void ReleasePayload();
    }
}
