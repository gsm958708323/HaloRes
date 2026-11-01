using UnityEngine;
using UnityEngine.UI;
namespace GameUI.Components
{
    public sealed class UIModelView : MonoBehaviour
    {
        public RawImage target;
        public GameObject modelPrefab;
        public Vector2Int rtSize = new Vector2Int(512, 512);
        public string stageTag;
        public int flags;
        public new string tag;
    }
}
