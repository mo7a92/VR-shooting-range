# PROJECT_PROGRESS

Branch: `claude/determined-feynman-nc2t0l` (pushed to GitHub `mo7a92/VR-shooting-range`).
Cloud session: no Unity editor available; code validated with the Roslyn reference build (see `TEST_RESULTS.md`).

## Done (cloud)
- [x] Environment inspection, version decisions (Unity 6.3 LTS, URP 17.3, XRI 3.3.2, OpenXR 1.16.1, Input System 1.14.2)
- [x] Project scaffold: manifest, project version, gitignore/gitattributes, asmdefs, XRI Starter Assets sample committed
- [x] Cloud compile toolchain (`Tools/CloudCompileCheck`): Unity 6.3 engine/editor reference assemblies + package references; all project assemblies compile
- [x] Runtime: weapons (3 definitions, XRI grab/activate, fire modes, reload, visual recoil, haptics, return-to-counter), pooled projectiles with drop + swept hits, impact FX, targets (bullseye scoring, markers, reset), moving/pop-up controller with difficulty, lanes, score manager, 3 game modes, world-space UI binding, audio manager, rig setup
- [x] Generated art: weapon OBJ parts + layout, PBR textures, target faces, decals, FX sprites, signs; synthesized audio
- [x] Editor automation: project/XR/URP/OpenXR settings, import settings, materials, FX/target/weapon prefabs, environment, UI, scene, bootstrap dialog + menu + batch entry points, APK build script, validator with report
- [x] EditMode tests (ballistics, scoring, grades, distance presets)
- [x] Docs + Windows scripts + handoff

## Done (cloud, real Unity 6000.3.27f1 in a container, 2026-10-30)
- [x] Licence activated via Unity command line (seat returned afterwards), project imported, all assemblies compiled
- [x] Full first-open setup ran in batch mode: 47 checks OK; generated scene, prefabs, settings, XR/OpenXR/URP config committed with their .meta GUIDs
- [x] EditMode 16/16 and PlayMode 4/4 tests passed (a pistol shot hits and scores in the generated scene)
- [x] Quest 2 APK built (IL2CPP, ARM64, Vulkan, OpenXR) — 46 MB, see TEST_RESULTS.md; APK is not committed (gitignored), rebuild locally with `Tools/Windows/build_apk.ps1`
- [x] Fixes from the real editor: URP asset setters, Unity null check in `EditorRefs.Ensure`, coroutine on deactivating weapon, Gradle repository order template

## Done (V2 premium upgrade, cloud + real Unity, 2026-10-30)
- [x] Weapon weight & handling: `WeaponHandlingTransformer` (inertia lag, sag, sway, two-point aim, grip blend, recoil spring) with per-weapon profiles; grip-dependent recoil and haptics
- [x] Visible animated hands (XR Hands rig) with pose presets, trigger finger, reload pinch, grip anchors per weapon, controller model hidden
- [x] Art: 11 CC0 ambientCG PBR sets, detailed weapon generators, skin texture, UI sprites/icons; environment rebuilt (booths with acoustic/glass dividers, baffles, duct, waiting area, officer station, backstop traps, reverb zone)
- [x] Gameplay/UI: shell casings, lane scoreboards, combo, personal bests, countdown + round-complete audio, slide-rack sound, sliced UI panels
- [x] Verified in Unity 6000.3.27f1: 0 compile errors, EditMode 21/21, PlayMode 10/10 (handling, grips, recoil, hands)
- [x] V2 Quest 2 APK built (0.2.0, versionCode 2, IL2CPP ARM64 Vulkan OpenXR, 57 MB), delivered as a session file; not committed
- [ ] Review screenshots could not be rendered in the container (software GL too slow); `VisualCaptureTests` runs locally with a GPU

## Done (V3 PCVR upgrade + QA, cloud + real Unity, 2026-10-30)
- [x] Windows x64 PCVR player: OpenXR loader + 7 interaction profiles for Standalone, Mono/D3D11 player settings, windowed 1080p mirror, `PcvrBuildScript` + `build_pcvr.ps1`; Vive wand fallback bindings
- [x] Graphics presets: quality levels `Quest 2` / `PCVR` with their own URP assets (PCVR: HDR, 4 cascades @4096, soft shadows, per-pixel shadowed lights, SSAO, probe blending) + post-processing volume (ACES, bloom, grading) switched at runtime by `PlatformGraphicsSetup`
- [x] Spectator camera for OBS (`SpectatorCamera`): first/third person/fixed, smoothing, 16:9 letterbox, headset mirror disabled, hotkeys, half-rate option, headset-only layer for the vignette; never active on Android
- [x] Test suite: 63 PlayMode + 34 EditMode tests (weapon state machine, grips, reload, fire modes, projectiles/pooling, targets/difficulty, modes/UI/restart, null handling, hands, spectator, stress/leaks, full gameplay sequence, prefab/scene integrity, build configuration)
- [x] Bugs found and fixed by the suite: shell-casing component missing from its prefab (class/file name mismatch), accuracy challenge re-entering the countdown after the shot limit (replayed GO), weapons re-posed by their sockets at start, duct/sign/beam intersections, unbaked scene too dark (subdivided meshes for per-vertex lights, brighter interim ambient, lightmap UVs)
- [x] Visual overhaul: CC0 weapon models (Poly Haven pistol, OGA M4A1, OGA Micro Uzi) processed with Blender-as-a-module, subdivided hands with baked skin, 27 Poly Haven props (decimated), steel-framed tables, fixture-based lighting with baked lightmaps (4×2048), reflection probes, Rajdhani UI restyle
- [x] Visual review from in-container software-GL screenshots across 6 rounds (`Docs/Screenshots/before_v3` → `Docs/Screenshots/final_v7`), issues fixed between rounds (duct/sign clashes, prop import scale/rotation, grip heights, SMG support hand)

- [x] V3 builds in the container: Quest 2 APK 0.3.0 (versionCode 3, IL2CPP ARM64 Vulkan OpenXR, 114 MiB) and Windows x64 PCVR player 0.3.0 (Mono/D3D11/OpenXR, 296 MB, 137 MiB zip), both delivered as session files, not committed; sizes/hashes in TEST_RESULTS.md
- [x] Docs: FINAL_QA_REPORT.md (severity-classed findings, readiness: not production-ready until headset sessions), TEST_RESULTS.md Stage 4, LOCAL_HANDOFF.md §5b Virtual Desktop / VDXR / SteamVR / OBS

## Next actionable tasks (local PC, see LOCAL_HANDOFF.md)
1. Open in Unity 6.3 LTS (6000.3.27f1 preferred): the scene, prefabs and settings are already generated and committed, so no setup dialog is expected. Run **VR Range → Validate Project** once.
2. Optional: **VR Range → Setup → Rebuild Everything** to switch the UI to the standard TMP font (TMP essentials are now committed).
3. Press Play with the XR Interaction Simulator or a headset via Link to sanity-check grabbing, firing, scoring and UI with real controllers.
4. Tune `gripPitchDegrees` / `primaryPalm` / `supportPalm` in `weapons_layout.json` if weapons or hands sit wrongly; rebuild prefabs. Tune the *Weight & handling* block of the weapon assets in Play mode to taste.
5. Install the V3 APK (or rebuild), run the headset checklist in `LOCAL_HANDOFF.md`: one-handed sag/lag per weapon, two-handed aim without snapping, recoil difference per grip, hand poses and grip alignment, casings, audio, 72 FPS; fill in `TEST_RESULTS.md`.
5b. PCVR: unzip the Windows player, start Virtual Desktop (VDXR) or SteamVR, run the exe, check the Player.log runtime line, the spectator window and 90 FPS (section 5b + checklist in `LOCAL_HANDOFF.md`).
6. Bake lighting before a final build (`VR Range → Build → Bake Lighting`).
7. Optional polish: artist-made weapon models, scoreboard history, settings persistence.

## Known gaps / risks
- Nothing has run on a headset yet; XR could not initialise in the container, so controller interaction is unverified.
- Weapon orientation in hand, hand/grip alignment, the *feel* of the weight simulation and UI panel placement are untested in VR; no screenshots could be rendered in the container.
- Lightmaps are not baked (needs local GPU/CPU time).
- `Tools/CloudCompileCheck` depends on a scratch toolchain that is not committed (binaries); the scripts document how it was assembled.
