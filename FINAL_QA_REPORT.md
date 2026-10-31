# FINAL QA REPORT — VR Shooting Range (V3: PCVR + visual overhaul + QA)

Date: 2026-10-31. Unity 6000.3.27f1 (real editor in a Linux container), URP 17.3, OpenXR 1.16.1, XRI 3.3.2.
Platforms: Meta Quest 2 standalone (Android ARM64 APK) and Windows x64 PCVR (OpenXR: Virtual Desktop / VDXR, SteamVR, Oculus).

**Headline:** every automated check passes (EditMode 34/34, PlayMode 63/63 + 1 headless-skipped screenshot test, setup 0 warnings),
both builds compile and package, lightmaps are baked, and in-container screenshots were reviewed. **Nothing has run on a physical
headset.** Release readiness is therefore *"ready for device acceptance testing"*, not *"production-ready"* — see §9.

## 1. What was tested and how
| Layer | Method | Real vs mocked |
|---|---|---|
| Compilation / setup | Unity batch rebuild of settings, prefabs, scene (`RunFullSetupBatch -vrrangeForce`) | real editor |
| EditMode (34) | NUnit in the editor: maths, assets, prefab/scene integrity, build configuration | real assets |
| PlayMode (63) | NUnit in Play mode on the generated scene. Controllers are **mocked by construction**: scripted `XRDirectInteractor`s and the rig's own interactors are moved from code and XRI activate/deactivate events are raised for trigger pulls. Everything downstream (XRI selection, weapon state machine, handling transformer, projectiles, scoring, modes, UI, hands) runs for real. | mocked tracking + buttons, real game logic |
| Integration | One scripted session: launch → enter range → select mode → pistol fire + reload → rifle two-handed → moving targets → SMG → finish challenge → results → restart → cleanup checks | as above |
| Stress | 6 s sustained automatic fire, 30 rapid weapon switches with reloads, 20 mode restarts; pools, instance counts and managed heap compared before/after | as above |
| Visual | 20 screenshots rendered in the container (Mesa software OpenGL) after the lightmap bake, inspected image by image | real rendering, software GPU |
| Builds | Quest 2 APK (IL2CPP/ARM64/Vulkan) and Windows x64 player (Mono/D3D11) built in the container; APK manifest inspected with `aapt`, build logs checked for errors/warnings | real builds |
| Hardware | none available in the cloud | **not tested** |

## 2. Feature coverage (automated)
- **Weapons (pistol, rifle, SMG):** pickup/release/reacquire, hand swap, one- and two-handed grips, weight/lag/sag (rifle sags 1.5–12°, trails within the clamp, settles), two-point aim (< 6° error), smooth support release (no snap), grip-dependent recoil (unsupported > 1.3× supported), trigger semantics (semi fires once per pull, auto continues while held and stops on release/drop), fire-rate limiting, ammo counting, dry fire, reload transitions (ignored when full/already reloading, completes when dropped, cancelled by restart), fire-mode toggle per weapon, socket return (timer path and below-floor path), 12 grab cycles without duplicate/stuck state, haptics code path (device-less).
- **Hands:** both present and calibrated on the subdivided rig (26 joints), follow the controller, grip closes fingers, trigger curls the index, blended (not snapped) curls, palm sits within 2.5–3 cm of the weapon grip anchors (pistol primary, rifle primary + support), trigger finger follows the trigger while holding, smooth one↔two-hand transition.
- **Projectiles:** pooled and recycled (bounded pool), lifetime expiry, gravity drop consistent with the configured multiplier, impact decal + marker + reset, ring scoring from the actual hit radius, projectile/weapon layer exclusion (shots pass through weapons).
- **Targets / modes:** static, horizontal, near/far, random, pop-up (fold-away and re-expose), difficulty scaling (speed, exposure, multiplier), distance presets and clamping, countdown 3-2-1-GO once, accuracy challenge ends at the shot limit (state sequence Countdown→Running→Finishing→Finished), moving challenge ends on the timer, results/grades, personal best persistence, restart/reset of score, ammo, targets, combo.
- **UI:** panels wired, buttons drive the manager, labels/HUD/results text, readability constraints (≥ 16 px, world-space scale, within reach), XRUIInputModule present, lane scoreboards.
- **PCVR:** spectator camera (desktop-only, separate from the XR camera, first/third/fixed modes, smoothing, 16:9 letterbox maths, half-rate rendering, off), platform graphics switch (post volume, PCVR shadow lights, camera post-processing), OpenXR loaders + interaction profiles for both targets, quality levels per platform, URP preset differences, player settings, Vive-wand fallback bindings.
- **Robustness:** 9 null/missing-reference scenarios degrade gracefully (logged, never thrown); prefab/scene integrity scans (missing scripts, broken references, missing textures, out-of-room placement).

## 3. Bugs found and fixed during this QA pass
| # | Severity | Bug | Root cause | Fix | Verified by |
|---|---|---|---|---|---|
| 1 | High | Shell casings spammed "referenced script is missing" (4,675 errors in a 2-minute session) and never played bounce sounds | `ShellCasing` MonoBehaviour lived in `ShellCasingPool.cs`, so the component could not be serialised into the prefab | moved to `ShellCasing.cs` | prefab integrity test, 0 missing-script errors in the PlayMode log |
| 2 | Medium | Accuracy challenge replayed the countdown/GO and resumed the timer after the shot limit | the "last shots landing" pause reused `ModeState.Countdown`, which `Update()` re-ran | new `ModeState.Finishing` | `AccuracyChallengeCountsDownThenRunsAndFinishesWithResults` |
| 3 | Medium | A dropped two-handed weapon kept reporting `IsTwoHanded`/support state | `WeaponHandlingTransformer` only cleared state on unlink | implements `IXRDropTransformer.OnDrop` | stress test (30 switches) |
| 4 | Low | Weapons shifted ~8 cm and could tilt when their sockets re-posed them at start | socket attach did not mirror the weapon attach | builder mirrors attach pose; EditMode test asserts it | `SceneIsWiredCompletelyAndSanelyPlaced` |
| 5 | Low | Ceiling duct intersected the firing-line sign, beam and a scoreboard; sign overlapped the lane sign | layout | duct moved beside the centre lane, sign moved to the side walls | screenshots |
| 6 | Medium (presentation) | Scene far too dark / flat; point lights invisible on big walls | unbaked lighting + per-vertex additional lights on 4-vertex walls | subdivided meshes, lightmap UVs, baked lightmaps in the cloud, fixture-based lighting | screenshots, bake log |
| 7 | Low | Vive wands had no reload / fire-mode input | only A/B/X/Y bound | trackpad-click / menu fallbacks | `WeaponExtraButtonsHaveFallbackBindings` |
| 8 | Low | Obsolete `Lightmapping.giWorkflowMode`, unused field warnings | — | removed | 0 project warnings in build logs |
| 9 | Test infra | Screenshot capture hung in batch mode | `WaitForEndOfFrame` never resumes in `-batchmode` | removed; resolution via env vars | capture runs |

## 4. Visual overhaul (requested mid-task) — what changed
- **Weapons:** procedural box models replaced by CC0 assets: Poly Haven *Service Pistol* (photoscan, 2K), OpenGameArt *M4A1* (2K PBR, separate magazine/trigger/charging-handle parts), OpenGameArt *Micro Uzi* (mesh bevelled, AO baked, steel textures generated). Grip/muzzle/palm anchors re-authored per model (`weapons_layout.json`), all handling/recoil systems unchanged.
- **Hands:** XR Hands rig subdivided (2.7k → 16k tris each), smooth-shaded, baked skin textures (AO-driven creases, roughness map).
- **Environment:** 27 Poly Haven props (fluorescent fixtures with emissive tubes, caged lamps over the counter, steel shelving with crates/ammo boxes, gun rack with AKM + bolt-action display rifles, officer desk/stool/monitor/console, benches and chairs, lockers, briefing screen, bins, wet-floor sign, clock, fire alarm, cameras, power box, tool cart, hand truck), steel-framed shooting tables with magazine shelves and ammo boxes, ricochet steel guards downrange, side-wall firing-line signs.
- **Lighting:** baked lightmaps (16 texels/m, 2048), 26 fixture area lights, 5 booth strips, 10 target spots, 3 reflection probes, 476 probes; interim ambient lowered now that GI carries the room.
- **UI:** Rajdhani (OFL) font, smoked-glass panels with amber accents, primary/secondary buttons, dividers, slightly smaller angled panels.
- **Budget:** props decimated to per-model limits; scene ≈ 203k triangles unculled, 620 renderers, 73 materials (static batching + SRP batcher).

## 5. Visual verification
Screenshots were rendered inside the container after each build round (Mesa llvmpipe, OpenGL 4.5, 1280×720, URP with the
baked lightmaps) and inspected one by one. Final set: `Docs/Screenshots/final_v7/` (previous rounds: `Docs/Screenshots/before_v3` = before the overhaul, intermediate rounds not kept).

| View | Before (shots_v3) | After (shots_v7) |
|---|---|---|
| Spawn, looking downrange | dark, flat booths, placeholder HUD | lit booths with steel-framed tables, baked light pools, readable HUD and scoreboards |
| Weapon counter | black box weapons, unlit counter | real pistol / M4A1 / Uzi on stands, caged lamps, stocked staff area behind |
| Booth + menu panel | default-looking blue buttons | smoked-glass panel, Rajdhani type, amber primary action |
| Waiting area | empty tiled room with box benches | benches, chairs, lockers, briefing screen, bins, wet-floor sign, clock, alarm, cameras |
| Downrange / targets | grey corridor | fluorescent rows, baffles, wall-mounted duct, ricochet guards, lit target line |
| Pistol in hand | blob hand on a box pistol | P38-style photoscan in a smooth hand at the correct grip height |
| Rifle two-handed | box rifle | M4A1 with the right hand on the pistol grip and the left under the rail |
| SMG one/two-handed | box SMG | Micro Uzi, support hand ahead of the trigger guard, no overlap |

Visual issues found and fixed during the rounds: duct crossing signs/beam/baffles (moved to the right wall), firing-line sign
clash (moved to side walls), single-mesh props importing 100× too small or lying on their back (prop placement now composes on
the FBX root transform), briefing screen hiding the welcome sign, hand sitting too low on the pistol/rifle grips, SMG support hand
overlapping the primary hand, 1 M-triangle prop budget (decimated to ~110k).

What still needs eyes in a headset: stereo scale of the weapons and hands, finger clipping into grips at arm's length, lightmap
seams up close, UI distance/readability at real resolution, and the overall brightness on the Quest 2 display (the bake was
judged on a desktop monitor).

## 6. Build results
Both players were built in the cloud container from the final scene (with lightmaps) and are delivered as session files;
neither is committed (gitignored). Exact sizes and hashes are in `TEST_RESULTS.md` (Stage 4).

| Target | Configuration | Result | Checks |
|---|---|---|---|
| Meta Quest 2 — `VRShootingRange.apk` | Android ARM64, IL2CPP, Vulkan, OpenXR (Meta Quest feature + Oculus Touch profile), ASTC 6x6, min API 29, version 0.3.0 (code 3) | **succeeded**, 0 errors | `aapt dump badging`: package `com.universityproject.vrshootingrange`, `arm64-v8a` only, `android.hardware.vr.headtracking`, OpenXR permissions; warnings are the 3 TextMeshPro IL2CPP size notes + a disk-space notice from the container; 2 obsolete-API compiler notices in the editor scripts were removed after the build |
| Windows PCVR — `VRShootingRange_PCVR_Windows.zip` | x64, Mono, Direct3D11 (D3D12 plug-in folder included by Unity), OpenXR loader with 7 interaction profiles, windowed 1920×1080, run-in-background, version 0.3.0 | **succeeded**, 0 errors, 2 warnings (obsolete-API notices in our editor scripts, removed afterwards) | zip contains `VRShootingRange.exe`, `UnityPlayer.dll`, `VRShootingRange_Data`, `MonoBleedingEdge`, `README_PCVR.txt`; debug-symbol folders excluded; the Windows Build Support module was unpacked from Unity's official installer and mounted into the Linux editor |

Compilation alone was not treated as proof of correctness: every build followed a green test round on the same project state, and the
spectator/OpenXR configuration is covered by the EditMode build-configuration tests.

## 7. Performance (what can honestly be said)
- Container measurements (4 CPU cores, software GL, editor Play mode): sustained SMG fire averaged 0.8 ms of CPU frame time with one ~0.9 s spike (shader/asset warm-up on the first burst); 20 mode restarts and 6 s of fire changed the managed heap by < 1 MB after GC; pools stayed bounded (projectiles, casings, decals, particles, audio).
- Static budgets: ~203k triangles unculled (≈ 60k in the shooter's typical view), 15 realtime/mixed lights (per-vertex on Quest except the main directional), 73 materials, no dynamic rigidbodies at rest, all textures ASTC 6x6 ≤ 1K on Android.
- **Not measured:** GPU frame time on Quest 2 (72 FPS target) and on an RTX 4070 Super at 90 Hz. The project is configured for those targets (MSAA 4x, single-pass instanced, baked GI, per-platform quality levels) but the numbers must come from the headset (OVR Metrics Tool / Virtual Desktop overlay) — see `LOCAL_HANDOFF.md` §7.

## 8. Remaining issues
| # | Severity | Issue | Status / plan |
|---|---|---|---|
| R1 | **High (unverified)** | No headset run: controller feel, hand/grip alignment in stereo, comfort, audio spatialisation and frame rate (72 / 90 FPS) are unmeasured | Blocking for "production-ready"; checklist in `LOCAL_HANDOFF.md` §7 (Quest) and §5b (PCVR) |
| R2 | Medium | Hands read as smooth "mittens" when fully wrapped around a grip (fingers merge visually); no per-weapon finger-contact IK | Pose presets are tunable (`Assets/Settings/HandPoses`); proper finger IK is out of scope |
| R3 | Medium | Micro Uzi is a CC0 untextured mesh with generated steel textures; it is the least detailed of the three weapons | Swap for a textured CC0/CC-BY SMG if one becomes available; layout entry is data-only |
| R4 | Medium | Lightmaps were judged on screenshots only; seams/light leaks up close and the Quest display brightness need a headset check | Re-bake locally (GPU lightmapper) if needed; settings in `RangeSceneBuilder.ConfigureLightingAndAmbient` |
| R5 | Low | Quest 2 additional lights are per-vertex: dynamic objects (weapons, hands, casings) get point-light shading per vertex | Baked GI + probes carry most of the look; switch `URP-Quest2` to per-pixel with max 2 lights if the headset budget allows |
| R6 | Low | `Rebuild Everything` regenerates the scene without lightmaps | Documented; re-bake takes minutes |
| R7 | Low | Spectator camera hotkeys need a keyboard focus on the desktop window; no in-headset toggle | By design for OBS workflows |
| R8 | Low | Windows build is Mono (not IL2CPP): fine for PCVR, slightly slower scripting than IL2CPP | Build IL2CPP locally on Windows if desired (module + toolchain) |
| R9 | Low | In-container frame-time spike (~0.9 s) on the first automatic burst is shader/asset warm-up in the editor | Build players warm shaders at load; verify on device |
| R10 | Info | Props use one PBR material each (Poly Haven single-set assets only); multi-material assets (e.g. the fire extinguisher) were replaced by procedural stand-ins | — |

No known **critical** issues. No known reproducible functional bug remains in the automated scope.

## 9. Release readiness assessment
- **Code and content:** ready. Compiles clean, 97 automated tests pass, prefab/scene integrity verified, both players build, assets are CC0/OFL/Unity-licensed with credits.
- **Not production-ready yet** because the two high-severity items are *unverified*, not failed: no session on a Meta Quest 2 (standalone APK) and no session on a PC headset through Virtual Desktop / SteamVR. Frame-rate targets (72 FPS Quest 2, 90 FPS PCVR) are therefore unproven.
- **Recommendation:** treat this as a *release candidate for device acceptance*. Run the headset checklists in `LOCAL_HANDOFF.md`, record results in `TEST_RESULTS.md`, and fix whatever the headset reveals (most likely tuning: grip offsets, hand poses, UI distance, lightmap brightness). Nothing in the automated results suggests a blocking defect.
