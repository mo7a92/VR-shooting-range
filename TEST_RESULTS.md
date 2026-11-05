# TEST_RESULTS

Honest record of what was verified and how. Two stages so far: a cloud *static* stage (no Unity) and a cloud
*real-editor* stage (Unity 6000.3.27f1 in a Linux container, GameCI image, Student/Personal licence activated
through Unity's command-line activation and returned afterwards). Stage 5 adds the first PCVR headset sessions
(Quest 2 via Virtual Desktop); stages 6, 7 and 8 are the headset-feedback rounds (PCVR only); the standalone Quest APK has still not run on a headset.

## Stage 8: round 3 — hands actually hold the guns, real Unity editor on the owner's PC (2026-11-05, PCVR only) — **ACCEPTED by the test, headset check pending**
Starting point: `e177dea` + the round-2c patch (`Docs/TestResults/round2c/`). Unity 6000.3.27f1 on Windows (GPU rendering, not the
cloud container), packages unchanged, prefab rebuild only (`RunPrefabsBatch`), no scene rebuild. Nothing in this stage ran on a headset.

**Status in one sentence:** the acceptance test `HandPenetrationTests` passes (two consecutive runs), EditMode **34 / 34**, PlayMode **73 / 73**,
Windows build 0.3.3 succeeds; the hands wrap the grips (fingers around the front strap onto the far side, palm on the panel, thumb resting
on the frame, trigger finger through the guard on the trigger) and both two-handed holds sit tight on the gun.

**What was actually wrong (found with per-segment diagnostics and screenshots, not guessed):**
1. The finger solve stopped a whole finger at its first contact, so a fingertip grazing the front strap left the finger straight.
2. The shooting-thumb search required the thumb on the palm's side of the grip; every natural pose was rejected.
3. The palm de-penetration only ever pushed out, so a palm whose anchor sat off the surface kept hovering.
4. The held hand copied the empty-hand fist orientation, whose knuckle line is ~25° off a straight grip, so the fingers spiralled past the grip.
5. One box per part cannot follow a raked grip: the rifle grip box was 71 mm deep around the visible grip and the fingers rested on air.
6. The pistol trigger guard was one solid volume, so any finger on the trigger counted as inside the gun.
7. The solve cost 20–90 ms and ran 20 times a second while holding.

**Changes (`HandPresenceGrasp.cs` new, `HandPresence.cs`, `ContactHull.cs` new, `WeaponPrefabBuilder.cs`, `weapons_layout.json`):**
- Convex hull (26-direction polytope) per contact volume, fitted to the mesh at prefab build; every solver and the test use it.
- Shooting hand: target-seeking IK per finger (tip aims at the far side of the grip at its knuckle's height), joint limits + penetration
  penalty, then a touch pass onto the nearest surface. Thumb rests forward along the near side of the frame (this rig's thumb base sits
  beside the slide, so the far side is out of reach without passing through it). Trigger finger rests on the trigger, blends into the
  trigger curl as it is pulled.
- Support hand: coupled grasp closure (joints close together, a touching segment locks the joints behind it) or sequential wrap,
  whichever hugs closer, then the touch pass.
- Palm: slides along the grip so the middle knuckle sits 6 mm behind the front strap, then settles along its normal until it touches,
  from either side; only the thenar / hypothenar compress by their bulge, the rest ≤ 5 mm, skin on skin ≤ 3 mm.
- Anchors re-authored: knuckle line along each grip's rake, pistol 12°, rifle 23° (grip boxes 25°), SMG 0°; support palms heel-on-panel.
- Performance: solve only on change (grab, hand joins / leaves, other hand re-solves, hand still settling); palm placement per tick,
  grasp only once the hand has settled, one finger per frame. Measured in the editor: 2 grasp solves per one-hand grab, longest frame
  stage 12–26 ms (Mono editor; a player build is faster). Only weapon volumes within 13 cm of the palm take part.
- The shooting hand no longer reacts to the support hand (deterministic, no feedback loop); only the support hand adapts.

**Test changes (stated, not hidden):** the cupping support palm is measured to whichever it rests on, the shooting hand or the grip
(same 8 mm limit), because the support heel now rests on the grip panel; two older tests checked the removed `ContactCurl` value and now
check `GraspActive`; `HeadsetRound1Tests` measures the palm centre against the contact hulls instead of the old support box. No limit was loosened.

**Final numbers (`Docs/TestResults/round3/hand_report_round3.txt`):**

| Configuration | Max depth outside zone | Palm skin gap | Fingertip bone gaps (thumb / middle / ring / little) | Wrist pitch | Held vs free wrist |
|---|---|---|---|---|---|
| Pistol, shooting hand | 2.0 mm | 6.3 mm | 8.9 / 7.6 / 10.6 / 9.0 mm | −12° | 1.6 cm |
| Pistol, both: shooting / support | 2.6 / 0.0 mm | 6.1 / 7.5 mm | 9.3 / 7.3 / 8.6 / 9.2 · support – / 9.1 / 6.9 / 7.3 | −12° / −12° | 1.6 cm |
| Rifle, shooting hand | 1.0 mm | 5.9 mm | 10.2 / 9.4 / 9.8 / 7.0 mm | −23° | 2.7 cm |
| Rifle, both: shooting / support | 0.6 / 0.6 mm | 5.7 / 7.2 mm | 10.4 / 9.9 / 10.4 / 7.4 · support – / 7.8 / 6.5 / 6.6 | −23° / +26° | 1.7 cm |
| SMG, shooting hand | 1.2 mm | 6.1 mm | 8.7 / 8.1 / 8.5 / 8.6 mm | 0° | 2.8 cm |
| SMG, both: shooting / support | 1.4 / 2.7 mm | 5.8 / 8.0 mm | 8.6 / 8.8 / 9.8 / 9.1 · support – / 8.7 / 7.2 / 6.9 | 0° / −12° | 3.4 cm |

Limits: ≤ 4 mm outside the compression zone, palm ≤ 8 mm, fingertip bone ≤ 12 mm (flesh radius ≈ 9 mm, so ≈ 9 mm is touching), wrist
pitch ≤ 25° below (support 30°), held wrist < 4 cm. Margins: the SMG support palm is at the limit (8.0 mm) and run-to-run noise is
about ±0.5 mm, so that one can flip; everything else has ≥ 1 mm margin.

**Known limits:** the rigid XR Hands mesh cannot arch its palm, so the little finger on the deep pistol grip curls onto the front strap
rather than the far panel, and the thumb rests along the near side of the frame instead of the far side. The wrist end of the hand model
is an open stump (no forearm). Screenshots: `Docs/Screenshots/round3_fixes/` (21, all looked at).

## Stage 7: headset feedback round 2 — hands on grips, real Unity editor in the cloud (2026-11-04, PCVR only) — **NOT ACCEPTED**
Starting point: commit `36d343b` (round 1). The owner's round-1 screenshots showed hands sinking into the weapons and pistol wrists
tilted too far down. Same rules as round 1: Unity 6000.3.27f1, packages unchanged, no scene rebuild / re-bake (prefab + settings
rebuild only), every interaction through the rig's own `NearFarInteractor`s, time-based waits. Nothing in this stage ran on a headset.

**Status in one sentence:** nothing sinks into a weapon any more and the wrists sit like a grip-pose fist, but the palms still hover
12–15 mm off the grip panels, the shooting thumbs do not rest on the frames, and the two-handed pistol / SMG holds are loose cups,
so the acceptance test `HandPenetrationTests` **fails** and the PlayMode suite is **71 / 72 with 1 failure**. The first version of this
stage (commit `7b093c8`) reported a pass; its criteria did not require the palm to touch anything and I had looked at screenshots of
floating guns and called them acceptable. That was wrong, and this section replaces it.

**Method.** `HandPenetrationTests.HandsRestOnTheWeaponsWithoutSinkingInAndWristsSitLikeAHighGrip` (6 configurations: pistol / rifle / SMG
× shooting hand alone / shooting + support hand). Per configuration: controllers at a fixed pose, 0.5 s free, grab through the right
`NearFarInteractor`, support grab through the left one, ≥ 0.9 s (1.6 s with support) plus a quiet-state wait, then both hand meshes are
baked (`SkinnedMeshRenderer.BakeMesh`, 8,651 vertices each) and every vertex is tested against the weapon's fitted contact volumes and
the other hand's palm box / finger capsules / knuckle spheres. Reported per hand: max depth (overall, outside the thenar/hypothenar
compression zone, inside the other hand), share of vertices inside, vertices in front of the muzzle, the palm centre's skin point
distance to the grip (or, for a cupping support hand, to the shooting hand), each wrapping fingertip's bone distance to the nearest
surface (flesh radius 9.5 mm, so ≈ 10 mm means the pad touches), wrist pitch, back-of-hand roll, held wrist vs the empty hand's wrist.
Screenshots (software GL, 960×640): `Docs/Screenshots/round2_fixes/` (48 files, all looked at). Full report:
`Docs/TestResults/round2/hand_report_r2.txt`; suite XMLs and the build report next to it.

Criteria: max depth ≤ 4 mm outside the compression zone (the thenar / hypothenar / side ridges of this rigid mesh may squash into a grip
panel or under a frame overhang by up to 12 mm, a stand-in for flesh), ≤ 4 mm inside the other hand, no vertex in front of the muzzle,
palm centre skin ≤ 8 mm from the grip (the palm is concave, its pads stand up to 7 mm proud of the centre), wrapping fingertips ≤ 12 mm
(SMG thumb 16 mm), shooting wrist ≤ 25° below the bore, support ≤ 30°, held wrist within 4 cm of the empty hand's wrist.

| Check | Measured (R = shooting hand, L = support hand) | Result |
|---|---|---|
| Compile (6000.3.27f1, packages unchanged) | prefab + settings rebuild, `Logs/prefabs_r1.log` | 0 errors, 0 compiler warnings |
| Skin thickness per hand | palm 22.4 mm, back of hand 16.7 mm from the bone line; finger flesh radius 9.5 mm; wrist→middle knuckle 96 mm (an adult male is ≈ 85–90 mm, so the hand model is roughly 10 % large; not scaled this round) | measured |
| Max depth outside the compression zone (≤ 4 mm) | pistol R 1.7 mm, L 0.0; rifle R 1.6, L 0.9; SMG R 1.7 alone / 4.0 with support (support palm box), L 8.4 (shooting hand's index knuckle) | **SMG support fails** |
| Depth in the compression zone (≤ 12 mm) | pistol 2.0 mm, rifle 3.1, SMG 4.0; rifle support 4.5 (bottom rail) | met |
| Hand in front of the muzzle | 0 vertices on every configuration in the headless run; 424 vertices of the SMG support hand in the rendered run (the cupping hand's fingertips reach past the 15 cm Micro Uzi) | **not reliably met on the SMG** |
| Palm on the grip (centre skin ≤ 8 mm) | pistol R **14.2 mm**, rifle R **14.7 mm**, SMG R **12.3 / 13.7 mm**; rifle L **18.4 mm** below the handguard; pistol L **27.9–32.7 mm** off the shooting hand, SMG L **23.8–26.1 mm** | **not met anywhere** (was 19–28 mm at `7b093c8`, 15–20 mm hidden behind the "no penetration" pass) |
| Fingertips | pistol R thumb **19.9**, middle 7.9, ring 7.6, little 9.8; rifle R thumb 9.9, middle **12.2**, ring 7.8, little 9.1; SMG R thumb **38.1**, middle 8.8, ring 10.1, little 7.8; pistol L 6.5 / 7.6 / 18.9 (free), rifle L 7.5 / 9.6 / 14.5 (free), SMG L 8.3 / 13.4 / 8.0 | **pistol and SMG thumbs, rifle middle finger not met** |
| Wrist pitch | shooting −21.7° (all); pistol L −11.6°; rifle L +25.8°; SMG L +7.5° | met |
| Back-of-hand roll | shooting +17.9° above outward (OpenXR grip-pose fist); pistol L +7.4°; rifle L −72.6° (under the guard); SMG L −88° | reported |
| Held wrist vs empty hand | pistol 1.4 cm; rifle 2.4 / 0.8 cm; SMG 1.1 / 0.9 cm | met |
| Full PlayMode suite | `Docs/TestResults/round2/playmode-results.xml`, 73 tests | **71 passed, 1 failed (`HandPenetrationTests`), 1 skipped** |
| Full EditMode suite | `Docs/TestResults/round2/editmode-results.xml` | 34 / 34 passed |
| Windows x64 PCVR build 0.3.2 | `Docs/TestResults/round2/VRRangeBuildReport_Windows_r2.txt`: 0 errors, 2 OpenXR validation notes; 296 MB unpacked; zip 143,345,179 bytes, sha256 `e1b0e73346faeecfe94a7436ea4ce22d2c78097ccaefcb342358f51cb1380362`, handed over as a session file, not committed | built, **not run on Windows or a headset**; hands as described above |
| Quest 2 APK | not built (PCVR only) | skipped |

Why the palms still float (measured, not guessed): the de-penetration backs the whole rigid hand out along its palm normal until the
deepest vertex clears. With the skin and bulge allowances in, the remaining driver is the upper thumb-side palm (hand-local
(−25, −28, −9) mm on the pistol, 11.7 mm inside the fitted *frame* box; (−25, −26, −6) mm inside the SMG receiver box): the fitted boxes
of the frame / receiver are as wide as the grip and cut into the web of the hand where a real frame is narrower and the web is thin.
Lowering the anchors 8–16 mm brought the gap from 19–28 mm to 12–15 mm; closing it needs either tighter contact volumes around the
beavertail / receiver underside (convex hulls per part instead of boxes) or a hand mesh with a thinner web, both outside this round.
The shooting thumbs float because the lowered hand no longer brings the thumb to the frame within the opposition range the thumb solve
searches. The SMG support hold was moved from "under the receiver" (it stood in front of the muzzle) to a pistol-style cup, because the
Micro Uzi's 10 cm of free receiver between the trigger finger and the muzzle cannot take a 9 cm wide hand; the cup overlaps the
shooting hand's index knuckle by 8.4 mm and still reaches past the muzzle in one of two runs.

What did get fixed and holds: no hand part sinks into a weapon beyond the allowed compression, the shooting-hand wrists sit like the
empty hand's fist (knuckle line 21.7° below the bore, wrist within 2.4 cm), a hand in front of the muzzle is now a test failure, the
pistol support thumb lies forward along the frame, and the de-penetration push dropped from 17–22 mm to 5–13 mm.

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
