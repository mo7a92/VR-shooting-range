# TEST_RESULTS

Honest record of what was verified and how. **Nothing in this project has yet been run inside the Unity
Editor, built to an APK, or executed on a Meta Quest 2.** Those steps are pending the local stage.

## Verified in the cloud (2026-10-26)
| Check | Method | Result |
|---|---|---|
| `VRRange.Runtime` compiles (editor mode and Android player mode) | Roslyn 4.12 against reference assemblies built from UnityCsReference 6000.3.27f1 + XRI 3.3.2, OpenXR 1.16.1, Input System 1.14.2, Core Utils 2.5.3, XR Management 4.5.4, ugui/TMP sources | **0 errors** (23 files) |
| `VRRange.Editor` compiles | same, with a declaration-only `UnityEditor` reference generated from Unity's editor sources | **0 errors** (16 of 18 files; `UrpSettingsSetup.cs` and `OpenXRSettingsSetup.cs` are excluded because URP / OpenXR-editor assemblies are not in the reference build; their API calls were checked against package source by inspection) |
| `VRRange.Tests.EditMode` compiles | same + NUnit 3.14 | **0 errors** |
| XRI Starter Assets sample scripts compile against the same references | same | 0 errors |
| Obsolete-API scan on project code | compile with warnings enabled (CS0618/0619/0612) | see `Tools/CloudCompileCheck` output in the session log; remaining warnings, if any, are non-blocking |
| Generated weapon meshes | rendered with `Tools/AssetGen/preview_weapons.py` and visually inspected | recognisable pistol / rifle / SMG silhouettes, correct part pivots |
| Generated textures / signs / audio | files produced and inspected (textures visually; audio by waveform statistics only) | 30 textures, 19 signs/FX, 16 audio clips |
| Python asset generators | executed in the cloud | succeed, deterministic (seeded) |

## Not verified (pending local stage)
| Item | Status |
|---|---|
| Unity import of the project, package resolution, script compilation in the real editor | **pending** |
| First-open setup (settings, URP asset, OpenXR features, TMP import, prefab + scene generation) | **pending** |
| EditMode tests executed in the Unity Test Runner | **pending** (they compile; not executed) |
| Play-mode behaviour: grabbing, firing, reload, two-handed grip, scoring, moving targets, modes, UI rays | **pending** |
| Lightmap bake | **pending** |
| Android APK build (IL2CPP, ARM64, Vulkan) | **pending** — no APK exists yet |
| Installation and run on Meta Quest 2 | **pending** |
| Frame rate on Quest 2 (target 72 FPS) | **unknown until measured on device** |
| Comfort (teleport, snap turn, vignette), controller bindings on device | **pending** |

## How to update this file
After each local milestone, add a row with the date, Unity version, build number and the outcome
(`Logs/VRRangeSetupReport.md`, `Logs/VRRangeBuildReport.txt`, `Logs/quest_logcat.txt` excerpts).
