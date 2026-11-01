using GameUI.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Editor
{
    public static class UIViewCreator
    {
        [MenuItem("GameObject/GameUI/Create View", false, 10)]
        public static void CreateView()
        {
            var root = new GameObject("UIView", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(UIBindingData));
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 500f);
            root.GetComponent<Canvas>().overrideSorting = true;
            Undo.RegisterCreatedObjectUndo(root, "Create GameUI View");
            Selection.activeGameObject = root;
        }
    }
}
