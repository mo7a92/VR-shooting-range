using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRRange.Hands;

namespace VRRange.Editor
{
    /// <summary>
    /// 0.4.1 changes applied to the existing scene without a rebuild (lightmaps stay valid): menus that never hide, gloved
    /// hands, the jazz radio and the environment touches. Every step is idempotent; the scene builder does the same for a
    /// fresh scene.
    /// </summary>
    public static class ScenePatches
    {
        [MenuItem("VR Range/Setup/Apply 0.4.1 Scene Patches (open scene)", priority = 41)]
        public static void MenuApply()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            ApplyAll();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Unity.exe -batchmode -executeMethod VRRange.Editor.ScenePatches.RunBatch -quit</summary>
        public static void RunBatch()
        {
            SetupLog.Begin("0.4.1 scene patches (no rebuild)");
            ArtImportSetup.Apply(); AssetDatabase.Refresh(); // new music (streamed) and boombox textures (normal map) get their import settings
            var scene = EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            ApplyAll();
            EditorSceneManager.SaveScene(scene);
            SetupLog.Ok($"Scene saved: {RangePaths.MainScene}");
        }

        public static void ApplyAll()
        {
            var root = GameObject.Find("--- Range ---");
            RadioBuilder.Build(root != null ? root.transform : null);
            ShelfStockBuilder.Build(root != null ? root.transform : null);
            UiBuilder.PatchMenusInOpenScene();
            int gloves = 0;
            foreach (var hp in Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (GloveBuilder.ApplyTo(hp.gameObject, hp.IsLeft)) gloves++;
            SetupLog.Ok($"Gloves on {gloves} hands");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
