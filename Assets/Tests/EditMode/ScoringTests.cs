using NUnit.Framework;
using UnityEngine;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;

namespace VRRange.Tests
{
    public class ScoringTests
    {
        ScoringRules MakeRules() => ScriptableObject.CreateInstance<ScoringRules>();

        [TestCase(0.00f, 10)]
        [TestCase(0.049f, 10)]
        [TestCase(0.08f, 8)]
        [TestCase(0.15f, 5)]
        [TestCase(0.25f, 2)]
        [TestCase(0.29f, 0)]
        [TestCase(1.00f, 0)]
        public void RingPointsMatchSpecification(float distance, int expectedPoints)
        {
            var rules = MakeRules();
            int ring = rules.RingIndexFor(distance);
            Assert.AreEqual(expectedPoints, rules.PointsFor(ring));
            Object.DestroyImmediate(rules);
        }

        [Test]
        public void MissHasNoNameOrPoints()
        {
            var rules = MakeRules();
            Assert.AreEqual(ScoringRules.MissIndex, rules.RingIndexFor(5f));
            Assert.AreEqual("Miss", rules.NameFor(ScoringRules.MissIndex));
            Assert.AreEqual(0, rules.PointsFor(ScoringRules.MissIndex));
            Assert.AreEqual(10, rules.MaxPoints);
            Object.DestroyImmediate(rules);
        }

        [Test]
        public void AccuracyIsHitsOverShots()
        {
            var s = new SessionStats { shotsFired = 8, hits = 6 };
            Assert.AreEqual(0.75f, s.Accuracy, 1e-6f);
            Assert.AreEqual(75f, s.AccuracyPercent, 1e-4f);
            Assert.AreEqual(0f, new SessionStats().Accuracy);
        }

        [Test]
        public void GradesFollowScoreRatio()
        {
            Assert.AreEqual("S", GameModeManager.GradeFor(new SessionStats { shotsFired = 20, hits = 20, score = 190 }, 200));
            Assert.AreEqual("A", GameModeManager.GradeFor(new SessionStats { shotsFired = 20, hits = 18, score = 160 }, 200));
            Assert.AreEqual("D", GameModeManager.GradeFor(new SessionStats { shotsFired = 20, hits = 2, score = 10 }, 200));
            Assert.AreEqual("-", GameModeManager.GradeFor(new SessionStats(), 200));
        }

        [Test]
        public void DistancePresetsSnapToNearest()
        {
            var settings = ScriptableObject.CreateInstance<GameModeSettings>();
            Assert.AreEqual(10f, settings.ClampDistance(11f));
            Assert.AreEqual(25f, settings.ClampDistance(40f));
            Assert.AreEqual(5f, settings.ClampDistance(1f));
            Object.DestroyImmediate(settings);
        }
    }
}
