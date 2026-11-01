using System;
using System.IO;
using GameUI.Components;
using GameUI.Kernel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace GameUI.Editor
{
    public static class UIViewCreator
    {
        [MenuItem("Assets/GameUI/Create UI From Prefab", false, 10)]
        public static void CreateFromSelectedPrefab()
        {
            var prefab = Selection.activeObject as GameObject;
            var path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a saved UI prefab.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureComponents(root);
                var directory = Path.GetDirectoryName(path).Replace('\\','/');
                var transitions=root.GetComponent<UIViewTransitions>();
                if (transitions.enter == null) transitions.enter=CreateDirector(root,"Enter",directory+"/"+root.name+"Enter.playable",0,root.GetComponent<CanvasGroup>().alpha);
                if (transitions.exit == null) transitions.exit=CreateDirector(root,"Exit",directory+"/"+root.name+"Exit.playable",root.GetComponent<CanvasGroup>().alpha,0);
                var className = UIRegistryEmitter.Identifier(root.name);
                if (!className.EndsWith("View", StringComparison.Ordinal)) className += "View";
                var type = "GameUI.Views." + className;
                var data = root.GetComponent<UIBindingData>();
                data.logicType = type;
                var image = root.GetComponent<Image>();
                var rect = root.GetComponent<RectTransform>();
                var full = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one && image != null && image.color.a >= 0.99f;
                var sourcePath = directory + "/" + className + ".cs";
                if (!File.Exists(sourcePath))
                {
                    var source = "using GameUI.Kernel;\nusing GameUI.Runtime;\nnamespace GameUI.Views\n{\n"
                        + "    public sealed class " + className + "Args : UIArgs<" + className + "> { }\n"
                        + "    [UIView(Mode=UIMode." + (full ? "FullScreen" : "Popup") + ", CoversWorld=" + (full ? "true" : "false")
                        + ", PrefabPath=" + UIRegistryEmitter.Literal(path) + ")]\n"
                        + "    public sealed partial class " + className + " : UIView<" + className + "Args>\n"
                        + "    {\n        protected override void OnOpen(" + className + "Args args) { }\n    }\n}\n";
                    UIRegistryEmitter.WriteIfChanged(sourcePath,source);
                }
                UIBindingEmitter.Generate(data,className,"GameUI.Views",directory);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.Refresh();
        }

        [MenuItem("GameObject/GameUI/Create View", false, 10)]
        public static void CreateView()
        {
            var root = new GameObject("UIView",typeof(RectTransform));
            if (Selection.activeTransform != null) root.transform.SetParent(Selection.activeTransform,false);
            EnsureComponents(root);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(800f,500f);
            Undo.RegisterCreatedObjectUndo(root,"Create GameUI View");
            Selection.activeGameObject = root;
        }

        internal static void EnsureComponents(GameObject root)
        {
            if (root.GetComponent<Canvas>() == null) root.AddComponent<Canvas>();
            if (root.GetComponent<CanvasGroup>() == null) root.AddComponent<CanvasGroup>();
            if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
            if (root.GetComponent<UIBindingData>() == null) root.AddComponent<UIBindingData>();
            var transitions = root.GetComponent<UIViewTransitions>() ?? root.AddComponent<UIViewTransitions>();
            root.GetComponent<Canvas>().overrideSorting = true;
            if (transitions.enter != null) Configure(transitions.enter);
            if (transitions.exit != null) Configure(transitions.exit);
        }

        internal static void Configure(PlayableDirector director)
        {
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;
        }

        internal static PlayableDirector CreateDirector(GameObject root,string name,string path,float start,float end)
        {
            var timeline=AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null)
            {
                timeline=ScriptableObject.CreateInstance<TimelineAsset>();AssetDatabase.CreateAsset(timeline,path);
                var track=timeline.CreateTrack<CanvasGroupAlphaTrack>(null,name);
                var clip=track.CreateClip<CanvasGroupAlphaClip>();clip.start=0;clip.duration=0.12;
                var asset=(CanvasGroupAlphaClip)clip.asset;asset.template.startAlpha=start;asset.template.endAlpha=end;
                EditorUtility.SetDirty(asset);EditorUtility.SetDirty(timeline);
            }
            var child=new GameObject(name,typeof(PlayableDirector));child.transform.SetParent(root.transform,false);
            var director=child.GetComponent<PlayableDirector>();Configure(director);director.playableAsset=timeline;
            foreach(var track in timeline.GetOutputTracks()) director.SetGenericBinding(track,root.GetComponent<CanvasGroup>());
            return director;
        }
    }
}
