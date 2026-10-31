using System;
using UnityEngine;

namespace GameFramework.Asset.Editor.Build
{
    [CreateAssetMenu(menuName = "Game Framework/Asset Build Profile")]
    public sealed class AssetBuildProfile : ScriptableObject
    {
        public int version = 1;
        public AssetBuildEntry[] entries = Array.Empty<AssetBuildEntry>();
    }

    [Serializable]
    public sealed class AssetBuildEntry
    {
        public string address;
        public string bundle;
        public UnityEngine.Object asset;
    }
}
