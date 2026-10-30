# LOCAL_HANDOFF — from cloud to your Windows PC and Quest 2

Everything below happens on **your Windows PC**. The cloud session ran the real Unity 6000.3.27f1 editor in a
container: the project imports and compiles, its setup generated the scene/prefabs/settings (all committed),
EditMode and PlayMode tests pass, and a Quest 2 APK was built (`TEST_RESULTS.md`). What the cloud could not do
is touch a headset, so the on-device steps below are yours. Expect the first Unity open to take 5–15 minutes
(package download + import).

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

## 4. (Optional, recommended) bake lighting
**VR Range → Build → Bake Lighting**. Takes a few minutes; improves performance and look on Quest 2.
Without a bake the scene uses a handful of mixed/realtime lights and still runs.

## 5. Build the APK
A cloud-built APK already exists (V2, 0.2.0, 57 MB, see `TEST_RESULTS.md`); it was handed over as a file in the
session and is not stored in git. To rebuild locally:
- Menu **VR Range → Build → Build Quest 2 APK** → `Builds/VRShootingRange.apk`, or
- `powershell -ExecutionPolicy Bypass -File Tools\Windows\build_apk.ps1` (add `-BakeLighting` to bake first,
  `-Development` for a debug build). The first IL2CPP build takes 10–25 minutes.
- Build log: Unity Console / `Logs/build_apk.log`, summary in `Logs/VRRangeBuildReport.txt`.

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

## 8. If the weapon or the hand sits wrongly
- Weapon angle in the hand: edit `gripPitchDegrees` in `Assets/Art/Weapons/weapons_layout.json` (e.g. `-30`), then
  **VR Range → Setup → Rebuild Everything**; or rotate the `Attach` child of the weapon prefab in `Assets/Generated/Prefabs`.
- Hand placement on the grip: the `primaryPalm` / `supportPalm` entries in the same JSON (palm position, finger
  direction, back-of-hand direction in weapon space) drive the `PrimaryGrip` / `SupportGrip` anchors; the free-hand pose
  relative to the controller is on the `Hand Visual` object's `HandPresence` component (*Free Palm Offset / Euler*).
- Weight feel too strong or weak: tune the *Weight & handling* block of `Assets/Settings/Weapon_*.asset` in Play mode.

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
