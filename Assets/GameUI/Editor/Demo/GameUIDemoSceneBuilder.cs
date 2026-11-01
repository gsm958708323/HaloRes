using System.IO;
using GameUI.Demo;
using GameUI.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace GameUI.Editor
{
    public static class GameUIDemoSceneBuilder
    {
        private const string Folder = "Assets/GameUI/Demo/Generated";

        [MenuItem("GameUI/Create Complete Demo")]
        public static void Create()
        {
            EnsureFolder();
            CreatePrefab("DemoHome", new Color(0.04f, 0.07f, 0.13f, 1f), new[] { "OpenPopup", "OpenOverlay" });
            CreatePrefab("DemoPopup", new Color(0.12f, 0.18f, 0.32f, 1f), new[] { "ClosePopup" });
            CreatePrefab("DemoOverlay", new Color(0.2f, 0.08f, 0.28f, 0.95f), new[] { "CloseOverlay" });

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("WorldCamera", typeof(Camera));
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.015f, 0.02f, 0.04f, 1f);
            camera.GetComponent<Camera>().transform.position = new Vector3(0f, 0f, -10f);
            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(UIRoot));
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var bootstrap = root.AddComponent<GameUIDemoBootstrap>();
            var rootField = typeof(GameUIDemoBootstrap).GetField("root", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            rootField.SetValue(bootstrap, root.GetComponent<UIRoot>());
            var cameraField = typeof(GameUIDemoBootstrap).GetField("worldCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            cameraField.SetValue(bootstrap, camera.GetComponent<Camera>());
            EditorSceneManager.SaveScene(scene, "Assets/GameUI/Demo/GameUIDemo.unity");
            Selection.activeGameObject = root;
            Debug.Log("Created GameUI demo at Assets/GameUI/Demo/GameUIDemo.unity");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/GameUI/Demo")) AssetDatabase.CreateFolder("Assets/GameUI", "Demo");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameUI/Demo", "Generated");
        }

        private static void CreatePrefab(string name, Color color, string[] buttons)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(760f, 420f);
            var image = root.AddComponent<Image>(); image.color = color;
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = new GameObject(buttons[i], typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(root.transform, false);
                var buttonRect = button.GetComponent<RectTransform>(); buttonRect.sizeDelta = new Vector2(220f, 56f); buttonRect.anchoredPosition = new Vector2(0f, 80f - i * 80f);
                button.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.95f, 1f);
                var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(button.transform, false);
                var labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
                var text = label.GetComponent<Text>(); text.text = buttons[i]; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            var path = Folder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
    }
}
