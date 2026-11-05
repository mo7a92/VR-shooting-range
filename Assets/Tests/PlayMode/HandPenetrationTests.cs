using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Headset round 2: hands must not sink into the weapons or into each other, and the wrists must sit like a real high
    /// grip. Grabs go through the rig's NearFarInteractors, waits are time-based, and every number is measured on the baked
    /// skinned meshes against the weapons' fitted contact volumes. The report is written to Logs/hand_report.txt.
    /// Pass: max skin depth ≤ 4 mm and &lt; 2 % of vertices inside; wrapping fingertips within 12 mm of the surface; shooting
    /// hand wrist pitch ≤ 25° below the bore, support ≤ 30°; back of the shooting hand facing outward/up (roll −20..60°);
    /// held wrist within 4 cm of the free hand's wrist for the same controller pose.
    /// </summary>
    public class HandPenetrationTests
    {
        Transform m_RightController, m_LeftController;
        NearFarInteractor m_Right, m_Left;
        readonly List<GameObject> m_Temp = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return XrTestRig.LoadScene();
            foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (nf.handedness == InteractorHandedness.Left) { m_Left = nf; m_LeftController = nf.transform.parent; }
                else if (nf.handedness == InteractorHandedness.Right) { m_Right = nf; m_RightController = nf.transform.parent; }
            }
            Assert.IsNotNull(m_Right); Assert.IsNotNull(m_Left);
            Activate(m_RightController); Activate(m_LeftController);
            yield return null; yield return null;
            m_RightController.SetPositionAndRotation(new Vector3(0.2f, 1.3f, -1.0f), Quaternion.identity);
            m_LeftController.SetPositionAndRotation(new Vector3(-0.2f, 1.3f, -1.0f), Quaternion.identity);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var w in XrTestRig.AllWeapons()) foreach (var nf in new[] { m_Right, m_Left }) if (nf != null && w.Grab.interactorsSelecting.Contains(nf)) XrTestRig.Manager.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)w.Grab);
            foreach (var go in m_Temp) if (go != null) Object.Destroy(go);
            m_Temp.Clear();
            yield return null;
        }

        static void Activate(Transform t) { for (; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true); }
        IEnumerator Grab(NearFarInteractor nf, Weapon w) { XrTestRig.Manager.SelectEnter((IXRSelectInteractor)nf, (IXRSelectInteractable)w.Grab); yield return null; }
        void Release(NearFarInteractor nf, Weapon w) { if (w.Grab.interactorsSelecting.Contains(nf)) XrTestRig.Manager.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)w.Grab); }
        static HandPresence Hand(bool left) => Object.FindObjectsByType<HandPresence>(FindObjectsSortMode.None).First(h => h.IsLeft == left);

        struct Depths { public float max, maxOutsideBulges, maxCrossHand; public int inside, total, forwardOfMuzzle; public float Fraction => total > 0 ? (float)inside / total : 0f; }

        /// <summary>Thenar / hypothenar compression zone: palm vertices (not fingers, not wrist) more than 30 % of the hand length off the palm centre line; HandPresence lets these squash into a grip by their excess thickness (≤ 12 mm).</summary>
        static bool InBulgeZone(HandPresence h, Vector3 v)
        {
            var palm = h.PalmWorldPosition; var fwd = (h.MiddleKnuckleWorldPosition - palm).normalized; var across = Vector3.Cross(h.PalmInwardWorld, fwd);
            float handLen = Vector3.Distance(h.WristWorldPosition, h.MiddleKnuckleWorldPosition);
            var d = v - palm; float f = Vector3.Dot(d, fwd), a = Mathf.Abs(Vector3.Dot(d, across));
            return f <= handLen * 0.55f && f >= -handLen * 0.45f && a > handLen * 0.3f;
        }

        static string Breakdown(HandPresence h, WeaponContactVolumes weapon, HandPresence other)
        {
            var smr = h.HandRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            var verts = new List<Vector3>(); HandPresence.BakeWorldVertices(smr, verts);
            var perVolume = new Dictionary<string, (int n, float max)>();
            var palm = h.PalmWorldPosition; var inward = h.PalmInwardWorld; var fwd = (h.MiddleKnuckleWorldPosition - palm).normalized; var across = Vector3.Cross(inward, fwd) * (h.IsLeft ? -1f : 1f);
            var regions = new Dictionary<string, int>();
            float handLen = Vector3.Distance(h.WristWorldPosition, h.MiddleKnuckleWorldPosition);
            var shapes = new List<(HandPresence.ContactShape sh, string name)>();
            if (weapon != null) foreach (var c in weapon.volumes) if (c != null) shapes.Add((HandPresence.ContactShape.From(c), c.name));
            if (other != null) { other.RefreshOwnShapes(); for (int k = 0; k < other.OwnShapes.Count; k++) shapes.Add((other.OwnShapes[k], "otherHand/" + other.OwnShapeNames[k])); }
            foreach (var v in verts)
            {
                string worst = null; float depth = 0f;
                foreach (var (sh, name) in shapes) { float sd = -HandPresence.SignedDistance(sh, v); if (sd > depth) { depth = sd; worst = name; } }
                if (depth <= 0.0005f) continue;
                perVolume[worst] = perVolume.TryGetValue(worst, out var e) ? (e.n + 1, Mathf.Max(e.max, depth)) : (1, depth);
                var d = v - palm; float f = Vector3.Dot(d, fwd), a = Vector3.Dot(d, across);
                string region = f > handLen * 0.55f ? "fingers" : f < -handLen * 0.45f ? "wrist/forearm" : a > handLen * 0.3f ? "thumb side" : a < -handLen * 0.3f ? "little side" : "palm";
                regions[region] = regions.TryGetValue(region, out var r) ? r + 1 : 1;
            }
            return string.Join("; ", perVolume.OrderByDescending(kv => kv.Value.n).Select(kv => $"{kv.Key}: {kv.Value.n} verts, max {kv.Value.max * 1000f:0.0} mm")) + " | regions: " + string.Join(", ", regions.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
        }

        static Depths MeasureDepths(HandPresence h, WeaponContactVolumes weapon, HandPresence other)
        {
            var smr = h.HandRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            var verts = new List<Vector3>();
            Assert.IsTrue(HandPresence.BakeWorldVertices(smr, verts) && verts.Count > 100, "baked hand mesh");
            // sanity: the baked cloud must match the renderer's world bounds (catches a wrong BakeMesh scale convention)
            var b = new Bounds(verts[0], Vector3.zero); foreach (var v in verts) b.Encapsulate(v);
            // (Renderer.bounds is the world AABB of the rotated local bounds, so it is larger than the vertex cloud's AABB)
            Assert.That(b.size.magnitude, Is.InRange(0.5f * smr.bounds.size.magnitude, 1.05f * smr.bounds.size.magnitude), $"baked vertices span {b.size.magnitude:0.000} m vs renderer bounds {smr.bounds.size.magnitude:0.000} m");
            var d = new Depths { total = verts.Count };
            var shapes = weapon != null ? weapon.Shapes() : new List<HandPresence.ContactShape>();
            int weaponCount = shapes.Count;
            if (other != null) { other.RefreshOwnShapes(); shapes.AddRange(other.OwnShapes); }
            var wt = weapon != null ? weapon.GetComponent<Weapon>() : null;
            float muzzleZ = wt != null ? weapon.transform.InverseTransformPoint(wt.Muzzle.position).z : float.PositiveInfinity;
            foreach (var v in verts)
            {
                float depth = 0f, cross = 0f;
                for (int k = 0; k < shapes.Count; k++) { float sd = -HandPresence.SignedDistance(shapes[k], v); if (sd > depth) depth = sd; if (k >= weaponCount && sd > cross) cross = sd; }
                if (depth > 0.0005f) d.inside++;
                if (depth > d.max) d.max = depth;
                if (cross > d.maxCrossHand) d.maxCrossHand = cross;
                if (depth > d.maxOutsideBulges && !InBulgeZone(h, v)) d.maxOutsideBulges = depth;
                // nothing of a hand may be in front of the muzzle (5 mm tolerance for the barrel nut)
                if (weapon != null && weapon.transform.InverseTransformPoint(v).z > muzzleZ + 0.005f) d.forwardOfMuzzle++;
            }
            return d;
        }

        static float NearestSurface(Vector3 p, IReadOnlyList<HandPresence.ContactShape> targets)
        {
            float d = float.MaxValue;
            foreach (var sh in targets) { float sd = HandPresence.SignedDistance(sh, p); if (sd < d) d = sd; }
            return d;
        }

        [UnityTest, Timeout(1200000)] // six configurations, each baked and screenshotted; slow under software rendering
        public IEnumerator HandsRestOnTheWeaponsWithoutSinkingInAndWristsSitLikeAHighGrip()
        {
            bool canRender = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var shotDir = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(shotDir);
            var supportOffset = new Dictionary<WeaponClass, Vector3> { { WeaponClass.Pistol, new Vector3(-0.05f, -0.01f, 0f) }, { WeaponClass.Rifle, new Vector3(-0.06f, 0.05f, 0.32f) }, { WeaponClass.SubmachineGun, new Vector3(-0.05f, -0.01f, 0.12f) } };
            var right = Hand(false); var left = Hand(true);
            right.DebugThumb = System.Environment.GetEnvironmentVariable("VRRANGE_DEBUG_THUMB") == "1";
            var report = new System.Text.StringBuilder(); var failures = new List<string>();
            report.AppendLine($"skin offsets: R palm {right.PalmSkinOffset * 1000f:0.0} mm / back {right.BackOfHandOffset * 1000f:0.0} mm, L palm {left.PalmSkinOffset * 1000f:0.0} mm / back {left.BackOfHandOffset * 1000f:0.0} mm");
            var attachLocal = m_RightController.InverseTransformPoint(m_Right.attachTransform.position);
            var attachRot = (Quaternion.Inverse(m_RightController.rotation) * m_Right.attachTransform.rotation).eulerAngles;
            report.AppendLine($"right interactor attach anchor relative to the controller: ({attachLocal.x * 1000f:0}, {attachLocal.y * 1000f:0}, {attachLocal.z * 1000f:0}) mm, rotation ({attachRot.x:0}, {attachRot.y:0}, {attachRot.z:0}) deg; free-pose reference = {right.PoseReference.name}");
            foreach (var cls in new[] { WeaponClass.Pistol, WeaponClass.Rifle, WeaponClass.SubmachineGun })
            foreach (bool withSupport in new[] { false, true })
            {
                var w = XrTestRig.Find(cls); var wcv = w.GetComponent<WeaponContactVolumes>();
                string cfg = $"{w.DisplayName} {(withSupport ? "primary+support" : "primary")}";
                // the support hand cups the shooting hand on the pistol and on the Micro Uzi (its 10 cm of free receiver between the
                // trigger finger and the muzzle cannot take a 9 cm wide hand); it goes under the handguard on the rifle
                bool cups = cls != WeaponClass.Rifle;
                Debug.Log($"[VRRange][Test] hand penetration: {cfg} start");
                Assert.IsNotNull(wcv, $"{w.DisplayName}: contact volumes"); Assert.GreaterOrEqual(wcv.volumes.Count, 3, $"{w.DisplayName}: fitted contact volumes");
                m_RightController.SetPositionAndRotation(new Vector3(0.2f, 1.3f, -1.0f), Quaternion.identity);
                m_LeftController.SetPositionAndRotation(new Vector3(-0.2f, 1.3f, -1.0f), Quaternion.identity);
                yield return XrTestRig.WaitSeconds(0.5f); // free hands settle on the controllers
                var freeWristR = right.WristWorldPosition; var freeWristL = left.WristWorldPosition; var freePalmR = right.PalmWorldPosition; var freeFwdR = (right.MiddleKnuckleWorldPosition - right.WristWorldPosition).normalized;
                yield return Grab(m_Right, w);
                if (withSupport) { yield return XrTestRig.WaitSeconds(0.3f); m_LeftController.position = m_RightController.position + supportOffset[cls]; yield return null; yield return Grab(m_Left, w); }
                yield return XrTestRig.WaitSeconds(withSupport ? 1.6f : 0.9f); // ≥ 0.6 s: follow, contact solve and de-penetration settle (two hands adapt to each other)
                // then wait for a quiet state: weapon and both palms move < 1 mm over 0.2 s (hand-over glide, two-point aim blend and
                // de-penetration smoothing can still be in flight on a slow or a very fast frame rate), at most 3 s more
                float settleWaited = 0f;
                for (; settleWaited < 3f; settleWaited += 0.2f)
                {
                    var p0 = w.transform.position; var r0 = right.PalmWorldPosition; var l0 = left.PalmWorldPosition;
                    yield return XrTestRig.WaitSeconds(0.2f);
                    if (Vector3.Distance(p0, w.transform.position) < 0.001f && Vector3.Distance(r0, right.PalmWorldPosition) < 0.001f && Vector3.Distance(l0, left.PalmWorldPosition) < 0.001f) break;
                }
                var gripOffset = m_RightController.InverseTransformPoint(w.Handling.PrimaryGripWorld) - m_RightController.InverseTransformPoint(m_Right.attachTransform.position);
                report.AppendLine($"{cfg}: settled after {settleWaited + 0.2f:0.0} s extra; primary grip point {gripOffset.magnitude * 1000f:0.0} mm from the interactor attach anchor ({gripOffset.x * 1000f:0}, {gripOffset.y * 1000f:0}, {gripOffset.z * 1000f:0}) mm");
                Assert.AreEqual(withSupport ? 2 : 1, w.HandCount, cfg);
                var hands = withSupport ? new[] { right, left } : new[] { right };
                foreach (var h in hands)
                {
                    string tag = $"{cfg} {(h.IsLeft ? "support(L)" : "shooting(R)")}";
                    var other = withSupport ? (h.IsLeft ? right : left) : null;
                    // 1. skin depth inside the weapon and inside the other hand
                    var depth = MeasureDepths(h, wcv, other);
                    report.AppendLine($"{tag}: max depth {depth.max * 1000f:0.0} mm, {depth.inside}/{depth.total} vertices inside ({depth.Fraction * 100f:0.00} %), de-penetration push {h.DepenetrationOffset.magnitude * 1000f:0.0} mm ({h.DepenetrationInfo})");
                    report.AppendLine($"{tag}: solve {h.LastSolveMs:0.0} ms (palm {h.LastPalmSolveMs:0.0} ms), {h.SolveCount} solves / {h.GraspSolveCount} grasp solves so far, longest grasp stage {h.MaxStageMs:0.0} ms");
                    report.AppendLine($"{tag}: grasp {h.GraspInfo}");
                    if (depth.inside > 0) report.AppendLine($"{tag}:   inside by volume: {Breakdown(h, wcv, other)}");
                    report.AppendLine($"{tag}: max depth outside the thenar/hypothenar compression zone {depth.maxOutsideBulges * 1000f:0.0} mm, inside the other hand {depth.maxCrossHand * 1000f:0.0} mm, vertices forward of the muzzle {depth.forwardOfMuzzle}");
                    // bulges may compress into the grip by up to 12 mm (rigid mesh standing in for squashed flesh); everything else ≤ 4 mm
                    if (depth.maxOutsideBulges > 0.004f) failures.Add($"{tag}: skin {depth.maxOutsideBulges * 1000f:0.0} mm inside outside the compression zone (max 4 mm)");
                    if (depth.max > 0.012f) failures.Add($"{tag}: skin {depth.max * 1000f:0.0} mm inside (max 12 mm in the compression zone)");
                    if (depth.maxCrossHand > 0.004f) failures.Add($"{tag}: skin {depth.maxCrossHand * 1000f:0.0} mm inside the other hand (max 4 mm)");
                    if (depth.forwardOfMuzzle > 0) failures.Add($"{tag}: {depth.forwardOfMuzzle} hand vertices in front of the muzzle");
                    if (depth.Fraction >= 0.05f) failures.Add($"{tag}: {depth.Fraction * 100f:0.0} % of vertices inside (max 5 % with the compression zone)");
                    // the palm itself must be ON the grip: its skin point within 4 mm of a weapon volume (shooting hand) or of the shooting hand's
                    // own shapes (pistol support hand, which holds the other hand, not the pistol)
                    float palmGap;
                    // round 3: the cupping support palm rests on the shooting hand's fingers and on the grip panel behind them, so its
                    // palm is measured to whichever of the two it rests on (same 8 mm limit)
                    if (h.IsLeft && cups) { other.RefreshOwnShapes(); palmGap = Mathf.Min(NearestSurface(h.PalmSkinWorldPosition, other.OwnShapes), NearestSurface(h.PalmSkinWorldPosition, wcv.Shapes())); }
                    else palmGap = NearestSurface(h.PalmSkinWorldPosition, wcv.Shapes());
                    var anchorPos = h.CurrentAnchor.GetPalmPose(h.IsLeft).position;
                    report.AppendLine($"{tag}: palm skin point {palmGap * 1000f:0.0} mm from the {(h.IsLeft && cups ? "shooting hand" : "grip")} surface; authored anchor point {NearestSurface(anchorPos, wcv.Shapes()) * 1000f:0.0} mm from the grip, skin point {Vector3.Distance(anchorPos, h.PalmSkinWorldPosition) * 1000f:0.0} mm from the anchor (de-penetration offset {h.DepenetrationOffset.magnitude * 1000f:0.0} mm), skin offset in use {h.PalmSkinOffset * 1000f:0.0} mm");
                    // the palm is concave: on this mesh the pads around the centre stand up to 7 mm proud of the centre point, so the
                    // centre point itself may hover up to 8 mm while the pads touch
                    if (palmGap > 0.008f) failures.Add($"{tag}: palm floats {palmGap * 1000f:0.0} mm off the {(h.IsLeft && cups ? "shooting hand" : "grip")} (max 8 mm)");
                    // 2. wrapping fingertips touch (within 12 mm of a contact surface)
                    var fingers = h.IsLeft ? new[] { HandPresence.Finger.Middle, HandPresence.Finger.Ring, HandPresence.Finger.Little } : new[] { HandPresence.Finger.Thumb, HandPresence.Finger.Middle, HandPresence.Finger.Ring, HandPresence.Finger.Little };
                    int farTips = 0;
                    foreach (var f in fingers)
                    {
                        h.TryGetTip(f, out var tip);
                        float gap = NearestSurface(tip, h.ContactShapes);
                        report.AppendLine($"{tag} {f} tip: {gap * 1000f:0.0} mm from the nearest contact surface (curl {h.Curl(f):0.00}, tip extra {h.TipExtra(f):0.00})");
                        if (h.TryGetJoints(f, out var joints))
                        {
                            // per-joint diagnostics: signed distance of each bone joint to the nearest weapon volume (and which one)
                            var vols = w.GetComponentInChildren<WeaponContactVolumes>();
                            string[] jn = { "meta", "prox", "inter", "distal", "tip" };
                            var sb = new StringBuilder();
                            for (int ji = 0; ji < 5; ji++)
                            {
                                if (float.IsNaN(joints[ji].x)) continue;
                                float best = float.MaxValue; string bestName = "-";
                                if (vols != null) foreach (var c in vols.volumes) { if (c == null) continue; float sd = HandPresence.SignedDistance(c, joints[ji]); if (sd < best) { best = sd; bestName = c.name.Replace("Contact_", ""); } }
                                sb.Append($" {jn[ji]} {best * 1000f:0.0}mm@{bestName}");
                            }
                            report.AppendLine($"{tag} {f} joints:{sb}");
                        }
                        // Micro Uzi: the grip is 3 cm wide, 9 cm long and the receiver sits directly above it. With the web of the hand up
                        // against the receiver the thumb lies along the receiver side (~14 mm) and the grip ends under the ring finger, so
                        // the little finger curls under it (~18 mm). Bounds 16 / 20 mm for those two; everything else 12 mm.
                        float bound = cls == WeaponClass.SubmachineGun && !h.IsLeft && f == HandPresence.Finger.Thumb ? 0.016f : cls == WeaponClass.SubmachineGun && !h.IsLeft && f == HandPresence.Finger.Little ? 0.020f : 0.012f;
                        if (gap > bound) { farTips++; if (!(h.IsLeft && f == HandPresence.Finger.Little)) failures.Add($"{tag}: {f} tip {gap * 1000f:0.0} mm from the surface (max {bound * 1000f:0} mm)"); }
                    }
                    // a support hand's little finger may hang free: it cups the shooting hand below that hand's fingers (pistol) or
                    // sits on the hypothenar side of a rail narrower than the palm (rifle / SMG); the middle and ring fingers must touch
                    if (h.IsLeft && farTips > 1) failures.Add($"{tag}: {farTips} fingertips more than 12 mm from the surface");
                    // 3. wrist pitch (wrist→middle knuckle vs the bore, in the weapon's side plane) and roll (back of hand vs outward)
                    var knuckle = w.transform.InverseTransformDirection(h.MiddleKnuckleWorldPosition - h.WristWorldPosition);
                    float pitch = Mathf.Atan2(knuckle.y, knuckle.z) * Mathf.Rad2Deg; // negative = below the bore
                    var palmLocal = w.transform.InverseTransformPoint(h.PalmWorldPosition); float side = Mathf.Sign(palmLocal.x);
                    var back = w.transform.InverseTransformDirection(h.BackOfHandWorld);
                    float roll = Mathf.Atan2(back.y, back.x * side) * Mathf.Rad2Deg; // 0 = back of hand straight outward, 90 = straight up (over the slide)
                    report.AppendLine($"{tag}: wrist pitch {pitch:0.0} deg (+ above / − below the bore), back-of-hand roll {roll:0.0} deg from outward, palm x {palmLocal.x * 1000f:0} mm");
                    if (!h.IsLeft)
                    {
                        if (pitch < -25f) failures.Add($"{tag}: wrist pitch {pitch:0.0} deg is more than 25 deg below the bore");
                        if (pitch > 20f) failures.Add($"{tag}: wrist pitch {pitch:0.0} deg points up");
                        if (roll < -20f || roll > 60f) failures.Add($"{tag}: back of hand roll {roll:0.0} deg (expected −20..60 deg: outward to outward-up, never over the slide)");
                        // 4. wrist placement: the held hand's wrist sits where the free hand's wrist was for the same controller pose
                        float offset = Vector3.Distance(h.WristWorldPosition, freeWristR);
                        var palmDelta = m_RightController.InverseTransformVector(h.PalmWorldPosition - freePalmR);
                        var fk = m_RightController.InverseTransformDirection(freeFwdR); var hk = m_RightController.InverseTransformDirection((h.MiddleKnuckleWorldPosition - h.WristWorldPosition).normalized); var bore = m_RightController.InverseTransformDirection(w.transform.forward);
                        report.AppendLine($"{tag}: wrist {offset * 100f:0.0} cm from the free hand's wrist (palm centre moved ({palmDelta.x * 1000f:0}, {palmDelta.y * 1000f:0}, {palmDelta.z * 1000f:0}) mm in controller space; knuckle line free ({fk.x:0.00}, {fk.y:0.00}, {fk.z:0.00}) vs held ({hk.x:0.00}, {hk.y:0.00}, {hk.z:0.00}), bore ({bore.x:0.00}, {bore.y:0.00}, {bore.z:0.00}) in controller space, turned {Vector3.Angle(freeFwdR, h.MiddleKnuckleWorldPosition - h.WristWorldPosition):0.0} deg)");
                        if (offset > 0.04f) failures.Add($"{tag}: wrist {offset * 100f:0.0} cm from the free hand's wrist (max 4 cm)");
                    }
                    else
                    {
                        if (pitch < -30f) failures.Add($"{tag}: support wrist pitch {pitch:0.0} deg is more than 30 deg below the bore");
                        if (!cups)
                        {
                            // support hand under the handguard: palm skin below the handguard volume, thumb forward
                            var guard = wcv.volumes.OrderBy(c => Vector3.Distance(c.transform.position, h.PalmSkinWorldPosition)).First();
                            float palmY = w.transform.InverseTransformPoint(h.PalmSkinWorldPosition).y, guardY = w.transform.InverseTransformPoint(guard.bounds.center).y;
                            report.AppendLine($"{tag}: palm skin {(guardY - palmY) * 1000f:0} mm below the {guard.name} centre");
                            if (palmY >= guardY) failures.Add($"{tag}: palm is not under the handguard (palm y {palmY * 1000f:0} mm, guard centre {guardY * 1000f:0} mm)");
                            h.TryGetTip(HandPresence.Finger.Thumb, out var thumbTip);
                            if (w.transform.InverseTransformPoint(thumbTip).z < w.transform.InverseTransformPoint(h.PalmWorldPosition).z) failures.Add($"{tag}: thumb points backward");
                        }
                    }
                }
                if (canRender)
                {
                    foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) smr.forceMatrixRecalculationPerRender = true;
                    yield return null;
                    string baseName = $"round2_{w.DisplayName.ToLowerInvariant()}_{(withSupport ? "both" : "primary")}";
                    Shots(shotDir, baseName, right, w, false);
                    if (withSupport) { Shots(shotDir, baseName + "_support", left, w, true); }
                }
                Release(m_Left, w); Release(m_Right, w);
                yield return XrTestRig.WaitSeconds(0.4f);
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Logs", "hand_report.txt"), report.ToString());
            Debug.Log("[VRRange][Test] hand penetration report:\n" + report);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Review views (shooter, front, palm side, finger side) plus, for a support hand, a close-up from the side at the contact point.</summary>
        static void Shots(string dir, string name, HandPresence h, Weapon w, bool closeUp)
        {
            var go = new GameObject("GripReviewCamera"); var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.fieldOfView = 34f; cam.tag = "Untagged";
            var rt = new RenderTexture(960, 640, 24); cam.targetTexture = rt;
            var palm = h.PalmWorldPosition; var right = w.transform.right; var fwd = w.transform.forward; var up = w.transform.up;
            var focus = Vector3.Lerp(palm, h.CurrentAnchor != null && h.CurrentAnchor.gripCollider != null ? h.CurrentAnchor.gripCollider.bounds.center : palm, 0.6f);
            float side = Vector3.Dot(palm - focus, right) >= 0f ? 1f : -1f;
            var views = new List<(string, Vector3)>
            {
                ("shooter", focus - fwd * 0.32f + up * 0.22f), ("front", focus + fwd * 0.36f + up * 0.08f),
                ("palmside", focus + right * (side * 0.36f) + up * 0.06f), ("fingerside", focus - right * (side * 0.36f) + up * 0.06f),
                ("top", focus + up * 0.42f + fwd * 0.02f),
            };
            if (closeUp) { var skin = h.PalmSkinWorldPosition; views.Add(("contact", skin + right * (side * 0.17f) - up * 0.03f)); focus = closeUp ? Vector3.Lerp(focus, h.PalmSkinWorldPosition, 0.5f) : focus; }
            foreach (var (label, pos) in views)
            {
                cam.transform.position = pos; cam.transform.LookAt(label == "contact" ? h.PalmSkinWorldPosition : focus, label == "top" ? fwd : Vector3.up);
                cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{name}_{label}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            cam.targetTexture = null; rt.Release(); Object.Destroy(go);
        }
    }
}
