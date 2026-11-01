using UnityEngine;
namespace GameUI.Components
{
    public sealed class UISortingSlot : MonoBehaviour
    {
        [Range(1, 17)] public int offset = 1;
        public int flags;
        public new string tag;
    }
}
