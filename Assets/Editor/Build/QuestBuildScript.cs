using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Android APK build for Meta Quest 2 (menu + batch mode) and the optional lightmap bake.</summary>
    public static class QuestBuildScript
    {
        public const string DefaultApkPath = "Builds/VRShootingRange.apk";

        [MenuItem("VR Range/Build/Build Quest 2 APK", priority = 100)]
        public static void BuildApkMenu()
        {
            var report = BuildApk(DefaultApkPath, false);
            if (report != null && !Application.isBatchMode)
                EditorUtility.DisplayDialog("Quest 2 build", report.summary.result == BuildResult.Succeeded ? $"APK built:\n{Path.GetFullPath(DefaultApkPath)}\n\nInstall with Tools/Windows/install_apk.ps1" : $"Build {report.summary.result}: see Console and Logs/VRRangeBuildReport.txt", "OK");
        }

        [MenuItem("VR Range/Build/Build Quest 2 APK (Development)", priority = 101)]
        public static void BuildApkDevMenu() => BuildApk("Builds/VRShootingRange-dev.apk", true);

        [MenuItem("VR Range/Build/Bake Lighting (can take minutes)", priority = 120)]
        public static void BakeLighting()
        {
            if (!RangeSceneBuilder.SceneExists) { Debug.LogWarning("[VRRange] Scene missing; run setup first."); return; }
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != RangePaths.MainScene)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(RangePaths.MainScene);
            Debug.Log("[VRRange] Baking lightmaps...");
            bool ok = Lightmapping.Bake();
            Debug.Log(ok ? "[VRRange] Lightmap bake finished" : "[VRRange] Lightmap bake failed or was cancelled");
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }

        /// <summary>Batch entry: Unity.exe -batchmode -projectPath . -executeMethod VRRange.Editor.QuestBuildScript.BuildApkBatch -apkPath Builds/x.apk [-development] [-bakeLighting] -quit</summary>
        public static void BuildApkBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            string apk = DefaultApkPath; bool dev = false, bake = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-apkPath" && i + 1 < args.Length) apk = args[i + 1];
                if (args[i] == "-development") dev = true;
                if (args[i] == "-bakeLighting") bake = true;
            }
            if (!RangeSceneBuilder.SceneExists) { Debug.Log("[VRRange] Scene missing: running setup first"); RangeProjectBootstrap.RunFullSetup(false); }
            if (bake) BakeLighting();
            var report = BuildApk(apk, dev);
            if (report == null || report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        public static BuildReport BuildApk(string apkPath, bool development)
        {
            if (!RangeSceneBuilder.SceneExists) { Debug.LogError("[VRRange] Cannot build: scene not generated. Run VR Range > Setup > Run Full Setup."); return null; }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[VRRange] Android Build Support is not installed. In Unity Hub: Installs > (your editor) > Add modules > Android Build Support + OpenJDK + Android SDK & NDK Tools.");
                return null;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[VRRange] Switching active build target to Android...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android)) { Debug.LogError("[VRRange] Could not switch to Android"); return null; }
            }
            ProjectSettingsSetup.ApplyPlayerSettings();
            RangeSceneBuilder.AddToBuildSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(apkPath)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { RangePaths.MainScene },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? (BuildOptions.Development | BuildOptions.AllowDebugging) : BuildOptions.None,
            };
            EditorUserBuildSettings.buildAppBundle = false;
            Debug.Log($"[VRRange] Building APK -> {apkPath} (development={development})");
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            var text = $"Result: {s.result}\nOutput: {s.outputPath}\nSize: {s.totalSize / (1024 * 1024)} MB\nTime: {s.totalTime}\nErrors: {s.totalErrors}  Warnings: {s.totalWarnings}\nUnity: {Application.unityVersion}\nDate: {System.DateTime.Now:s}\n";
            try { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/VRRangeBuildReport.txt", text); } catch { }
            if (s.result == BuildResult.Succeeded) Debug.Log("[VRRange] " + text); else Debug.LogError("[VRRange] " + text);
            return report;
        }
    }
}
