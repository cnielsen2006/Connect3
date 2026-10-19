using System.IO;
using Cascade.App;
using Cascade.Unity.Platform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cascade.Unity.View;

namespace Cascade.Editor
{
    /// <summary>
    /// Creates the generated assets (URP asset, panel settings, Boot scene) if missing.
    /// Idempotent; BuildScript calls it first so a fresh checkout builds the same everywhere.
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/Cascade";
        public const string BootScenePath = Root + "/Scenes/Boot.unity";
        const string PanelSettingsPath = Root + "/UI/PanelSettings.asset";
        const string UrpAssetPath = Root + "/Settings/Cascade_URP.asset";
        const string UrpRendererPath = Root + "/Settings/Cascade_URP_Renderer.asset";

        [MenuItem("Cascade/Apply Project Setup")]
        public static void Apply()
        {
            Directory.CreateDirectory(Root + "/Settings");
            DataCatalogBuilder.Refresh();
            EnsureUrp();
            EnsurePanelSettings();
            EnsureBootScene();
            IconSetup.Apply();
            IconSetup.ApplySplash();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        static void EnsureUrp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
        }

        static void EnsurePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            bool create = panel == null;
            if (create) panel = ScriptableObject.CreateInstance<PanelSettings>();
            // The UI is designed in panel units: UiRoot sets "match" per layout so a wide screen is 1080 units tall and a tall one 1080 wide.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;
            if (create) AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            else EditorUtility.SetDirty(panel);
        }

        static void EnsureBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene unloads unreferenced assets, so load what the scene references only now.
            var catalog = AssetDatabase.LoadAssetAtPath<DataCatalog>(DataCatalogBuilder.CatalogPath);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var style = AssetDatabase.LoadAssetAtPath<PieceStyle>(PebbleMeshBuilder.StylePath);

            var cameraGo = new GameObject("Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(58, 46, 38, 255);

            var app = new GameObject("App");
            var doc = app.AddComponent<UIDocument>();
            var docSo = new SerializedObject(doc);
            docSo.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            docSo.FindProperty("sourceAsset").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/UI/Shell.uxml");
            docSo.ApplyModifiedPropertiesWithoutUndo();
            var bootstrap = app.AddComponent<AppBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("style").objectReferenceValue = style;
            so.FindProperty("boardCamera").objectReferenceValue = camera;
            SkinWiring.Assign(so);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, BootScenePath);
        }
    }
}
