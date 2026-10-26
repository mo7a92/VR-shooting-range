using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using VRRange.Audio;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Player;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Editor
{
    /// <summary>Creates the playable ShootingRange scene from prefabs and builders. Re-running with force=true rebuilds it from scratch.</summary>
    public static class RangeSceneBuilder
    {
        public static bool SceneExists => AssetDatabase.LoadAssetAtPath<SceneAsset>(RangePaths.MainScene) != null;

        public static void Build(bool force)
        {
            SetupLog.Section("Scene");
            if (SceneExists && !force) { SetupLog.Ok($"Scene already exists: {RangePaths.MainScene} (use VR Range > Setup > Rebuild Scene to regenerate)"); AddToBuildSettings(); return; }
            if (FxPrefabBuilder.Projectile == null || TargetPrefabBuilder.Target == null) { SetupLog.Error("Prefabs were not built; run the full setup."); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("--- Range ---").transform;

            BuildManagers(root);
            EnvironmentBuilder.Build(root);
            var lanes = BuildLanes(root);
            var weapons = BuildWeaponCounter(root);
            BuildRig(root);
            var ui = UiBuilder.Build(root);
            ConfigureLightingAndAmbient();

            var gm = Object.FindFirstObjectByType<GameModeManager>();
            if (gm != null) gm.Initialize(SettingsAssetBuilder.ModeSettings, lanes, weapons);

            RangePaths.EnsureFolder(RangePaths.Scenes);
            EditorSceneManager.SaveScene(scene, RangePaths.MainScene);
            AddToBuildSettings();
            SetupLog.Ok($"Scene saved: {RangePaths.MainScene}");
        }

        static void BuildManagers(Transform root)
        {
            var managers = new GameObject("Managers"); managers.transform.SetParent(root, false);
            managers.AddComponent<RangeRuntimeInfo>();
            managers.AddComponent<ScoreManager>();
            managers.AddComponent<GameModeManager>();

            var pool = new GameObject("ProjectilePool"); pool.transform.SetParent(managers.transform, false);
            var pp = pool.AddComponent<ProjectilePool>();
            EditorRefs.Set(pp, "m_ProjectilePrefab", FxPrefabBuilder.Projectile.GetComponent<Projectile>());

            var fx = new GameObject("ImpactEffects"); fx.transform.SetParent(managers.transform, false);
            var ie = fx.AddComponent<ImpactEffects>();
            EditorRefs.Set(ie, "m_DecalPrefab", FxPrefabBuilder.Decal);
            EditorRefs.Set(ie, "m_ParticlePrefab", FxPrefabBuilder.ImpactParticles.GetComponent<ParticleSystem>());
            var so = new SerializedObject(ie);
            var surfaces = so.FindProperty("m_Surfaces");
            var defs = new (SurfaceType type, string clip, Color color, bool decal)[]
            {
                (SurfaceType.Concrete, "Impacts/impact_concrete", new Color(0.8f, 0.78f, 0.7f), true), (SurfaceType.Metal, "Impacts/impact_metal", new Color(1f, 0.85f, 0.5f), true),
                (SurfaceType.Paper, "Impacts/impact_paper", new Color(0.9f, 0.9f, 0.85f), true), (SurfaceType.Wood, "Impacts/impact_wood", new Color(0.6f, 0.45f, 0.3f), true),
                (SurfaceType.Rubber, "Impacts/impact_rubber", new Color(0.3f, 0.3f, 0.3f), true), (SurfaceType.Generic, "Impacts/impact_concrete", new Color(0.7f, 0.7f, 0.7f), true),
            };
            surfaces.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var el = surfaces.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("surface").enumValueIndex = (int)defs[i].type;
                el.FindPropertyRelative("particleColor").colorValue = defs[i].color;
                el.FindPropertyRelative("leaveDecal").boolValue = defs[i].decal;
                var clips = el.FindPropertyRelative("clips"); clips.arraySize = 1; clips.GetArrayElementAtIndex(0).objectReferenceValue = SettingsAssetBuilder.Clip(defs[i].clip);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var audio = new GameObject("AudioManager"); audio.transform.SetParent(managers.transform, false);
            var am = audio.AddComponent<AudioManager>();
            var amb = audio.AddComponent<AudioSource>(); amb.playOnAwake = false; amb.loop = true; amb.spatialBlend = 0f;
            EditorRefs.Set(am, "m_OneShotPrefab", FxPrefabBuilder.OneShotAudio.GetComponent<AudioSource>());
            EditorRefs.Set(am, "m_AmbienceSource", amb);
            EditorRefs.Set(am, "m_AmbienceLoop", SettingsAssetBuilder.Clip("Ambience/range_ambience_loop"));
            EditorRefs.Set(am, "m_UiClick", SettingsAssetBuilder.Clip("UI/ui_click"));
            EditorRefs.Set(am, "m_TargetHitDing", SettingsAssetBuilder.Clip("Impacts/target_ding"));
        }

        static List<TargetLane> BuildLanes(Transform root)
        {
            var lanesRoot = new GameObject("Lanes"); lanesRoot.transform.SetParent(root, false);
            var lanes = new List<TargetLane>();
            for (int i = 0; i < EnvironmentBuilder.LaneCount; i++)
            {
                var laneGo = new GameObject($"Lane{i + 1}"); laneGo.transform.SetParent(lanesRoot.transform, false);
                laneGo.transform.localPosition = new Vector3(EnvironmentBuilder.LaneX(i), 0f, 0f);
                var carrier = new GameObject("Carrier"); carrier.transform.SetParent(laneGo.transform, false);
                carrier.transform.localPosition = new Vector3(0f, 0f, SettingsAssetBuilder.ModeSettings != null ? SettingsAssetBuilder.ModeSettings.defaultDistance : 10f);
                var target = (GameObject)PrefabUtility.InstantiatePrefab(TargetPrefabBuilder.Target);
                target.transform.SetParent(carrier.transform, false);
                var popPivot = target.transform.Find("PopUpPivot");
                var motion = laneGo.AddComponent<MovingTargetController>();
                motion.Initialize(carrier.transform, popPivot, EnvironmentBuilder.LaneWidth * 0.5f - 0.35f, 4f, EnvironmentBuilder.BackstopZ - 2f);
                var lane = laneGo.AddComponent<TargetLane>();
                lane.Initialize(i, target.GetComponent<BullseyeTarget>(), motion, SettingsAssetBuilder.ModeSettings);
                lanes.Add(lane);
            }
            SetupLog.Ok($"{lanes.Count} lanes with targets");
            return lanes;
        }

        static List<Weapon> BuildWeaponCounter(Transform root)
        {
            var weapons = new List<Weapon>();
            var counter = new GameObject("WeaponSockets"); counter.transform.SetParent(root, false);
            counter.transform.localPosition = new Vector3(0f, 1.14f, EnvironmentBuilder.CounterZ);
            int i = 0;
            foreach (var key in new[] { "pistol", "rifle", "smg" })
            {
                if (!WeaponPrefabBuilder.Prefabs.TryGetValue(key, out var prefab) || prefab == null) { SetupLog.Warn($"Weapon prefab missing: {key}"); i++; continue; }
                var socketGo = new GameObject($"Socket_{key}"); socketGo.transform.SetParent(counter.transform, false);
                socketGo.transform.localPosition = new Vector3(-0.9f + i * 0.9f, 0f, 0f);
                socketGo.transform.localRotation = Quaternion.identity; // weapons rest upright on the counter, muzzles pointing downrange (+Z)
                var trigger = socketGo.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = 0.25f;
                var socket = socketGo.AddComponent<XRSocketInteractor>();
                socket.showInteractableHoverMeshes = false; socket.recycleDelayTime = 0.5f;
                var attach = new GameObject("Attach"); attach.transform.SetParent(socketGo.transform, false); attach.transform.localRotation = Quaternion.identity;
                socket.attachTransform = attach.transform;

                var weaponGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                weaponGo.transform.SetParent(root, false);
                weaponGo.transform.position = attach.transform.position; weaponGo.transform.rotation = attach.transform.rotation;
                var grab = weaponGo.GetComponent<XRGrabInteractable>();
                socket.startingSelectedInteractable = grab;
                var home = weaponGo.GetComponent<WeaponHome>(); if (home != null) home.HomeSocket = socket;
                weapons.Add(weaponGo.GetComponent<Weapon>());
                i++;
            }
            SetupLog.Ok($"{weapons.Count} weapons on the counter");
            return weapons;
        }

        static void BuildRig(Transform root)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangePaths.XrRigPrefab);
            GameObject rig;
            if (rigPrefab != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                rig.transform.SetParent(root, false);
                SetupLog.Ok("XR Origin (XR Rig) instantiated from the XRI Starter Assets sample");
            }
            else
            {
                SetupLog.Error($"XRI Starter Assets rig prefab not found at {RangePaths.XrRigPrefab}. Import the 'Starter Assets' sample of XR Interaction Toolkit (Package Manager > XR Interaction Toolkit > Samples) and rebuild the scene.");
                rig = new GameObject("XR Origin (fallback)");
                rig.transform.SetParent(root, false);
                var origin = rig.AddComponent<XROrigin>();
                var offset = new GameObject("Camera Offset"); offset.transform.SetParent(rig.transform, false);
                var cam = new GameObject("Main Camera"); cam.transform.SetParent(offset.transform, false); cam.tag = "MainCamera";
                var camera = cam.AddComponent<Camera>(); camera.nearClipPlane = 0.05f;
                cam.AddComponent<AudioListener>();
                cam.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                origin.CameraFloorOffsetObject = offset; origin.Camera = camera;
            }
            rig.transform.position = new Vector3(0f, 0f, -1.4f);
            rig.transform.rotation = Quaternion.identity;
            rig.AddComponent<PlayerRigSetup>();
            var vignette = AssetDatabase.LoadAssetAtPath<GameObject>(RangePaths.TunnelingVignettePrefab);
            var cameraTf = rig.GetComponentInChildren<Camera>()?.transform;
            if (vignette != null && cameraTf != null && cameraTf.Find("TunnelingVignette") == null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(vignette); v.transform.SetParent(cameraTf, false);
            }
        }

        static void ConfigureLightingAndAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.46f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.23f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(RangePaths.LightingSettings);
            if (ls == null)
            {
                ls = new LightingSettings { name = "RangeLightingSettings" };
                RangePaths.EnsureFolder(RangePaths.Settings);
                AssetDatabase.CreateAsset(ls, RangePaths.LightingSettings);
            }
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU; // works on every machine; switch to ProgressiveGPU locally for speed
            ls.realtimeGI = false; ls.bakedGI = true; ls.mixedBakeMode = MixedLightingMode.Subtractive;
            ls.lightmapResolution = 6f; ls.lightmapMaxSize = 1024; ls.lightmapPadding = 2; ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directSampleCount = 16; ls.indirectSampleCount = 128; ls.environmentSampleCount = 64; ls.maxBounces = 2;
            ls.ao = true; ls.aoMaxDistance = 1f; ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
            SetupLog.Ok("Lighting settings assigned (baked GI, subtractive mixed lighting; bake via VR Range > Build > Bake Lighting)");
        }

        public static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            bool present = false;
            foreach (var s in EditorBuildSettings.scenes) { if (s.path == RangePaths.MainScene) { present = true; s.enabled = true; } scenes.Add(s); }
            if (!present) scenes.Insert(0, new EditorBuildSettingsScene(RangePaths.MainScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
