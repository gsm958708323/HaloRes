using GameUI.Kernel;
using UnityEngine;

namespace GameUI.Runtime
{
    public interface IUIAssetService
    {
        IUILoadOp<IUIAssetHandle> LoadPrefab(string path);
    }

    public interface IUIAssetHandle
    {
        Object Asset { get; }
        void Release();
    }
}
