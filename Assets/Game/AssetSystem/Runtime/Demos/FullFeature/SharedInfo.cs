using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// Content shared by <c>ui_shop</c> and <c>battle_fx</c>. It lives in its
    /// own bundle so the demo can prove that two dependent bundles reuse one
    /// payload instead of loading it twice.
    /// </summary>
    public sealed class SharedInfo : ScriptableObject
    {
        public string version;
        public Color color = Color.white;
        public string label;
    }
}
