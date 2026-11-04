# Next session: headset feedback round 1 (2026-11-04)

Paste everything below into the Claude Code session that works on this repo.

---

Continue the VR Shooting Range project. The owner played the **Windows PCVR build on a Quest 2 through Virtual Desktop
(VDXR)** on their PC (RTX 4070 SUPER) for the first time. A local Claude Code session fixed the blocking bugs it found
(listed under "Already fixed"). Their commit is on branch `local/headset-fixes-2026-11-04`. Start from it:

```
git fetch origin
git checkout claude/determined-feynman-nc2t0l
git merge --ff-only origin/local/headset-fixes-2026-11-04
```

Read `CLAUDE.md`, then `TEST_RESULTS.md` **Stage 5**. Then fix the six issues below in the order given.

**This session's credits run out today. The owner needs a final, polished, working Windows PCVR build at the end of it.**
Work in priority order, commit and push after each issue, and keep the build green at every commit. If time runs short,
ship what works rather than leaving half-done work.

## Hard rules for this round
- **Use Unity 6000.3.27f1 exactly.** Do not upgrade the editor or any package. A test build on Unity 6000.6 force-upgraded
  XRI to 3.6.1, Input System to 1.20.1 and OpenXR to 1.18 and broke grabbing.
- **Keep `CLAUDE.md`'s rules:** no camera shake or headset recoil (recoil goes on the weapon model plus haptics only),
  stable GUIDs, idempotent generators.
- **Avoid a scene rebuild.** "Rebuild Everything" drops the baked lightmaps. If you do rebuild, re-bake
  (`VR Range > Build > Bake Lighting`) before the final build. Prefer changing settings assets, prefabs and runtime code.
- **Test through the rig's real interactors.** The earlier suites grabbed through scripted `XRDirectInteractor`s and
  `SelectEnter` calls, so they missed the fact that the real `NearFarInteractor` could never see weapons. Every new
  interaction test must use the rig's `NearFarInteractor` and its casters, as `Assets/Tests/PlayMode/RigGrabAndThumbTests.cs` does.
- **Tests must measure time, not frames.** Local frame rates are uncapped and much higher than in your container, so a loop
  over N frames covers far less time. One existing test failed locally for exactly this reason and was fixed.
- **This round is PCVR only.** The deliverable is the Windows PCVR build (`Tools/Windows/build_pcvr.ps1`, zipped).
  **Skip the Quest APK entirely**: don't build it, and don't tune for Quest 2 performance. Don't break the Quest
  configuration either. The focus is polishing and fixing the issues below.

## Owner's decisions (follow these; don't ask again)
- **Pistol two-handed release:** if the shooting hand lets go while the support hand is on the pistol, the pistol
  **passes to the support hand**, which becomes the new primary grip. The pistol must not drop.
- **One-handed rifle:** it stays **usable but hard to control**. It leans and sags down noticeably, has much worse
  recoil control, and wanders under auto fire. Never block one-handed fire.
- **Weight on every weapon:** all three weapons get believable, distinct weight (sag, lag, inertia and recoil from their
  mass and balance). The pistol is light but still has some, the SMG is in between, and the rifle is heaviest and
  front-heavy.

## Already fixed locally (in the commit you merged; verified with Unity 6000.3.27f1 on the owner's PC)
1. **Weapons could not be grabbed at all.**
   - **Cause:** the XRI Starter Assets `NearFarInteractor` casters only query the `Default` layer (near caster,
     `SphereInteractionCaster.physicsLayerMask`) and `Default`, `UI` and layer 31 (far caster,
     `CurveInteractionCaster.raycastMask`). The weapons live on the `Weapon` layer (8).
   - **Fix:** `PlayerRigSetup.Start` ORs the Weapon layer into both masks. It logs
     `[VRRange] ... grab casters now include the Weapon layer`.
   - **What this means for you:** the owner's first real grabs happened only after this fix. Nothing about weapon
     handling had been felt on a headset before (see issues 1, 2 and 5).
2. **The thumb folded down like a finger** (`HandPresence.Calibrate` / `ApplyCurls`).
   - **Causes:** the thumb's curl axis aimed at the palm normal; it used finger-sized limits (165° total), which folded
     it into the palm; and the thumb's spread sign was mirrored wrong on one hand.
   - **Fix:** the thumb now bends toward the middle finger's intermediate joint, has its own limits
     (`m_ThumbMaxDegrees` = 15/45/55°), and gets empirical sign checks for both bend and spread at calibration.
   - **Result:** the fist thumb tip in palm space is (across, inward, forward) = (-11, +11, +29) mm, identical for both hands.
3. **The player was too short.** OpenXR 1.16 maps Floor mode to the STAGE space, and Virtual Desktop's STAGE floor can be
   wrong. `PlayerRigSetup` now has a floor calibration:
   - Rest a controller on the floor and click both thumbsticks for 1 s, or press PageUp / PageDown (±2 cm) or Home
     (reset). The offset is stored in PlayerPrefs.
   - The game logs the eye height to `Player.log` 5 s after start. The owner's last session logged 1.51 m.
4. **Tests:**
   - Added `RigGrabAndThumbTests`: the real casters include the Weapon layer, the real near caster finds a touched
     pistol, and the thumb opposes without entering the palm.
   - Made `TargetMotionTypesBehaveAsSpecified` time-based.
   - Local results: EditMode 34/34, PlayMode 67/67. Windows PCVR build: 0 errors.

## Issues to fix (owner's feedback after playing), in priority order

### 1. Bullet speed is too slow (quick data change; do it first)
- **Current values:** `muzzleVelocity` 90 / 180 / 110 m/s with `gravityMultiplier` 3 / 2 / 3. The owner wants real values.
  These were researched already; don't spend time searching:

| Weapon (model) | Round | Real muzzle velocity | Real cyclic rate |
|---|---|---|---|
| Pistol (P38-style service pistol) | 9×19 mm, 125 mm barrel | **350–365 m/s** (use 360) | semi-auto |
| Rifle (M4A1) | 5.56×45 M855, 14.5" barrel | **≈880–900 m/s** (use 885) | 700–950 rpm (use 800) |
| SMG (Micro Uzi) | 9×19 mm | **≈350 m/s** | ≈1,200 rpm (use 1,200) |

- **Changes:**
  - Set `gravityMultiplier` to **1** for all three.
  - Raise the `[Range]` limits in `WeaponDefinition` (`muzzleVelocity` to at least 1,000; `roundsPerMinute` is already 1,500).
  - Update the defaults in `Assets/Editor/Build/SettingsAssetBuilder.cs` so a regeneration keeps the new values.
- **Checks:**
  - The `Projectile` swept hit test must not tunnel at 900 m/s: at 90 Hz a rifle bullet moves about 10 m per frame. Add a
    PlayMode test that shoots a 1 cm-thick target at 25 m with each weapon and requires a hit.
  - Tracers will be close to instant. Keep a short, faint tracer streak so shots stay readable; that's realistic too.
  - Update ballistics tests and docs that assumed the old numbers.

### 2. The rifle has no weight when held in one hand
- **Symptom:** the owner carries and fully auto-fires the M4A1 one-handed exactly as if it were held with two hands. It
  doesn't sag or drop.
- **First, confirm the handling transformer actually applies under the real `NearFarInteractor`.** It replaced XRI's
  general grab transformer, but it was only tested with scripted direct interactors. Log the live sag angle and
  response while holding the rifle, and fix any path, such as far-grab attach, that bypasses it.
- **Then make one-handed rifle use clearly worse.** A 3.4 kg rifle with its balance about 28 cm forward can't be held level
  at arm's length:
  - **Sag:** `oneHandSagDegrees` around 18–25°. Raise the `[Range]` cap of 12 and keep the clamps sensible.
  - **Lag:** `oneHandResponseSeconds` around 0.25–0.35 s.
  - **Sway:** clearly visible.
  - **Recoil:** much larger one-handed, so `recoilUnsupportedMultiplier` around 2.5–3. Instability builds quickly during
    auto fire and makes the muzzle climb and wander.
  - **Two hands:** with the support hand on the handguard it's steady, as now.
- **SMG:** somewhere in between: about 2.4 kg, so moderate sag and lag one-handed, and steady with two hands.
- **Pistol:** light (about 0.9 kg) but not weightless. Give it a small visible inertia and a slight droop after fast
  movements; it stays quick to aim.
- **One-handed rifle auto fire stays possible** (owner's decision) but loses control: the muzzle climbs and wanders until
  it's mostly off target after about 5–8 rounds, and recovers once you stop firing.
- Keep it fun and usable: the clamps must stop the weapon from going inside the body or the floor.
- Update `WeaponHandlingTests` to the new expectations.

### 3. Recoil is too weak
- **Symptom:** the weapon stays almost still even during rapid consecutive shots.
- **Current values:** `kickUpDegrees` pistol 6 / rifle 2.5 / SMG 1.8. Recovery is fast (`recoverySpeed` 16–24,
  `recoverySupported` 16–18), so each kick is gone before the next shot.
- **Target behaviour:**
  - Larger visible kick per shot: pistol muzzle flip about 8–12°, rifle about 3–5° upward plus a backward push, SMG fast
    and jittery.
  - Slower recovery, so muzzle climb **accumulates** during follow-up and auto fire (recoil pattern: mostly up, with a
    random horizontal walk).
  - The weapon recovers toward the aim point after the trigger is released.
  - Stronger, sharper haptics per shot.
  - Supported (two-handed, or pistol with a support hand, see issue 4) recoil is noticeably smaller.
- Recoil stays on the weapon model only, never on the camera.
- Add a PlayMode test: 10 rapid rifle shots must raise the muzzle measurably more than 1 shot, and less when supported.

### 4. Pistol: the second hand should support, not take the gun
- **Symptom:** squeezing Grip with the other hand near the pistol takes it out of the first hand.
- **Cause:** the pistol is built with `selectMode = Single` because `twoHanded` is 0. See
  `Assets/Editor/Build/WeaponPrefabBuilder.cs:137`.
- **Wanted:** a proper two-handed pistol grip. When the second hand grips near the pistol grip or the shooting hand, it
  wraps around the shooting hand (support-hand cup, thumbs forward). It never steals the weapon.
- **Implementation:**
  - Use `Multiple` select mode for the pistol too, plus a `SupportGrip` anchor whose palm pose wraps the primary hand.
    Add it to `weapons_layout.json`, `supportPalm`.
  - `WeaponHandlingTransformer` treats this as "supported": less sway, less recoil, faster recovery.
  - The support hand's visual snaps around the primary hand without intersecting it.
  - **Releasing the primary hand while supported hands the pistol over to the support hand** (owner's decision). The
    support hand smoothly becomes the primary grip on the pistol grip. Don't drop it, and don't snap it visibly.
    Releasing the support hand returns the pistol to one-handed.
- Test it with the rig's real interactors.

### 5. Hands go through the weapon grips; the finger wrap is wrong
- **Symptom:** when holding a weapon, the hand mesh penetrates the grip and the fingers don't wrap it believably.
- **Review method:** render per-weapon screenshots, as in `VisualCaptureTests` and `RigGrabAndThumbTests.Shot`. Show the
  pistol, rifle primary + support and SMG from the palm side, thumb side and top. Inspect every one.
  - Warning: on the owner's PC, manual `Camera.Render()` screenshots in batch mode may not update skinned meshes between
    poses. Check that the open and fist images actually differ, and force a skinning update if needed.
- **Fix:**
  - Re-author palm placement per weapon: `primaryPalm` / `supportPalm` in `Assets/Art/Weapons/weapons_layout.json`.
  - Add per-finger curl per weapon pose in `Assets/Settings/HandPoses/*.asset`.
  - Preferably, add a simple per-finger contact solve. For each finger, increase the curl until its segments touch the
    weapon's grip collider (sphere-cast or `Physics.ComputePenetration` against a grip collider on the weapon), then
    stop. Fingers then wrap the actual grip shape on every weapon and never pass through it.
  - Trigger finger: resting outside the guard, then on the trigger when it's pulled.
- **Acceptance:**
  - No visible penetration in the screenshots, both hands.
  - The thumb wraps the grip on the opposite side from the fingers.
  - The rifle support hand holds the handguard from below.
  - Add an automated check that fingertip joints sit at most about 5 mm inside the grip collider.

### 6. Overall environment, texture and feature polish (time-box this; only after 1–5 work)
The owner's focus is **polishing what exists and fixing the issues above**, not big new features. Polish before features.
- **Visuals (PCVR first):**
  - Check texture resolution and tiling on the floor, walls, counter and booths for visible blur or obvious repetition;
    use the 2K/4K CC0 sets where they help.
  - Add bullet-hole decals that suit each surface, plus dust or sparks on impact.
  - Better reflection-probe coverage at the counter.
  - Check the lightmap brightness against the real-time lights.
- **Features, picked for low risk and high value:**
  - A results board with per-weapon stats (accuracy, group size).
  - A hit-confirm sound on the target.
  - Visible magazine count / ammo on the weapon.
  - A clear "how to play" sign at the counter (Grip to grab, support hand, calibrate floor).
- Don't destabilise anything that works. Skip any feature that doesn't fit the remaining time.

## Definition of done (report each item in `TEST_RESULTS.md` Stage 6)
- Compiles with 0 errors.
- EditMode and PlayMode suites pass, including the new tests for each issue above.
- Windows PCVR build succeeds with 0 errors. Hand over `VRShootingRange_PCVR_Windows.zip` with its sha256.
- The per-weapon hand screenshots are committed under `Docs/Screenshots/round1_fixes/` and inspected.
- `LOCAL_HANDOFF.md` and `README.md` are updated: controls (support grip on the pistol, floor calibration) and the new
  ballistics numbers.
- Write a short on-headset checklist for the owner covering issues 1–5, and list anything you could not verify without a
  headset.
- Everything is committed and pushed to `claude/determined-feynman-nc2t0l`.
