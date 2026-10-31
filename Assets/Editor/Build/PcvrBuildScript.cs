using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Windows x64 PCVR build (menu + batch). Runs on any editor host that has the Windows Build Support (Mono) module.</summary>
    public static class PcvrBuildScript
    {
        public const string DefaultExePath = "Builds/Windows/VRShootingRange.exe";
        public const string ReportPath = "Logs/VRRangeBuildReport_Windows.txt";

        [MenuItem("VR Range/Build/Build Windows PCVR (x64)", priority = 110)]
        public static void BuildWindowsMenu()
        {
            var report = BuildWindows(DefaultExePath, false);
            if (report != null && !Application.isBatchMode)
                EditorUtility.DisplayDialog("Windows PCVR build", report.summary.result == BuildResult.Succeeded ? $"Built:\n{Path.GetFullPath(DefaultExePath)}\n\nStart your OpenXR runtime (Virtual Desktop / SteamVR / Oculus) and run the exe." : $"Build {report.summary.result}: see Console and {ReportPath}", "OK");
        }

        [MenuItem("VR Range/Build/Build Windows PCVR (x64, Development)", priority = 111)]
        public static void BuildWindowsDevMenu() => BuildWindows("Builds/Windows-dev/VRShootingRange.exe", true);

        /// <summary>Batch entry: Unity -batchmode -projectPath . -buildTarget Win64 -executeMethod VRRange.Editor.PcvrBuildScript.BuildWindowsBatch -outPath Builds/Windows/VRShootingRange.exe [-development] -quit</summary>
        public static void BuildWindowsBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            string exe = DefaultExePath; bool dev = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-outPath" && i + 1 < args.Length) exe = args[i + 1];
                if (args[i] == "-development") dev = true;
            }
            if (!RangeSceneBuilder.SceneExists) { Debug.Log("[VRRange] Scene missing: running setup first"); RangeProjectBootstrap.RunFullSetup(false); }
            var report = BuildWindows(exe, dev);
            if (report == null || report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        public static BuildReport BuildWindows(string exePath, bool development)
        {
            if (!RangeSceneBuilder.SceneExists) { Debug.LogError("[VRRange] Cannot build: scene not generated. Run VR Range > Setup > Run Full Setup."); return null; }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[VRRange] Windows Build Support is not installed. In Unity Hub: Installs > (your editor) > Add modules > Windows Build Support (Mono / IL2CPP).");
                return null;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.Log("[VRRange] Switching active build target to Windows x64...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Standalone, BuildTarget.StandaloneWindows64)) { Debug.LogError("[VRRange] Could not switch to Windows x64"); return null; }
            }
            ProjectSettingsSetup.ApplyStandalonePlayerSettings();
            RangeSceneBuilder.AddToBuildSettings();
            var dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { RangePaths.MainScene },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? (BuildOptions.Development | BuildOptions.AllowDebugging) : BuildOptions.None,
            };
            Debug.Log($"[VRRange] Building Windows PCVR player -> {exePath} (development={development})");
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            var text = $"Result: {s.result}\nOutput: {s.outputPath}\nSize: {s.totalSize / (1024 * 1024)} MB\nTime: {s.totalTime}\nErrors: {s.totalErrors}  Warnings: {s.totalWarnings}\nUnity: {Application.unityVersion}\nTarget: StandaloneWindows64 (Mono, D3D11, OpenXR)\nDate: {System.DateTime.Now:s}\n";
            try { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); } catch { }
            if (s.result == BuildResult.Succeeded)
            {
                try { File.WriteAllText(Path.Combine(dir, "README_PCVR.txt"), ReadmeText()); } catch { }
                Debug.Log("[VRRange] " + text);
            }
            else Debug.LogError("[VRRange] " + text);
            return report;
        }

        static string ReadmeText() =>
@"VR Shooting Range - Windows PCVR build (OpenXR)
================================================
Requirements: Windows 10/11 x64, a PC VR OpenXR runtime, a VR headset.

Meta Quest 2 through Virtual Desktop (recommended):
 1. On the PC install Virtual Desktop Streamer (https://www.vrdesktop.net), sign in with your Oculus/Meta username.
 2. On the Quest 2 install Virtual Desktop from the Meta Store, connect to your PC (5 GHz Wi-Fi or USB-C).
 3. In the Streamer window (PC) > Options, set 'OpenXR runtime' to 'VDXR' (Virtual Desktop's runtime).
    SteamVR also works: set the runtime to SteamVR and start SteamVR before the game.
 4. In the headset: Virtual Desktop menu > Streaming > 'Launch game' or start VRShootingRange.exe on the desktop.

Other headsets: make sure your runtime is the active OpenXR runtime (SteamVR settings > OpenXR > Set SteamVR as OpenXR runtime,
or the Oculus / Windows Mixed Reality / Pico runtime's own setting), then run VRShootingRange.exe.

Desktop window: a clean 16:9 spectator view for OBS. Hotkeys: F1 first person, F2 third person, F3 fixed camera, F4 off,
F5 cycle third-person presets, [ / ] field of view, F6 half-rate rendering. Capture the window with OBS 'Game Capture' or 'Window Capture'.
Controls: Grip = grab, Trigger = fire, A/X (trackpad click on Vive) = reload, B/Y (menu on Vive) = fire mode, left stick teleport, right stick snap turn.
Log file: %USERPROFILE%\AppData\LocalLow\UniversityProject\VR Shooting Range\Player.log (contains the detected OpenXR runtime).
";
    }
}
