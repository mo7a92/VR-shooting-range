using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// 0.4.0: the AKM and the bolt-action sniper rifle from the gun rack are playable. They start in their rack sockets,
    /// fire with real ballistics, the sniper needs its bolt worked between shots, and its scope shows a magnified view whose
    /// line of sight crosses the bullet's path at the 25 m zero.
    /// </summary>
    public class RackWeaponsTests
    {
        static readonly Vector3 HandPos = new Vector3(0f, 1.4f, -1f);
        readonly List<GameObject> m_Temp = new List<GameObject>();

        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in m_Temp) if (go != null) Object.Destroy(go);
            m_Temp.Clear();
            yield return null;
        }

        XRDirectInteractor Hand(string name = "R") { var h = XrTestRig.MakeHand(name, HandPos, Quaternion.identity, InteractorHandedness.Right); m_Temp.Add(h.gameObject); return h; }

        [UnityTest]
        public IEnumerator RackRiflesStartInTheirRackSocketsLoadedAndTracked()
        {
            Assert.IsNull(GameObject.Find("RackRifleAKM"), "the static decor AKM is gone"); Assert.IsNull(GameObject.Find("RackRifleBolt"), "the static decor bolt-action is gone");
            foreach (var cls in new[] { WeaponClass.AssaultRifle, WeaponClass.SniperRifle })
            {
                var w = XrTestRig.Find(cls);
                Assert.IsTrue(w.Grab.isSelected, $"{w.DisplayName} starts socketed");
                var socket = w.Grab.interactorsSelecting[0] as XRSocketInteractor;
                Assert.IsNotNull(socket, $"{w.DisplayName}: held by a socket"); StringAssert.StartsWith("RackSocket_", socket.name);
                Assert.AreSame(socket, w.GetComponent<WeaponHome>().HomeSocket, $"{w.DisplayName}: its home is the rack");
                Assert.AreEqual(w.MagazineSize, w.Rounds, $"{w.DisplayName}: loaded");
                Assert.IsTrue(GameModeManager.Instance.Weapons.Contains(w), $"{w.DisplayName}: refilled on mode restarts and shown on the HUD");
                Assert.Less(Vector3.Distance(w.transform.position, GameObject.Find("GunRack").transform.position), 1.2f, $"{w.DisplayName}: on the gun rack");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SniperBoltMustBeWorkedBetweenShotsAndEjectsTheCase()
        {
            var sniper = XrTestRig.Find(WeaponClass.SniperRifle);
            Assert.IsTrue(sniper.IsBoltAction);
            var anim = sniper.GetComponent<WeaponAnimator>(); Assert.IsTrue(anim.HasBolt, "bolt rig built");
            var hand = Hand();
            yield return XrTestRig.Grab(hand, sniper);
            int fired = 0, dry = 0, cycled = 0; sniper.Fired += _ => fired++; sniper.DryFired += _ => dry++; sniper.BoltCycled += _ => cycled++;
            Assert.IsTrue(sniper.TryFire(), "first shot"); Assert.AreEqual(4, sniper.Rounds);
            Assert.IsTrue(sniper.NeedsBoltCycle, "the fired case stays in the chamber");
            StringAssert.Contains("BOLT", sniper.GetComponentInChildren<WeaponAmmoDisplay>().GetComponent<TMPro.TMP_Text>().text);
            yield return XrTestRig.WaitSeconds(sniper.Definition.SecondsBetweenShots + 0.05f);
            Assert.IsFalse(sniper.TryFire(), "no second shot without working the bolt"); Assert.AreEqual(1, dry, "just a click");
            Assert.AreEqual(4, sniper.Rounds);
            var bolt = sniper.transform.GetComponentsInChildren<Transform>().First(t => t.name == "BoltCarrier");
            var boltHome = bolt.localPosition;
            Assert.IsTrue(sniper.CycleBolt(), "bolt cycle starts");
            Assert.IsFalse(sniper.CycleBolt(), "one cycle at a time");
            float maxBack = 0f, maxLift = 0f;
            for (float t = 0f; t < sniper.Definition.boltCycleSeconds + 0.2f; t += Time.unscaledDeltaTime) { yield return null; maxBack = Mathf.Max(maxBack, anim.BoltBackNow); maxLift = Mathf.Max(maxLift, anim.BoltLiftNow); }
            Assert.Greater(maxBack, 0.06f, $"bolt travels back ({maxBack * 1000f:0} mm)"); Assert.Greater(maxLift, 60f, $"handle lifts ({maxLift:0} deg)");
            Assert.Less(Vector3.Distance(bolt.localPosition, boltHome), 0.0005f, "bolt closed again");
            Assert.AreEqual(1, cycled); Assert.IsFalse(sniper.NeedsBoltCycle);
            Assert.Greater(Object.FindObjectsByType<VRRange.Ballistics.ShellCasing>(FindObjectsSortMode.None).Count(c => c.gameObject.activeInHierarchy), 0, "the case was ejected");
            for (int shot = 2; shot <= 5; shot++)
            {
                yield return XrTestRig.WaitSeconds(sniper.Definition.SecondsBetweenShots + 0.05f);
                Assert.IsTrue(sniper.TryFire(), $"shot {shot} after working the bolt");
                Assert.IsTrue(sniper.CycleBolt()); yield return XrTestRig.WaitSeconds(sniper.Definition.boltCycleSeconds + 0.1f);
            }
            Assert.AreEqual(0, sniper.Rounds); Assert.AreEqual(5, fired);
            sniper.StartReload(); yield return XrTestRig.WaitSeconds(sniper.Definition.reloadSeconds + sniper.Definition.boltCycleSeconds + 0.3f);
            Assert.AreEqual(5, sniper.Rounds, "five-round magazine refilled"); Assert.IsFalse(sniper.NeedsBoltCycle, "reload chambers a round");
            sniper.ToggleFireMode(); Assert.AreEqual(FireMode.Semi, sniper.CurrentFireMode, "B / Y works the bolt; there is no auto mode");
            XrTestRig.Release(hand, sniper);
        }

        [UnityTest]
        public IEnumerator SniperScopeShowsAMagnifiedViewZeroedAt25Metres()
        {
            var sniper = XrTestRig.Find(WeaponClass.SniperRifle);
            var scope = sniper.GetComponent<WeaponScope>(); Assert.IsNotNull(scope, "scope component");
            Assert.IsNotNull(scope.ScopeCamera); Assert.IsNotNull(scope.Lens);
            Assert.AreEqual("VRRange/ScopeLens", scope.Lens.sharedMaterial.shader.name);
            Assert.IsFalse(scope.IsActive, "dark while on the rack"); Assert.IsFalse(scope.ScopeCamera.enabled);
            var hand = Hand();
            yield return XrTestRig.Grab(hand, sniper);
            yield return XrTestRig.WaitSeconds(0.8f);
            scope.ForceActive = true; // no headset in the runner: the eye-near-the-eyepiece check is bypassed
            yield return null; yield return null; yield return null;
            Assert.IsTrue(scope.IsActive); Assert.IsTrue(scope.ScopeCamera.enabled); Assert.IsNotNull(scope.Texture);
            Assert.AreEqual(4.5f, scope.ScopeCamera.fieldOfView, 0.01f, "PU-type 3.5x scope: ~4.5 deg true field");
            Assert.AreEqual(StereoTargetEyeMask.None, scope.ScopeCamera.stereoTargetEye);
            Assert.AreEqual(0, scope.ScopeCamera.cullingMask & (1 << LayerMask.NameToLayer("Weapon")), "the rifle's own front sight never shows in the scope");
            // zero: the line of sight crosses the bullet's path 25 m out (within 5 mm), and is above it closer in
            var cam = scope.ScopeCamera.transform; var muzzle = sniper.Muzzle; var def = sniper.Definition;
            Vector3 BulletAt(float range)
            {
                // muzzle along its forward, gravity on (world), at the horizontal distance 'range' from the scope camera along the bore
                float along = range - Vector3.Dot(muzzle.position - cam.position, muzzle.forward);
                float t = along / def.muzzleVelocity;
                return muzzle.position + muzzle.forward * along + Physics.gravity * (0.5f * def.gravityMultiplier * t * t);
            }
            float Miss(float range) { var b = BulletAt(range); var onAxis = cam.position + cam.forward * Vector3.Dot(b - cam.position, cam.forward); return Vector3.Distance(onAxis, b); }
            float miss25 = Miss(scope.ZeroMetres);
            // the hold here is near level, so the weapon-level zero is exact to well under a millimetre per degree of cant
            Assert.Less(miss25, 0.005f, $"line of sight vs bullet at {scope.ZeroMetres:0} m: {miss25 * 1000f:0.0} mm");
            Assert.Greater(Miss(5f), 0.02f, "5 m out the bullet is still below the line of sight (scope height over the bore)");
            // the image is a real render of the range (not black, not a flat colour)
            yield return null; yield return null; // (WaitForEndOfFrame never fires in batch mode; the texture is read directly)
            var rt = scope.Texture; var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var px = tex.GetPixels(); float mean = px.Average(c => c.grayscale); float var = px.Average(c => (c.grayscale - mean) * (c.grayscale - mean));
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "scope_view_rt.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Assert.Greater(mean, 0.02f, "scope image is lit"); Assert.Greater(var, 1e-5f, "scope image has detail");
            // what the eye sees: a camera 6 cm behind the eyepiece
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var eyeGo = new GameObject("EyeCam"); m_Temp.Add(eyeGo); var eye = eyeGo.AddComponent<Camera>(); eye.fieldOfView = 40f; eye.nearClipPlane = 0.01f;
                var lens = scope.Lens.transform; eyeGo.transform.SetPositionAndRotation(lens.position - lens.forward * 0.06f, lens.rotation);
                var ert = new RenderTexture(960, 640, 24); eye.targetTexture = ert; eye.Render();
                RenderTexture.active = ert; var et = new Texture2D(960, 640, TextureFormat.RGB24, false); et.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); et.Apply(); RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, "scope_eye_view.png"), et.EncodeToPNG());
                eye.targetTexture = null; ert.Release(); Object.Destroy(et);
            }
            scope.ForceActive = false;
            XrTestRig.Release(hand, sniper); yield return null; yield return null;
            Assert.IsFalse(scope.IsActive, "off once the rifle is put down"); Assert.IsFalse(scope.ScopeCamera.enabled);
        }

        [UnityTest]
        public IEnumerator AkmFiresSemiAndFullAutoAtItsRealRate()
        {
            var akm = XrTestRig.Find(WeaponClass.AssaultRifle);
            var hand = Hand();
            yield return XrTestRig.Grab(hand, akm);
            Assert.AreEqual(FireMode.Semi, akm.CurrentFireMode);
            int shots = 0; akm.Fired += _ => shots++;
            XrTestRig.PullTrigger(hand, akm); yield return XrTestRig.WaitSeconds(0.5f); XrTestRig.ReleaseTrigger(hand, akm);
            Assert.AreEqual(1, shots, "semi: one shot per pull");
            akm.ToggleFireMode(); Assert.AreEqual(FireMode.Auto, akm.CurrentFireMode);
            shots = 0; float t0 = Time.time;
            XrTestRig.PullTrigger(hand, akm); yield return XrTestRig.WaitSeconds(1.0f); XrTestRig.ReleaseTrigger(hand, akm);
            float rate = shots / Mathf.Max(0.01f, Time.time - t0) * 60f;
            Assert.That(shots, Is.InRange(7, 12), $"auto: ~600 rpm ({shots} shots, {rate:0} rpm)");
            Assert.IsTrue(akm.IsTwoHanded == false && akm.HandCount == 1);
            XrTestRig.Release(hand, akm);
        }

        [UnityTest]
        public IEnumerator ADroppedRackRifleGoesBackToTheRack()
        {
            foreach (var cls in new[] { WeaponClass.AssaultRifle, WeaponClass.SniperRifle })
            {
                var w = XrTestRig.Find(cls);
                var homePos = w.transform.position; var homeRot = w.transform.rotation;
                var hand = Hand(cls.ToString());
                yield return XrTestRig.Grab(hand, w);
                Assert.IsTrue(w.IsHeld);
                hand.transform.position += new Vector3(0.5f, 0f, 0.5f); yield return null;
                XrTestRig.Release(hand, w); yield return null;
                w.GetComponent<WeaponHome>().ReturnHome(); yield return null; yield return null;
                Assert.IsTrue(w.Grab.isSelected && w.Grab.interactorsSelecting[0] is XRSocketInteractor, $"{w.DisplayName} back in its socket");
                Assert.Less(Vector3.Distance(w.transform.position, homePos), 0.01f, $"{w.DisplayName} back on the rack");
                Assert.Less(Quaternion.Angle(w.transform.rotation, homeRot), 1f, $"{w.DisplayName} leaning as before");
            }
        }
    }
}
