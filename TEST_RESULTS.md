# TEST_RESULTS

Honest record of what was verified and how. Two stages so far: a cloud *static* stage (no Unity) and a cloud
*real-editor* stage (Unity 6000.3.27f1 in a Linux container, GameCI image, Student/Personal licence activated
through Unity's command-line activation and returned afterwards). **Nothing has run on a Meta Quest 2 yet.**

## Stage 3: V2 premium upgrade, real Unity editor in the cloud (2026-10-30, later session)
| Check | Method / evidence | Result |
|---|---|---|
| Compile after V2 (weight/handling transformer, hands, casings, new environment, UI) | Unity 6000.3.27f1 batch rebuild (`RunFullSetupBatch -vrrangeForce`) | **0 errors**, setup 51 ✅ |
| EditMode tests | `Builds/editmode-results.xml` (ballistics, scoring, grades, **WeaponHandlingMath**: grip pose, two-point aim, lag clamps, recoil/recovery/sag/sway relations) | **21 / 21 passed** |
| PlayMode tests on the generated scene | `Builds/playmode-results.xml`: scene wiring; pistol shot hits and scores; accuracy challenge; lane bounds; **all weapons have handling profiles + grip anchors; one-handed rifle sags 1.5–12° and trails a sudden move within the lag clamp, then settles; pistol trails less and sags less than the rifle; two-handed grip aims the support grip at the support hand (< 6° error), support release has no snap; unsupported recoil clearly larger than supported; both hands present, calibrated, skinned mesh enabled, half-curl moves the index tip toward the palm, grip anchors valid for both hands** | **10 / 10 passed** (1 screenshot test skipped headless) |
| Review screenshots (software OpenGL in the container) | `VisualCaptureTests` with xvfb (Mesa llvmpipe, OpenGL 4.5) | **not obtained**: the scene loads but URP rendering under software GL stalled past the 15-minute budget, so no images were produced. Hand/grip alignment and the look of the environment therefore remain visually unverified until opened locally. The test stays in the project and runs in any editor with a GPU (Logs/shots/*.png). |
| Android APK (V2) | `QuestBuildScript.BuildApkBatch` in the same container: IL2CPP, ARM64, Vulkan, OpenXR; full build 10 min 27 s, 0 errors, 4 warnings (TMP IL2CPP size notes, one obsolete-API notice); report in `Builds/VRRangeBuildReport_v2.txt` | **passed** — `VRShootingRange.apk` 0.2.0 (versionCode 2), 59,788,275 bytes (57 MB), sha256 `c97c949ffc7aa005563d5cdfd1bb0499a229a2cf17f27dc608605366ff65e82b` |
| APK inspection (`aapt dump badging`) | package `com.universityproject.vrshootingrange` 0.2.0, minSdk 29, targetSdk 36, native-code `arm64-v8a` only, `android.hardware.vr.headtracking`, OpenXR permissions | **as expected** |

Bugs the real editor caught in this stage (all fixed): hand FBX lost its skinning when the rig type was set to None;
hand FBX is authored in centimetres (file scale must be kept); `HandPresence` scoped its renderer/interactor lookups one
level too high and hid the other hand; the first curl test metric was wrong (a full fist returns the fingertip to the palm plane).

Still unverified on hardware: how the weight simulation, two-handed aiming and hand poses *feel* with real controllers,
hand/grip alignment in VR, frame rate.

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
