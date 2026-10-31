# LOCAL_HANDOFF — from cloud to your Windows PC, Quest 2 (standalone) and PCVR (Virtual Desktop)

Everything below happens on **your Windows PC**. The cloud session ran the real Unity 6000.3.27f1 editor in a
container: the project imports and compiles, its setup generated the scene/prefabs/settings (all committed),
EditMode and PlayMode tests pass, a Quest 2 APK and a Windows x64 PCVR player were built (`TEST_RESULTS.md`,
`FINAL_QA_REPORT.md`). What the cloud could not do is touch a headset, so the on-device steps below are yours.
Expect the first Unity open to take 5–15 minutes (package download + import).

There are **two ways to play**: standalone on the Quest 2 (APK, sections 3–6) and **PCVR** on your PC with the
Quest 2 as a wireless/wired display through Virtual Desktop (section 5b). Both use the same project and scene.

## 0. What you need (once)
1. **Unity Hub** → https://unity.com/download. Sign in (free Personal licence is fine).
2. In Unity Hub → *Installs* → *Install Editor* → **Unity 6.3 LTS (6000.3.x)**. Tick these modules:
   **Android Build Support**, **OpenJDK**, **Android SDK & NDK Tools**. (If you already have Unity 6.3, use
   *Add modules* on it.)
3. **Git** for Windows (https://git-scm.com) or GitHub Desktop.
4. Quest 2 in **Developer Mode**: install the *Meta Horizon* app on your phone → Menu → Devices → your Quest
   → *Headset settings* → *Developer Mode* → on. (You need a Meta developer account: https://developer.oculus.com/manage/organizations/create/ — free.)
5. USB-C cable that carries data. Optional but useful: SideQuest (https://sidequestvr.com) for installing APKs with a button.

## 1. Get the project
```powershell
git clone https://github.com/mo7a92/VR-shooting-range.git
cd VR-shooting-range
git checkout claude/determined-feynman-nc2t0l
```
(or open the pull request on GitHub, merge it, and clone `main`).

## 2. Open and initialise
1. Unity Hub → *Add* → *Add project from disk* → choose the `VR-shooting-range` folder → open it with 6.3 LTS.
   If Hub says the exact version `6000.3.27f1` is missing, pick your installed 6000.3.x; Unity upgrades in place.
2. Wait for packages to resolve and scripts to compile (Console must show no red errors).
3. The scene, prefabs and all settings are already generated and committed, so normally **no setup dialog
   appears**. If one does (for example after changing Unity version), choose *Run setup*; it is idempotent.
   - Optional: **VR Range → Setup → Rebuild Everything** switches the UI to the standard TMP font
     (the cloud build used a generated fallback font because TMP essentials could not be imported in batch mode).
   - Command-line alternative: `powershell -ExecutionPolicy Bypass -File Tools\Windows\run_setup.ps1`.
4. **VR Range → Validate Project** → read the dialog / `Logs/VRRangeSetupReport.md`. Fix anything marked ⚠️.
5. Open the scene: **VR Range → Open Shooting Range Scene**. Press Play to see it (no headset needed to view;
   use Window → Analysis → XR Interaction Debugger or the *XR Interaction Simulator* sample for desktop testing).

## 3. Quest 2 / Android settings (already applied by the setup; verify once)
- *Edit → Project Settings → XR Plug-in Management → Android tab*: **OpenXR** ticked.
- *OpenXR* page (Android): *Enabled Interaction Profiles* = **Oculus Touch Controller Profile**; *OpenXR Feature
  Groups* → **Meta Quest Support** ticked; Render Mode *Single Pass Instanced*.
- *Player → Android*: Scripting Backend **IL2CPP**, Target Architectures **ARM64** only, Graphics API **Vulkan**,
  Minimum API 29, Color Space **Linear**.
- *Graphics*: default render pipeline = `Assets/Settings/URP-Quest2.asset`.
- *File → Build Profiles → Android → Switch Platform* (setup switches it during the build if needed).

## 4. Lighting is already baked (re-bake after a rebuild)
The committed scene ships with lightmaps baked in the cloud (`Assets/Scenes/ShootingRange/`). If you run
**VR Range → Setup → Rebuild Everything**, the scene is regenerated without them: run **VR Range → Build → Bake Lighting**
afterwards (Progressive CPU, a few minutes; switch the lightmapper to GPU in the Lighting window for speed).

## 5. Build the APK
A cloud-built APK already exists (V3, 0.3.0, 114 MiB with the new assets and lightmaps, see `TEST_RESULTS.md`); it was
handed over as a file in the session and is not stored in git. To rebuild locally:
- Menu **VR Range → Build → Build Quest 2 APK** → `Builds/VRShootingRange.apk`, or
- `powershell -ExecutionPolicy Bypass -File Tools\Windows\build_apk.ps1` (add `-BakeLighting` to bake first,
  `-Development` for a debug build). The first IL2CPP build takes 10–25 minutes.
- Build log: Unity Console / `Logs/build_apk.log`, summary in `Logs/VRRangeBuildReport.txt`.

## 5b. Windows PCVR build and Virtual Desktop (VDXR / SteamVR)
The cloud produced `VRShootingRange_PCVR_Windows.zip` (Windows x64, Mono, Direct3D11, OpenXR; see `TEST_RESULTS.md`).
It is not stored in git. To rebuild locally the editor needs the **Windows Build Support (Mono)** module
(Unity Hub → Installs → Add modules), then:
- Menu **VR Range → Build → Build Windows PCVR (x64)** → `Builds/Windows/VRShootingRange.exe`, or
- `powershell -ExecutionPolicy Bypass -File Tools\Windows\build_pcvr.ps1` (`-Development` for a debug build).
- Build log `Logs/build_pcvr.log`, summary `Logs/VRRangeBuildReport_Windows.txt`. A `README_PCVR.txt` is written next to the exe.

**Install / run with Virtual Desktop (recommended for Quest 2):**
1. PC: install **Virtual Desktop Streamer** (https://www.vrdesktop.net/), sign in with the same Meta account name as the headset.
2. Quest 2: buy/install **Virtual Desktop** from the Meta Store, connect to the PC (5 GHz Wi-Fi 6 router or USB-C; set a
   bitrate of 100–150 Mbps in the VD settings for a clean image).
3. Streamer window → **Options → OpenXR runtime → VDXR**. (SteamVR is also supported: choose *SteamVR* there, start SteamVR,
   and in SteamVR → Settings → OpenXR make SteamVR the active OpenXR runtime.)
4. Unzip `VRShootingRange_PCVR_Windows.zip` anywhere (keep the `VRShootingRange_Data`, `MonoBleedingEdge` and `UnityPlayer.dll`
   next to the exe). Start `VRShootingRange.exe` from the desktop, or add it to the Virtual Desktop *Games* tab (VD scans
   Steam/Oculus libraries; custom exes can be launched from the streamed desktop).
5. In the headset you should be standing behind the firing line at your real height (the OpenXR *floor* origin is used; if
   your height is wrong, redo the Guardian floor level in the Quest or SteamVR room setup). The desktop window shows the
   spectator view (below).
6. The player log is at `%USERPROFILE%\AppData\LocalLow\UniversityProject\VR Shooting Range\Player.log`; the first
   `[VRRange]` line names the detected OpenXR runtime (e.g. `VDXR`, `SteamVR/OpenXR`, `Oculus`). If it says the runtime is
   not initialised, no OpenXR runtime was active when the game started.

Other PC headsets: Index/Vive via SteamVR (profiles enabled: Valve Index, HTC Vive, Oculus Touch, Quest Pro/Plus, Reverb G2,
Microsoft Motion). On Vive wands: trackpad click = reload, menu button = fire mode.

**Spectator / OBS recording (desktop window, 16:9):** the window shows a clean spectator view that is independent of the
headset image: **F1** first person (smoothed head view), **F2** third person over the shoulder, **F3** fixed tripod,
**F4** off, **F5** cycle third-person presets, **[** / **]** field of view, **F6** render every second frame (cheaper).
In OBS add a *Game Capture* (or *Window Capture*) of `VR Shooting Range`; game audio is the normal desktop output. The
headset itself is unaffected (the spectator is a separate camera that renders only to the desktop).

**Graphics presets:** the Windows build uses the *PCVR* quality level (`Assets/Settings/URP-PCVR.asset`: HDR, 4 shadow
cascades at 4096, soft shadows, per-pixel lights with shadows, SSAO, ACES tonemapping + bloom). The Quest build uses
*Quest 2* (`URP-Quest2.asset`). Switch between them in Edit → Project Settings → Quality when testing in the editor.
Supersampling is best set in Virtual Desktop / SteamVR (per-app resolution) rather than in the URP `renderScale`.

## 6. Install and run on the headset
1. Connect the Quest 2 by USB, put it on, accept **Allow USB debugging** (tick *Always allow*).
2. `powershell -ExecutionPolicy Bypass -File Tools\Windows\install_apk.ps1` — installs and launches.
   Manual equivalent: `adb devices`, `adb install -r Builds\VRShootingRange.apk`, then in the headset
   *Library → Unknown Sources → VR Shooting Range*.
3. Logs: `powershell -ExecutionPolicy Bypass -File Tools\Windows\logcat.ps1` → `Logs/quest_logcat.txt`.
   Look for lines starting with `[VRRange]`.

## 7. Test checklist (tick in `TEST_RESULTS.md`)
- [ ] App starts in VR, floor height correct, controllers visible, teleport (left stick) and snap turn (right stick) work.
- [ ] Spawn: you face the lanes; the main menu panel is on your left, target controls on your right, HUD above lane 3.
- [ ] Point at a panel button and pull the trigger: it presses (click sound).
- [ ] Walk/teleport to the counter behind you; grab the **pistol** with grip. Trigger fires (flash, sound, tracer,
      recoil kick, haptics). 12 shots then dry-fire click; A/X reloads (magazine animation).
- [ ] **Rifle**: grab with one hand, add the second hand on the handguard (two-handed rotation). B/Y toggles
      semi/auto; auto fires while holding the trigger.
- [ ] **SMG**: automatic by default, faster fire, lighter recoil, distinct sound.
- [ ] Drop a weapon on the floor: it returns to the counter after ~8 s.
- [ ] Hits on the paper target show markers and score 10/8/5/2; impacts on walls/backstop leave holes and sounds.
- [ ] Target controls: distance 5/10/15/25 m moves the target; horizontal / near-far / random / pop-up motion.
- [ ] Accuracy Challenge: 3-s countdown, 20 shots, results panel with grade; Restart works.
- [ ] Moving Target Challenge: 60-s timer, moving targets by difficulty, results.
- [ ] **Hands**: both hands visible at all times, fingers close with grip, index follows trigger; no controller model shown.
- [ ] **Weight**: rifle one-handed sags and lags visibly; adding the second hand on the handguard steadies it and reduces recoil;
      pistol stays snappy; SMG in between. If a weapon sits wrongly in the hand, see section 8.
- [ ] Shell casings eject and tinkle; lane scoreboard above the booth updates; combo text appears after 3 hits in a row.
- [ ] Performance: enable the OVR Metrics Tool (Meta Quest Developer Hub) or read `adb logcat -s VrApi`
      for FPS. Target 72 FPS. If low: bake lighting, reduce `renderScale` in `URP-Quest2.asset`, or lower MSAA to 2x.
- [ ] Comfort: no camera shake; snap turn 45°; vignette during smooth movement if enabled.

PCVR (Virtual Desktop) additions:
- [ ] `Player.log` first `[VRRange]` line shows the OpenXR runtime (VDXR or SteamVR) and `XR device: OpenXR Display`.
- [ ] Floor height correct; the counter top is at hip height (1.0 m), the HUD above eye level.
- [ ] Both controllers tracked; grip/trigger/A-B-X-Y/sticks mapped as on the Quest build; hands visible and animated.
- [ ] Desktop window: 16:9 spectator image, F1/F2/F3 switch views, F4 blanks it; the headset view is not affected.
- [ ] OBS captures the window at 1080p60 without stutter in the headset (if it stutters press F6 for half-rate spectator rendering).
- [ ] 90 FPS (or the headset's configured rate) in Virtual Desktop's performance overlay on an RTX 4070 Super; if not, lower
      the VD streaming resolution first, then MSAA (4x → 2x) in `URP-PCVR.asset`, then disable SSAO on `URP-PCVR-Renderer.asset`.
- [ ] SteamVR runtime path (optional): same checks with SteamVR as the OpenXR runtime.

## 8. If the weapon or the hand sits wrongly
- Weapon angle in the hand: edit `gripPitchDegrees` in `Assets/Art/Weapons/weapons_layout.json` (e.g. `-30`), then
  **VR Range → Setup → Rebuild Everything**; or rotate the `Attach` child of the weapon prefab in `Assets/Generated/Prefabs`.
- Hand placement on the grip: the `primaryPalm` / `supportPalm` entries in the same JSON (palm position, finger
  direction, back-of-hand direction in weapon space) drive the `PrimaryGrip` / `SupportGrip` anchors; the free-hand pose
  relative to the controller is on the `Hand Visual` object's `HandPresence` component (*Free Palm Offset / Euler*).
- Weight feel too strong or weak: tune the *Weight & handling* block of `Assets/Settings/Weapon_*.asset` in Play mode.
- The weapon models are real CC0 assets (`Assets/Art/Weapons/Models`, see `ASSET_CREDITS.md`); their anchors (`grip`, `muzzle`,
  `primaryPalm`, `supportPalm`, `ejectionPort`) are numbers in `weapons_layout.json` relative to the model origin (grip centre,
  barrel along +Z). Part bounds are listed in each model's `anchors.json` to help with adjustments.

## 9. Handing problems back to a Claude Code *local* session
Open Claude Code in the project folder on your PC and paste:

```
Continue the VR Shooting Range project (Unity 6.3, URP, OpenXR + XRI 3.3.2, Meta Quest 2). Read CLAUDE.md,
PROJECT_PROGRESS.md and TEST_RESULTS.md first. The cloud session generated the project; you have Unity locally.
Current problem: <describe what happened>. Attached logs: Logs/VRRangeSetupReport.md, Logs/build_apk.log
(or Editor.log), Logs/quest_logcat.txt. Fix the issue, re-run VR Range > Setup / the build scripts in
Tools/Windows, and update TEST_RESULTS.md and PROJECT_PROGRESS.md with what you verified on the headset.
```
Unity's editor log is at `%LOCALAPPDATA%\Unity\Editor\Editor.log`.
