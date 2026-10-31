# VR Shooting Range (Meta Quest 2) — university project

A standalone VR shooting-range simulator for the Meta Quest 2 built with **Unity 6.3 LTS**, **URP**,
**OpenXR** and the **XR Interaction Toolkit 3.3.2**: five lanes, three weapons (pistol, rifle, SMG) with
distinct handling, pooled projectiles with drop and time of flight, bullseye and moving/pop-up targets,
three play modes with world-space VR menus, teleport + snap-turn comfort locomotion.

The project was written in a cloud session and then verified with the real Unity 6000.3.27f1 editor in a
container: it imports and compiles, the generated scene/prefabs/settings are committed, EditMode (34) and
PlayMode (63) tests pass, and both a Quest 2 APK and a Windows x64 PCVR player build (`TEST_RESULTS.md`,
`FINAL_QA_REPORT.md`). Headset testing is still pending.

**V3 (PCVR + QA):** the same project runs on Windows through OpenXR (Meta Quest 2 via Virtual Desktop / VDXR or
SteamVR, Index/Vive/G2/WMR controllers bound), with a separate high-end graphics preset (HDR, 4-cascade soft shadows,
SSAO, ACES + bloom), a desktop spectator camera for OBS (first/third person, fixed tripod, 16:9, F1–F6), and an
automated test suite that drives the whole game through scripted XR interactors (weapons, hands, projectiles,
targets, modes, UI, stress and a full launch-to-restart session).

**V2 premium upgrade:** weapons have simulated weight and balance (inertia lag, sag, sway, two-point aiming with a
support hand, grip-dependent recoil and haptics), visible animated hands follow the controllers and snap to per-weapon
grip anchors, shell casings eject, and the range was rebuilt with CC0 PBR materials, booths, baffles and reverb.
Art is a mix of CC0 photoscanned/game-ready assets (Poly Haven, OpenGameArt), procedurally generated textures and synthesised
audio, all redistributable (`ASSET_CREDITS.md`). Screenshots: `Docs/Screenshots/final_v7` (before the overhaul: `before_v3`).

## Quick start (Windows)
1. Install Unity Hub + Unity 6.3 LTS with *Android Build Support, OpenJDK, Android SDK & NDK Tools*.
2. Clone this repo (branch `claude/determined-feynman-nc2t0l` or merged `main`), open the folder in Unity Hub.
3. Open `Assets/Scenes/ShootingRange.unity` (already generated); *VR Range → Validate Project* should be green.
4. *VR Range → Build → Build Quest 2 APK* (or `Tools\Windows\build_apk.ps1`), then
   `Tools\Windows\install_apk.ps1` with the Quest 2 connected in Developer Mode.

Full beginner steps: **`LOCAL_HANDOFF.md`**. Requirements: `PROJECT_SPEC.md`. Status: `PROJECT_PROGRESS.md`.
What was and was not tested: `TEST_RESULTS.md`.

## Repository layout
```
Assets/Scripts     runtime code (VRRange.Runtime): weapons, ballistics, targets, scoring, modes, UI, audio
Assets/Editor      generators + settings automation (VRRange.Editor), menu "VR Range"
Assets/Art, Audio  generated meshes/textures/signs/audio (+ generators in Tools/AssetGen)
Assets/Samples     XRI Starter Assets sample (official rig, input actions, locomotion)
Assets/Tests       EditMode unit tests (ballistics, scoring, grades)
Tools/Windows      PowerShell: run_setup, build_apk, install_apk, logcat
Tools/CloudCompileCheck  Roslyn-based compile validation used in the cloud (no Unity needed)
```

## Controls (Quest 2 Touch)
Grip = grab/drop weapon · Trigger = fire / press UI buttons · A/X = reload · B/Y = semi/auto ·
Left stick = teleport · Right stick = snap turn.
