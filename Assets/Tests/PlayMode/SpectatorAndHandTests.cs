using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Recording;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Spectator/recording camera behaviour, the per-platform graphics switch, and hand animation/alignment with scripted input.</summary>
    public class SpectatorAndHandTests
    {
        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        static SpectatorCamera Spectator() => Object.FindFirstObjectByType<SpectatorCamera>(FindObjectsInactive.Include);

        [UnityTest]
        public IEnumerator SpectatorCameraIsDesktopOnlyAndSeparateFromTheHeadsetCamera()
        {
            var spec = Spectator();
            Assert.IsNotNull(spec, "SpectatorCamera missing from the scene");
            Assert.IsFalse(SpectatorCamera.SupportedOnPlatform(RuntimePlatform.Android), "never active on Quest");
            Assert.IsTrue(SpectatorCamera.SupportedOnPlatform(RuntimePlatform.WindowsPlayer));
            Assert.IsTrue(spec.IsSupported, "supported in the editor");
            var cam = spec.Camera; var main = Camera.main;
            Assert.IsNotNull(cam); Assert.IsNotNull(main);
            Assert.AreNotSame(cam, main, "spectator is not the XR camera");
            Assert.AreSame(main.transform, spec.Head, "it follows the XR camera");
            Assert.AreEqual(StereoTargetEyeMask.None, cam.stereoTargetEye, "renders to the desktop only");
            Assert.AreEqual(0, cam.targetDisplay);
            Assert.IsFalse(cam.CompareTag("MainCamera"));
            Assert.IsNull(cam.GetComponent<AudioListener>(), "one audio listener (on the headset)");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            Assert.IsFalse(cam.GetUniversalAdditionalCameraData().allowXRRendering);
            int headsetOnly = LayerMask.NameToLayer("HeadsetOnly");
            Assert.GreaterOrEqual(headsetOnly, 0, "HeadsetOnly layer exists");
            Assert.AreEqual(0, cam.cullingMask & (1 << headsetOnly), "spectator culls headset-only objects");
            var vignette = main.transform.Find("TunnelingVignette");
            if (vignette != null) Assert.AreEqual(headsetOnly, vignette.gameObject.layer, "vignette is headset-only");
            Assert.Greater(cam.depth, main.depth, "draws over the mirror view");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpectatorModesFollowSmoothlyAndCanBeTurnedOff()
        {
            var spec = Spectator(); var cam = spec.Camera; var head = spec.Head;
            head.SetPositionAndRotation(new Vector3(0f, 1.6f, -1f), Quaternion.Euler(0f, 30f, 0f));
            spec.SetMode(SpectatorMode.FirstPerson); yield return null;
            Assert.IsTrue(cam.enabled);
            Assert.Less(Vector3.Distance(cam.transform.position, head.position), 0.01f, "first person starts on the head");
            head.position += new Vector3(0.5f, 0f, 0f);
            yield return null;
            float d1 = Vector3.Distance(cam.transform.position, head.position);
            Assert.Greater(d1, 0.001f, "smoothing: not teleported");
            yield return null; yield return null;
            float d2 = Vector3.Distance(cam.transform.position, head.position);
            Assert.Less(d2, d1, "converging on the head");
            yield return XrTestRig.WaitSeconds(1.0f);
            Assert.Less(Vector3.Distance(cam.transform.position, head.position), 0.01f, "settled");

            spec.SetMode(SpectatorMode.ThirdPerson); yield return null;
            var target = spec.TargetPose();
            var expected = SpectatorMath.ThirdPersonPose(head.position, head.rotation, spec.ThirdPersonOffset, 4f);
            Assert.Less(Vector3.Distance(target.position, expected.position), 1e-4f);
            Assert.Greater(Vector3.Distance(cam.transform.position, head.position), 0.5f, "third person sits away from the head");
            head.rotation = Quaternion.Euler(60f, 30f, 20f); // pitch/roll of the head must not tilt the third-person camera
            yield return XrTestRig.WaitSeconds(1.5f);
            Assert.Less(Mathf.Abs(Vector3.Dot(cam.transform.right, Vector3.up)), 0.05f, "no roll");
            spec.SetThirdPersonPreset(1); Assert.AreEqual(1, spec.ThirdPersonPreset); spec.SetThirdPersonPreset(-1); Assert.AreEqual(3, spec.ThirdPersonPreset);

            spec.SetFixedPose(new Vector3(2f, 1.7f, -2f), new Vector3(0f, 1.3f, 2f));
            spec.SetMode(SpectatorMode.Fixed); yield return null;
            Assert.Less(Vector3.Distance(cam.transform.position, new Vector3(2f, 1.7f, -2f)), 1e-3f, "fixed tripod snaps");
            head.position += Vector3.forward; yield return null; yield return null;
            Assert.Less(Vector3.Distance(cam.transform.position, new Vector3(2f, 1.7f, -2f)), 1e-3f, "fixed camera ignores head motion");

            spec.FieldOfView = 200f; Assert.AreEqual(110f, cam.fieldOfView, "FOV clamped");
            spec.RenderEveryNthFrame = 2; yield return null;
            bool a = cam.enabled; yield return null; bool b = cam.enabled;
            Assert.AreNotEqual(a, b, "half-rate rendering alternates frames");
            spec.RenderEveryNthFrame = 1;
            spec.SetMode(SpectatorMode.Off); yield return null;
            Assert.IsFalse(cam.enabled, "off disables the desktop cameras");
            Assert.AreEqual(SpectatorMode.Off, spec.Mode);
            spec.SetMode(SpectatorMode.FirstPerson);
        }

        [Test]
        public void LetterboxMathCoversAllWindowShapes()
        {
            var r = SpectatorMath.LetterboxRect(1920, 1080, 16f / 9f); Assert.AreEqual(new Rect(0, 0, 1, 1), r);
            r = SpectatorMath.LetterboxRect(1920, 1200, 16f / 9f); Assert.AreEqual(1f, r.width, 1e-4f); Assert.AreEqual(0.9f, r.height, 1e-3f); Assert.AreEqual(0.05f, r.y, 1e-3f);
            r = SpectatorMath.LetterboxRect(2560, 1080, 16f / 9f); Assert.AreEqual(1f, r.height, 1e-4f); Assert.AreEqual(0.75f, r.width, 1e-3f); Assert.AreEqual(0.125f, r.x, 1e-3f);
            r = SpectatorMath.LetterboxRect(0, 0, 16f / 9f); Assert.AreEqual(new Rect(0, 0, 1, 1), r);
            Assert.AreEqual(1f, SpectatorMath.SmoothFactor(0f, 0.016f)); Assert.That(SpectatorMath.SmoothFactor(10f, 0.1f), Is.InRange(0.6f, 0.65f));
            var yaw = SpectatorMath.YawOnly(Quaternion.Euler(45f, 90f, 30f)).eulerAngles; Assert.AreEqual(0f, yaw.x, 1e-3f); Assert.AreEqual(90f, yaw.y, 1e-3f); Assert.AreEqual(0f, yaw.z, 1e-3f);
            var p = SpectatorMath.ThirdPersonPose(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(0f, 0.5f, -2f), 4f);
            Assert.Less(Vector3.Distance(p.position, new Vector3(-2f, 2.1f, 0f)), 1e-3f, "offset rotated into the head yaw frame");
            Assert.Greater(Vector3.Dot(p.rotation * Vector3.forward, Vector3.right), 0.9f, "looks where the player looks");
        }

        [UnityTest]
        public IEnumerator PlatformGraphicsSwitchTogglesPcvrFeatures()
        {
            var gfx = Object.FindFirstObjectByType<PlatformGraphicsSetup>();
            Assert.IsNotNull(gfx, "PlatformGraphicsSetup missing");
            Assert.IsFalse(PlatformGraphicsSetup.IsPcvrPlatform(RuntimePlatform.Android)); Assert.IsTrue(PlatformGraphicsSetup.IsPcvrPlatform(RuntimePlatform.WindowsPlayer));
            var volume = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(v => v.name.Contains("PCVR"));
            Assert.IsNotNull(volume, "PCVR post-process volume missing"); Assert.IsNotNull(volume.sharedProfile, "post profile missing");
            Assert.IsTrue(volume.sharedProfile.Has<Tonemapping>() && volume.sharedProfile.Has<Bloom>(), "profile has tonemapping + bloom");
            var main = Camera.main.GetUniversalAdditionalCameraData();
            gfx.Apply(false);
            Assert.IsFalse(volume.enabled); Assert.IsFalse(main.renderPostProcessing, "Quest: no post-processing");
            gfx.Apply(true);
            Assert.IsTrue(volume.enabled); Assert.IsTrue(main.renderPostProcessing, "PCVR: post-processing on the XR camera");
            Assert.AreEqual(AntialiasingMode.None, main.antialiasing, "MSAA only; no post AA in VR");
            var shadowLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Point && l.shadows == LightShadows.Soft).ToArray();
            Assert.AreEqual(2, shadowLights.Length, "PCVR enables soft shadows on the two firing-line fill lights");
            gfx.Apply(false);
            Assert.AreEqual(0, Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.type == LightType.Point && l.shadows != LightShadows.None), "Quest: point lights cast no shadows");
            gfx.Apply(PlatformGraphicsSetup.IsPcvrPlatform(Application.platform));
            yield return null;
        }

        static HandPresence[] ActivateHands()
        {
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var h in hands) for (var t = h.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            return hands;
        }

        [UnityTest]
        public IEnumerator FreeHandsFollowTheControllerAndAnimateWithGripAndTrigger()
        {
            var hands = ActivateHands(); yield return null; yield return null;
            Assert.AreEqual(2, hands.Length);
            foreach (var h in hands)
            {
                Assert.IsTrue(h.IsCalibrated);
                h.SetInputOverride(0f, 0f);
                yield return XrTestRig.WaitSeconds(0.5f);
                float idleMiddle = h.Curl(HandPresence.Finger.Middle), idleIndex = h.Curl(HandPresence.Finger.Index);
                Assert.Less(idleMiddle, 0.5f, "relaxed hand is mostly open");
                var controller = h.transform.parent;
                Assert.Less(Vector3.Distance(h.PalmWorldPosition, controller.position), 0.12f, "free hand sits on the controller");
                controller.position += new Vector3(0.2f, 0.1f, 0f);
                yield return XrTestRig.WaitSeconds(0.5f);
                Assert.Less(Vector3.Distance(h.PalmWorldPosition, controller.position), 0.12f, "hand followed the controller move");
                h.SetInputOverride(1f, 0f);
                yield return XrTestRig.WaitSeconds(0.6f);
                Assert.Greater(h.Curl(HandPresence.Finger.Middle), idleMiddle + 0.3f, "grip closes the fingers");
                Assert.Greater(h.Curl(HandPresence.Finger.Little), 0.7f);
                h.SetInputOverride(0f, 1f);
                yield return XrTestRig.WaitSeconds(0.6f);
                Assert.Greater(h.Curl(HandPresence.Finger.Index), idleIndex + 0.3f, "trigger curls the index finger");
                Assert.Less(h.Curl(HandPresence.Finger.Middle), 0.6f, "trigger alone does not make a fist");
                // smoothness: no instantaneous jumps between frames
                h.SetInputOverride(1f, 1f);
                float last = h.Curl(HandPresence.Finger.Middle); float maxStep = 0f;
                for (int i = 0; i < 20; i++) { yield return null; float c = h.Curl(HandPresence.Finger.Middle); maxStep = Mathf.Max(maxStep, Mathf.Abs(c - last)); last = c; }
                Assert.Less(maxStep, 0.7f, "curl changes are blended over several frames, not snapped in one");
                h.SetInputOverride(null, null);
            }
        }

        [UnityTest]
        public IEnumerator HandsSnapToWeaponGripAnchorsAndTransitionBetweenOneAndTwoHands()
        {
            var hands = ActivateHands(); yield return null; yield return null;
            var right = hands.First(h => !h.IsLeft); var left = hands.First(h => h.IsLeft);
            var rc = right.transform.parent; var lc = left.transform.parent;
            rc.SetPositionAndRotation(new Vector3(0.2f, 1.3f, -1f), Quaternion.identity); lc.SetPositionAndRotation(new Vector3(-0.2f, 1.3f, -1f), Quaternion.identity);
            var rInteractor = rc.GetComponentInChildren<XRBaseInputInteractor>(true); var lInteractor = lc.GetComponentInChildren<XRBaseInputInteractor>(true);
            Assert.IsNotNull(rInteractor, "rig right interactor"); Assert.IsNotNull(lInteractor, "rig left interactor");
            var rifle = XrTestRig.Find(WeaponClass.Rifle); var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var manager = XrTestRig.Manager;

            manager.SelectEnter((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)pistol.Grab);
            yield return XrTestRig.WaitSeconds(0.6f);
            Assert.AreSame(pistol, right.HeldWeapon); Assert.IsNotNull(right.CurrentAnchor); Assert.AreEqual(GripRole.Primary, right.CurrentAnchor.role);
            var palmPose = right.CurrentAnchor.GetPalmPose(false);
            Assert.Less(Vector3.Distance(right.PalmWorldPosition, palmPose.position), 0.025f, "right palm sits on the pistol grip anchor");
            Assert.Greater(right.Curl(HandPresence.Finger.Middle), 0.7f, "fingers wrap the grip");
            Assert.Less(right.Curl(HandPresence.Finger.Index), 0.6f, "index rests along the frame until the trigger is pulled");
            right.SetInputOverride(null, 1f);
            yield return XrTestRig.WaitSeconds(0.5f);
            Assert.Greater(right.Curl(HandPresence.Finger.Index), 0.7f, "trigger finger follows the trigger while holding");
            right.SetInputOverride(null, null);
            Assert.IsNull(left.HeldWeapon, "the other hand is unaffected");
            manager.SelectExit((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)pistol.Grab);
            yield return XrTestRig.WaitSeconds(0.5f);
            Assert.IsNull(right.HeldWeapon); Assert.Less(Vector3.Distance(right.PalmWorldPosition, rc.position), 0.12f, "released hand returns to the controller");

            // rifle: primary right, support left; the support anchor is a different grip
            manager.SelectEnter((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)rifle.Grab);
            yield return XrTestRig.WaitSeconds(0.5f);
            lc.position = rc.position + new Vector3(-0.06f, 0.05f, 0.32f);
            yield return null;
            manager.SelectEnter((IXRSelectInteractor)lInteractor, (IXRSelectInteractable)rifle.Grab);
            yield return XrTestRig.WaitSeconds(0.7f);
            Assert.IsTrue(rifle.IsTwoHanded); Assert.AreSame(rifle, left.HeldWeapon); Assert.AreSame(rifle, right.HeldWeapon);
            Assert.AreEqual(GripRole.Primary, right.CurrentAnchor.role); Assert.AreEqual(GripRole.Support, left.CurrentAnchor.role);
            Assert.Less(Vector3.Distance(left.PalmWorldPosition, left.CurrentAnchor.GetPalmPose(true).position), 0.03f, "left palm on the support anchor");
            Assert.Less(Vector3.Distance(right.PalmWorldPosition, right.CurrentAnchor.GetPalmPose(false).position), 0.03f, "right palm on the primary anchor");
            Assert.Greater(Vector3.Distance(left.PalmWorldPosition, right.PalmWorldPosition), 0.15f, "hands are on different grips (no overlap)");
            // release the support hand: smooth transition (no large per-frame jumps of the weapon or hand)
            var lastPalm = right.PalmWorldPosition; float maxJump = 0f;
            manager.SelectExit((IXRSelectInteractor)lInteractor, (IXRSelectInteractable)rifle.Grab);
            for (int i = 0; i < 30; i++) { yield return null; maxJump = Mathf.Max(maxJump, Vector3.Distance(lastPalm, right.PalmWorldPosition)); lastPalm = right.PalmWorldPosition; }
            Assert.IsFalse(rifle.IsTwoHanded); Assert.IsNull(left.HeldWeapon); Assert.AreSame(rifle, right.HeldWeapon);
            Assert.Less(maxJump, 0.06f, "primary hand stays smoothly on its grip through the transition");
        }
    }
}
