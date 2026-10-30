using NUnit.Framework;
using UnityEngine;
using VRRange.Weapons;

namespace VRRange.Tests
{
    public class WeaponHandlingMathTests
    {
        [Test]
        public void PoseFromGripPutsGripExactlyOnHand()
        {
            var hand = new Pose(new Vector3(1f, 1.5f, -2f), Quaternion.Euler(10f, 40f, 5f));
            var gripLocal = new Pose(new Vector3(0f, -0.06f, -0.05f), Quaternion.Euler(0f, 0f, 0f));
            var w = WeaponHandlingMath.PoseFromGrip(hand, gripLocal);
            var gripWorld = w.position + w.rotation * gripLocal.position;
            Assert.Less(Vector3.Distance(gripWorld, hand.position), 1e-4f);
            Assert.Less(Quaternion.Angle(w.rotation * gripLocal.rotation, hand.rotation), 1e-3f);
        }

        [Test]
        public void TwoPointAimPointsSupportGripAtSupportHand()
        {
            var oneHand = Quaternion.Euler(0f, 30f, 0f);
            var primary = Vector3.zero;
            var support = new Vector3(0.1f, 0.05f, 0.3f);
            var gripToSupportLocal = new Vector3(0f, 0.06f, 0.35f);
            var r = WeaponHandlingMath.TwoPointAim(oneHand, primary, support, gripToSupportLocal, 1f);
            var predicted = (r * gripToSupportLocal).normalized;
            Assert.Less(Vector3.Angle(predicted, (support - primary).normalized), 0.1f);
            // zero influence leaves the one-handed rotation untouched
            Assert.AreEqual(0f, Quaternion.Angle(oneHand, WeaponHandlingMath.TwoPointAim(oneHand, primary, support, gripToSupportLocal, 0f)), 1e-4f);
            // partial influence lies between
            var half = WeaponHandlingMath.TwoPointAim(oneHand, primary, support, gripToSupportLocal, 0.5f);
            Assert.Greater(Quaternion.Angle(oneHand, half), 0f);
            Assert.Less(Quaternion.Angle(oneHand, half), Quaternion.Angle(oneHand, r) + 1e-3f);
        }

        [Test]
        public void LagClampsStayWithinLimits()
        {
            var target = Vector3.zero; var lagged = new Vector3(0.1f, 0f, 0f);
            Assert.AreEqual(0.02f, WeaponHandlingMath.ClampLag(target, lagged, 0.02f).magnitude, 1e-5f);
            Assert.AreEqual(lagged, WeaponHandlingMath.ClampLag(target, lagged, 0.5f));
            var tr = Quaternion.identity; var lr = Quaternion.Euler(0f, 30f, 0f);
            Assert.AreEqual(6f, Quaternion.Angle(tr, WeaponHandlingMath.ClampLag(tr, lr, 6f)), 0.1f);
            Assert.AreEqual(1f, WeaponHandlingMath.LagFactor(1f, 0f));
            Assert.Less(WeaponHandlingMath.LagFactor(0.016f, 0.2f), WeaponHandlingMath.LagFactor(0.016f, 0.02f), "heavier (slower response) must follow less per frame");
        }

        [Test]
        public void HeavierAndUnsupportedWeaponsKickMoreAndRecoverSlower()
        {
            float pistolOneHand = WeaponHandlingMath.RecoilScale(1.3f, 0.65f, 0f, 0.9f);
            float rifleOneHand = WeaponHandlingMath.RecoilScale(1.3f, 0.65f, 0f, 3.4f);
            float rifleTwoHand = WeaponHandlingMath.RecoilScale(1.3f, 0.65f, 1f, 3.4f);
            Assert.Greater(rifleOneHand, rifleTwoHand, "support hand must reduce recoil");
            Assert.Less(rifleOneHand, pistolOneHand, "per-shot kick is damped by mass");
            Assert.Greater(WeaponHandlingMath.RecoverySpeed(9f, 16f, 1f), WeaponHandlingMath.RecoverySpeed(9f, 16f, 0f));
            Assert.Greater(WeaponHandlingMath.Sag(3f, 0.5f, 0f), WeaponHandlingMath.Sag(3f, 0.5f, 1f));
            Assert.Greater(WeaponHandlingMath.SwayAmplitude(0.5f, 0.1f, 0f, 1f), WeaponHandlingMath.SwayAmplitude(0.5f, 0.1f, 0f, 0f), "instability increases sway");
        }

        [Test]
        public void SwayIsBoundedAndDeterministic()
        {
            for (float t = 0f; t < 10f; t += 0.37f)
            {
                var a = WeaponHandlingMath.Sway(t, 0.7f, 0.5f, 3f); var b = WeaponHandlingMath.Sway(t, 0.7f, 0.5f, 3f);
                Assert.AreEqual(a, b);
                Assert.LessOrEqual(Mathf.Abs(a.x), 0.5f); Assert.LessOrEqual(Mathf.Abs(a.y), 0.5f);
            }
        }
    }
}
