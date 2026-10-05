using Cascade.App;
using Cascade.Unity.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>Puts every built skin's style asset into the <c>skins</c> list of the Boot scene's AppBootstrap.</summary>
    public static class SkinWiring
    {
        const string BootScenePath = "Assets/Cascade/Scenes/Boot.unity";

        public static void Assign(SerializedObject bootstrap)
        {
            var list = bootstrap.FindProperty("skins");
            list.arraySize = SkinDefs.All.Length;
            for (int i = 0; i < SkinDefs.All.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<PieceStyle>("Assets/Cascade/Styles/" + SkinDefs.All[i].StyleFile);
        }

        public static void WireBootScene()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != BootScenePath)
            {
                Debug.LogWarning("[Cascade] The open scene has unsaved changes; skins not wired into Boot.unity. Save it and run Cascade/Build Pebble Assets again.");
                return;
            }
            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            var app = Object.FindFirstObjectByType<AppBootstrap>();
            if (app == null) { Debug.LogWarning("[Cascade] Boot scene has no AppBootstrap; skins not wired."); return; }
            var so = new SerializedObject(app);
            Assign(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
