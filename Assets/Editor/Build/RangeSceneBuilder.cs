using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using VRRange.Audio;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Modes;
using VRRange.Player;
using VRRange.Recording;
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
            var rig = BuildRig(root);
            BuildSpectatorAndGraphics(root, rig);
            var ui = UiBuilder.Build(root);
            UiBuilder.BuildLaneScoreboards(root, lanes);
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

            var casings = new GameObject("ShellCasingPool"); casings.transform.SetParent(managers.transform, false);
            var cp = casings.AddComponent<ShellCasingPool>();
            if (FxPrefabBuilder.ShellCasing != null) EditorRefs.Set(cp, "m_CasingPrefab", FxPrefabBuilder.ShellCasing.GetComponent<Rigidbody>());
            EditorRefs.SetArray(cp, "m_BounceClips", new Object[] { SettingsAssetBuilder.Clip("Weapons/casing_bounce_1"), SettingsAssetBuilder.Clip("Weapons/casing_bounce_2"), SettingsAssetBuilder.Clip("Weapons/casing_bounce_3") });

            var audio = new GameObject("AudioManager"); audio.transform.SetParent(managers.transform, false);
            var am = audio.AddComponent<AudioManager>();
            var amb = audio.AddComponent<AudioSource>(); amb.playOnAwake = false; amb.loop = true; amb.spatialBlend = 0f;
            EditorRefs.Set(am, "m_OneShotPrefab", FxPrefabBuilder.OneShotAudio.GetComponent<AudioSource>());
            EditorRefs.Set(am, "m_AmbienceSource", amb);
            EditorRefs.Set(am, "m_AmbienceLoop", SettingsAssetBuilder.Clip("Ambience/range_ambience_loop"));
            EditorRefs.Set(am, "m_UiClick", SettingsAssetBuilder.Clip("UI/ui_click"));
            EditorRefs.Set(am, "m_TargetHitDing", SettingsAssetBuilder.Clip("Impacts/target_ding"));
            EditorRefs.SetArray(am, "m_CountdownClips", new Object[] { SettingsAssetBuilder.Clip("UI/countdown_1"), SettingsAssetBuilder.Clip("UI/countdown_2"), SettingsAssetBuilder.Clip("UI/countdown_3"), SettingsAssetBuilder.Clip("UI/countdown_4") });
            EditorRefs.Set(am, "m_RoundComplete", SettingsAssetBuilder.Clip("UI/round_complete"));
            EditorRefs.Set(am, "m_MenuConfirm", SettingsAssetBuilder.Clip("UI/menu_confirm"));
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
                weaponGo.transform.position = socketGo.transform.position; weaponGo.transform.rotation = socketGo.transform.rotation;
                var grab = weaponGo.GetComponent<XRGrabInteractable>();
                // The socket aligns the weapon's own attach transform to the socket attach; mirror that offset so the runtime
                // resting pose equals the authored one (root on the socket, barrel level, muzzle downrange).
                if (grab.attachTransform != null) { attach.transform.localPosition = grab.attachTransform.localPosition; attach.transform.localRotation = grab.attachTransform.localRotation; }
                socket.startingSelectedInteractable = grab;
                var home = weaponGo.GetComponent<WeaponHome>(); if (home != null) home.HomeSocket = socket;
                weapons.Add(weaponGo.GetComponent<Weapon>());
                i++;
            }
            SetupLog.Ok($"{weapons.Count} weapons on the counter");
            return weapons;
        }

        static GameObject BuildRig(Transform root)
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
            EditorRefs.Ensure<PlayerRigSetup>(rig);
            BuildHands(rig);
            var vignette = AssetDatabase.LoadAssetAtPath<GameObject>(RangePaths.TunnelingVignettePrefab);
            var cameraTf = rig.GetComponentInChildren<Camera>()?.transform;
            if (vignette != null && cameraTf != null && cameraTf.Find("TunnelingVignette") == null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(vignette); v.transform.SetParent(cameraTf, false);
                // headset-only overlay: the spectator camera culls this layer so recordings never show the vignette mesh
                int headsetOnly = LayerMask.NameToLayer(ProjectSettingsSetup.HeadsetOnlyLayer);
                if (headsetOnly >= 0) foreach (var t in v.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = headsetOnly;
            }
            return rig;
        }

        /// <summary>Desktop spectator cameras (PCVR recording) and the per-platform graphics switch (post volume, PCVR shadow lights).</summary>
        static void BuildSpectatorAndGraphics(Transform root, GameObject rig)
        {
            var xrCamera = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            var specRoot = new GameObject("SpectatorRig"); specRoot.transform.SetParent(root, false);
            var camGo = new GameObject("SpectatorCamera"); camGo.transform.SetParent(specRoot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            var barsGo = new GameObject("LetterboxBars"); barsGo.transform.SetParent(specRoot.transform, false);
            var bars = barsGo.AddComponent<Camera>();
            var spectator = specRoot.AddComponent<SpectatorCamera>();
            spectator.Configure(cam, bars, xrCamera != null ? xrCamera.transform : null);
            int headsetOnly = LayerMask.NameToLayer(ProjectSettingsSetup.HeadsetOnlyLayer);
            cam.cullingMask = headsetOnly >= 0 ? ~(1 << headsetOnly) : ~0;
            var camData = cam.GetUniversalAdditionalCameraData(); if (camData != null) { camData.allowXRRendering = false; camData.renderPostProcessing = false; camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None; }
            var barsData = bars.GetUniversalAdditionalCameraData(); if (barsData != null) { barsData.allowXRRendering = false; barsData.renderPostProcessing = false; barsData.renderShadows = false; }

            var managers = root.Find("Managers");
            var volGo = new GameObject("PostProcessVolume (PCVR)"); volGo.transform.SetParent(managers != null ? managers : root, false);
            var volume = volGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true; volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(RangePaths.PcvrPostProfile);
            volume.enabled = false; // PlatformGraphicsSetup turns it on for PCVR at runtime
            var gfx = (managers != null ? managers.gameObject : volGo).AddComponent<PlatformGraphicsSetup>();
            gfx.Configure(volume, EnvironmentBuilder.PcvrShadowLights.ToArray(), new[] { xrCamera, cam });
            SetupLog.Ok($"Spectator camera rig (desktop only, 16:9, F1-F6 hotkeys) and platform graphics switch added ({EnvironmentBuilder.PcvrShadowLights.Count} PCVR shadow lights)");
        }

        /// <summary>Adds visible, animated hands (XR Hands sample rig) under each controller of the rig.</summary>
        static void BuildHands(GameObject rig)
        {
            int built = 0;
            foreach (var isLeft in new[] { true, false })
            {
                // per-hand skin set baked by Tools/AssetGen/blender_process.py (albedo with AO-driven knuckle/crease tone, roughness, AO)
                var skin = MaterialLibrary.PbrFromFolder(RangePaths.ArtHands, isLeft ? "skin_left" : "skin_right", isLeft ? "HandSkinLeft" : "HandSkinRight", 0.42f);
                string controllerName = isLeft ? "Left Controller" : "Right Controller";
                Transform controller = null;
                foreach (var t in rig.GetComponentsInChildren<Transform>(true)) if (t.name == controllerName) { controller = t; break; }
                if (controller == null) { SetupLog.Warn($"{controllerName} not found in the rig; hand not added"); continue; }
                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(isLeft ? RangePaths.HandLeftModel : RangePaths.HandRightModel);
                if (modelAsset == null) { SetupLog.Warn($"Hand model missing: {(isLeft ? RangePaths.HandLeftModel : RangePaths.HandRightModel)}"); continue; }
                var holder = EditorRefs.Child(controller.gameObject, "Hand Visual");
                EditorRefs.DestroyChildren(holder.transform);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.transform.SetParent(holder.transform, false);
                model.name = isLeft ? "LeftHandModel" : "RightHandModel";
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = skin;
                    r.sharedMaterials = mats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                var hand = EditorRefs.Ensure<HandPresence>(holder);
                hand.Configure(isLeft, model.transform, controller, SettingsAssetBuilder.Pose("Open"), SettingsAssetBuilder.Pose("Fist"), SettingsAssetBuilder.Pose("Idle"), SettingsAssetBuilder.Pose("Pinch"));
                built++;
            }
            SetupLog.Ok($"{built} animated hands added to the rig");
        }

        static void ConfigureLightingAndAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            // interim ambient (brighter than a baked scene needs) so unbaked builds are readable; the bake tones it down via baked GI
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.68f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.51f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.29f, 0.28f);
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
            // the cloud container bakes this in a few minutes on 4 CPU cores; local GPU bakes are faster still
            ls.lightmapResolution = 16f; ls.lightmapMaxSize = 2048; ls.lightmapPadding = 2; ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directSampleCount = 32; ls.indirectSampleCount = 256; ls.environmentSampleCount = 128; ls.maxBounces = 3;
            ls.ao = true; ls.aoMaxDistance = 0.8f; ls.aoExponentIndirect = 1.2f; ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
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
