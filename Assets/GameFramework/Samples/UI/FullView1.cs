using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace GameFramework.UI.Samples
{
    [Preserve]
    public sealed class FullView1 : UIGameView
    {
        protected override void OnAwake()
        {
            transform.Find("Button").GetComponent<Button>().onClick.AddListener(CloseSelf);
        }

        protected override void OnStart(object[] args)
        {
            var template = UIViewTemplate();
            var layout = transform.Find("Layout");
            for (var i = 0; i < 4; i++) CreateItem<ButtonItem>(template, layout, i);
        }

        private GameObject UIViewTemplate()
        {
            foreach (var node in transform.GetComponentsInChildren<Transform>(true))
                if (node.name == nameof(ButtonItem)) return node.gameObject;
            throw new System.InvalidOperationException("FullView1 is missing its ButtonItem template.");
        }
    }
}
