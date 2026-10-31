using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Editor;
using VRRange.Hands;
using VRRange.Recording;
using VRRange.Targets;
using VRRange.UI;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Prefabs, settings assets and the generated scene: required components, no missing scripts or broken references, no missing textures, sane placement.</summary>
    public class ProjectIntegrityTests
    {
        static readonly string[] WeaponPrefabs = { "Weapon_Pistol", "Weapon_Rifle", "Weapon_SMG" };

        static GameObject Prefab(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{RangePaths.Prefabs}/{name}.prefab");
            Assert.IsNotNull(go, $"prefab {name} missing (run VR Range > Setup)");
            return go;
        }

        /// <summary>Object-reference fields that point at a destroyed/missing asset (instance id set, object null).</summary>
        static List<string> MissingReferences(GameObject root)
        {
            var problems = new List<string>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) { problems.Add($"{root.name}: missing script"); continue; }
                var so = new SerializedObject(c); var it = so.GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        problems.Add($"{c.gameObject.name}.{c.GetType().Name}.{it.propertyPath}");
            }
            return problems;
        }

        [Test]
        public void WeaponPrefabsHaveEveryRequiredComponentAndReference()
        {
            foreach (var name in WeaponPrefabs)
            {
                var go = Prefab(name);
                var weapon = go.GetComponent<Weapon>(); Assert.IsNotNull(weapon, $"{name}: Weapon");
                Assert.IsNotNull(weapon.Definition, $"{name}: WeaponDefinition assigned");
                Assert.IsNotNull(go.GetComponent<XRGrabInteractable>(), $"{name}: XRGrabInteractable");
                Assert.IsFalse(go.GetComponent<XRGrabInteractable>().addDefaultGrabTransformers, $"{name}: default transformers replaced by the handling transformer");
                Assert.IsNotNull(go.GetComponent<XRGrabInteractable>().attachTransform, $"{name}: attach transform");
                Assert.IsNotNull(go.GetComponent<Rigidbody>(), $"{name}: Rigidbody"); Assert.IsNotNull(go.GetComponent<Collider>(), $"{name}: Collider");
                Assert.IsNotNull(go.GetComponent<WeaponHandlingTransformer>(), $"{name}: WeaponHandlingTransformer");
                Assert.IsNotNull(go.GetComponent<WeaponHome>(), $"{name}: WeaponHome"); Assert.IsNotNull(go.GetComponent<WeaponAnimator>(), $"{name}: WeaponAnimator");
                Assert.IsNotNull(go.GetComponent<AudioSource>(), $"{name}: AudioSource");
                Assert.AreNotSame(go.transform, weapon.Muzzle, $"{name}: muzzle transform assigned");
                Assert.Greater(weapon.Muzzle.localPosition.z, 0.05f, $"{name}: muzzle is in front of the grip");
                var anchors = go.GetComponentsInChildren<WeaponGripAnchor>(true);
                Assert.IsTrue(anchors.Any(a => a.role == GripRole.Primary), $"{name}: primary grip anchor");
                if (weapon.Definition.twoHanded) { Assert.IsTrue(anchors.Any(a => a.role == GripRole.Support), $"{name}: support grip anchor"); Assert.IsNotNull(go.GetComponent<XRGrabInteractable>().secondaryAttachTransform, $"{name}: secondary attach"); }
                foreach (var a in anchors) Assert.IsNotNull(a.pose, $"{name}: anchor {a.role} has a hand pose");
                Assert.AreEqual(LayerMask.NameToLayer("Weapon"), go.layer, $"{name}: Weapon layer");
                Assert.IsTrue(go.GetComponentsInChildren<MeshRenderer>(true).Length >= 2, $"{name}: visible model parts");
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) foreach (var m in mr.sharedMaterials) { Assert.IsNotNull(m, $"{name}: {mr.name} has a null material"); Assert.IsNotNull(m.GetTexture("_BaseMap"), $"{name}: {mr.name} material {m.name} has no albedo texture"); }
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go), $"{name}: missing scripts");
                CollectionAssert.IsEmpty(MissingReferences(go), $"{name}: broken references");
            }
        }

        [Test]
        public void TargetProjectileAndFxPrefabsAreComplete()
        {
            var target = Prefab("Target_Bullseye");
            var bt = target.GetComponent<BullseyeTarget>(); Assert.IsNotNull(bt); Assert.IsNotNull(bt.Rules, "scoring rules assigned");
            Assert.AreNotSame(target.transform, bt.Face, "face assigned"); Assert.IsNotNull(bt.Face.GetComponent<Collider>(), "face collider");
            Assert.IsNotNull(target.transform.Find("PopUpPivot"), "pop-up pivot");
            Assert.AreEqual(LayerMask.NameToLayer("Target"), bt.Face.gameObject.layer);
            var projectile = Prefab("Projectile");
            Assert.IsNotNull(projectile.GetComponent<Projectile>()); Assert.IsNotNull(projectile.GetComponent<TrailRenderer>(), "tracer trail");
            Assert.AreEqual(LayerMask.NameToLayer("Projectile"), projectile.layer);
            Assert.IsNull(projectile.GetComponent<Rigidbody>(), "projectiles are swept, not rigidbodies");
            var casing = Prefab("ShellCasing"); Assert.IsNotNull(casing.GetComponent<Rigidbody>()); Assert.IsNotNull(casing.GetComponent<ShellCasing>());
            foreach (var n in new[] { "BulletHoleDecal", "ImpactParticles", "MuzzleFlash", "ImpactMarker", "OneShotAudio" }) Prefab(n);
            Assert.IsNotNull(Prefab("ImpactParticles").GetComponent<ParticleSystem>());
            foreach (var n in new[] { "Target_Bullseye", "Projectile", "ShellCasing", "BulletHoleDecal", "ImpactParticles", "MuzzleFlash", "ImpactMarker" })
            {
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(Prefab(n)), $"{n}: missing scripts");
                CollectionAssert.IsEmpty(MissingReferences(Prefab(n)), $"{n}: broken references");
            }
        }

        [Test]
        public void WeaponDefinitionsAreConsistent()
        {
            var defs = new[] { "Weapon_Pistol", "Weapon_Rifle", "Weapon_SMG" }.Select(n => AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"{RangePaths.Settings}/{n}.asset")).ToArray();
            Assert.IsTrue(defs.All(d => d != null), "weapon definitions exist");
            var pistol = defs[0]; var rifle = defs[1]; var smg = defs[2];
            foreach (var d in defs)
            {
                Assert.IsTrue(d.Supports(d.defaultFireMode), $"{d.displayName}: default fire mode supported");
                Assert.Greater(d.magazineSize, 0); Assert.Greater(d.reloadSeconds, 0f); Assert.Greater(d.muzzleVelocity, 0f); Assert.Greater(d.SecondsBetweenShots, 0f);
                Assert.IsNotNull(d.fireClip, $"{d.displayName}: fire clip"); Assert.IsNotNull(d.dryFireClip, $"{d.displayName}: dry-fire clip");
                Assert.IsNotNull(d.reloadStartClip); Assert.IsNotNull(d.reloadEndClip); Assert.IsNotNull(d.slideRackClip);
                Assert.Greater(d.virtualMassKg, 0f); Assert.Greater(d.recoilUnsupportedMultiplier, d.recoilSupportedMultiplier, $"{d.displayName}: unsupported recoil > supported");
                Assert.Less(d.recoveryUnsupported, d.recoverySupported + 0.001f, $"{d.displayName}: supported grip recovers at least as fast");
                Assert.GreaterOrEqual(d.oneHandSagDegrees, d.twoHandSagDegrees); Assert.GreaterOrEqual(d.oneHandResponseSeconds, d.twoHandResponseSeconds);
            }
            Assert.IsFalse(pistol.twoHanded); Assert.IsTrue(rifle.twoHanded); Assert.IsTrue(smg.twoHanded);
            Assert.AreEqual(FireMode.Semi, pistol.supportedFireModes); Assert.IsTrue(rifle.Supports(FireMode.Auto) && rifle.Supports(FireMode.Semi)); Assert.AreEqual(FireMode.Auto, smg.defaultFireMode);
            Assert.Less(pistol.virtualMassKg, smg.virtualMassKg); Assert.Less(smg.virtualMassKg, rifle.virtualMassKg, "pistol < SMG < rifle weight");
            Assert.Greater(rifle.muzzleVelocity, pistol.muzzleVelocity); Assert.Greater(smg.roundsPerMinute, rifle.roundsPerMinute);
        }

        [Test]
        public void SettingsAssetsExist()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ScoringRules>($"{RangePaths.Settings}/ScoringRules.asset"));
            var modes = AssetDatabase.LoadAssetAtPath<GameModeSettings>($"{RangePaths.Settings}/GameModeSettings.asset"); Assert.IsNotNull(modes);
            Assert.AreEqual(3, modes.profiles.Length); Assert.AreEqual(4, modes.distancePresets.Length);
            foreach (var n in new[] { "Open", "Idle", "Fist", "Pinch", "Point", "PistolGrip", "RiflePrimary", "RifleSupport", "SmgGrip" })
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<HandPose>($"{RangePaths.Settings}/HandPoses/{n}.asset"), $"hand pose {n}");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(RangePaths.PcvrPostProfile), "PCVR post-process profile");
        }

        [Test]
        public void GeneratedMaterialsHaveShadersAndTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { RangePaths.Materials });
            Assert.Greater(guids.Length, 20, "generated materials present");
            var problems = new List<string>();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g); var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m.shader == null || m.shader.name.Contains("InternalErrorShader") || m.shader.name == "Hidden/InternalErrorShader") problems.Add($"{m.name}: error shader");
                if (!m.shader.name.StartsWith("Universal Render Pipeline")) problems.Add($"{m.name}: not a URP shader ({m.shader.name})");
                var so = new SerializedObject(m); var texs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                for (int i = 0; texs != null && i < texs.arraySize; i++)
                {
                    var el = texs.GetArrayElementAtIndex(i); var tex = el.FindPropertyRelative("second.m_Texture");
                    if (tex != null && tex.objectReferenceValue == null && tex.objectReferenceInstanceIDValue != 0) problems.Add($"{m.name}: missing texture {el.FindPropertyRelative("first").stringValue}");
                }
                if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null && m.GetTexture("_BaseMap") == null) problems.Add($"{m.name}: normal map without base map");
            }
            CollectionAssert.IsEmpty(problems);
            // the key PBR sets must be textured (a missing download would silently fall back to white)
            foreach (var n in new[] { "FloorConcrete", "WallConcrete", "MetalBrushed", "Wood", "Weapon_pistol", "Weapon_rifle", "Weapon_smg", "HandSkinLeft", "HandSkinRight" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{RangePaths.Materials}/{n}.mat");
                Assert.IsNotNull(m, $"{n}.mat"); Assert.IsNotNull(m.GetTexture("_BaseMap"), $"{n}: albedo texture");
            }
        }

        [Test]
        public void SceneIsWiredCompletelyAndSanelyPlaced()
        {
            Assert.IsTrue(RangeSceneBuilder.SceneExists);
            var scene = EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            try
            {
                var roots = scene.GetRootGameObjects();
                var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
                Assert.AreEqual(0, all.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount), "no missing scripts in the scene");
                var broken = roots.SelectMany(MissingReferences).ToList();
                CollectionAssert.IsEmpty(broken, "broken references in the scene");

                Assert.AreEqual(1, all.Count(g => g.GetComponent<XROrigin>() != null), "exactly one XR Origin");
                var cams = all.Where(g => g.GetComponent<Camera>() != null).Select(g => g.GetComponent<Camera>()).ToArray();
                Assert.AreEqual(1, cams.Count(c => c.CompareTag("MainCamera")), "exactly one MainCamera (the headset)");
                Assert.AreEqual(1, all.Count(g => g.GetComponent<AudioListener>() != null), "exactly one AudioListener");
                Assert.AreEqual(5, all.Count(g => g.GetComponent<TargetLane>() != null)); Assert.AreEqual(5, all.Count(g => g.GetComponent<BullseyeTarget>() != null));
                var weapons = all.Where(g => g.GetComponent<Weapon>() != null).Select(g => g.GetComponent<Weapon>()).ToArray();
                Assert.AreEqual(3, weapons.Length);
                foreach (var w in weapons)
                {
                    var home = w.GetComponent<WeaponHome>(); Assert.IsNotNull(home.HomeSocket, $"{w.name}: socket");
                    var grab = w.GetComponent<XRGrabInteractable>(); // Weapon.Grab is cached in Awake (play mode only)
                    Assert.Less(Vector3.Distance(w.transform.position, home.HomeSocket.transform.position), 0.02f, $"{w.name} rests on its socket");
                    Assert.Less(Vector3.Distance(grab.attachTransform.position, home.HomeSocket.attachTransform.position), 0.005f, $"{w.name}: socket attach mirrors the weapon attach (no re-pose at runtime)");
                    Assert.Less(Quaternion.Angle(grab.attachTransform.rotation, home.HomeSocket.attachTransform.rotation), 0.5f, $"{w.name}: socket attach rotation mirrors the weapon attach");
                }
                Assert.AreEqual(3, all.Count(g => g.GetComponent<XRSocketInteractor>() != null));
                Assert.AreEqual(2, all.Count(g => g.GetComponent<HandPresence>() != null), "two hands on the rig");
                var ui = all.Select(g => g.GetComponent<RangeUI>()).FirstOrDefault(c => c != null); Assert.IsNotNull(ui);
                foreach (var f in typeof(RangeUI).GetFields().Where(f => f.IsPublic && !f.IsStatic))
                {
                    var v = f.GetValue(ui);
                    if (v is Object o) Assert.IsTrue(o != null, $"RangeUI.{f.Name} unassigned");
                    else if (v is System.Array arr) { Assert.Greater(arr.Length, 0, $"RangeUI.{f.Name} empty"); foreach (Object e in arr) Assert.IsTrue(e != null, $"RangeUI.{f.Name} has a null entry"); }
                }
                var spectator = all.Select(g => g.GetComponent<SpectatorCamera>()).FirstOrDefault(c => c != null); Assert.IsNotNull(spectator, "spectator camera in scene");
                Assert.IsNotNull(spectator.Camera); Assert.IsFalse(spectator.Camera.CompareTag("MainCamera"));
                Assert.IsNotNull(all.Select(g => g.GetComponent<PlatformGraphicsSetup>()).FirstOrDefault(c => c != null), "platform graphics switch in scene");
                Assert.IsNotNull(all.Select(g => g.GetComponent<ProjectilePool>()).FirstOrDefault(c => c != null));
                Assert.IsNotNull(all.Select(g => g.GetComponent<ImpactEffects>()).FirstOrDefault(c => c != null));
                Assert.IsNotNull(all.Select(g => g.GetComponent<ShellCasingPool>()).FirstOrDefault(c => c != null));
                Assert.IsNotNull(all.Select(g => g.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>()).FirstOrDefault(c => c != null), "teleport area");
                Assert.IsNotNull(all.Select(g => g.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider>()).FirstOrDefault(c => c != null), "snap turn provider");
                Assert.IsNotNull(all.Select(g => g.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>()).FirstOrDefault(c => c != null), "teleport provider");
                Assert.IsNotNull(all.Select(g => g.GetComponent<AudioReverbZone>()).FirstOrDefault(c => c != null), "reverb zone");
                Assert.IsNotNull(all.Select(g => g.GetComponent<ReflectionProbe>()).FirstOrDefault(c => c != null), "reflection probe");
                Assert.IsNotNull(all.Select(g => g.GetComponent<LightProbeGroup>()).FirstOrDefault(c => c != null), "light probes");

                // placement: every renderer inside the room volume; nothing below the floor slab or above the ceiling
                var problems = new List<string>();
                foreach (var r in all.Select(g => g.GetComponent<Renderer>()).Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    var b = r.bounds;
                    if (b.min.y < -0.15f) problems.Add($"{r.name} below the floor (min y {b.min.y:0.00})");
                    if (b.max.y > 3.6f) problems.Add($"{r.name} above the ceiling (max y {b.max.y:0.00})");
                    if (Mathf.Abs(b.center.x) > 5.5f || b.center.z < -8.5f || b.center.z > 31f) problems.Add($"{r.name} outside the room ({b.center})");
                }
                CollectionAssert.IsEmpty(problems, "misplaced objects");
                // targets hang at face height on every lane
                foreach (var t in all.Select(g => g.GetComponent<BullseyeTarget>()).Where(t => t != null))
                    Assert.AreEqual(TargetPrefabBuilder.FaceHeight, t.Face.position.y, 0.05f, $"{t.name} face height");
                // lighting: baked GI settings assigned, lightmaps noted as not baked yet
                Assert.IsTrue(Lightmapping.TryGetLightingSettings(out var ls) && ls != null, "lighting settings assigned");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
