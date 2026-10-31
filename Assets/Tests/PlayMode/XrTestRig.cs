using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Scoring;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Shared helpers for PlayMode tests. IMPORTANT: there is no headset in the test runner. "Hands" are scripted
    /// XRDirectInteractors moved from code, grabs go through the real XRInteractionManager, and trigger pulls invoke the
    /// XRI activate/deactivate events that a controller would raise. Everything downstream of those events (weapon state,
    /// projectiles, scoring, UI, hands) runs for real; controller tracking and button hardware are mocked by construction.
    /// </summary>
    public static class XrTestRig
    {
        public const string SceneName = "ShootingRange";
        public static XRInteractionManager Manager { get; private set; }

        public static IEnumerator LoadScene()
        {
            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.IsNotNull(load, $"Scene '{SceneName}' is not in the build settings; run VR Range > Setup first.");
            while (!load.isDone) yield return null;
            yield return null; yield return null; // Awake/Start/first Update
            Manager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();
            Assert.IsNotNull(Manager, "XRInteractionManager missing from the scene");
        }

        public static XRDirectInteractor MakeHand(string name, Vector3 pos, Quaternion rot, InteractorHandedness handedness)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, rot);
            var col = go.AddComponent<SphereCollider>(); col.isTrigger = true; col.radius = 0.05f;
            var hand = go.AddComponent<XRDirectInteractor>();
            hand.handedness = handedness;
            hand.interactionManager = Manager;
            return hand;
        }

        public static Weapon Find(WeaponClass c) => UnityEngine.Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition != null && w.Definition.weaponClass == c);
        public static Weapon[] AllWeapons() => UnityEngine.Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None);

        public static IEnumerator Grab(XRDirectInteractor hand, Weapon weapon, int settleFrames = 3)
        {
            Manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)weapon.Grab);
            for (int i = 0; i < settleFrames; i++) yield return null;
        }

        public static void Release(XRDirectInteractor hand, Weapon weapon)
        {
            if (weapon.Grab.interactorsSelecting.Contains(hand)) Manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)weapon.Grab);
        }

        /// <summary>Simulates the trigger press that XRI's "Activate" action would raise for this hand.</summary>
        public static void PullTrigger(XRBaseInputInteractor hand, Weapon weapon)
            => weapon.Grab.activated.Invoke(new ActivateEventArgs { interactorObject = hand, interactableObject = weapon.Grab });

        public static void ReleaseTrigger(XRBaseInputInteractor hand, Weapon weapon)
            => weapon.Grab.deactivated.Invoke(new DeactivateEventArgs { interactorObject = hand, interactableObject = weapon.Grab });

        /// <summary>Points a (held or free) weapon from origin at target; kinematic so physics does not move it.</summary>
        public static void Aim(Weapon weapon, Vector3 origin, Vector3 target)
        {
            var rb = weapon.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
            var dir = (target - origin).normalized;
            weapon.transform.SetPositionAndRotation(origin - weapon.Muzzle.localPosition.z * dir, Quaternion.LookRotation(dir));
        }

        /// <summary>Aims a free weapon at a target face with drop compensation and fires until a hit registers (moving targets).</summary>
        public static IEnumerator FireUntilHit(Weapon weapon, VRRange.Targets.BullseyeTarget target, int maxShots, float timeoutPerShot = 0.8f, bool requireScore = false)
        {
            int hits = target.HitsSinceReset;
            int scored = ScoreManager.Instance != null ? ScoreManager.Instance.Stats.hits : 0;
            bool Done() => requireScore ? (ScoreManager.Instance != null && ScoreManager.Instance.Stats.hits > scored) : target.HitsSinceReset > hits;
            for (int i = 0; i < maxShots && !Done(); i++)
            {
                var face = target.Face;
                var origin = new Vector3(face.position.x, face.position.y, 0f);
                float dist = Vector3.Distance(origin, face.position);
                float t = dist / weapon.Definition.muzzleVelocity;
                float drop = 0.5f * Mathf.Abs(Physics.gravity.y) * weapon.Definition.gravityMultiplier * t * t;
                Aim(weapon, origin, face.position + Vector3.up * drop);
                weapon.RefillInstant();
                weapon.TryFire();
                float w = 0f;
                while (w < timeoutPerShot && !Done()) { w += Time.unscaledDeltaTime; yield return null; }
            }
            Assert.IsTrue(Done(), $"no {(requireScore ? "scoring " : "")}hit on {target.name} after {maxShots} compensated shots");
        }

        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            float t = 0f;
            while (!condition() && t < timeoutSeconds) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(condition(), $"timed out after {timeoutSeconds}s waiting for: {what}");
        }

        public static IEnumerator WaitSeconds(float seconds) { float t = 0f; while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; } }

        public static int ActiveProjectiles() => UnityEngine.Object.FindObjectsByType<VRRange.Ballistics.Projectile>(FindObjectsSortMode.None).Count(p => p.IsActive);
    }
}
