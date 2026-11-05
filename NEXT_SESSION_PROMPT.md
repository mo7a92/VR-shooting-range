# UPDATE 2026-11-05 (round 3): the hand-grip problem below is FIXED

`TEST_RESULTS.md` Stage 8 has the details and numbers. Acceptance test `HandPenetrationTests` passes, EditMode 34/34, PlayMode 73/73,
Windows build 0.3.3. The hand solve now lives mostly in `Assets/Scripts/Hands/HandPresenceGrasp.cs` (IK grasp, convex-hull contact,
solve-on-change); `Assets/Scripts/Weapons/ContactHull.cs` is fitted at prefab build. Next step: test on the headset (grab each weapon one-
and two-handed, check the hands look right, check there is no hitch at the moment of grabbing). The text below is the round-2 handoff,
kept for history.

# Next session: make the hands actually hold the guns (round 2 is NOT accepted)

Read this whole file before touching code. It is the handoff from the cloud sessions of 2026-11-04/05.
Branch: `claude/determined-feynman-nc2t0l`, last commit with code changes `3dcf7e7` (round 2b). `TEST_RESULTS.md` Stage 7 is the
honest record. `Docs/Screenshots/round2_fixes/` shows the state at `3dcf7e7`. Nothing has run on a headset since round 1.

## 1. The problem, in one paragraph
Look at `Docs/Screenshots/round2_fixes/round2_pistol_both_front.png` and `round2_smg_both_palmside.png` from commit `7b093c8`
(the owner's words: "can anyone in the world hold a gun like that? the gun is floating"). The hands hover around the weapons instead
of gripping them: the palm centre's skin sits 12–15 mm off the grip panel, the shooting thumb does not touch the frame (18–38 mm),
and the two-handed pistol / SMG holds are open cups 20–40 mm apart. The first round-2 test "passed" because it only checked
"nothing inside the weapon" and "fingertip within 12 mm"; a hand floating with fingertips grazing the front strap satisfies both.
The acceptance criteria were tightened in `3dcf7e7` and the test now **fails by design** until the hands are right.

## 2. Hard rules (unchanged from the owner)
- Unity **6000.3.27f1** only, packages unchanged (XRI 3.3.2, OpenXR 1.16.1, Input System 1.14.2, URP 17.3). PCVR only, no APK.
- No scene rebuild, no re-bake (prefab + settings rebuild only: `VRRange.Editor.RangeProjectBootstrap.RunPrefabsBatch`).
- Every interaction test through the rig's real `NearFarInteractor`s; time-based waits, not frames.
- Never claim a pass without the numbers; look at every screenshot before claiming anything; do not loosen the criteria to get green.
- No camera recoil / shake. Keep GUIDs stable. Do not commit builds, licence files or credentials.
- **Push only when the acceptance test passes** (owner's instruction). Commit locally as often as you like.

## 3. Acceptance test (already written, keep it)
`Assets/Tests/PlayMode/HandPenetrationTests.cs::HandsRestOnTheWeaponsWithoutSinkingInAndWristsSitLikeAHighGrip`, 6 configurations
(pistol / rifle / SMG × shooting hand alone / with support hand). It bakes both hand meshes and reports, per hand:
- max depth inside the weapon outside the "compression zone" (≤ 4 mm), inside the zone (≤ 12 mm), inside the other hand (≤ 4 mm)
- vertices in front of the muzzle (must be 0)
- palm centre skin point distance to the grip (≤ 8 mm; for a cupping support hand: to the shooting hand's shapes)
- fingertip bone distance to the nearest contact surface (≤ 12 mm; SMG thumb 16 mm; a support little finger may hang free)
- wrist pitch (shooting ≤ 25° below the bore, support ≤ 30°), back-of-hand roll (reported), held wrist vs empty-hand wrist (< 4 cm)
It writes `Logs/hand_report.txt` and 48 screenshots to `Logs/shots/round2_*.png`. Read the report first, then the screenshots.

## 4. How the hand system works (files)
- `Assets/Scripts/Hands/HandPresence.cs` — everything: calibration (`Calibrate`), per-hand measurement of palm skin / back-of-hand
  thickness and finger flesh radius from the baked mesh (`MeasureSkinOffsets`, `MeasureFingerRadius`, `BakeWorldVertices`),
  own contact shapes (palm box + finger capsules, `RefreshOwnShapes`), targets (`RefreshTargets`: the weapon's
  `WeaponContactVolumes` + the other hand), palm de-penetration on a cached rest-pose vertex cloud (`CachePalmCloud`,
  `SolvePalmDepenetration`: pushes the hand root out along its palm normal until the deepest vertex clears, with a per-vertex
  "flesh compression" allowance `m_PalmCloudExcess`), finger closing (`SolveFinger` bisection to first contact, `SolveTipExtra` closes the
  end joints until the tip touches, `SolveNearestApproach` when nothing touches), thumb (`SolveThumbOnPalmSide`: joint search over
  opposition and curl, must stay on the palm's side of the grip). Free hand pose = OpenXR grip-pose fist
  (`DefaultFreePalmForward/Up`); held pose = anchor pose + skin thickness along the anchor's up, plus the de-penetration offset.
- `Assets/Scripts/Weapons/WeaponContactVolumes.cs` + `Assets/Editor/Build/WeaponPrefabBuilder.cs::BuildContactVolumes` — per-part
  **boxes** shrink-wrapped to mesh vertices inside authored seed boxes (`contactVolumes` in `Assets/Art/Weapons/weapons_layout.json`),
  on layer `HandContact` (collides with nothing). Anchors `primaryPalm` / `supportPalm` (pos = the palm SKIN contact point, forward =
  fingers, up = back of the hand) live in the same JSON; the prefab builder overrides the primary forward/up with the free-pose fist.
- `Assets/Scripts/Weapons/WeaponHandlingTransformer.cs` — holds the weapon by `PalmAttach` (anchor + up × 22 mm) at the controller;
  mirrors X for a left shooting hand; `PrimaryGripWorld` is the point actually used.
- Hand mesh: the XR Hands sample rig (skinned mesh, 8,651 vertices per hand), driven from controller input by `Assets/Scripts/Hands`.

## 5. What was measured and what the real causes are
1. **The hand model is too fat and too big.** Baked: palm skin 22.4 mm + back of hand 16.7 mm = 39 mm thick (adult ≈ 28 mm), wrist to
   middle knuckle 96 mm (adult ≈ 88 mm), thenar / hypothenar / web 10–17 mm thicker than the palm centre. The rigid thenar held every
   palm 15–20 mm off the grip panel.
2. **Box contact volumes do not match the palm.** The fitted frame / receiver boxes are as wide as the grip and cut 11–12 mm into the
   web of the hand; the grip box is a "widest point" box, so the palm pressed on it at the 18° grip-pose roll touches along one edge
   while the centre floats. Every allowance that lets the edge squash also lets the palm sink elsewhere (round 2c measured:
   palm 5.8–8.0 mm off, but rifle 6.2 mm and SMG 10.7 mm inside the grip box).
3. **The thumb solve finds no admissible pose** once the hand sits low and the thenar is pressed against the grip: it falls back to
   curl 0 and the thumb points up along the slide 18–38 mm away. Needs `DebugThumb` output (`VRRANGE_DEBUG_THUMB=1`) to see which
   constraint rejects every candidate (side-of-grip test, `SampleDeep` bone clearance, or the ≥ 3 mm palm-side rule).
4. **Two-handed cups** (pistol and SMG: the Micro Uzi has only 10 cm of free receiver between the trigger finger and the muzzle,
   so an under-receiver support hand stands in front of the muzzle; it was changed to cup the shooting hand like the pistol). The
   support palm still sits 20–40 mm off the shooting hand: the cross-hand clearance (3–7 mm inflation of capsules) and the thenar
   bulge keep it off, and the authored `supportPalm` positions were never measured against the thinner hand.
5. Authored skin points: the rifle and SMG `primaryPalm` x values were measured 7–8 mm off their panels (fixed in the patch:
   rifle x 0.012, SMG x 0.010); pistol 1.8 mm (fine).

## 6. What was tried and rejected (do not repeat blindly)
- Heel-lift rotation about the knuckle line: the bulge sits mid-palm, so it saturated at 14°, moved the wrist 4–5 cm, no gain.
- Knuckle spheres + 3–4 mm cross-hand inflation alone: support palm sank 6.5–9 mm into the index knuckle. Spheres kept, see patch.
- Under-receiver SMG support hand: in front of the muzzle whatever the z.
- "Nearest approach" finger fallback: kept, works (rifle little finger 12.9 → 8.8 mm).

## 7. Unpushed progress: apply this patch first
`Docs/TestResults/round2c/round2c_hand_proportions_experiment.patch` (on top of `3dcf7e7`, `git apply`). It contains, all measured
in a run whose report is `Docs/TestResults/round2c/hand_report_round2c_last_run.txt`:
- hand root scaled once at calibration to adult proportions `(0.92, 0.72, 0.92)` (hand-root Y ≈ palm normal) → palm 15.8 mm, back of
  hand 12.1 mm, finger radius 8.6 mm, wrist→knuckle 88 mm; `BakeWorldVertices` made exact (`BakeMesh(mesh, true)` + `TransformPoint`)
  so measurements are right under that scale
- de-penetration travel = exact `ExitDistance` along the push direction (the old depth / cos(face angle) doubled the push at a box corner)
- both hands know each other (`RefreshTargets`), the shooting hand never moves for the support hand, pass 2 only for support hands
- SMG `supportHandInfluence` = 0 (cup hold, no two-point aim), thumb opposition search −60..75°, thumb base joint uses bone clearance
- anchors: pistol primary y −0.026, rifle (0.012, −0.036, −0.018), SMG (0.010, 0.06, −0.006), cups x 0.03
Result of that run: palm centre 8.0 / 5.8 / 7.1 mm off (pistol / rifle / SMG) — at the bound — but rifle 6.2 mm and SMG 10.7 mm
inside the grip box outside the compression zone, thumbs 21 / 18 / 26 mm, cups 20–25 mm. It is progress, not a fix.

## 8. Recommended next steps, in order
1. **Replace the box contact volumes for grip and frame / receiver with convex hulls of the mesh parts** (MeshCollider convex, or a
   signed-distance over the hull planes computed at prefab build time and stored on `WeaponContactVolumes`). `HandPresence.ContactShape`
   has `kind` box / capsule / sphere; add a convex kind with `SignedDistance`, `DepthAndNormal`, `ExitDistance`. This removes the
   edge-versus-penetration trade-off that blocked round 2c. Keep the `HandContact` layer.
2. With hulls in place, drop the compression allowance to a small uniform value (2–3 mm) and re-measure; the palm should then sit on
   the real panel with the centre ≤ 4 mm off and nothing > 4 mm inside.
3. Thumb: run with `VRRANGE_DEBUG_THUMB=1`, print per candidate which constraint rejects it, and fix that constraint (likely the
   `SampleDeep` clearance for the metacarpal now that the thenar is pressed on the grip, or the side-of-grip sign for the frame).
4. Cups: author `supportPalm` by measurement — place the support palm skin point on the shooting hand's middle/ring finger capsules
   (the report prints "authored anchor point ... from the grip" and the skin-point gap), target ≤ 4 mm skin-to-capsule with the 3 mm
   cross-hand inflation; keep the shooting hand's fingers out of the support thumb.
5. Only then: full PlayMode + EditMode, Windows build, screenshots, `TEST_RESULTS.md` Stage 7 rewritten with the new numbers,
   `FINAL_QA_REPORT.md` R2 / R12, commit, push.

## 9. How to run in the cloud (what worked)
Scripts are committed in `Tools/CloudUnity/` (they were scratch files before). They need `VRRANGE_SCRATCH` pointing at a scratch
dir containing: `unityproj/` (a copy of the repo; `sync_back.sh` copies Unity-written assets/settings back), `docker/data` (docker
data root), `unityhome/`, `unitycfg/`, `winmono/root` (the Windows Mono build-support module unpacked from Unity's installer, mounted
into the Linux editor). Image: `unityci/editor:ubuntu-6000.3.27f1-android-3.2.2` (19 GB; pull once). The docker daemon disappears
between sessions: `dockerd --data-root $VRRANGE_SCRATCH/docker/data --storage-driver overlay2 --iptables=false --bridge=none &`.
Licence: `PASS_CREDS=1 unity_run.sh activate.log -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -serial "$UNITY_SERIAL" -quit`
(env vars only, never print them), and **return it at the end** with `-returnlicense`; delete the activate/return logs.
- One hand-test run with screenshots (~4 min + activation): `round2_gfx.sh` (set `SKIP_ACTIVATE=1` when already activated).
- Everything (hand test → prefabs → PlayMode → EditMode → Windows build + zip + sha256, ~12 min): `final_chain2.sh`.
Results: `unityproj/Logs/{hand_report.txt,gfx-results.xml,playmode-results.xml,editmode-results.xml,VRRangeBuildReport_Windows.txt}`,
screenshots `unityproj/Logs/shots/`. Budget note: a run costs minutes of editor time but the expensive part is reading long reports;
print only the lines you need.
