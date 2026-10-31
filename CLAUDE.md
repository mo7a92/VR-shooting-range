# VR Shooting Range — Claude guardrails (keep short)

**What this is:** Unity 6.3 LTS (6000.3) / URP / OpenXR + XR Interaction Toolkit 3.3.2 project for a
Meta Quest 2 standalone VR shooting range (university project). C#, Android ARM64, IL2CPP, Vulkan.

**Where things are**
- `Assets/Scripts/` runtime code (asmdef `VRRange.Runtime`, namespaces `VRRange.*`).
- `Assets/Editor/` editor automation (asmdef `VRRange.Editor`): project configuration, art import,
  prefab + scene generation, APK build. Menu: **VR Range > ...**. Everything is idempotent.
- `Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/` = the official XRI sample, committed
  as Package Manager would import it (rig prefab, input actions, locomotion). Do not edit.
- `Assets/Art/`, `Assets/Audio/` generated source assets (OBJ meshes, PNG textures, WAV audio) and
  their Python generators in `Tools/AssetGen/`.
- `Tools/CloudCompileCheck/` static C# compile check used in cloud sessions (no Unity needed).
- Docs: `PROJECT_SPEC.md` (requirements), `DECISIONS.md`, `PROJECT_PROGRESS.md`, `TEST_RESULTS.md`,
  `ASSET_CREDITS.md`, `LOCAL_HANDOFF.md` (Windows + Quest 2 steps).

**Rules**
- Never claim Unity ran, an APK built, or Quest 2 was tested without evidence; record it in `TEST_RESULTS.md`.
- No camera/headset recoil or camera shake. Weapon recoil is visual on the weapon model + haptics only.
- Gameplay numbers are fictional and tunable (ScriptableObjects in `Assets/Settings/` after generation).
- Generators must be idempotent: re-running must not duplicate scene objects or assets.
- Keep `Library/`, `Temp/`, `Logs/`, builds and credentials out of git.
- Prefer APIs verified against package sources (XRI 3.3.2, OpenXR 1.16.1, Input System 1.14.2, URP 17.3.0).

**State (2026-10-30):** verified in a real Unity 6000.3.27f1 container run: compiles, setup OK, EditMode 16/16,
PlayMode 4/4, Quest 2 APK builds. Generated scene/prefabs/settings and all `.meta` files are committed; keep GUIDs
stable (do not delete/recreate `.meta` files). Headset testing pending.

**V2 (premium upgrade):** `WeaponHandlingTransformer` (weight/lag/sag/sway/two-point aim/recoil) replaces XRI's default grab
transformer on weapons; `Assets/Scripts/Hands` drives the XR Hands sample rig from controller input and weapon grip anchors
(`PrimaryGrip`/`SupportGrip`, authored in `weapons_layout.json`). CC0 PBR sets live in `Assets/Art/Textures/PBR` (ambientCG).

**V3 (PCVR + QA):** Windows x64 OpenXR player (`Assets/Editor/Build/PcvrBuildScript.cs`, `Tools/Windows/build_pcvr.ps1`), two
quality levels with their own URP assets (`URP-Quest2`, `URP-PCVR` + SSAO + `PCVR-PostProcess` volume switched at runtime by
`PlatformGraphicsSetup`), desktop spectator camera (`Assets/Scripts/Recording/SpectatorCamera.cs`, F1–F6), and a large PlayMode/
EditMode suite under `Assets/Tests` that mocks controllers with scripted XRI interactors (see `XrTestRig`). Results: `FINAL_QA_REPORT.md`.

**Asset pipeline (V3):** `Tools/AssetGen/fetch_polyhaven.py` (CC0 props/weapon textures) → `blender_process.py` (needs `pip install bpy`:
weapon FBX with named parts, hands subdivision + skin bake, decor) → `decimate_models.py` (Quest budgets). Weapons are described by
`Assets/Art/Weapons/weapons_layout.json` (`model`, `textureSet`, part roles, grip/muzzle/palm anchors). Lightmaps live in
`Assets/Scenes/ShootingRange/`; a scene rebuild drops them, so bake again (`VR Range > Build > Bake Lighting`).

**Commands**
- Cloud compile check: `python3 Tools/CloudCompileCheck/compile_check.py` (needs toolchain, see script header).
- Local (Windows): open in Unity Hub → **VR Range > Setup > Run Full Setup**; build via
  **VR Range > Build > Build Quest 2 APK** or `Tools/Windows/build_apk.ps1`; install with `Tools/Windows/install_apk.ps1`.
- Windows PCVR: **VR Range > Build > Build Windows PCVR (x64)** or `Tools/Windows/build_pcvr.ps1` (needs the Windows Build Support module).
