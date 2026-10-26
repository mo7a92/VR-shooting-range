# Windows helper scripts

Run from PowerShell (right-click > *Run with PowerShell*, or `powershell -ExecutionPolicy Bypass -File <script>`).

| Script | What it does |
|---|---|
| `run_setup.ps1 [-Force]` | Runs the Unity batch-mode setup (same as menu **VR Range > Setup > Run Full Setup**) and prints `Logs/VRRangeSetupReport.md`. |
| `build_apk.ps1 [-Development] [-BakeLighting]` | Builds `Builds/VRShootingRange.apk` in batch mode (same as **VR Range > Build > Build Quest 2 APK**). |
| `install_apk.ps1` | `adb install -r -g` the APK on the connected Quest 2 and launches it. |
| `logcat.ps1 [-All]` | Streams headset logs to `Logs/quest_logcat.txt`. |

The scripts locate `Unity.exe` from `ProjectSettings/ProjectVersion.txt` under `C:\Program Files\Unity\Hub\Editor\<version>` and `adb.exe` from the editor's Android SDK. Override with the environment variables `UNITY_EDITOR_EXE` and `ANDROID_ADB` if they live elsewhere.
