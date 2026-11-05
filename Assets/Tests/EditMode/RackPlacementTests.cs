using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Editor;
using VRRange.Modes;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// The playable rack rifles must not breach the gun rack (the old decor rifles ran through two shelves): in the saved
    /// scene no triangle edge of either rifle crosses the rack or a prop around it, no point of a rifle is inside a prop,
    /// nothing is below the floor, and below the middle shelf's top surface every point is in front of the rack.
    /// </summary>
    public class RackPlacementTests
    {
        [Test]
        public void RackRiflesLeanOnTheRackWithoutPassingThroughItOrAnyProp()
        {
            EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            Assert.IsNull(GameObject.Find("RackRifleAKM")); Assert.IsNull(GameObject.Find("RackRifleBolt"));
            var group = GameObject.Find(RackWeaponsBuilder.GroupName); Assert.IsNotNull(group, "RackWeapons group in the scene");
            var rack = GameObject.Find("GunRack"); Assert.IsNotNull(rack);
            var weapons = group.GetComponentsInChildren<Weapon>();
            CollectionAssert.AreEquivalent(new[] { WeaponClass.AssaultRifle, WeaponClass.SniperRifle }, weapons.Select(w => w.Definition.weaponClass));
            var rackVerts = RackWeaponsBuilder.WorldVertices(rack);
            var rb = new Bounds(rackVerts[0], Vector3.zero); foreach (var v in rackVerts) rb.Encapsulate(v);
            float lipTop = rackVerts.Where(v => Mathf.Abs(v.y - 0.91f) < 0.04f && v.z > rb.max.z - 0.03f).Max(v => v.y);
            var gm = Object.FindFirstObjectByType<GameModeManager>();
            using (var probe = new RackWeaponsBuilder.ObstacleProbe(rack, group.transform, rb))
            {
                foreach (var w in weapons)
                {
                    var pts = RackWeaponsBuilder.SampledPoints(w.gameObject);
                    Assert.Greater(pts.Count, 10000, $"{w.DisplayName}: sampled surface");
                    Assert.IsNull(probe.Intersects(w.gameObject, pts), $"{w.DisplayName} intersects ({probe.Describe()})");
                    Assert.GreaterOrEqual(pts.Min(p => p.y), 0f, $"{w.DisplayName}: nothing below the floor");
                    Assert.Less(pts.Min(p => p.y), 0.01f, $"{w.DisplayName}: the butt rests on the floor");
                    Assert.Less(pts.Max(p => p.y), 1.385f, $"{w.DisplayName}: clear of the shelf above");
                    float behind = pts.Where(p => p.y <= lipTop).Max(p => rb.max.z - p.z);
                    Assert.LessOrEqual(behind, 0f, $"{w.DisplayName}: below the shelf top everything is in front of the rack ({behind * 1000f:0.0} mm behind the front)");
                    Assert.Less(pts.Where(p => p.y <= lipTop).Min(p => p.z) - rb.max.z, 0.012f, $"{w.DisplayName}: actually leans on the lip");
                    Assert.GreaterOrEqual(pts.Min(p => p.x), rb.min.x + 0.02f); Assert.LessOrEqual(pts.Max(p => p.x), rb.max.x - 0.02f);
                    var home = w.GetComponent<WeaponHome>().HomeSocket;
                    Assert.IsNotNull(home); Assert.AreSame(w.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(), home.startingSelectedInteractable, $"{w.DisplayName}: starts in its rack socket");
                    Assert.IsTrue(gm.Weapons.Contains(w), $"{w.DisplayName}: tracked by the game mode manager");
                }
            }
            Assert.AreEqual(5, gm.Weapons.Count);
        }
    }
}
