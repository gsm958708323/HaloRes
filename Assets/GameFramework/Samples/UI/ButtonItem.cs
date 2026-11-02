using UnityEngine.Scripting;
using UnityEngine.UI;

namespace GameFramework.UI.Samples
{
    [Preserve]
    public sealed class ButtonItem : UIItem
    {
        protected override void OnStart(object[] args)
        {
            var index = (int)args[0];
            gameObject.GetComponentInChildren<Button>().onClick.AddListener(() =>
            {
                switch (index)
                {
                    case 0: Parent.OpenOneChildUI<ChildView1>(); break;
                    case 1: Parent.OpenOneChildUI<ChildView2>(); break;
                    case 2: Parent.OpenOneChildUI<ChildView3>(); break;
                    case 3: Parent.OpenOneChildUI<ChildView4>(); break;
                }
            });
            gameObject.GetComponentInChildren<Text>().text = "Child " + (index + 1);
        }
    }
}
