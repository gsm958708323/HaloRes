using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Editor
{
    public static class UIStyleMenu
    {
        [MenuItem("GameObject/GameUI/Create Styled Button", false, 11)]
        public static void CreateButton()
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var parent = Selection.activeTransform;
            if (parent != null) go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.color = new Color(0.08f, 0.12f, 0.2f, 1f);
            Undo.RegisterCreatedObjectUndo(go, "Create GameUI Button");
            Selection.activeGameObject = go;
        }
    }
}
