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

## Next actionable tasks (local PC, see LOCAL_HANDOFF.md)
1. Open in Unity 6.3 LTS (6000.3.27f1 preferred): the scene, prefabs and settings are already generated and committed, so no setup dialog is expected. Run **VR Range → Validate Project** once.
2. Optional: **VR Range → Setup → Rebuild Everything** to switch the UI to the standard TMP font (TMP essentials are now committed).
3. Press Play with the XR Interaction Simulator or a headset via Link to sanity-check grabbing, firing, scoring and UI with real controllers.
4. Tune `gripPitchDegrees` in `weapons_layout.json` if weapons sit wrongly in the hand; rebuild prefabs.
5. Bake lighting, build APK, install on Quest 2, run the test checklist, measure FPS, fill in `TEST_RESULTS.md`.
6. Optional polish: higher-fidelity weapon models, ejected shell casings, scoreboard history, settings persistence.

## Known gaps / risks
- Nothing has run on a headset yet; XR could not initialise in the container, so controller interaction is unverified.
- Weapon orientation in hand and UI panel placement are untested in VR.
- Lightmaps are not baked (needs local GPU/CPU time).
- `Tools/CloudCompileCheck` depends on a scratch toolchain that is not committed (binaries); the scripts document how it was assembled.
