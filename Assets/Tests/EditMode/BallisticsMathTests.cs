using NUnit.Framework;
using UnityEngine;
using VRRange.Ballistics;

namespace VRRange.Tests
{
    public class BallisticsMathTests
    {
        [Test]
        public void DropGrowsWithDistance()
        {
            float near = BallisticsMath.DropAtDistance(5f, 90f, 9.81f * 3f);
            float far = BallisticsMath.DropAtDistance(25f, 90f, 9.81f * 3f);
            Assert.Greater(far, near);
            Assert.Greater(far / near, 20f, "drop scales with the square of distance");
        }

        [Test]
        public void TimeOfFlightIsDistanceOverSpeed()
        {
            Assert.AreEqual(0.25f, BallisticsMath.TimeOfFlight(25f, 100f), 1e-5f);
            Assert.IsTrue(float.IsPositiveInfinity(BallisticsMath.TimeOfFlight(10f, 0f)));
        }

        [Test]
        public void IntegrationFallsUnderGravity()
        {
            Vector3 pos = Vector3.zero, vel = Vector3.forward * 100f;
            for (int i = 0; i < 100; i++) BallisticsMath.Integrate(ref pos, ref vel, 0.01f, Vector3.down * 9.81f);
            Assert.Less(pos.y, 0f);
            Assert.AreEqual(100f, pos.z, 0.5f);
        }

        [Test]
        public void SpreadStaysWithinCone()
        {
            var dir = Vector3.forward;
            for (int i = 0; i < 200; i++)
            {
                var d = BallisticsMath.ApplySpread(dir, 2f, (i % 10) / 10f, (i * 7 % 13) / 13f);
                Assert.LessOrEqual(Vector3.Angle(dir, d), 2.001f);
                Assert.AreEqual(1f, d.magnitude, 1e-4f);
            }
            Assert.AreEqual(dir, BallisticsMath.ApplySpread(dir, 0f, 0.5f, 0.5f));
        }

        [Test]
        public void CompensationAngleIsSmallAndPositive()
        {
            float a = BallisticsMath.CompensationAngleDegrees(25f, 90f, 9.81f * 3f);
            Assert.Greater(a, 0f);
            Assert.Less(a, 10f);
            Assert.AreEqual(0f, BallisticsMath.CompensationAngleDegrees(0f, 90f, 9.81f));
        }
    }
}
