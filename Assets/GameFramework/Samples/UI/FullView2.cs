using UnityEngine.Scripting;
using UnityEngine.UI;

namespace GameFramework.UI.Samples
{
    [Preserve]
    public sealed class FullView2 : UIGameView
    {
        protected override void OnAwake()
        {
            transform.Find("Button").GetComponent<Button>().onClick.AddListener(CloseSelf);
        }
    }
}

