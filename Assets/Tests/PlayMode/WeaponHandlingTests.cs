using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Drives weapons with scripted XRI interactors (no headset): one-handed sag/lag, two-handed two-point aiming,
    /// grip transitions, grip-dependent recoil and hand presence. Headset feel remains a manual test.
    /// </summary>
    public class WeaponHandlingTests
    {
        XRInteractionManager m_Manager;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            var load = SceneManager.LoadSceneAsync("ShootingRange", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            yield return null;
            m_Manager = Object.FindFirstObjectByType<XRInteractionManager>();
            Assert.IsNotNull(m_Manager);
        }

        XRDirectInteractor MakeHand(string name, Vector3 pos, Quaternion rot, InteractorHandedness handedness)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, rot);
            var col = go.AddComponent<SphereCollider>(); col.isTrigger = true; col.radius = 0.05f;
            var hand = go.AddComponent<XRDirectInteractor>();
            hand.handedness = handedness;
            hand.interactionManager = m_Manager;
            return hand;
        }

        Weapon Find(WeaponClass c) => Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition.weaponClass == c);

        IEnumerator Grab(XRDirectInteractor hand, Weapon weapon)
        {
            m_Manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)weapon.Grab);
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator AllWeaponsHaveHandlingProfilesAndAnchors()
        {
            foreach (var w in Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None))
            {
                Assert.IsNotNull(w.Handling, $"{w.DisplayName} has no WeaponHandlingTransformer");
                Assert.IsNotNull(w.GetGripAnchorFor(null), $"{w.DisplayName} has no primary grip anchor");
                Assert.Greater(w.Definition.virtualMassKg, 0f);
            }
            var rifle = Find(WeaponClass.Rifle); var pistol = Find(WeaponClass.Pistol);
            Assert.Greater(rifle.Definition.virtualMassKg, pistol.Definition.virtualMassKg);
            Assert.Greater(rifle.Definition.oneHandSagDegrees, pistol.Definition.oneHandSagDegrees);
            Assert.Greater(rifle.Definition.oneHandResponseSeconds, pistol.Definition.oneHandResponseSeconds, "rifle must have more inertia");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OneHandedRifleSagsAndFollowsWithLag()
        {
            var rifle = Find(WeaponClass.Rifle);
            var handPos = new Vector3(0f, 1.4f, -1f);
            var hand = MakeHand("RightHand", handPos, Quaternion.identity, InteractorHandedness.Right);
            yield return Grab(hand, rifle);
            yield return new WaitForSeconds(0.6f); // settle
            Assert.AreEqual(1, rifle.HandCount);
            Assert.IsFalse(rifle.IsTwoHanded);
            // weight sag: the barrel points slightly below the hand's forward
            float pitch = Vector3.SignedAngle(hand.transform.forward, rifle.Muzzle.forward, hand.transform.right);
            Assert.Greater(pitch, 1.5f, $"expected noticeable muzzle sag for the one-handed rifle, got {pitch:0.00} deg");
            Assert.Less(pitch, 12f);
            // grip stays on the hand (lag clamped)
            float gripDist = Vector3.Distance(rifle.Handling.PrimaryGrip.position, hand.transform.position);
            Assert.Less(gripDist, rifle.Definition.maxPositionLagMetres + 0.005f);
            // sudden move: the weapon trails the hand, then catches up
            hand.transform.position += new Vector3(0.3f, 0f, 0f);
            yield return null; yield return null;
            float trailing = Vector3.Distance(rifle.Handling.PrimaryGrip.position, hand.transform.position);
            Assert.Greater(trailing, 0.004f, "heavy rifle should trail a sudden hand move");
            Assert.LessOrEqual(trailing, rifle.Definition.maxPositionLagMetres + 0.005f, "but never further than the clamp");
            yield return new WaitForSeconds(0.8f);
            Assert.Less(Vector3.Distance(rifle.Handling.PrimaryGrip.position, hand.transform.position), 0.006f, "settles back onto the hand");
            m_Manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)rifle.Grab);
        }

        [UnityTest]
        public IEnumerator PistolIsMoreResponsiveThanRifle()
        {
            var pistol = Find(WeaponClass.Pistol); var rifle = Find(WeaponClass.Rifle);
            var h1 = MakeHand("R", new Vector3(-1f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Right);
            var h2 = MakeHand("L", new Vector3(1f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Left);
            yield return Grab(h1, pistol); yield return Grab(h2, rifle);
            yield return new WaitForSeconds(0.5f);
            h1.transform.position += new Vector3(0f, 0f, 0.25f); h2.transform.position += new Vector3(0f, 0f, 0.25f);
            yield return null; yield return null;
            float pistolTrail = Vector3.Distance(pistol.Handling.PrimaryGrip.position, h1.transform.position);
            float rifleTrail = Vector3.Distance(rifle.Handling.PrimaryGrip.position, h2.transform.position);
            Assert.LessOrEqual(pistolTrail, rifleTrail + 1e-4f, $"pistol trail {pistolTrail:0.000} should not exceed rifle trail {rifleTrail:0.000}");
            float pistolSag = Vector3.SignedAngle(h1.transform.forward, pistol.Muzzle.forward, h1.transform.right);
            float rifleSag = Vector3.SignedAngle(h2.transform.forward, rifle.Muzzle.forward, h2.transform.right);
            Assert.Less(pistolSag, rifleSag, "pistol sags less than the rifle");
        }

        [UnityTest]
        public IEnumerator TwoHandedGripAimsAlongBothHandsAndReleasesSmoothly()
        {
            var rifle = Find(WeaponClass.Rifle);
            var primary = MakeHand("Primary", new Vector3(0f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Right);
            yield return Grab(primary, rifle);
            yield return new WaitForSeconds(0.4f);
            // support hand up and to the left: the weapon must pitch up and yaw left to pass through it
            var supportPos = primary.transform.position + new Vector3(-0.12f, 0.12f, 0.33f);
            var support = MakeHand("Support", supportPos, Quaternion.identity, InteractorHandedness.Left);
            yield return Grab(support, rifle);
            Assert.IsTrue(rifle.IsTwoHanded); Assert.AreEqual(2, rifle.HandCount);
            yield return new WaitForSeconds(0.6f); // blend in
            Assert.Greater(rifle.Handling.TwoHandBlend, 0.95f);
            var gripToSupport = rifle.Handling.SupportGrip.position - rifle.Handling.PrimaryGrip.position;
            var handsDir = support.transform.position - primary.transform.position;
            float err = Vector3.Angle(gripToSupport, handsDir);
            Assert.Less(err, 6f, $"two-point aim error {err:0.0} deg (support influence 0.85 + sag/sway allowed)");
            // the support grip sits close to the support hand
            Assert.Less(Vector3.Distance(rifle.Handling.SupportGrip.position, support.transform.position), 0.08f);
            // release the support hand: smooth transition back, weapon stays on the primary hand throughout
            var rotBefore = rifle.transform.rotation;
            m_Manager.SelectExit((IXRSelectInteractor)support, (IXRSelectInteractable)rifle.Grab);
            float maxStep = 0f; var last = rifle.transform.rotation;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                maxStep = Mathf.Max(maxStep, Quaternion.Angle(last, rifle.transform.rotation)); last = rifle.transform.rotation;
                Assert.Less(Vector3.Distance(rifle.Handling.PrimaryGrip.position, primary.transform.position), 0.04f);
            }
            Assert.IsFalse(rifle.IsTwoHanded);
            Assert.Less(maxStep, 15f, "no snapping when the support hand releases");
            Assert.Greater(Quaternion.Angle(rotBefore, rifle.transform.rotation), 1f, "weapon returns toward one-handed orientation");
        }

        [UnityTest]
        public IEnumerator RecoilIsLargerUnsupportedThanSupported()
        {
            var rifle = Find(WeaponClass.Rifle);
            var primary = MakeHand("Primary", new Vector3(0f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Right);
            yield return Grab(primary, rifle);
            yield return new WaitForSeconds(0.5f);
            float Measure()
            {
                var before = rifle.transform.rotation;
                rifle.Handling.AddRecoilImpulse(0.04f, 6f, 0f);
                return 0f; // measured on the next frames by the caller
            }
            Measure(); yield return null; yield return null;
            var afterOne = rifle.transform.rotation;
            yield return new WaitForSeconds(1.0f);
            var restOne = rifle.transform.rotation;
            float kickOne = Quaternion.Angle(restOne, afterOne);

            var support = MakeHand("Support", primary.transform.position + new Vector3(0f, 0.06f, 0.33f), Quaternion.identity, InteractorHandedness.Left);
            yield return Grab(support, rifle);
            yield return new WaitForSeconds(0.6f);
            Measure(); yield return null; yield return null;
            var afterTwo = rifle.transform.rotation;
            yield return new WaitForSeconds(1.0f);
            float kickTwo = Quaternion.Angle(rifle.transform.rotation, afterTwo);
            Assert.Greater(kickOne, kickTwo * 1.3f, $"unsupported kick {kickOne:0.00} should clearly exceed supported kick {kickTwo:0.00}");
            Assert.Greater(kickOne, 1f);
        }

        [UnityTest]
        public IEnumerator HandsAreVisibleCalibratedAndFollowGrips()
        {
            // Without tracked devices the XRI modality manager keeps the controller objects inactive; activate them for the test.
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(2, hands.Length, "expected a left and a right HandPresence on the rig");
            foreach (var h in hands) for (var t = h.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            yield return null; yield return null;
            Assert.IsTrue(hands.Any(h => h.IsLeft) && hands.Any(h => !h.IsLeft));
            foreach (var h in hands)
            {
                Assert.IsTrue(h.IsCalibrated, "hand rig joints not found");
                var smr = h.HandRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.IsNotNull(smr, "hand has no skinned mesh"); Assert.IsTrue(smr.enabled);
                // curl calibration: from a fully open hand, a half curl (~95 deg over three joints) must move the index tip
                // toward the palm side. (A full fist would bring the tip back near the palm plane, so we compare open vs half.)
                h.OverridePose(HandPose.Create("TestOpen", 0f, 0f, 0f, 0f, 0f, 0.5f, false), 3f);
                yield return new WaitForSeconds(0.6f);
                Assert.IsTrue(h.TryGetTip(HandPresence.Finger.Index, out var tipOpen));
                var palm = h.PalmWorldPosition; var inward = h.PalmInwardWorld;
                float handLen = Vector3.Distance(palm, tipOpen);
                Assert.That(handLen, Is.InRange(0.05f, 0.25f), $"hand model scale looks wrong (palm to index tip {handLen:0.000} m)");
                float openDot = Vector3.Dot(tipOpen - palm, inward);
                h.OverridePose(HandPose.Create("TestHalf", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.3f, false), 3f);
                yield return new WaitForSeconds(0.6f);
                Assert.IsTrue(h.TryGetTip(HandPresence.Finger.Index, out var tipClosed));
                float closedDot = Vector3.Dot(tipClosed - h.PalmWorldPosition, h.PalmInwardWorld);
                Assert.Greater(closedDot, openDot + 0.02f, $"{(h.IsLeft ? "left" : "right")} index tip should move toward the palm when curling (open {openDot:0.000}, half {closedDot:0.000})");
                h.OverridePose(null, 0f);
            }
            // weapon grip anchors are valid for both hands
            var rifle = Find(WeaponClass.Rifle);
            var anchor = rifle.GetGripAnchorFor(null);
            var right = anchor.GetPalmPose(false); var left = anchor.GetPalmPose(true);
            Assert.Less(Vector3.Distance(right.position, left.position), 0.1f);
            Assert.IsNotNull(anchor.pose);
        }
    }
}
