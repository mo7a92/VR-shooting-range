# TEST_RESULTS

Honest record of what was verified and how. Two stages so far: a cloud *static* stage (no Unity) and a cloud
*real-editor* stage (Unity 6000.3.27f1 in a Linux container, GameCI image, Student/Personal licence activated
through Unity's command-line activation and returned afterwards). Stage 5 adds the first PCVR headset sessions
(Quest 2 via Virtual Desktop); stages 6 and 7 are the headset-feedback rounds (PCVR only); the standalone Quest APK has still not run on a headset.

## Stage 7: headset feedback round 2 — hands on grips, real Unity editor in the cloud (2026-11-04, PCVR only)
Starting point: commit `36d343b` (round 1). The owner's round-1 screenshots showed hands sinking into the weapons and pistol wrists
tilted too far down. Same rules as round 1: Unity 6000.3.27f1, packages unchanged, no scene rebuild / re-bake (prefab + settings
rebuild only), every interaction through the rig's own `NearFarInteractor`s, time-based waits. Nothing in this stage ran on a headset;
the owner's checklist is in `LOCAL_HANDOFF.md` §7 ("Headset round 2").

**Method.** `HandPenetrationTests.HandsRestOnTheWeaponsWithoutSinkingInAndWristsSitLikeAHighGrip` (one PlayMode test, 6 configurations:
pistol / rifle / SMG × shooting hand alone / shooting + support hand). Per configuration: controllers at a fixed pose, 0.5 s free so the
empty hands settle, grab through the right `NearFarInteractor`, support grab through the left one, ≥ 0.9 s (1.6 s with support) plus a
quiet-state wait (weapon and both palms moving < 1 mm per 0.2 s), then both hand meshes are baked (`SkinnedMeshRenderer.BakeMesh`,
8,651 vertices each) and every vertex is tested against the weapon's fitted contact volumes and the other hand's palm box / finger
capsules. Reported per hand: max depth, share of vertices inside, the deepest volume and hand region, each wrapping fingertip's bone
distance to the nearest contact surface (flesh radius 9.5 mm measured on the mesh, so ≈ 10 mm means the pad touches), wrist pitch
(wrist → middle-knuckle line vs the bore, projected on the weapon's side plane), back-of-hand roll (0° = straight outward, + = up),
and the distance between the held wrist and where the empty hand's wrist sits for the same controller pose. Software-rendered
screenshots (xvfb + llvmpipe, 960×640) of every configuration from the round-1 angles plus a support-hand close-up:
`Docs/Screenshots/round2_fixes/round2_{pistol,rifle,smg}_{primary,both}_{shooter,front,palmside,fingerside,top}.png`,
`round2_*_both_support_{shooter,front,palmside,fingerside,top,contact}.png` (48 files, all inspected). Full report: `Docs/TestResults/round2/hand_report_r2.txt` (suite XMLs and the build report next to it).

Pass criteria (from the round-2 brief): max depth ≤ 4 mm and < 2 % of vertices inside; wrapping fingertips within 12 mm (bone) of the
surface (SMG shooting thumb 16 mm, SMG shooting little finger 20 mm — the Micro Uzi grip is 3 cm wide and 9 cm long with the receiver
directly above it; a support hand's little finger may hang free); shooting-hand wrist ≤ 25° below the bore, support ≤ 30° below; held wrist
within 4 cm of the free hand's wrist; rifle / SMG support palm under the handguard.

| Check | Measured (right = shooting hand, left = support hand) | Result |
|---|---|---|
| Compile (6000.3.27f1, packages unchanged) | `Logs/prefabs_r1.log`, prefab + settings rebuild (`RunPrefabsBatch`, now also applies the `HandContact` physics layer) | **0 errors, 0 compiler warnings** |
| Skin thickness measured per hand | median of the baked rest-pose mesh in a 1.4 cm column through the palm centre: palm 22.4 mm, back of hand 16.7 mm from the bone line; finger flesh radius 9.5 mm (middle finger, middle segment); de-penetration cloud 2,693 palm / thenar / knuckle / wrist vertices | measured, not assumed |
| Max skin depth inside a weapon or the other hand | pistol R 2.3 mm (12 / 8,651 = 0.14 %, trigger finger on the trigger-guard volume), pistol L 0.0 mm (0 %); rifle R 0.0 mm (0 %), rifle L 3.8 mm (37 = 0.43 %, thumb-side thenar against the bottom rail of the handguard); SMG R 0.0 mm (0 %), SMG L 0.0 mm (0 %); with the pistol support hand on, the shooting hand's vertices inside the support hand's capsules: 0 | **all ≤ 4 mm, all < 2 %** |
| Fingertips on the surface (bone → nearest contact surface, mm) | pistol R thumb 10.3 / middle 10.0 / ring 7.7 / little 8.5; pistol L middle 8.6 / ring 10.2 / little 19.3 (free, cups below the shooting fingers); rifle R thumb 9.5 / middle 8.6 / ring 10.1 / little 8.8; rifle L middle 7.5 / ring 9.8 / little 15.0 (free); SMG R thumb 13.6 (bound 16) / middle 7.8 / ring 10.2 / little 10.9; SMG L middle 8.9 / ring 9.9 / little 18.5 (free) | **all within bounds** |
| Wrist pitch (wrist → middle knuckle vs bore) | shooting hand −21.7° (pistol, rifle, SMG: the same fist as the empty hand); pistol support −18.4°; rifle / SMG support +25.8° (the under-guard hand points up along the handguard) | **≤ 25° below (shooting), ≤ 30° below (support)** |
| Wrist roll (back of the shooting hand) | 17.9° above straight outward on all three weapons, the thumb along the frame on the palm side, nothing over the slide / receiver; pistol support 9.5° above outward; rifle / SMG support −72.6° (back of the hand facing down and outward under the handguard, fingers up the far side, thumb along the near side). Justification: the OpenXR grip pose is a fist with the back of the hand outward and slightly up; the shooting-hand anchors use exactly that orientation, so the held wrist is the real wrist | reported, justified |
| Wrist placement vs the empty hand (same controller pose) | pistol 2.2 cm; rifle 3.1 cm alone (one-handed sag turns the knuckle line 21°) / 1.7 cm supported; SMG 2.6 cm / 2.5 cm; the palm centroid of a held hand sits 16–21 mm further out along the controller's palm normal than the empty hand's (thenar bulge resting on the grip panel) | **all < 4 cm** |
| Rifle / SMG support palm under the handguard | rifle support palm skin 49 mm below the bottom-rail volume centre, SMG 48 mm below the receiver centre; fingers up one side (fingertips 7.5–9.9 mm from the band / receiver volumes), thumb along the other | **passed** |
| Pistol support hand on the shooting hand | the support palm's bulges rest on the shooting hand's finger capsules (inflated 7 mm; de-penetration push 31.5 mm); the palm centre's skin point stays 46 mm from the nearest shooting-hand capsule, i.e. the cup is loose (see "Known limits") | passes the overlap rule; visually a loose cup |
| Round-1 tests under the new geometry | `PistolSecondHandSupportsAndTakesOverWhenTheShootingHandLetsGo`: palm-centre distance bound widened from 14 to 19 cm (centres are now bone-line points one skin thickness outside the contact, +4.5 cm) and the hand-over check reads the mirrored grip point; `HandsSnapToWeaponGripAnchorsAndTransitionBetweenOneAndTwoHands`: "fingers wrap" now checks the contact solve found the grip and the middle fingertip is within 12 mm (curl alone no longer says "wrapped"); palm-anchor checks use the palm skin point | adapted, documented in the test comments |
| Full PlayMode suite | `Docs/TestResults/round2/playmode-results.xml`: 73 tests = 72 round-1 + `HandPenetrationTests` | **72 / 72 passed** (1 screenshot test skipped headless), 190 s; the SMG two-handed configuration needed the full 3 s quiet-state wait (the two-handed sway never falls under 1 mm / 0.2 s) and then passed with the same numbers as the solo run |
| Full EditMode suite | `Docs/TestResults/round2/editmode-results.xml` | **34 / 34 passed** |
| Windows x64 PCVR build 0.3.2 | `PcvrBuildScript.BuildWindowsBatch` in the container (Windows Mono module mounted), `Docs/TestResults/round2/VRRangeBuildReport_Windows_r2.txt`: 0 errors, 2 warnings (the OpenXR package's project-validation notes, not compiler warnings) | **passed** — `VRShootingRange.exe` 296 MB unpacked, 186 files, no `DoNotShip` folders; zip `VRShootingRange_PCVR_Windows.zip` 143,344,456 bytes (137 MiB), sha256 `1f92644ee1077a9b68ed2b5c37733c8fd3f557801095dbfab91a6cd081a8e3dc`; handed over as a session file, not committed. **Not run on Windows or a headset here** |
| Quest 2 APK | not built (PCVR only, owner's instruction) | skipped |

Rejected on the way (kept out of the code, see `DECISIONS.md`): a "heel lift" that rotated the hand about its knuckle line so the thenar bulge
could back out without moving the knuckles — the bulge sits mid-palm, so the lift hit its 14° cap, moved the wrist 4–5 cm and pitched the
knuckle line to −27° without reducing the push; knuckle spheres + a 3–4 mm cross-hand inflation — the support palm sank 6.5–9 mm into the
shooting hand's index knuckle (the meshes are rounder than their capsules), so the 7 mm inflation stays.

Known limits: the hand mesh is rigid, so the thenar / hypothenar bulges (33–39 mm from the bone line vs 22 mm at the palm centre) hold the
palm centre 15–20 mm off a flat grip panel; a finger that cannot reach the grip at any curl now closes to its nearest approach (rifle little
finger 12.9 → 8.8 mm) instead of hanging at the pose curl. Screenshots are software-rendered without the PCVR post-processing.

## Stage 6: headset feedback round 1 fixes, real Unity editor in the cloud (2026-11-04, PCVR only)
Starting point: the owner's local fixes merged (`local/headset-fixes-2026-11-04`). Every new interaction test grabs through the
rig's own `NearFarInteractor`s and waits on time, not frames. Nothing in this stage ran on a headset; see the on-headset checklist in
`LOCAL_HANDOFF.md` §7 ("Headset round 1 fixes").

| Check | Method / evidence | Result |
|---|---|---|
| Compile (Unity 6000.3.27f1, packages unchanged: XRI 3.3.2, Input System 1.14.2, OpenXR 1.16.1) | prefab/settings-only rebuild (`RunPrefabsBatch`, scene and lightmaps untouched), `Logs/prefabs_r1.log` | **0 errors, 0 compiler warnings** |
| 1. Bullet speed | `Weapon_*.asset`: pistol 360 m/s, rifle 885 m/s / 800 rpm, SMG 350 m/s / 1,200 rpm, gravity ×1; exact constant-gravity integrator; tracer 35 ms. `HeadsetRound1Tests.RealMuzzleVelocitiesHitAOneCentimetreTargetAt25MetresWithoutTunnelling`: 3 shots per weapon into a 1 cm plate at 25 m, every shot must hit; drop ≤ 3 cm | **passed** (container frame steps of up to ~30 m per frame at 885 m/s, no tunnelling); `GravityDropsTheProjectileOverDistance` now asserts the real 3.8 mm drop at 10 m within 3 mm |
| 2. Weight under the real interactor | `RifleHeldByTheRigsNearFarInteractorSagsLagsAndSwaysOneHanded`: the rig's right NearFarInteractor grabs the rifle; `Handling.PrimaryInteractor` is that interactor; sag 15–35° (profile 22°), sway range > 0.3°, a 30 cm jump of the XR Origin leaves the grip trailing the controller (peak 4 cm = clamp) and it settles within 2 s; SMG and pistol sag ordered rifle > SMG > pistol (pistol 0.5–5°), pistol trails less. A `[VRRange][Handling]` line per second while held (`hands=1 via Near-Far Interactor ... sag=22.0deg lag=4.0cm`). Note: moving the controller transform directly is unreliable in a multi-test session (the rig's tracked pose driver writes it back), so the test moves the origin | **passed**; far grabs now use `farAttachMode = Near` |
| 3. Recoil | `RapidRifleFireAccumulatesMuzzleClimbAndTheSupportHandReducesIt`: one shot ≈ 6°, 10 rapid one-handed shots ≈ 30° peak (clamp), recovers to < 1° within 3 s, 10 supported shots ≈ 3° | **passed** (log: "burst of 1: 6.0 deg, burst of 10: 29.9 deg, supported: 3.2 deg") |
| 4. Pistol support hand + hand-over | `PistolSecondHandSupportsAndTakesOverWhenTheShootingHandLetsGo` (rig interactors): second hand adds (HandCount 2, primary unchanged, blend > 0.9, support anchor), support trigger does nothing, shooting trigger fires; releasing the shooting hand keeps the pistol held by the support hand at every frame of the 0.8 s hand-over with peak speed < 2.5 m/s (no snap), new hand fires; `WeaponInteractionTests.SecondHandSupportsThePistolAndTakesOverWhenTheFirstLetsGo` (direct interactors) | **passed** |
| 5. Hands on grips | `FingersWrapEveryGripWithoutPenetratingItAndTheThumbOpposesTheFingers`: both hands on each weapon via the rig interactors; palm centre outside the grip box (10–16 mm), fingertips (thumb, middle, ring, little) ≤ 5 mm inside the grip collider (measured: 0–4 mm outside, one support little finger 0.7 mm inside), ≥ 2 fingers in contact per hand, shooting-hand thumb on the palm side with the fingers on the other side, support thumb pointing forward, rifle support palm below the handguard. Screenshots (software GL): `Docs/Screenshots/round1_fixes/round1_{pistol,rifle,smg}_{primary,support}_{palmside,fingerside,front,shooter}.png`, inspected | **passed**; the hands were re-posed 5 times from the screenshots (fingers pointing forward along the grip, thumb opposition solve) before this |
| Full PlayMode suite | `Builds/playmode-results.xml`: 72 tests = 67 existing (4 adapted to the new ballistics/support semantics) + 5 round-1 tests | **71 / 71 passed** (1 screenshot test skipped headless), 166 s |
| Full EditMode suite | `Builds/editmode-results.xml` (ballistics with real numbers, prefab integrity incl. grip colliders and Multiple select, build configuration) | **34 / 34 passed** |
| Windows x64 PCVR build 0.3.1 | `PcvrBuildScript.BuildWindowsBatch` in the container (Windows Mono module mounted), `Builds/VRRangeBuildReport_Windows_r1.txt`: 0 errors, 2 warnings (OpenXR project-validation notes logged by the package, not compiler warnings) | **passed** — `VRShootingRange.exe` 296 MB unpacked, 205 files, no `DoNotShip` folders; zip `VRShootingRange_PCVR_Windows.zip` 143,331,173 bytes (137 MiB), sha256 `f2a276815c0e27fc63cd0d260890ce15404dcf640c1e961d9e3f7c7f99904fea`; handed over as a session file, not committed. **Not run on Windows or a headset here** |
| Quest 2 APK | not built this round (owner's instruction: PCVR only; the Quest configuration is unchanged and still validated by `BuildConfigurationTests`) | skipped |

Bugs found by the suite in this stage (all fixed): the test aim helper ignored the muzzle's vertical offset (hidden by the old slow
rounds); semi-implicit Euler over-estimated the drop by the frame time squared (now exact); a prefab integrity check rejected the
ammo counter's SDF font material; the first finger-contact version treated a knuckle lying on the grip surface as penetration.

## Stage 5: local Windows PC + Quest 2 via Virtual Desktop (VDXR), first headset sessions (2026-11-04)
| Check | Method / evidence | Result |
|---|---|---|
| Local editor | Unity 6000.3.27f1 (Windows Mono module built in) installed from Unity's signed installer; project opened without package changes (XRI 3.3.2, Input System 1.14.2, OpenXR 1.16.1) | **ok** |
| PCVR runs on the headset | `Player.log`: `XR device: OpenXR Display`, `OpenXR runtime: VirtualDesktopXR 1.1.0`, system "Oculus Quest2", Oculus Touch profile bound | **ok** (user session) |
| Note: Unity 6000.6.7f1 | An earlier test build with 6000.6 force-upgraded XRI to 3.6.1, Input System 1.20.1, OpenXR 1.18.0 and broke `objectReferenceInstanceIDValue` in `ProjectIntegrityTests`; discarded, do not use 6000.6 for this project | discarded |
| **Bug: weapons could not be grabbed** (headset) | XRI Starter Assets NearFar casters query only Default (near) / Default+UI (far); weapons are on the `Weapon` layer. Earlier suites grabbed through scripted `XRDirectInteractor`s and missed it. Fix: `PlayerRigSetup` adds the Weapon layer to both casters at start. New `RigGrabAndThumbTests` use the rig's real casters | **fixed in code + tests pass; re-check on headset** |
| **Bug: thumb folded down like a finger** (headset) | Thumb curl axis aimed at the palm normal; finger-sized limits (165°) folded it into the palm; spread sign mirrored on one hand. Fix in `HandPresence`: thumb bends toward the middle finger, own limits (15/45/55°), empirical bend + spread sign checks. Test: fist thumb tip (across, inward, forward) = (-11, +11, +29) mm, identical for L and R | **fixed in code + tests pass; re-check on headset** |
| Player too short (headset) | OpenXR 1.16 uses STAGE floor. Added floor calibration (both thumbsticks 1 s with a controller on the floor; PageUp/PageDown/Home) and an eye-height log line. Next session logged 1.51 m | **added; calibration not yet exercised on headset** |
| Test suites on the local PC (RTX 4070 SUPER) | EditMode 34/34; PlayMode 67/67 after making `TargetMotionTypesBehaveAsSpecified` sample 1.5 s instead of 60 frames (frame-count sampling was too short at uncapped local frame rates) | **passed** |
| Windows PCVR build | `Tools/Windows/build_pcvr.ps1`, `Logs/VRRangeBuildReport_Windows.txt` | **Succeeded, 0 errors** |

## Stage 4: V3 PCVR upgrade, visual overhaul and QA suite, real Unity editor in the cloud (2026-10-30/31)
| Check | Method / evidence | Result |
|---|---|---|
| Compile after V3 (PCVR settings, spectator camera, graphics presets, prop library, model-based weapons, new tests) | Unity 6000.3.27f1 batch rebuild (`RunFullSetupBatch -vrrangeForce`), `Logs/VRRangeSetupReport.md` | **0 errors**, setup **0 warnings** (51 CC0 props placed, 26 light fixtures, Rajdhani font asset, Windows Build Support detected) |
| EditMode tests | `Builds/editmode-results.xml`: ballistics, scoring, grades, handling maths, **prefab integrity** (components, references, missing scripts, materials/textures), **weapon definitions**, **settings assets**, **scene wiring and placement**, **build configuration** (OpenXR loaders/profiles per platform, quality levels, URP presets, player settings, bindings) | **34 / 34 passed** |
| PlayMode tests on the generated scene (scripted XRI interactors = mocked controllers, real game logic) | `Builds/playmode-results.xml`: weapon interaction (pickup/release/reacquire, hand swap, semi vs auto, fire rate, dry fire, reload transitions, fire-mode toggle, ammo events, socket return, 12 grab cycles), projectiles (pool reuse, lifetime, gravity drop, impact FX, ring scoring, self-hit exclusion), targets (every motion type, difficulty scaling, presets), modes and UI (countdown, timer, completion, restart, personal best, grades, buttons, HUD text, readability, scoreboards), null handling (9 cases), spectator camera, platform graphics switch, hands (follow, grip/trigger animation, grip anchors, one↔two-hand transition), **full gameplay sequence** (launch → mode → pistol → reload → rifle two-handed → moving targets → SMG → finish → results → restart → cleanup), **stress** (6 s sustained auto fire, 30 weapon switches, 20 restarts: bounded pools, heap delta < 1 MB), scene budgets | **63 / 63 passed** (1 screenshot test skipped headless) |
| Bugs found by the suite and fixed | shell-casing component missing from its prefab (class/file name mismatch → 4,675 "missing script" errors per 2-min session, no bounce sound); accuracy challenge re-entering the countdown after the shot limit (second GO, timer resumed); weapons re-posed by their sockets at start; stale two-handed state after dropping a weapon | **fixed, covered by tests** |
| Lightmap bake in the container | `QuestBuildScript.BakeLighting` under software GL: Progressive CPU, 16 texels/m, 2048 atlases, 256 indirect samples, AO, 41 baked lights, 476 probes, 3 reflection probes | **passed** — 4 lightmaps (light + directional), 3 reflection probes, `LightingData.asset` committed in `Assets/Scenes/ShootingRange/`; LightBaker stage 5.9 min, 21 min wall time including the software-GL meta pass (`Logs/bake.log`) |
| Review screenshots (software OpenGL in the container, 1280×720, after the bake) | `VisualCaptureTests` → `Docs/Screenshots/final_v7/*.png` (before: `Docs/Screenshots/before_v3`): spawn, counter, staff area, officer station, booth, menu panel, waiting area ×3, gun rack, lanes from above, targets, wide view, downrange, pistol in hand ×2, rifle two-handed ×3, SMG one/two-handed ×3, free hand | **inspected across 5 rounds**; issues found and fixed between rounds (duct/sign/beam clashes, props 100× too small or lying on their back, hand too low on grips, SMG support hand overlap, shelves hiding the welcome sign, unusable locker model); final set shows no floating/clipping/missing-texture problems at screenshot resolution |
| Android APK (V3) | `QuestBuildScript.BuildApkBatch` in the container: IL2CPP, ARM64, Vulkan, OpenXR, ASTC; full build 7 min 44 s; build report `Builds/VRRangeBuildReport_v3.txt`: 0 errors, 4 warnings (3 TextMeshPro IL2CPP "large method" notes + the container's low-disk-space notice); the compile log also showed 2 obsolete-API notices (`LightingSettings.autoGenerate`, `PlayerSettings.captureSingleScreen`) in our editor scripts, removed afterwards | **passed** — `VRShootingRange.apk` 0.3.0 (versionCode 3), 119,181,825 bytes (114 MiB; +59 MB vs V2 from the model textures, props and lightmaps), sha256 `bba24fee8ee5cdec4faadd711446384057d137560ab45784fe120fb30039d46b`; `aapt dump badging`: package `com.universityproject.vrshootingrange`, `arm64-v8a` only |
| Windows x64 PCVR player (V3) | `PcvrBuildScript.BuildWindowsBatch` with the Windows Mono module (unpacked from Unity's official 6000.3.27f1 installer) mounted into the GameCI container; Mono, D3D11, OpenXR; player build 45 s after the shared asset/shader stage; report `Builds/VRRangeBuildReport_Windows_v3.txt`: 0 errors, 2 warnings (the same 2 obsolete-API notices, removed afterwards) | **passed** — `Builds/Windows/VRShootingRange.exe` 296 MB unpacked (205 files, `README_PCVR.txt` included, `*_DoNotShip` folders excluded); zip `VRShootingRange_PCVR_Windows.zip` 143,318,583 bytes (137 MiB), sha256 `aa2a754f0a4bb5d21d8c4bf3fd826fe66035c5b6d59732e731df4e9eb010e393`. **Not run on Windows** — only produced here; see `LOCAL_HANDOFF.md` §5b |
| Performance numbers measured here | editor/CPU in a 4-core container: sustained auto fire avg frame 0.8 ms (max spikes ~0.9 s from shader/asset warm-up), managed heap delta after 6 s of fire: < 1 MB; scene 620 renderers, 73 materials, ~203k triangles unculled, 15 realtime/mixed lights | **not a headset measurement** |

Still unverified on hardware: everything that needs a headset (see `FINAL_QA_REPORT.md`).

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
