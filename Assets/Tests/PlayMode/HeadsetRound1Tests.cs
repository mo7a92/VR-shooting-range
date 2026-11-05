using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Modes;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Headset feedback round 1 (2026-11-04). Every interaction here goes through the rig's own NearFarInteractors (the
    /// interactors the headset uses), never scripted direct interactors, and every wait is time-based so uncapped local
    /// frame rates and the slow container behave the same.
    /// 1. real muzzle velocities never tunnel through a thin target; 2. the rifle held by the real interactor sags, lags
    /// and sways; 3. rapid fire accumulates muzzle climb, less when supported; 4. the pistol's second hand supports and
    /// takes the weapon over on release; 5. fingers wrap every grip without penetrating it (+ review screenshots).
    /// </summary>
    public class HeadsetRound1Tests
    {
        Transform m_RightController, m_LeftController;
        NearFarInteractor m_Right, m_Left;
        readonly List<GameObject> m_Temp = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return XrTestRig.LoadScene();
            // The controllers are the parents of the rig's NearFarInteractors (never looked up by name: other objects share the
            // name and FindObjectsByType's order is not stable between runs).
            foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (nf.handedness == InteractorHandedness.Left) { m_Left = nf; m_LeftController = nf.transform.parent; }
                else if (nf.handedness == InteractorHandedness.Right) { m_Right = nf; m_RightController = nf.transform.parent; }
            }
            Assert.IsNotNull(m_Right, "rig right NearFarInteractor"); Assert.IsNotNull(m_Left, "rig left NearFarInteractor");
            Assert.AreEqual("Right Controller", m_RightController.name); Assert.AreEqual("Left Controller", m_LeftController.name);
            Activate(m_RightController); Activate(m_LeftController);
            yield return null; yield return null; // PlayerRigSetup.Start (caster masks, far attach mode), HandPresence.OnEnable
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
        static float Pitch(Transform hand, Weapon w) => Vector3.SignedAngle(hand.forward, w.Muzzle.forward, hand.right); // + = muzzle down relative to the hand
        IEnumerator Grab(NearFarInteractor nf, Weapon w) { XrTestRig.Manager.SelectEnter((IXRSelectInteractor)nf, (IXRSelectInteractable)w.Grab); yield return null; }
        void Release(NearFarInteractor nf, Weapon w) { if (w.Grab.interactorsSelecting.Contains(nf)) XrTestRig.Manager.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)w.Grab); }
        static HandPresence Hand(bool left) => Object.FindObjectsByType<HandPresence>(FindObjectsSortMode.None).First(h => h.IsLeft == left);

        class HitCounter : MonoBehaviour, IHitReceiver { public int hits; public HitInfo last; public void OnProjectileHit(in HitInfo hit) { hits++; last = hit; } }

        // ------------------------------------------------------------------ 1. ballistics
        [UnityTest]
        public IEnumerator RealMuzzleVelocitiesHitAOneCentimetreTargetAt25MetresWithoutTunnelling()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var expected = new Dictionary<WeaponClass, (float v, float rpm)> { { WeaponClass.Pistol, (360f, 360f) }, { WeaponClass.Rifle, (885f, 800f) }, { WeaponClass.SubmachineGun, (350f, 1200f) }, { WeaponClass.AssaultRifle, (715f, 600f) }, { WeaponClass.SniperRifle, (830f, 60f) } };
            foreach (var w in XrTestRig.AllWeapons())
            {
                var def = w.Definition;
                Assert.AreEqual(expected[def.weaponClass].v, def.muzzleVelocity, 1f, $"{w.DisplayName}: real muzzle velocity");
                Assert.AreEqual(expected[def.weaponClass].rpm, def.roundsPerMinute, 1f, $"{w.DisplayName}: cyclic rate");
                Assert.AreEqual(1f, def.gravityMultiplier, 1e-3f, $"{w.DisplayName}: real gravity");
                // 1 cm thick plate 25 m downrange on the free x = 0.45 m path (clears the lane target faces)
                var plate = new GameObject($"ThinPlate_{w.DisplayName}"); m_Temp.Add(plate);
                plate.transform.position = new Vector3(0.45f, 1.4f, 25f);
                var box = plate.AddComponent<BoxCollider>(); box.size = new Vector3(0.6f, 0.6f, 0.01f);
                var counter = plate.AddComponent<HitCounter>();
                Physics.SyncTransforms();
                var origin = new Vector3(0.45f, 1.4f, 0f);
                var p = new ProjectileParams { speed = def.muzzleVelocity, gravityMultiplier = def.gravityMultiplier, lifetime = def.projectileLifetime, tracerColor = def.tracerColor };
                for (int shot = 0; shot < 3; shot++)
                {
                    int before = counter.hits;
                    var proj = ProjectilePool.Instance.Fire(origin, Vector3.forward, p, w);
                    Assert.IsNotNull(proj);
                    yield return XrTestRig.WaitUntil(() => counter.hits > before || !proj.IsActive, 1.5f, $"{w.DisplayName} shot {shot} to land");
                    Assert.AreEqual(before + 1, counter.hits, $"{w.DisplayName} shot {shot} at {def.muzzleVelocity} m/s must hit the 1 cm plate at 25 m (frame step {Time.deltaTime * def.muzzleVelocity:0.0} m)");
                    Assert.AreEqual(25f, counter.last.travelDistance, 0.2f, "reported travel distance");
                    Assert.Less(Mathf.Abs(counter.last.point.y - 1.4f), 0.03f, "drop at 25 m is a few millimetres at real velocities");
                }
                Object.Destroy(plate);
                yield return null;
            }
        }

        // ------------------------------------------------------------------ 2. weight under the real interactor
        [UnityTest]
        public IEnumerator RifleHeldByTheRigsNearFarInteractorSagsLagsAndSwaysOneHanded()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle); var pistol = XrTestRig.Find(WeaponClass.Pistol); var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            var attach = m_Right.GetAttachTransform(rifle.Grab);
            yield return Grab(m_Right, rifle);
            yield return XrTestRig.WaitSeconds(1.2f);
            Assert.AreSame(m_Right, rifle.Handling.PrimaryInteractor, "the handling transformer runs for the rig's NearFarInteractor");
            Assert.AreEqual(1, rifle.HandCount); Assert.IsFalse(rifle.IsTwoHanded);
            float sag = Pitch(attach, rifle);
            Assert.Greater(sag, 15f, $"one-handed rifle droops clearly (measured {sag:0.0} deg, profile {rifle.Definition.oneHandSagDegrees} deg)");
            Assert.Less(sag, 35f, "clamped so it stays usable");
            Assert.Greater(rifle.Handling.CurrentSagDegrees, 15f);
            // sway: the muzzle direction keeps moving while the hand is perfectly still
            float minPitch = float.MaxValue, maxPitch = float.MinValue, minYaw = float.MaxValue, maxYaw = float.MinValue;
            for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
            {
                float p = Pitch(attach, rifle), y = Vector3.SignedAngle(attach.forward, rifle.Muzzle.forward, attach.up);
                minPitch = Mathf.Min(minPitch, p); maxPitch = Mathf.Max(maxPitch, p); minYaw = Mathf.Min(minYaw, y); maxYaw = Mathf.Max(maxYaw, y);
                yield return null;
            }
            Assert.Greater(Mathf.Max(maxPitch - minPitch, maxYaw - minYaw), 0.3f, $"visible sway (pitch range {maxPitch - minPitch:0.00}, yaw range {maxYaw - minYaw:0.00} deg)");
            // inertia: a sudden 30 cm controller move leaves the grip trailing the controller, then it settles back. Measured
            // against the controller (not the interactor's attach anchor): XRI's InteractionAttachController stabilises the
            // anchor itself (position stabilisation 0.25), so what the player feels is that smoothing plus the weapon's own lag.
            // The move is applied to the XR Origin (the controller transform belongs to the rig's tracked pose driver, which
            // can write it back every frame in a multi-test session); the hand, its attach anchor and the weapon all move with it.
            var attachOffset = m_RightController.InverseTransformPoint(attach.position);
            var origin = m_RightController.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform;
            origin.position += new Vector3(0.3f, 0f, 0f);
            float trailing = 0f; int frames = 0;
            for (float t = 0f; t < 0.35f || frames < 5; t += Time.unscaledDeltaTime, frames++)
            {
                yield return null;
                trailing = Mathf.Max(trailing, Mathf.Max(Vector3.Distance(rifle.Handling.PrimaryGrip.position, m_RightController.TransformPoint(attachOffset)), rifle.Handling.CurrentLagMetres));
            }
            Assert.Greater(trailing, 0.01f, $"heavy rifle trails a sudden hand move (peak {trailing * 100f:0.0} cm behind the controller)");
            Assert.LessOrEqual(trailing, 0.31f, "never further than the move itself");
            yield return XrTestRig.WaitSeconds(2.0f);
            Assert.Less(Vector3.Distance(rifle.Handling.PrimaryGrip.position, m_RightController.TransformPoint(attachOffset)), 0.008f, "settles back onto the hand");
            Release(m_Right, rifle);
            yield return XrTestRig.WaitSeconds(0.3f);

            // the other weapons have weight too, ordered rifle > SMG > pistol
            yield return Grab(m_Right, smg); yield return XrTestRig.WaitSeconds(1.2f);
            float smgSag = Pitch(m_Right.GetAttachTransform(smg.Grab), smg);
            Release(m_Right, smg); yield return XrTestRig.WaitSeconds(0.3f);
            yield return Grab(m_Right, pistol); yield return XrTestRig.WaitSeconds(1.2f);
            float pistolSag = Pitch(m_Right.GetAttachTransform(pistol.Grab), pistol);
            Assert.Greater(pistolSag, 0.5f, $"the pistol is light but not weightless ({pistolSag:0.00} deg)");
            Assert.Less(pistolSag, 5f, "and stays quick to aim");
            Assert.Greater(smgSag, pistolSag, $"SMG ({smgSag:0.0}) sags more than the pistol ({pistolSag:0.0})");
            Assert.Greater(sag, smgSag, $"rifle ({sag:0.0}) sags more than the SMG ({smgSag:0.0})");
            var pAttach = m_Right.GetAttachTransform(pistol.Grab); var pOffset = m_RightController.InverseTransformPoint(pAttach.position);
            origin.position += new Vector3(0.3f, 0f, 0f);
            float pistolTrail = 0f; frames = 0;
            for (float t = 0f; t < 0.35f || frames < 5; t += Time.unscaledDeltaTime, frames++) { yield return null; pistolTrail = Mathf.Max(pistolTrail, Mathf.Max(Vector3.Distance(pistol.Handling.PrimaryGrip.position, m_RightController.TransformPoint(pOffset)), pistol.Handling.CurrentLagMetres)); }
            Assert.Less(pistolTrail, trailing, $"the pistol trails a sudden move less than the rifle ({pistolTrail * 100f:0.0} vs {trailing * 100f:0.0} cm)");
            Release(m_Right, pistol);
        }

        // ------------------------------------------------------------------ 3. recoil
        [UnityTest]
        public IEnumerator RapidRifleFireAccumulatesMuzzleClimbAndTheSupportHandReducesIt()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var attach = m_Right.GetAttachTransform(rifle.Grab);
            yield return Grab(m_Right, rifle);
            yield return XrTestRig.WaitSeconds(1.0f);
            float dtShot = rifle.Definition.SecondsBetweenShots;

            IEnumerator Burst(int shots, System.Action<float> peak)
            {
                float maxPitch = 0f; float maxMuzzleRise = 0f; var fwdBefore = rifle.Muzzle.forward; rifle.RefillInstant();
                for (int i = 0; i < shots; i++)
                {
                    Assert.IsTrue(rifle.TryFire(), $"shot {i} fired");
                    yield return null;
                    maxPitch = Mathf.Max(maxPitch, rifle.Handling.RecoilPitch);
                    maxMuzzleRise = Mathf.Max(maxMuzzleRise, Vector3.SignedAngle(fwdBefore, rifle.Muzzle.forward, -attach.right));
                    if (i < shots - 1) yield return XrTestRig.WaitSeconds(dtShot + 0.005f);
                }
                Debug.Log($"[VRRange][Test] burst of {shots}: peak recoil pitch {maxPitch:0.0} deg, muzzle rose {maxMuzzleRise:0.0} deg");
                peak(Mathf.Max(maxPitch, maxMuzzleRise));
            }

            float one = 0f, ten = 0f, tenSupported = 0f;
            yield return Burst(1, v => one = v);
            yield return XrTestRig.WaitSeconds(2.5f);
            Assert.Less(rifle.Handling.RecoilPitch, 0.8f, "recoil recovers toward the aim point once the trigger rests");
            yield return Burst(10, v => ten = v);
            Assert.Greater(one, 2f, $"a single one-handed rifle shot kicks visibly ({one:0.0} deg)");
            Assert.Greater(ten, one * 2f, $"10 rapid shots climb far more than one ({ten:0.0} vs {one:0.0} deg): recoil accumulates");
            Assert.Greater(ten, 10f, "one-handed auto fire wanders mostly off target");
            Assert.LessOrEqual(ten, rifle.Definition.maxRecoilPitchDegrees + 8f, "but the clamp holds");
            yield return XrTestRig.WaitSeconds(3f);
            Assert.Less(rifle.Handling.RecoilPitch, 1f, "recovers after the burst");

            // support hand on the handguard: much steadier
            m_LeftController.position = m_RightController.position + new Vector3(-0.06f, 0.05f, 0.32f);
            yield return null;
            yield return Grab(m_Left, rifle);
            yield return XrTestRig.WaitSeconds(1.0f);
            Assert.IsTrue(rifle.IsTwoHanded); Assert.Greater(rifle.Handling.TwoHandBlend, 0.95f);
            yield return Burst(10, v => tenSupported = v);
            Assert.Less(tenSupported, ten * 0.5f, $"supported burst climbs less than half ({tenSupported:0.0} vs {ten:0.0} deg)");
            Assert.Greater(tenSupported, 0.5f, "but recoil is still felt");
            Release(m_Left, rifle); Release(m_Right, rifle);
        }

        // ------------------------------------------------------------------ 4. pistol support hand + hand-over
        [UnityTest]
        public IEnumerator PistolSecondHandSupportsAndTakesOverWhenTheShootingHandLetsGo()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var right = Hand(false); var left = Hand(true);
            yield return Grab(m_Right, pistol);
            yield return XrTestRig.WaitSeconds(0.8f);
            Assert.AreSame(m_Right, pistol.PrimaryHandInteractor);
            var attachR = m_Right.GetAttachTransform(pistol.Grab);
            float sagBefore = pistol.Handling.CurrentSagDegrees;
            // second hand close to the shooting hand: it supports instead of taking the pistol
            m_LeftController.position = m_RightController.position + new Vector3(-0.05f, -0.01f, 0.0f);
            yield return null;
            yield return Grab(m_Left, pistol);
            yield return XrTestRig.WaitSeconds(0.8f);
            Assert.AreEqual(2, pistol.HandCount, "both hands on the pistol");
            Assert.AreSame(m_Right, pistol.PrimaryHandInteractor, "the shooting hand keeps the pistol");
            Assert.IsTrue(pistol.IsTwoHanded); Assert.Greater(pistol.Handling.TwoHandBlend, 0.9f, "handling treats it as supported");
            Assert.AreEqual(GripRole.Primary, right.CurrentAnchor.role); Assert.AreEqual(GripRole.Support, left.CurrentAnchor.role);
            Assert.AreSame(pistol, left.HeldWeapon);
            // Round 2: palm centres are bone-line points one measured skin thickness (~22 mm per hand) outside the contact, so the
            // centre-to-centre distance grew by ~4.5 cm (round 1 measured ~11 cm; round 2 ~15 cm). The skin-level contact is
            // checked by HandPenetrationTests (support palm vs the shooting hand's finger capsules).
            float handsApart = Vector3.Distance(left.PalmWorldPosition, right.PalmWorldPosition);
            Assert.That(handsApart, Is.InRange(0.03f, 0.19f), $"support palm cups the shooting hand ({handsApart * 100f:0.0} cm apart, bone line to bone line)");
            // the pistol stays on the shooting hand's grip (no two-point aim on a pistol)
            Assert.Less(Vector3.Distance(pistol.Handling.PrimaryGrip.position, attachR.position), 0.02f, "grip stays in the shooting hand");
            Assert.Less(Mathf.Abs(Pitch(attachR, pistol)), sagBefore + 1f, "support hand does not pull the pistol around");
            // only the shooting hand fires
            int rounds = pistol.Rounds;
            XrTestRig.PullTrigger(m_Left, pistol); XrTestRig.ReleaseTrigger(m_Left, pistol); yield return null;
            Assert.AreEqual(rounds, pistol.Rounds, "the support hand's trigger does nothing");
            XrTestRig.PullTrigger(m_Right, pistol); XrTestRig.ReleaseTrigger(m_Right, pistol); yield return null;
            Assert.AreEqual(rounds - 1, pistol.Rounds, "the shooting hand fires");
            // hand-over: the shooting hand lets go; the pistol glides into the support hand without dropping or snapping
            var attachL = m_Left.GetAttachTransform(pistol.Grab);
            var lastPos = pistol.transform.position; float maxJump = 0f; float elapsed = 0f;
            Release(m_Right, pistol);
            while (elapsed < 0.8f)
            {
                yield return null; elapsed += Time.unscaledDeltaTime;
                Assert.IsTrue(pistol.IsHeld, "pistol never drops during the hand-over");
                Assert.AreSame(m_Left, pistol.PrimaryHandInteractor, "the support hand owns the pistol");
                maxJump = Mathf.Max(maxJump, Vector3.Distance(lastPos, pistol.transform.position) / Mathf.Max(Time.unscaledDeltaTime, 1e-3f));
                lastPos = pistol.transform.position;
            }
            Assert.Less(maxJump, 2.5f, $"no visible snap: peak speed {maxJump:0.00} m/s during the hand-over");
            Assert.AreEqual(1, pistol.HandCount); Assert.IsFalse(pistol.IsTwoHanded);
            // round 2: a left shooting hand holds the mirrored grip point (palm on the pistol's -X side), so compare the grip as used
            Assert.Less(Vector3.Distance(pistol.Handling.PrimaryGripWorld, attachL.position), 0.02f, "grip settled in the new shooting hand");
            Assert.AreEqual(GripRole.Primary, left.CurrentAnchor.role, "the left hand now uses the primary grip pose");
            Assert.IsNull(right.HeldWeapon);
            XrTestRig.PullTrigger(m_Left, pistol); XrTestRig.ReleaseTrigger(m_Left, pistol); yield return null;
            Assert.AreEqual(rounds - 2, pistol.Rounds, "the new shooting hand fires");
            Release(m_Left, pistol); yield return null;
            Assert.IsFalse(pistol.IsHeld);
        }

        // ------------------------------------------------------------------ 5. hands on grips
        [UnityTest]
        public IEnumerator FingersWrapEveryGripWithoutPenetratingItAndTheThumbOpposesTheFingers()
        {
            bool canRender = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            var shotDir = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(shotDir);
            var probeGo = new GameObject("TipProbe"); m_Temp.Add(probeGo); var probe = probeGo.AddComponent<SphereCollider>(); probe.radius = 0.001f; probe.isTrigger = true;
            var supportOffset = new Dictionary<WeaponClass, Vector3> { { WeaponClass.Pistol, new Vector3(-0.05f, -0.01f, 0f) }, { WeaponClass.Rifle, new Vector3(-0.06f, 0.05f, 0.32f) }, { WeaponClass.SubmachineGun, new Vector3(-0.05f, 0.04f, 0.12f) } };
            var right = Hand(false); var left = Hand(true);
            var report = new System.Text.StringBuilder();
            var allFailures = new List<string>();
            foreach (var cls in new[] { WeaponClass.Pistol, WeaponClass.Rifle, WeaponClass.SubmachineGun })
            {
                var w = XrTestRig.Find(cls);
                m_RightController.SetPositionAndRotation(new Vector3(0.2f, 1.3f, -1.0f), Quaternion.identity);
                yield return Grab(m_Right, w);
                yield return XrTestRig.WaitSeconds(0.6f);
                m_LeftController.position = m_RightController.position + supportOffset[cls];
                yield return null;
                yield return Grab(m_Left, w);
                yield return XrTestRig.WaitSeconds(1.5f); // follow + contact solve settle
                Assert.AreEqual(2, w.HandCount, $"{w.DisplayName}: both hands on the weapon");
                if (canRender)
                {
                    foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) smr.forceMatrixRecalculationPerRender = true;
                    yield return null;
                    foreach (var h in new[] { right, left }) Shot(shotDir, $"round1_{w.DisplayName.ToLowerInvariant()}_{(h.IsLeft ? "support" : "primary")}", h, w);
                }
                var failures = new List<string>();
                foreach (var h in new[] { right, left })
                {
                    string tag = $"{w.DisplayName} {(h.IsLeft ? "L/support" : "R/primary")}";
                    Assert.AreSame(w, h.HeldWeapon, tag); Assert.IsNotNull(h.CurrentAnchor, tag);
                    var grip = h.CurrentAnchor.gripCollider;
                    Assert.IsNotNull(grip, $"{tag}: anchor has a grip collider");
                    // palm outside the grip volume
                    // round 3: the palm centre (bone line) must lie outside every contact volume the hand rests on (weapon hulls and, for the
                    // support hand, the shooting hand); the coarse support box around the shooting hand is no longer where the support palm sits
                    float palmGap = float.MaxValue;
                    foreach (var sh in h.ContactShapes) palmGap = Mathf.Min(palmGap, HandPresence.SignedDistance(sh, h.PalmWorldPosition));
                    report.AppendLine($"{tag}: palm centre {palmGap * 1000f:0} mm from the nearest contact surface");
                    if (palmGap <= 0.004f) failures.Add($"{tag}: palm centre inside/at a contact surface ({palmGap * 1000f:0} mm)");
                    int touching = 0;
                    foreach (var f in new[] { HandPresence.Finger.Thumb, HandPresence.Finger.Middle, HandPresence.Finger.Ring, HandPresence.Finger.Little })
                    {
                        Assert.IsTrue(h.TryGetTip(f, out var tip), $"{tag}: {f} tip");
                        bool inside = Physics.ComputePenetration(probe, tip, Quaternion.identity, grip, grip.transform.position, grip.transform.rotation, out _, out float dist);
                        float depth = inside ? dist - probe.radius : 0f;
                        float gap = inside ? 0f : Vector3.Distance(tip, grip.ClosestPoint(tip));
                        report.AppendLine($"{tag} {f}: grasp {(h.GraspActive(f) ? "on" : "off")} curl {h.Curl(f):0.00} tip {(inside ? $"{depth * 1000f:0.0} mm inside" : $"{gap * 1000f:0.0} mm outside")}");
                        if (depth > 0.005f) failures.Add($"{tag}: {f} tip {depth * 1000f:0.0} mm inside the grip collider (max 5 mm)");
                        if (h.GraspActive(f)) touching++;
                    }
                    if (touching < 2) failures.Add($"{tag}: only {touching} finger(s) reach the grip (contact solve)");
                    h.TryGetTip(HandPresence.Finger.Thumb, out var thumbTip); h.TryGetTip(HandPresence.Finger.Middle, out var middleTip);
                    var pl = grip.transform.InverseTransformPoint(h.PalmWorldPosition); var tl = grip.transform.InverseTransformPoint(thumbTip); var ml = grip.transform.InverseTransformPoint(middleTip);
                    report.AppendLine($"{tag}: grip-local mm: palm {pl * 1000f}, thumb tip {tl * 1000f}, middle tip {ml * 1000f}");
                    if (!h.IsLeft)
                    {
                        // shooting hand: thumb lies along the frame on the palm side, fingers wrap round to the other side
                        if (tl.x * ml.x >= 0f) failures.Add($"{tag}: thumb ({tl.x * 1000f:0} mm) and middle finger ({ml.x * 1000f:0} mm) are on the same side of the grip");
                        if (Mathf.Sign(tl.x) != Mathf.Sign(pl.x)) failures.Add($"{tag}: thumb ({tl.x * 1000f:0} mm) is not on the palm side ({pl.x * 1000f:0} mm)");
                    }
                    else if (cls != WeaponClass.Pistol)
                    {
                        // support hand under a handguard/receiver: fingers wrap over one side, the thumb points forward along it
                        if (tl.z < pl.z) failures.Add($"{tag}: support thumb points backward (thumb z {tl.z * 1000f:0} mm < palm z {pl.z * 1000f:0} mm)");
                    }
                    if (h.IsLeft && cls == WeaponClass.Rifle && h.PalmWorldPosition.y >= grip.bounds.center.y) failures.Add("rifle support hand does not hold the handguard from below");
                }
                Debug.Log($"[VRRange][Test] {w.DisplayName} grip report:\n{report}");
                report.Clear();
                Release(m_Left, w); Release(m_Right, w);
                yield return XrTestRig.WaitSeconds(0.4f);
                foreach (var f in failures) allFailures.Add($"{w.DisplayName}: {f}");
            }
            Assert.IsEmpty(allFailures, string.Join("\n", allFailures));
        }

        /// <summary>Views of one hand on its grip along the weapon's axes: palm side, finger side, front, and the shooter's view (review aid; GPU runs only).</summary>
        static void Shot(string dir, string name, HandPresence h, Weapon w)
        {
            var go = new GameObject("GripReviewCamera"); var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.fieldOfView = 34f; cam.tag = "Untagged";
            var rt = new RenderTexture(960, 640, 24); cam.targetTexture = rt;
            var palm = h.PalmWorldPosition;
            var focus = Vector3.Lerp(palm, h.CurrentAnchor != null && h.CurrentAnchor.gripCollider != null ? h.CurrentAnchor.gripCollider.bounds.center : palm, 0.6f);
            var right = w.transform.right; var fwd = w.transform.forward; var up = w.transform.up;
            // the palm is on the +x side of the weapon for the right hand and -x for the left hand
            float side = Vector3.Dot(palm - focus, right) >= 0f ? 1f : -1f;
            var views = new[]
            {
                ("palmside", focus + right * (side * 0.36f) + up * 0.06f, Vector3.up),
                ("fingerside", focus - right * (side * 0.36f) + up * 0.06f, Vector3.up),
                ("front", focus + fwd * 0.36f + up * 0.08f, Vector3.up),
                ("shooter", focus - fwd * 0.32f + up * 0.22f, Vector3.up),
            };
            foreach (var (label, pos, camUp) in views)
            {
                cam.transform.position = pos; cam.transform.LookAt(focus, camUp);
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
