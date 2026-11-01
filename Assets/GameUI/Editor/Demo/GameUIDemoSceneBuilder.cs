using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFramework.Asset.Editor.Build;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using GameUI.Components;
using GameUI.Demo;
using GameUI.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace GameUI.Editor
{
    public static class GameUIDemoSceneBuilder
    {
        public const string Folder="Assets/GameUI/Demo/Generated";
        public const string ProfilePath=Folder+"/UIBuildProfile.asset";
        public const string BundleRoot="Build/UIContent";
        [MenuItem("GameUI/Create Complete Demo")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before regenerating the demo.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var model=CreateModel();
            CreatePrefab("Home",typeof(DemoHomeView),new[]{"OpenPopup","OpenOverlay","OpenWidgets","OpenSystem","OpenConfirm"},true,model);
            CreatePrefab("Popup",typeof(DemoPopupView),new[]{"ClosePopup"},false,model);
            CreatePrefab("Overlay",typeof(DemoOverlayView),new[]{"CloseOverlay"},false,model);
            CreatePrefab("System",typeof(DemoSystemView),new[]{"CloseSystem"},false,model);
            CreatePrefab("Confirm",typeof(DemoConfirmView),new[]{"Confirm"},false,model);
            CreatePrefab("Widgets",typeof(DemoWidgetsView),new[]{"CloseWidgets"},true,model);
            CreateProfile();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var cameraObject=new GameObject("WorldCamera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.015f,0.02f,0.04f,1);
            var light=new GameObject("Directional Light",typeof(Light));light.GetComponent<Light>().type=LightType.Directional;
            var root=new GameObject("UIRoot",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(UIRoot));
            var input=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));input.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var bootstrap=root.AddComponent<GameUIDemoBootstrap>();
            var serialized=new SerializedObject(bootstrap);
            serialized.FindProperty("root").objectReferenceValue=root.GetComponent<UIRoot>();
            serialized.FindProperty("worldCamera").objectReferenceValue=camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene,"Assets/GameUI/Demo/GameUIDemo.unity");
            if(!EditorBuildSettings.scenes.Any(s=>s.path==scene.path))
                EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(scene.path,true)}).ToArray();
            UIRegistryEmitter.Generate();AssetDatabase.SaveAssets();
            Selection.activeGameObject=root;
        }

        private static void CreatePrefab(string suffix,Type logicType,string[] buttons,bool full,GameObject model)
        {
            var go=new GameObject("Demo"+suffix,typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster),typeof(Image),typeof(UIBindingData),typeof(UIViewTransitions));
            try
            {
                var rect=go.GetComponent<RectTransform>();
                if(full){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
                else rect.sizeDelta=new Vector2(760,420);
                go.GetComponent<Canvas>().overrideSorting=true;
                var image=go.GetComponent<Image>();image.color=new Color(0.04f,0.07f,0.13f,full?1:0.98f);image.raycastTarget=false;
                var data=go.GetComponent<UIBindingData>();data.logicType=logicType.FullName;
                for(var i=0;i<buttons.Length;i++)
                {
                    var button=Button(buttons[i],go.transform,new Vector2(suffix=="Widgets"?650:0,160-i*75));
                    data.entries.Add(new UIBindingEntry{name=buttons[i],component=button});
                }
                if(suffix=="Confirm")
                {
                    var label=Label("Message",go.transform,"Confirm action",new Vector2(0,-40),new Vector2(600,100));
                    data.entries.Add(new UIBindingEntry{name="Message",component=label});
                }
                if(suffix=="Widgets")
                {
                    var scroll=CreateScroll(go.transform,out var template);
                    var modelObject=new GameObject("Model",typeof(RectTransform),typeof(RawImage),typeof(UIModelView));
                    modelObject.transform.SetParent(go.transform,false);
                    var modelRect=modelObject.GetComponent<RectTransform>();modelRect.sizeDelta=new Vector2(400,400);modelRect.anchoredPosition=new Vector2(300,-60);
                    var modelView=modelObject.GetComponent<UIModelView>();modelView.target=modelObject.GetComponent<RawImage>();modelView.modelPrefab=model;modelView.target.raycastTarget=false;
                    data.entries.Add(new UIBindingEntry{name="List",component=scroll});
                    data.entries.Add(new UIBindingEntry{name="ItemTemplate",component=template});
                    data.entries.Add(new UIBindingEntry{name="Model",component=modelView});
                }
                var transitions=go.GetComponent<UIViewTransitions>();
                transitions.enter=Director(go,"Enter",suffix,0,1);
                transitions.exit=Director(go,"Exit",suffix,1,0);
                data.layoutHash=UIBindingEmitter.ComputeLayoutHash(data);
                var path=Folder+"/Demo"+suffix+".prefab";
                PrefabUtility.SaveAsPrefabAsset(go,path);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                UIBindingEmitter.Generate(saved.GetComponent<UIBindingData>(),logicType.Name,logicType.Namespace,Folder);
                var result=UIPrefabLinter.Check(saved);
                if(result.ErrorCount>0)throw new InvalidOperationException(string.Join("\n",result.Messages));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        private static PlayableDirector Director(GameObject go,string name,string suffix,float start,float end)
        {
            return UIViewCreator.CreateDirector(go,name,Folder+"/Demo"+suffix+name+".playable",start,end);
        }
        private static ScrollRect CreateScroll(Transform parent,out UIBindingData template)
        {
            var go=new GameObject("List",typeof(RectTransform),typeof(Image),typeof(ScrollRect));
            go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(460,540);rect.anchoredPosition=new Vector2(-350,-30);
            go.GetComponent<Image>().color=new Color(0.08f,0.1f,0.17f);go.GetComponent<Image>().raycastTarget=false;
            var viewport=new GameObject("Viewport",typeof(RectTransform),typeof(RectMask2D));viewport.transform.SetParent(go.transform,false);Fill(viewport.GetComponent<RectTransform>());
            var content=new GameObject("Content",typeof(RectTransform));content.transform.SetParent(viewport.transform,false);
            var scroll=go.GetComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=content.GetComponent<RectTransform>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;
            var item=new GameObject("ItemTemplate",typeof(RectTransform),typeof(Image),typeof(UIBindingData));item.transform.SetParent(content.transform,false);
            item.GetComponent<RectTransform>().sizeDelta=new Vector2(460,60);item.GetComponent<Image>().color=new Color(0.12f,0.2f,0.3f);item.GetComponent<Image>().raycastTarget=false;
            Label("Label",item.transform,"Item",Vector2.zero,new Vector2(400,54));
            template=item.GetComponent<UIBindingData>();template.layoutHash=UIBindingEmitter.ComputeLayoutHash(template);item.SetActive(false);return scroll;
        }
        private static GameObject CreateModel()
        {
            var path=Folder+"/Model.prefab";
            var model=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                var materialPath=Folder+"/Model.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,materialPath);}
                material.SetColor("_BaseColor",new Color(0.15f,0.65f,0.95f));EditorUtility.SetDirty(material);
                model.GetComponent<Renderer>().sharedMaterial=material;
                UnityEngine.Object.DestroyImmediate(model.GetComponent<Collider>());
                return PrefabUtility.SaveAsPrefabAsset(model,path);
            }
            finally{UnityEngine.Object.DestroyImmediate(model);}
        }
        private static Button Button(string name,Transform parent,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(260,58);rect.anchoredPosition=position;
            go.GetComponent<Image>().color=new Color(0.18f,0.55f,0.95f);Label("Label",go.transform,name,Vector2.zero,new Vector2(250,56));return go.GetComponent<Button>();
        }
        private static Text Label(string name,Transform parent,string value,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=size;rect.anchoredPosition=position;
            var text=go.GetComponent<Text>();text.text=value;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.fontSize=24;text.raycastTarget=false;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");return text;
        }
        private static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}

        private static void CreateProfile()
        {
            var profile=AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<AssetBuildProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            var names=new[]{"Home","Popup","Overlay","System","Confirm","Widgets"};
            profile.entries=names.Select(name=>new AssetBuildEntry{address="UI/Demo/"+name,bundle="ui_"+name.ToLowerInvariant(),asset=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Demo"+name+".prefab")}).ToArray();
            EditorUtility.SetDirty(profile);
        }
        [MenuItem("GameUI/Stage Demo Bundles")]
        public static void StageBundles(){StageBundlesFor(EditorUserBuildSettings.activeBuildTarget);}
        public static string StageBundlesFor(BuildTarget target)
        {
            var profile=AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
            if(profile==null)throw new InvalidOperationException("Create the complete UI demo first.");
            var platform=BundleBuilder.PlatformName(target);
            var built=BundleBuilder.Build(profile,Path.Combine(BundleRoot,platform,"v"+profile.version),target);
            var destination=Path.Combine(Application.streamingAssetsPath,"GameUIAsset",platform);
            var packaged=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var version=VersionFile.Parse(File.ReadAllText(Path.Combine(built,"version.json")),platform);
            var manifest=AssetManifest.FromJson(File.ReadAllText(Path.Combine(built,"asset_manifest.json")));
            var files=new List<string>{"version.json","asset_manifest.json",Path.Combine("manifests",version.manifestHash+".json")};
            foreach(var bundle in manifest.Bundles) files.Add(Path.Combine("bundles",bundle.Hash+".bundle"));
            foreach(var relative in files)
            {
                var source=Path.Combine(built,relative);
                var output=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(output));File.Copy(source,output,true);
                packaged.Add(Path.GetFullPath(output));
            }
            foreach(var file in Directory.GetFiles(destination,"*",SearchOption.AllDirectories))
            {
                if(file.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)||packaged.Contains(Path.GetFullPath(file)))continue;
                var assetPath="Assets"+Path.GetFullPath(file).Substring(Application.dataPath.Length).Replace('\\','/');
                if(!AssetDatabase.DeleteAsset(assetPath))throw new IOException("Could not remove obsolete packaged UI content: "+assetPath);
            }
            AssetDatabase.Refresh();return built;
        }
        [MenuItem("GameUI/Build Windows Demo Player")]
        public static void BuildPlayer()
        {
            StageBundlesFor(BuildTarget.StandaloneWindows64);
            var result=BuildPipeline.BuildPlayer(new[]{"Assets/GameUI/Demo/GameUIDemo.unity"},"Build/UIPlayer/GameUIDemo.exe",BuildTarget.StandaloneWindows64,BuildOptions.Development);
            Directory.CreateDirectory("Build/Verification");
            File.WriteAllText("Build/Verification/ui-player-build.txt",result.summary.result+"\n"+result.summary.totalTime+"\n"+result.summary.outputPath);
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("UI Player build failed.");
        }
    }
}
