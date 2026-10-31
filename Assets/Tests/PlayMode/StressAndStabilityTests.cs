using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Repeated firing, rapid weapon switching, repeated reloading and mode restarts: pools must stay bounded, no instances
    /// may leak, and the managed heap must settle. Frame times are logged for the report; they are editor/CPU numbers in a
    /// container, not headset measurements.
    /// </summary>
    public class StressAndStabilityTests
    {
        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        static int Count<T>(bool includeInactive = true) where T : Object => Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        static int CasingsActive() => Object.FindObjectsByType<ShellCasing>(FindObjectsSortMode.None).Count(c => c.gameObject.activeInHierarchy);
        static long ManagedHeap() { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect(); return System.GC.GetTotalMemory(true); }

        [UnityTest]
        public IEnumerator SustainedAutomaticFireKeepsPoolsBoundedAndHeapStable()
        {
            var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            var hand = XrTestRig.MakeHand("R", new Vector3(0f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, smg);
            XrTestRig.Aim(smg, new Vector3(0f, 1.4f, 0f), new Vector3(0f, 1.3f, 29f));
            // warm up so one-time allocations (pools, audio, UI strings) are excluded from the leak measurement
            XrTestRig.PullTrigger(hand, smg); yield return XrTestRig.WaitSeconds(1.0f); XrTestRig.ReleaseTrigger(hand, smg);
            smg.RefillInstant(); yield return XrTestRig.WaitSeconds(2.5f);
            long heap0 = ManagedHeap();
            int projectiles0 = Count<Projectile>(), casings0 = Count<ShellCasing>(), audio0 = Count<AudioSource>(), particles0 = Count<ParticleSystem>();

            int fired = 0; smg.Fired += _ => fired++;
            float maxFrame = 0f, sumFrame = 0f; int frames = 0;
            XrTestRig.PullTrigger(hand, smg);
            float t = 0f;
            while (t < 6f)
            {
                yield return null; t += Time.unscaledDeltaTime;
                frames++; sumFrame += Time.unscaledDeltaTime; maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime);
                if (smg.Rounds == 0 && !smg.IsReloading) smg.RefillInstant(); // keep the stream going (reload path is covered separately)
            }
            XrTestRig.ReleaseTrigger(hand, smg);
            Assert.Greater(fired, 40, "sustained fire produced a large number of shots");
            Debug.Log($"[VRRange][Perf] sustained auto fire: {fired} shots in 6 s, avg frame {sumFrame / frames * 1000f:0.0} ms, max {maxFrame * 1000f:0.0} ms (editor/CPU container numbers, not headset)");
            yield return XrTestRig.WaitUntil(() => XrTestRig.ActiveProjectiles() == 0, 6f, "projectiles to settle");
            yield return XrTestRig.WaitUntil(() => CasingsActive() == 0, 8f, "casings to be recycled");
            Assert.LessOrEqual(Count<Projectile>(), Mathf.Max(projectiles0, 64) + 8, "projectile pool bounded");
            Assert.LessOrEqual(Count<ShellCasing>(), 96 + 8, "casing pool bounded by its max size");
            Assert.LessOrEqual(Count<AudioSource>(), audio0 + 24, "audio one-shot pool bounded");
            Assert.LessOrEqual(Count<ParticleSystem>(), particles0 + 64, "impact particle pool bounded");
            long heap1 = ManagedHeap();
            Debug.Log($"[VRRange][Perf] managed heap before {heap0 / 1024} KB, after {heap1 / 1024} KB (delta {(heap1 - heap0) / 1024} KB)");
            Assert.Less(heap1 - heap0, 12L * 1024 * 1024, "managed heap must not grow by more than 12 MB across a 6 s burst (leak check)");
        }

        [UnityTest]
        public IEnumerator RapidWeaponSwitchingAndReloadingLeavesNoLeaksOrStuckState()
        {
            var weapons = XrTestRig.AllWeapons();
            var hand = XrTestRig.MakeHand("R", new Vector3(0f, 1.4f, -1f), Quaternion.identity, InteractorHandedness.Right);
            var support = XrTestRig.MakeHand("L", new Vector3(-0.05f, 1.45f, -0.7f), Quaternion.identity, InteractorHandedness.Left);
            int objects0 = Count<Transform>();
            for (int cycle = 0; cycle < 30; cycle++)
            {
                var w = weapons[cycle % weapons.Length];
                yield return XrTestRig.Grab(hand, w, 1);
                if (w.Definition.twoHanded && cycle % 2 == 0) { yield return XrTestRig.Grab(support, w, 1); }
                XrTestRig.PullTrigger(hand, w); yield return null; XrTestRig.ReleaseTrigger(hand, w);
                if (cycle % 5 == 0) { w.StartReload(); yield return null; }
                XrTestRig.Release(support, w); XrTestRig.Release(hand, w);
                yield return null;
                Assert.IsFalse(w.IsHeld, $"cycle {cycle}: {w.DisplayName} released"); Assert.AreEqual(0, w.HandCount);
            }
            foreach (var w in weapons) w.RefillInstant();
            yield return XrTestRig.WaitSeconds(1f);
            Assert.AreEqual(3, XrTestRig.AllWeapons().Length);
            Assert.IsFalse(hand.hasSelection); Assert.IsFalse(support.hasSelection);
            foreach (var w in weapons) { Assert.IsFalse(w.IsReloading, $"{w.DisplayName} stuck reloading"); Assert.IsFalse(w.IsTwoHanded); Assert.AreEqual(w.MagazineSize, w.Rounds); }
            yield return XrTestRig.WaitUntil(() => XrTestRig.ActiveProjectiles() == 0, 6f, "projectiles");
            int objects1 = Count<Transform>();
            Assert.LessOrEqual(objects1 - objects0, 120, $"scene object count grew by {objects1 - objects0} (pool growth only is expected)");
            // full reload cycles on the pistol (timed path)
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            yield return XrTestRig.Grab(hand, pistol);
            for (int i = 0; i < 4; i++)
            {
                pistol.TryFire(); yield return null; pistol.StartReload();
                yield return XrTestRig.WaitSeconds(pistol.Definition.reloadSeconds + 0.35f);
                Assert.IsFalse(pistol.IsReloading, $"reload {i} finished"); Assert.AreEqual(pistol.MagazineSize, pistol.Rounds);
            }
        }

        [UnityTest]
        public IEnumerator RepeatedModeRestartsAreStableAndDeterministic()
        {
            var gm = GameModeManager.Instance;
            int objects0 = Count<Transform>();
            long heap0 = ManagedHeap();
            for (int i = 0; i < 20; i++)
            {
                switch (i % 3) { case 0: gm.StartFreePractice(); break; case 1: gm.StartAccuracyChallenge(); break; default: gm.StartMovingTargetChallenge(); break; }
                gm.SetDifficulty((Difficulty)(i % 3));
                yield return null;
                if (gm.State == ModeState.Countdown || gm.State == ModeState.Running) { ScoreManager.Instance.RegisterShot(); if (i % 2 == 0) gm.Finish(); }
                gm.Restart();
                Assert.IsTrue(gm.Lanes.All(l => l.Motion.Carrier.localPosition.x == 0f), "lanes reset to the lane centre");
                yield return null;
                Assert.AreEqual(0, ScoreManager.Instance.Stats.shotsFired, $"restart {i} resets the stats");
                Assert.IsTrue(XrTestRig.AllWeapons().All(w => w.Rounds == w.MagazineSize));
            }
            gm.StartFreePractice(); yield return null;
            Assert.AreEqual(ModeState.Running, gm.State);
            Assert.AreEqual(1, Count<GameModeManager>(false)); Assert.AreEqual(1, Count<ScoreManager>(false));
            Assert.LessOrEqual(Count<Transform>() - objects0, 40, "restarts create no objects beyond pooled one-shot audio sources (bounded pool)");
            long heap1 = ManagedHeap();
            Assert.Less(heap1 - heap0, 4L * 1024 * 1024, "restarts do not leak managed memory");
            Debug.Log($"[VRRange][Perf] 20 mode restarts: heap delta {(heap1 - heap0) / 1024} KB, object delta {Count<Transform>() - objects0}");
        }

        [UnityTest]
        public IEnumerator SceneStatisticsAreWithinQuestBudgets()
        {
            // Static budgets that matter for Quest 2 (72 FPS target): they are necessary, not sufficient; real frame timing needs the headset.
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            int materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            int lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled && l.lightmapBakeType != LightmapBakeType.Baked);
            long triangles = 0; var seen = new System.Collections.Generic.HashSet<Mesh>();
            foreach (var r in renderers)
            {
                var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (mesh == null || !seen.Add(mesh)) continue; // static batching makes many renderers share one combined mesh: count each mesh once
                for (int sm = 0; sm < mesh.subMeshCount; sm++) triangles += (long)mesh.GetIndexCount(sm) / 3;
            }
            int rigidbodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Count(rb => rb.gameObject.activeInHierarchy && !rb.isKinematic);
            Debug.Log($"[VRRange][Perf] scene: {renderers.Length} active renderers, {materials} distinct materials, {triangles} triangles (unculled total), {lights} realtime/mixed lights, {rigidbodies} dynamic rigidbodies");
            Assert.Less(renderers.Length, 900, "renderer count (static batching/SRP batcher keep draw calls far lower)");
            Assert.Less(materials, 80, "distinct materials (SRP batcher groups by shader; keep material count low)");
            Assert.Less(triangles, 450000, "total triangle budget before culling");
            Assert.LessOrEqual(lights, 16, "realtime/mixed light count");
            Assert.Less(rigidbodies, 40, "idle dynamic rigidbodies");
            yield return null;
        }
    }
}
