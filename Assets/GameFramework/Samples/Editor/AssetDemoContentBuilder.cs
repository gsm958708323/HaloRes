using System;
using System.IO;
using System.Collections.Generic;
using GameFramework.Asset.Editor.Build;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Asset.Samples.Editor
{
    public static class AssetDemoContentBuilder
    {
        public const string Root = "Assets/GameFramework/Samples/Content";
        public const string ProfilePath = Root + "/DemoBuildProfile.asset";
        public const string ManifestPath = Root + "/EditorManifest.json";
        public const string LauncherPath = "Assets/Scenes/AssetLifecycleLab.unity";
        public const string BattlePath = Root + "/Battle_01.unity";

        [MenuItem("Game Framework/Assets/Create Demo Content")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Create sample assets outside Play Mode.");
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var originalScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var common = Material("Common", new Color(0.15f, 0.68f, 0.56f));
                var hero = Material("Hero", new Color(0.93f, 0.62f, 0.20f));
                var city = Prefab("MainCityPanel", PrimitiveType.Cube, common, new Vector3(2.2f, 1.5f, 0.4f));
                var panel = Prefab("BattlePanel", PrimitiveType.Cube, common, new Vector3(2.2f, 1.5f, 0.4f));
                var hud = Prefab("Hero001_Hud", PrimitiveType.Capsule, hero, new Vector3(0.6f, 0.6f, 0.6f));
                var battleScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var combatant = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                combatant.name = "Hero001";
                combatant.transform.position = new Vector3(2.5f, -2.0f, 0);
                combatant.GetComponent<Renderer>().sharedMaterial = hero;
                EditorSceneManager.SaveScene(battleScene, BattlePath);
                var profile = AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
                if (profile == null) { profile = ScriptableObject.CreateInstance<AssetBuildProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
                profile.version = 1;
                profile.entries = new[] {
                    Entry("UI/MainCityPanel", "ui_city", city), Entry("UI/BattlePanel", "ui_battle", panel),
                    Entry("Hero/Hero001_Hud", "hero_hud", hud), Entry("Shared/CommonMaterial", "ui_common", common),
                    Entry("Hero/Material", "hero_001", hero), Entry("Scene/Battle_01", "scene_battle01", AssetDatabase.LoadAssetAtPath<SceneAsset>(BattlePath))
                };
                EditorUtility.SetDirty(profile);
                File.WriteAllText(ManifestPath, ManifestGenerator.EditorManifest(profile), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(ManifestPath);
                var launcherScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0, 0, -12);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.10f, 0.11f);
                var launcher = new GameObject("Asset Lifecycle Lab").AddComponent<AssetDemoLauncher>();
                var serialized = new SerializedObject(launcher);
                serialized.FindProperty("mode").enumValueIndex = (int)AssetSystemMode.Bundle;
                serialized.FindProperty("editorManifest").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(launcherScene, LauncherPath);
                var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(LauncherPath, true) };
                foreach (var scene in EditorBuildSettings.scenes)
                    if (scene.path != LauncherPath && scene.path != BattlePath) scenes.Add(scene);
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(originalScenes); }
            Debug.Log("Asset lifecycle demo content created.");
        }

        [MenuItem("Game Framework/Assets/Build Demo Bundles")]
        public static void BuildDemo()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Run Create Demo Content first.");
            var target = EditorUserBuildSettings.activeBuildTarget;
            BundleBuilder.Build(profile, Path.Combine("Build", "AssetContent", BundleBuilder.PlatformName(target), "v" + profile.version), target);
        }

        [MenuItem("Game Framework/Assets/Stage Demo For Player")]
        public static void StageForPlayer()
        {
            StageForTarget(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void StageForTarget(BuildTarget target)
        {
            var profile = AssetDatabase.LoadAssetAtPath<AssetBuildProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Run Create Demo Content first.");
            var platform = BundleBuilder.PlatformName(target);
            var source = BundleBuilder.Build(profile, Path.Combine("Build", "AssetContent", platform, "v" + profile.version), target);
            var destination = Path.Combine(Application.streamingAssetsPath, "GameFrameworkAsset", platform);
            Directory.CreateDirectory(destination);
            CopyDirectory(Path.Combine(source, "bundles"), Path.Combine(destination, "bundles"));
            CopyDirectory(Path.Combine(source, "manifests"), Path.Combine(destination, "manifests"));
            File.Copy(Path.Combine(source, "version.json"), Path.Combine(destination, "version.json"), true);
            AssetDatabase.Refresh();
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        private static AssetBuildEntry Entry(string address, string bundle, UnityEngine.Object asset) =>
            new AssetBuildEntry { address = address, bundle = bundle, asset = asset };

        private static Material Material(string name, Color color)
        {
            var path = Root + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP Unlit shader was not found.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Prefab(string name, PrimitiveType primitive, Material material, Vector3 scale)
        {
            var instance = GameObject.CreatePrimitive(primitive);
            try
            {
                instance.name = name;
                instance.transform.localScale = scale;
                instance.GetComponent<Renderer>().sharedMaterial = material;
                UnityEngine.Object.DestroyImmediate(instance.GetComponent<Collider>());
                return PrefabUtility.SaveAsPrefabAsset(instance, Root + "/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
