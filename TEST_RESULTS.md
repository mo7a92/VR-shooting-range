# TEST_RESULTS

Honest record of what was verified and how. Two stages so far: a cloud *static* stage (no Unity) and a cloud
*real-editor* stage (Unity 6000.3.27f1 in a Linux container, GameCI image, Student/Personal licence activated
through Unity's command-line activation and returned afterwards). **Nothing has run on a Meta Quest 2 yet.**

## Stage 2: real Unity editor in the cloud (2026-10-30)
| Check | Method / evidence | Result |
|---|---|---|
| Licence activation | `Unity -batchmode -username -password -serial` inside `unityci/editor:ubuntu-6000.3.27f1-android-3.2.2`; log line "Successfully activated ULF license"; seat returned at the end with `-returnlicense` | **passed** |
| Package resolution + import | Real `Packages/manifest.json` against packages.unity.com; URP 17.3.0 bundled with the editor; `Packages/packages-lock.json` committed | **passed** |
| Script compilation (all assemblies) | First run: 3 errors in `UrpSettingsSetup.cs` (read-only URP properties); fixed. Second run: 0 errors, 0 warnings from project code | **passed** |
| First-open setup (`RunFullSetupBatch`) | 47 ✅ in `Logs/VRRangeSetupReport.md` (copy in `Builds/VRRangeSetupReport.md`): player settings, layers, XRI interaction layer, Input System, XR Plug-in Management with OpenXR loader (Android + Standalone), URP asset, OpenXR Meta Quest feature + Oculus Touch profile, import settings, materials, FX/target/weapon prefabs, environment, UI, scene, lighting settings | **passed** (one bug fixed on the way: `EditorRefs.Ensure<T>` null check) |
| EditMode tests (Unity Test Runner) | `-runTests -testPlatform EditMode`, `Builds/editmode-results.xml` | **16 / 16 passed** |
| PlayMode tests on the generated scene | `-runTests -testPlatform PlayMode`, `Builds/playmode-results.xml`: scene wiring (managers, 5 lanes, 3 loaded weapons); **a pistol shot flies, hits the lane target and scores**; accuracy challenge countdown + shot limit + finish; random-motion targets stay inside lane bounds (hard difficulty) | **4 / 4 passed** (one bug fixed on the way: coroutine started on a deactivating weapon during scene unload) |
| Android APK build | `QuestBuildScript.BuildApkBatch`: IL2CPP, ARM64, Vulkan; Gradle initially failed on Maven Central HTTP 429, fixed by `Assets/Plugins/Android/settingsTemplate.gradle` (Google Maven + Central mirror first); `Builds/VRRangeBuildReport.txt` | **passed** — `VRShootingRange.apk`, 46 MB, sha256 `4a577df21e31e256334ffc0c9d6e195a6ab9fd2450896d80572256a22959b226` |
| APK inspection (`aapt dump badging`) | package `com.universityproject.vrshootingrange` 0.1.0, minSdk 29, targetSdk 36, native-code `arm64-v8a` only, `libUnityOpenXR.so` + `libopenxr_loader.so` present, manifest has `com.oculus.intent.category.VR`, `com.oculus.supportedDevices`, `android.hardware.vr.headtracking`, OpenXR permissions | **as expected** |

Limitations of stage 2: XR could not initialise in the container (no device; `XRSettings.enabled == false` is
logged, as designed), so controller input, grabbing with real hands, haptics, UI ray interaction, comfort and
frame rate were **not** exercised. TMP Essential Resources were not importable in batch mode, so the generated
UI uses a generated fallback font asset; the essentials are now committed under `Assets/TextMesh Pro`, and a
local **VR Range → Setup → Rebuild Everything** switches the UI to the standard TMP font.

## Stage 1: static validation without Unity (2026-10-26)
| Check | Method | Result |
|---|---|---|
| `VRRange.Runtime` compiles (editor + Android player defines) | Roslyn 4.12 against reference assemblies built from UnityCsReference 6000.3.27f1 + XRI 3.3.2, OpenXR 1.16.1, Input System 1.14.2, Core Utils 2.5.3, XR Management 4.5.4, ugui/TMP | 0 errors |
| `VRRange.Editor`, `VRRange.Tests.EditMode` compile | same + generated `UnityEditor` reference, NUnit 3.14 | 0 errors (2 URP/OpenXR files excluded; one of them had the 3 errors found in stage 2) |
| Generated meshes / textures / audio | rendered previews, file inspection | OK |

## Not verified (pending on-device stage)
| Item | Status |
|---|---|
| Installation and launch on Meta Quest 2 (`Tools/Windows/install_apk.ps1`) | **pending** |
| Controller bindings, grab, trigger, reload (A/X), fire mode (B/Y), two-handed grip feel, weapon orientation in hand | **pending** |
| UI ray interaction on panels, teleport, snap turn, vignette | **pending** |
| Frame rate on Quest 2 (target 72 FPS) | **unknown until measured on device** |
| Lightmap bake (not run; mixed lights keep the scene lit) | **pending** |

## How to update this file
After each local milestone add a row with the date, Unity version, build number and outcome
(`Logs/VRRangeSetupReport.md`, `Logs/VRRangeBuildReport.txt`, `Logs/quest_logcat.txt` excerpts).
