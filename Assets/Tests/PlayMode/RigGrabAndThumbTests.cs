using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using VRRange.Core;
using VRRange.Hands;

namespace VRRange.Tests
{
    /// <summary>
    /// Regressions found on the headset (2026-11-04): the rig's real NearFarInteractor casters must see weapons (they live on
    /// the "Weapon" physics layer, which the XRI Starter Assets masks exclude), and the thumb must bend toward the fingers.
    /// Unlike the other suites these use the rig's own casters, not scripted XRDirectInteractors.
    /// </summary>
    public class RigGrabAndThumbTests
    {
        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return XrTestRig.LoadScene();
            // Without a headset the XRI input modality manager leaves the controller objects inactive; turn them on as a
            // tracked controller would.
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "Left Controller" || t.name == "Right Controller") t.gameObject.SetActive(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RigCastersIncludeTheWeaponLayer()
        {
            yield return null; // PlayerRigSetup.Start
            int weaponLayer = LayerMask.NameToLayer("Weapon");
            Assert.GreaterOrEqual(weaponLayer, 0, "Weapon layer exists");
            var nfs = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, nfs.Length, "two NearFarInteractors in the rig");
            foreach (var nf in nfs)
            {
                var near = nf.nearInteractionCaster as SphereInteractionCaster;
                var far = nf.farInteractionCaster as CurveInteractionCaster;
                Assert.IsNotNull(near, $"{nf.name} near caster"); Assert.IsNotNull(far, $"{nf.name} far caster");
                Assert.AreNotEqual(0, near.physicsLayerMask.value & (1 << weaponLayer), $"{nf.name} near caster sees weapons");
                Assert.AreNotEqual(0, far.raycastMask.value & (1 << weaponLayer), $"{nf.name} far caster sees weapons");
            }
        }

        [UnityTest]
        public IEnumerator RealNearCasterFindsAWeaponItTouches()
        {
            yield return null;
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var col = pistol.GetComponentsInChildren<Collider>().First(c => c.enabled);
            var nf = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None).First(n => n.handedness == InteractorHandedness.Right);
            var near = nf.nearInteractionCaster;
            var origin = ((InteractionCasterBase)near).effectiveCastOrigin;
            origin.position = col.bounds.center;
            Physics.SyncTransforms();
            var targets = new List<Collider>();
            near.TryGetColliderTargets(XrTestRig.Manager, targets);
            Assert.IsTrue(targets.Any(t => t.transform.IsChildOf(pistol.transform)), $"near caster at the pistol found: {string.Join(", ", targets.Select(t => t.name))}");
        }

        [UnityTest]
        public IEnumerator ThumbBendsTowardTheFingers()
        {
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsSortMode.None);
            Assert.AreEqual(2, hands.Length);
            bool canRender = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            foreach (var h in hands)
            {
                // the knuckle does not move when the fingers curl, so it is a stable reference for thumb opposition
                Transform middle = h.HandRoot.GetComponentsInChildren<Transform>(true).First(t => t.name.EndsWith("MiddleProximal"));
                string side = h.IsLeft ? "L" : "R";
                h.SetInputOverride(0f, 0f);
                yield return XrTestRig.WaitSeconds(0.6f);
                Assert.IsTrue(h.TryGetTip(HandPresence.Finger.Thumb, out var openTip));
                float open = Vector3.Distance(openTip, middle.position);
                if (canRender) Shot(h, $"thumb_{side}_open");
                h.SetInputOverride(1f, 0f);
                yield return XrTestRig.WaitSeconds(0.6f);
                h.TryGetTip(HandPresence.Finger.Thumb, out var fistTip);
                float fist = Vector3.Distance(fistTip, middle.position);
                if (canRender) Shot(h, $"thumb_{side}_fist");
                h.SetInputOverride(null, null);
                Vector3 Local(Vector3 p)
                {
                    var inward = h.PalmInwardWorld; var fwd = Vector3.ProjectOnPlane(middle.position - h.PalmWorldPosition, inward).normalized;
                    var across = Vector3.Cross(inward, fwd) * (h.IsLeft ? -1f : 1f); var d = p - h.PalmWorldPosition;
                    return new Vector3(Vector3.Dot(d, across), Vector3.Dot(d, inward), Vector3.Dot(d, fwd)) * 1000f;
                }
                Debug.Log($"[VRRange][Test] {side} thumb tip to middle knuckle: open {open:0.000} m, fist {fist:0.000} m | tip mm (across, inward, forward): open {Local(openTip)}, fist {Local(fistTip)}, middle knuckle {Local(middle.position)}");
                Assert.Less(fist, open - 0.01f, $"{(h.IsLeft ? "left" : "right")} thumb moves toward the fingers when gripping");
                Assert.Greater(Local(fistTip).y, 8f, $"{side} thumb tip stays in front of the palm surface (does not fold into the palm)");
            }
        }

        /// <summary>Renders the hand from the palm side and from the thumb side to Logs/shots (review aid, GPU runs only).</summary>
        static void Shot(HandPresence h, string name)
        {
            var dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "shots"); System.IO.Directory.CreateDirectory(dir);
            var go = new GameObject("ThumbReviewCamera"); var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f; cam.fieldOfView = 40f;
            var rt = new RenderTexture(800, 600, 24); cam.targetTexture = rt;
            var palm = h.PalmWorldPosition; var inward = h.PalmInwardWorld;
            var fingers = h.HandRoot.GetComponentsInChildren<Transform>(true).First(t => t.name.EndsWith("MiddleProximal")).position - palm;
            var thumbSide = Vector3.Cross(inward, fingers.normalized) * (h.IsLeft ? -1f : 1f);
            var views = new[] { ("palm", palm + inward * 0.3f), ("thumbside", palm + thumbSide * 0.3f + inward * 0.08f) };
            foreach (var (label, pos) in views)
            {
                cam.transform.position = pos; cam.transform.LookAt(palm, fingers.normalized);
                cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{name}_{label}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            cam.targetTexture = null; rt.Release(); Object.Destroy(go);
        }
    }
}
