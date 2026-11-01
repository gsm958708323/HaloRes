using GameUI.Components;
using UnityEngine;
using UnityEngine.UI;
namespace GameUI.Editor
{
    public sealed class RequiredComponentsRule : IUIPrefabLintRule
    {
        public string Id => "root-components";
        public UILintLevel Level => UILintLevel.Error;
        public void Check(GameObject root,UILintContext context)
        {
            if (root.GetComponent<Canvas>() == null) context.Report(root,"Canvas is required.");
            if (root.GetComponent<CanvasGroup>() == null) context.Report(root,"CanvasGroup is required.");
            if (root.GetComponent<GraphicRaycaster>() == null) context.Report(root,"GraphicRaycaster is required.");
            if (root.GetComponent<UIBindingData>() == null) context.Report(root,"UIBindingData is required.");
        }
    }
}
