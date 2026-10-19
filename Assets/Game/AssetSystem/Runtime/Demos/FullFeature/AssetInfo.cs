using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// Content of a leaf bundle. Holds a reference into the <c>shared</c>
    /// bundle, which is what makes the dependency edge real instead of
    /// declared-only.
    /// </summary>
    public sealed class AssetInfo : ScriptableObject
    {
        public string label;
        public SharedInfo shared;
    }
}
