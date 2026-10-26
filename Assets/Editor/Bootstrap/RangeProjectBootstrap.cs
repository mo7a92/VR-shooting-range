using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>
    /// First-open automation: when the project is opened and the generated scene is missing, runs the full setup
    /// (settings, URP, OpenXR, imports, prefabs, scene). Everything is also available from the VR Range menu and
    /// as batch-mode entry points. Steps are isolated so one failure does not stop the others.
    /// </summary>
    [InitializeOnLoad]
    public static class RangeProjectBootstrap
    {
        const int SetupVersion = 1;
        const string TmpPackagePathUgui = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        const string TmpPackagePathTmp = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        const string SessionKey = "VRRange.BootstrapAttempted";

        [System.Serializable] class State { public int completedVersion; public string lastRun; }

        static RangeProjectBootstrap()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        static void TryAutoRun()
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryAutoRun; return; }
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            var state = ReadState();
            if (state.completedVersion >= SetupVersion && RangeSceneBuilder.SceneExists) return;
            if (EditorUtility.DisplayDialog("VR Shooting Range setup",
                "This project generates its scene, prefabs and settings on first open.\n\nRun the full setup now? (You can also run it later from the menu VR Range > Setup > Run Full Setup.)", "Run setup", "Later"))
                RunFullSetup(false);
        }

        [MenuItem("VR Range/Setup/Run Full Setup", priority = 0)]
        public static void RunFullSetupMenu() => RunFullSetup(false);

        [MenuItem("VR Range/Setup/Rebuild Everything (overwrite prefabs + scene)", priority = 1)]
        public static void RebuildEverything()
        {
            if (EditorUtility.DisplayDialog("Rebuild everything", "This regenerates materials, prefabs, settings values and the ShootingRange scene, overwriting manual edits to them. Continue?", "Rebuild", "Cancel"))
                RunFullSetup(true);
        }

        [MenuItem("VR Range/Setup/Apply Project Settings Only", priority = 20)]
        public static void ApplySettingsOnly()
        {
            SetupLog.Begin("VR Range settings");
            Step("Project settings", () => ProjectSettingsSetup.ApplyAll());
            Step("URP", () => UrpSettingsSetup.Apply());
            Step("OpenXR", () => OpenXRSettingsSetup.Apply());
            Finish(false);
        }

        [MenuItem("VR Range/Setup/Import TMP Essential Resources", priority = 21)]
        public static void ImportTmpEssentials()
        {
            string path = File.Exists(TmpPackagePathUgui) ? TmpPackagePathUgui : (File.Exists(TmpPackagePathTmp) ? TmpPackagePathTmp : null);
            if (path == null) { Debug.LogWarning("[VRRange] TMP Essential Resources package not found in ugui/textmeshpro packages."); return; }
            AssetDatabase.ImportPackage(path, false);
            Debug.Log("[VRRange] Importing TMP Essential Resources...");
        }

        [MenuItem("VR Range/Open Shooting Range Scene", priority = 40)]
        public static void OpenScene()
        {
            if (!RangeSceneBuilder.SceneExists) { EditorUtility.DisplayDialog("Scene missing", "The scene has not been generated yet. Run VR Range > Setup > Run Full Setup.", "OK"); return; }
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(RangePaths.MainScene);
        }

        /// <summary>Batch-mode entry point: Unity.exe -batchmode -projectPath . -executeMethod VRRange.Editor.RangeProjectBootstrap.RunFullSetupBatch -quit</summary>
        public static void RunFullSetupBatch()
        {
            RunFullSetup(HasArg("-vrrangeForce"));
            if (SetupLog.ProblemCount > 0) Debug.LogWarning($"[VRRange] Setup finished with {SetupLog.ProblemCount} warning(s)/error(s); see {RangePaths.ReportFile}");
        }

        public static void RunFullSetup(bool force)
        {
            SetupLog.Begin(force ? "VR Range full rebuild" : "VR Range setup");
            MaterialLibrary.ResetCache(); EditorMeshUtil.ResetCache();
            bool tmpStarted = false;
            Step("Project settings", () => ProjectSettingsSetup.ApplyAll());
            Step("URP", () => UrpSettingsSetup.Apply());
            Step("OpenXR", () => OpenXRSettingsSetup.Apply());
            Step("Art import", () => { ArtImportSetup.Apply(); AssetDatabase.Refresh(); });
            Step("TMP Essentials", () =>
            {
                if (TMPro.TMP_Settings.instance != null) { SetupLog.Ok("TMP Essential Resources present"); return; }
                string path = File.Exists(TmpPackagePathUgui) ? TmpPackagePathUgui : (File.Exists(TmpPackagePathTmp) ? TmpPackagePathTmp : null);
                if (path == null) { SetupLog.Warn("TMP Essential Resources package not found; UI will use a fallback font."); return; }
                if (Application.isBatchMode) { SetupLog.Warn("Batch mode: TMP Essentials cannot be imported non-interactively here; UI uses a fallback font."); return; }
                AssetDatabase.importPackageCompleted += OnTmpImported;
                AssetDatabase.importPackageFailed += OnTmpFailed;
                AssetDatabase.ImportPackage(path, false);
                tmpStarted = true;
                SetupLog.Info("Importing TMP Essential Resources; setup continues when the import completes.");
            });
            if (tmpStarted) { s_PendingForce = force; return; }
            ContinueBuild(force);
        }

        static bool s_PendingForce;
        static void OnTmpImported(string packageName) { Unhook(); EditorApplication.delayCall += () => ContinueBuild(s_PendingForce); }
        static void OnTmpFailed(string packageName, string error) { Unhook(); SetupLog.Warn($"TMP import failed: {error}"); EditorApplication.delayCall += () => ContinueBuild(s_PendingForce); }
        static void Unhook() { AssetDatabase.importPackageCompleted -= OnTmpImported; AssetDatabase.importPackageFailed -= OnTmpFailed; }

        static void ContinueBuild(bool force)
        {
            Step("Settings assets", () => SettingsAssetBuilder.Build(force));
            Step("FX prefabs", () => FxPrefabBuilder.Build());
            Step("Target prefab", () => TargetPrefabBuilder.Build());
            Step("Weapon prefabs", () => WeaponPrefabBuilder.Build());
            Step("Scene", () => RangeSceneBuilder.Build(force));
            Step("Validation", () => ProjectValidator.Run());
            AssetDatabase.SaveAssets();
            WriteState();
            Finish(true);
        }

        static void Finish(bool offerOpen)
        {
            var report = SetupLog.Write();
            if (Application.isBatchMode) { Debug.Log(report); return; }
            string msg = SetupLog.ProblemCount == 0 ? "Setup finished without problems." : $"Setup finished with {SetupLog.ProblemCount} item(s) to check.";
            msg += $"\n\nReport: {RangePaths.ReportFile}";
            if (report.Contains("needs a restart")) msg += "\n\nInput handling was switched to the Input System: please close and reopen the project once.";
            EditorUtility.DisplayDialog("VR Shooting Range", msg, "OK");
        }

        static void Step(string name, System.Action action)
        {
            try { action(); }
            catch (System.Exception e) { SetupLog.Error($"{name} failed: {e.GetType().Name}: {e.Message}"); Debug.LogException(e); }
        }

        static bool HasArg(string arg) { foreach (var a in System.Environment.GetCommandLineArgs()) if (a == arg) return true; return false; }

        static State ReadState()
        {
            try { if (File.Exists(RangePaths.BootstrapState)) return JsonUtility.FromJson<State>(File.ReadAllText(RangePaths.BootstrapState)) ?? new State(); }
            catch { }
            return new State();
        }

        static void WriteState()
        {
            try { File.WriteAllText(RangePaths.BootstrapState, JsonUtility.ToJson(new State { completedVersion = SetupVersion, lastRun = System.DateTime.Now.ToString("s") }, true)); }
            catch (System.Exception e) { Debug.LogWarning($"[VRRange] Could not write bootstrap state: {e.Message}"); }
        }
    }
}
