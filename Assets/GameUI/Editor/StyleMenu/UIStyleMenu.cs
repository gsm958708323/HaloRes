using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace GameUI.Editor
{
    public static class UIStyleMenu
    {
        [MenuItem("GameObject/GameUI/Create Styled Image",false,11)]
        public static void CreateImage() { Create("Image",typeof(Image)).GetComponent<Image>().raycastTarget=false; }
        [MenuItem("GameObject/GameUI/Create Styled Text",false,12)]
        public static void CreateText()
        {
            var text=Create("Text",typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.text="Text"; text.fontSize=28; text.raycastTarget=false; text.color=Color.white;
            text.font=TMP_Settings.defaultFontAsset;
        }
        [MenuItem("GameObject/GameUI/Create Styled Button",false,13)]
        public static void CreateButton()
        {
            var go=Create("Button",typeof(Image),typeof(Button));
            go.GetComponent<Image>().color=new Color(0.08f,0.12f,0.2f,1f);
            var textObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
            textObject.transform.SetParent(go.transform,false);
            var rect=textObject.GetComponent<RectTransform>();
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
            var text=textObject.GetComponent<TextMeshProUGUI>();
            text.text="Button"; text.fontSize=28; text.raycastTarget=false; text.alignment=TextAlignmentOptions.Center;
            text.font=TMP_Settings.defaultFontAsset;
        }
        private static GameObject Create(string name,params System.Type[] types)
        {
            var go=new GameObject(name,typeof(RectTransform));
            if (Selection.activeTransform != null) go.transform.SetParent(Selection.activeTransform,false);
            foreach (var type in types) go.AddComponent(type);
            Undo.RegisterCreatedObjectUndo(go,"Create styled UI");
            Selection.activeGameObject=go;
            return go;
        }
    }
}
