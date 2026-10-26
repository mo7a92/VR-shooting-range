using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// Builds the indoor range: room shell, lane booths, ceiling rails, backstop, counter, lights, signs and props.
    /// Layout: firing line at z = 0, shooting area behind it (z &lt; 0), targets downrange (z &gt; 0), lanes along x.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public const int LaneCount = 5;
        public const float LaneWidth = 1.6f;
        public const float RoomWidth = LaneCount * LaneWidth + 1.2f;   // 9.2 m
        public const float RoomHeight = 3.2f;
        public const float BackWallZ = -6.5f;
        public const float BackstopZ = 29f;
        public const float RailHeight = TargetPrefabBuilder.RailHeight;
        public const float CounterZ = -3.2f;

        public static float LaneX(int lane) => (lane - (LaneCount - 1) * 0.5f) * LaneWidth;

        public static GameObject Build(Transform sceneRoot)
        {
            SetupLog.Section("Environment");
            var root = new GameObject("Environment"); root.transform.SetParent(sceneRoot, false);
            int envLayer = EditorRefs.Layer("Environment");
            var floorMat = MaterialLibrary.Pbr("floor_concrete", "FloorConcrete", 1f);
            var wallMat = MaterialLibrary.Pbr("wall_block_painted", "WallBlock", 1f);
            var ceilMat = MaterialLibrary.Pbr("ceiling_acoustic", "CeilingAcoustic", 1f);
            var rubberMat = MaterialLibrary.Pbr("backstop_rubber", "BackstopRubber", 1f);
            var woodMat = MaterialLibrary.Pbr("counter_wood", "CounterWood", 1f);
            var metalMat = MaterialLibrary.Pbr("metal_brushed", "RailMetal", 2f);
            var panelMat = MaterialLibrary.Pbr("divider_panel", "DividerPanel", 1f);
            var darkMat = MaterialLibrary.Flat("DarkMetal", new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.7f);
            var yellowMat = MaterialLibrary.Flat("SafetyYellow", new Color(0.95f, 0.78f, 0.1f), 0.3f);
            var redMat = MaterialLibrary.Flat("SafetyRed", new Color(0.75f, 0.08f, 0.06f), 0.4f);

            float depth = BackstopZ - BackWallZ + 1f; float zc = (BackstopZ + BackWallZ) * 0.5f;
            // Shell
            Static(EditorRefs.MeshObject(root, "Floor", EditorMeshUtil.Box("Floor", new Vector3(RoomWidth, 0.1f, depth), 0.5f), new[] { floorMat }, new Vector3(0f, -0.05f, zc), default, envLayer, true), SurfaceType.Concrete);
            Static(EditorRefs.MeshObject(root, "Ceiling", EditorMeshUtil.Box("Ceiling", new Vector3(RoomWidth, 0.1f, depth), 0.5f), new[] { ceilMat }, new Vector3(0f, RoomHeight + 0.05f, zc), default, envLayer, true), SurfaceType.Concrete);
            Static(EditorRefs.MeshObject(root, "WallLeft", EditorMeshUtil.Box("WallSide", new Vector3(0.2f, RoomHeight, depth), 0.5f), new[] { wallMat }, new Vector3(-RoomWidth * 0.5f - 0.1f, RoomHeight * 0.5f, zc), default, envLayer, true), SurfaceType.Concrete);
            Static(EditorRefs.MeshObject(root, "WallRight", EditorMeshUtil.Box("WallSide", new Vector3(0.2f, RoomHeight, depth), 0.5f), new[] { wallMat }, new Vector3(RoomWidth * 0.5f + 0.1f, RoomHeight * 0.5f, zc), default, envLayer, true), SurfaceType.Concrete);
            Static(EditorRefs.MeshObject(root, "WallBack", EditorMeshUtil.Box("WallEnd", new Vector3(RoomWidth + 0.4f, RoomHeight, 0.2f), 0.5f), new[] { wallMat }, new Vector3(0f, RoomHeight * 0.5f, BackWallZ - 0.1f), default, envLayer, true), SurfaceType.Concrete);
            // Backstop: rubber wall plus angled deflector plates
            Static(EditorRefs.MeshObject(root, "Backstop", EditorMeshUtil.Box("Backstop", new Vector3(RoomWidth + 0.4f, RoomHeight, 0.3f), 0.5f), new[] { rubberMat }, new Vector3(0f, RoomHeight * 0.5f, BackstopZ + 0.15f), default, envLayer, true), SurfaceType.Rubber);
            for (int i = 0; i < 3; i++)
                Static(EditorRefs.MeshObject(root, $"Deflector{i}", EditorMeshUtil.Box("Deflector", new Vector3(RoomWidth, 0.9f, 0.06f), 0.5f), new[] { darkMat }, new Vector3(0f, 0.6f + i * 0.9f, BackstopZ - 0.35f - i * 0.1f), new Vector3(-35f, 0f, 0f), envLayer, true), SurfaceType.Metal);
            Static(EditorRefs.MeshObject(root, "BackstopBerm", EditorMeshUtil.Box("Berm", new Vector3(RoomWidth, 0.5f, 1.2f), 0.5f), new[] { rubberMat }, new Vector3(0f, 0.25f, BackstopZ - 0.8f), default, envLayer, true), SurfaceType.Rubber);

            // Firing line stripe + lane booths
            Static(EditorRefs.MeshObject(root, "FiringLine", EditorMeshUtil.Box("FiringLine", new Vector3(RoomWidth, 0.01f, 0.12f), 1f), new[] { yellowMat }, new Vector3(0f, 0.005f, 0f), default, envLayer, true), SurfaceType.Concrete);
            Static(EditorRefs.MeshObject(root, "CeilingBeam", EditorMeshUtil.Box("CeilingBeam", new Vector3(RoomWidth, 0.25f, 0.25f), 1f), new[] { darkMat }, new Vector3(0f, RoomHeight - 0.125f, 0.9f), default, envLayer, true), SurfaceType.Metal);
            var dividerMesh = EditorMeshUtil.Box("Divider", new Vector3(0.08f, 2.1f, 1.6f), 1f);
            var shelfMesh = EditorMeshUtil.Box("Shelf", new Vector3(LaneWidth - 0.1f, 0.05f, 0.45f), 1f);
            var shelfLipMesh = EditorMeshUtil.Box("ShelfLip", new Vector3(LaneWidth - 0.1f, 0.12f, 0.03f), 1f);
            var railMesh = EditorMeshUtil.Box("TargetRail", new Vector3(0.08f, 0.1f, BackstopZ - 1.0f), 1f);
            for (int i = 0; i <= LaneCount; i++)
            {
                float x = -LaneCount * LaneWidth * 0.5f + i * LaneWidth;
                Static(EditorRefs.MeshObject(root, $"Divider{i}", dividerMesh, new[] { panelMat }, new Vector3(x, 1.05f, 0.2f), default, envLayer, true), SurfaceType.Wood);
            }
            for (int lane = 0; lane < LaneCount; lane++)
            {
                float x = LaneX(lane);
                Static(EditorRefs.MeshObject(root, $"Shelf{lane}", shelfMesh, new[] { woodMat }, new Vector3(x, 1.0f, 0.55f), default, envLayer, true), SurfaceType.Wood);
                Static(EditorRefs.MeshObject(root, $"ShelfLip{lane}", shelfLipMesh, new[] { woodMat }, new Vector3(x, 1.06f, 0.765f), default, envLayer, true), SurfaceType.Wood);
                Static(EditorRefs.MeshObject(root, $"Rail{lane}", railMesh, new[] { metalMat }, new Vector3(x, RailHeight + 0.05f, (BackstopZ - 1.0f) * 0.5f + 0.5f), default, envLayer, true), SurfaceType.Metal);
                Sign(root, $"LaneNumber{lane}", $"{RangePaths.ArtSigns}/lane_{lane + 1}.png", new Vector3(x, 2.4f, 0.9f), Vector3.zero, 0.3f, 0.3f);
            }
            // Distance markers on the floor and the left wall
            foreach (var d in new[] { 5, 10, 15, 25 })
            {
                Sign(root, $"DistanceFloor{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(-RoomWidth * 0.5f + 0.45f, 0.011f, d), new Vector3(90f, 0f, 0f), 0.6f, 0.3f);
                Sign(root, $"DistanceFloorR{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(RoomWidth * 0.5f - 0.45f, 0.011f, d), new Vector3(90f, 0f, 0f), 0.6f, 0.3f);
                Sign(root, $"DistanceWall{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.2f, d), new Vector3(0f, -90f, 0f), 0.8f, 0.4f);
                Static(EditorRefs.MeshObject(root, $"DistanceStripe{d}", EditorMeshUtil.Box("DistanceStripe", new Vector3(RoomWidth, 0.008f, 0.06f), 1f), new[] { MaterialLibrary.Flat("FloorStripeWhite", new Color(0.85f, 0.85f, 0.82f), 0.2f) }, new Vector3(0f, 0.004f, d), default, envLayer, true), SurfaceType.Concrete);
            }

            // Weapon counter behind the firing line
            var counter = EditorRefs.Child(root, "WeaponCounter", new Vector3(0f, 0f, CounterZ));
            Static(EditorRefs.MeshObject(counter, "CounterTop", EditorMeshUtil.Box("CounterTop", new Vector3(3.0f, 0.06f, 0.8f), 1f), new[] { woodMat }, new Vector3(0f, 0.97f, 0f), default, envLayer, true), SurfaceType.Wood);
            Static(EditorRefs.MeshObject(counter, "CounterBase", EditorMeshUtil.Box("CounterBase", new Vector3(2.9f, 0.94f, 0.7f), 1f), new[] { panelMat }, new Vector3(0f, 0.47f, 0f), default, envLayer, true), SurfaceType.Wood);
            Static(EditorRefs.MeshObject(counter, "CounterFoam", EditorMeshUtil.Box("CounterFoam", new Vector3(2.8f, 0.03f, 0.6f), 1f), new[] { MaterialLibrary.Flat("GreyFoam", new Color(0.2f, 0.2f, 0.22f), 0.05f) }, new Vector3(0f, 1.015f, 0f), default, envLayer, true), SurfaceType.Rubber);
            Sign(counter, "CounterSign", $"{RangePaths.ArtSigns}/sign_counter.png", new Vector3(0f, 2.3f, -0.3f), new Vector3(0f, 180f, 0f), 1.6f, 0.6f);
            for (int i = 0; i < 3; i++)
                Static(EditorRefs.MeshObject(counter, $"Stand{i}", EditorMeshUtil.Box("WeaponStand", new Vector3(0.12f, 0.06f, 0.08f), 2f), new[] { darkMat }, new Vector3(-0.9f + i * 0.9f, 1.06f, 0f), default, envLayer, true), SurfaceType.Metal);

            // Signs around the shooting area
            Sign(root, "SignWelcome", $"{RangePaths.ArtSigns}/sign_welcome.png", new Vector3(0f, 2.2f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 2.0f, 1.0f);
            Sign(root, "SignControls", $"{RangePaths.ArtSigns}/sign_controls.png", new Vector3(-2.6f, 1.9f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignRules", $"{RangePaths.ArtSigns}/sign_rules.png", new Vector3(2.6f, 1.9f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignSafetyLeft", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.0f, -3.5f), new Vector3(0f, -90f, 0f), 1.4f, 0.7f);
            Sign(root, "SignSafetyRight", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(RoomWidth * 0.5f - 0.01f, 2.0f, -3.5f), new Vector3(0f, 90f, 0f), 1.4f, 0.7f);
            Sign(root, "SignFiringLine", $"{RangePaths.ArtSigns}/sign_firing_line.png", new Vector3(0f, 2.75f, 0.77f), Vector3.zero, 2.0f, 1.0f);

            // Props: bench, bins, extinguisher, door, ammo crates, ceiling vents
            Static(EditorRefs.MeshObject(root, "Bench", EditorMeshUtil.Box("Bench", new Vector3(2.0f, 0.45f, 0.45f), 1f), new[] { woodMat }, new Vector3(-3.2f, 0.225f, BackWallZ + 0.45f), default, envLayer, true), SurfaceType.Wood);
            Static(EditorRefs.MeshObject(root, "Bin", EditorMeshUtil.Cylinder("Bin", 0.22f, 0.7f, 16, false), new[] { darkMat }, new Vector3(3.6f, 0.35f, BackWallZ + 0.5f), default, envLayer, true), SurfaceType.Metal);
            Static(EditorRefs.MeshObject(root, "Extinguisher", EditorMeshUtil.Cylinder("Extinguisher", 0.08f, 0.5f, 12, false), new[] { redMat }, new Vector3(RoomWidth * 0.5f - 0.12f, 1.0f, -5.0f), default, envLayer, true), SurfaceType.Metal);
            Static(EditorRefs.MeshObject(root, "DoorFrame", EditorMeshUtil.Box("DoorFrame", new Vector3(1.1f, 2.2f, 0.06f), 1f), new[] { darkMat }, new Vector3(-RoomWidth * 0.5f + 0.55f + 0.6f, 1.1f, BackWallZ + 0.02f), default, envLayer, true), SurfaceType.Metal);
            Static(EditorRefs.MeshObject(root, "Door", EditorMeshUtil.Box("Door", new Vector3(0.95f, 2.1f, 0.04f), 1f), new[] { MaterialLibrary.Flat("DoorGrey", new Color(0.35f, 0.37f, 0.4f), 0.5f, 0.3f) }, new Vector3(-RoomWidth * 0.5f + 1.15f, 1.05f, BackWallZ + 0.04f), default, envLayer, true), SurfaceType.Metal);
            for (int i = 0; i < 4; i++)
                Static(EditorRefs.MeshObject(root, $"AmmoCrate{i}", EditorMeshUtil.Box("AmmoCrate", new Vector3(0.4f, 0.22f, 0.28f), 2f), new[] { MaterialLibrary.Pbr("polymer_dark", "CrateDark", 2f) }, new Vector3(2.0f + (i % 2) * 0.45f, 0.11f + (i / 2) * 0.23f, BackWallZ + 0.35f), new Vector3(0f, (i % 2) * 6f, 0f), envLayer, true), SurfaceType.Wood);
            for (int i = 0; i < 6; i++)
                Static(EditorRefs.MeshObject(root, $"Vent{i}", EditorMeshUtil.Box("Vent", new Vector3(0.6f, 0.08f, 0.6f), 2f), new[] { metalMat }, new Vector3(-3f + (i % 3) * 3f, RoomHeight - 0.04f, -4.5f + (i / 3) * 10f), default, envLayer, true), SurfaceType.Metal);

            BuildLights(root);
            BuildTeleportArea(root);
            SetupLog.Ok("Environment built (room, 5 lanes, rails, backstop, counter, signs, props, lights)");
            return root;
        }

        static void Static(GameObject go, SurfaceType surface)
        {
            EditorRefs.Ensure<SurfaceMaterialTag>(go).surface = surface;
        }

        /// <summary>Quad signs face -Z at identity rotation (readable by a viewer at smaller z looking downrange); use Y=180 for signs behind the player.</summary>
        static void Sign(GameObject parent, string name, string texturePath, Vector3 pos, Vector3 euler, float width, float height)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) { SetupLog.Warn($"Sign texture missing: {texturePath}"); return; }
            var matName = "Sign_" + System.IO.Path.GetFileNameWithoutExtension(texturePath);
            var mat = MaterialLibrary.UnlitTexture(matName, tex, false, true);
            var mesh = EditorMeshUtil.Quad($"SignQuad_{width:0.00}x{height:0.00}".Replace('.', '_'), width, height);
            var go = EditorRefs.MeshObject(parent, name, mesh, new[] { mat }, pos, euler, EditorRefs.Layer("Environment"), false, true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(width, height, 0.01f);
            EditorRefs.Ensure<SurfaceMaterialTag>(go).surface = SurfaceType.Wood;
        }

        /// <summary>Ceiling light panels: emissive quads with baked rectangle lights, plus a few mixed point lights so the scene is lit before baking.</summary>
        static void BuildLights(GameObject root)
        {
            var lights = EditorRefs.Child(root, "Lights");
            var panelTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/light_panel_albedo.png");
            var panelMat = MaterialLibrary.UnlitTexture("LightPanel", panelTex, false, true, new Color(1.6f, 1.6f, 1.5f));
            var panelMesh = EditorMeshUtil.Quad("LightPanelQuad", 1.2f, 0.35f);
            int idx = 0;
            for (float z = BackWallZ + 1.5f; z < BackstopZ - 1f; z += 3.5f)
            {
                for (int col = -1; col <= 1; col++)
                {
                    float x = col * 3.0f;
                    var panel = EditorRefs.MeshObject(lights, $"LightPanel{idx}", panelMesh, new[] { panelMat }, new Vector3(x, RoomHeight - 0.02f, z), new Vector3(-90f, 0f, 0f), EditorRefs.Layer("Environment"), false, true);
                    panel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var lgo = EditorRefs.Child(lights, $"AreaLight{idx}", new Vector3(x, RoomHeight - 0.08f, z), new Vector3(90f, 0f, 0f));
                    var l = EditorRefs.Ensure<Light>(lgo);
                    l.type = LightType.Rectangle; l.areaSize = new Vector2(1.2f, 0.35f); l.intensity = 25f; l.color = new Color(1f, 0.97f, 0.9f); l.range = 12f;
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    idx++;
                }
            }
            // Mixed point lights over the shooting area and the first 10 m of lanes (realtime until baked; limited count for Quest)
            int p = 0;
            foreach (var z in new[] { -4.5f, -1.5f, 3f, 9f })
            {
                foreach (var x in new[] { -2.4f, 2.4f })
                {
                    var lgo = EditorRefs.Child(lights, $"FillLight{p++}", new Vector3(x, RoomHeight - 0.3f, z));
                    var l = EditorRefs.Ensure<Light>(lgo);
                    l.type = LightType.Point; l.range = 9f; l.intensity = 1.6f; l.color = new Color(1f, 0.96f, 0.9f);
                    l.lightmapBakeType = LightmapBakeType.Mixed; l.shadows = LightShadows.None;
                }
            }
            // One soft baked directional for ambient fill on dynamic objects via probes
            var dgo = EditorRefs.Child(lights, "FillDirectional", new Vector3(0f, 3f, 0f), new Vector3(60f, 30f, 0f));
            var d = EditorRefs.Ensure<Light>(dgo); d.type = LightType.Directional; d.intensity = 0.35f; d.color = new Color(0.95f, 0.95f, 1f);
            d.lightmapBakeType = LightmapBakeType.Mixed; d.shadows = LightShadows.Soft; d.shadowStrength = 0.6f;

            // Light probes for dynamic objects (weapons, targets) and a reflection probe
            var lpg = EditorRefs.Child(lights, "LightProbes"); var group = EditorRefs.Ensure<LightProbeGroup>(lpg);
            var probes = new System.Collections.Generic.List<Vector3>();
            for (float z = BackWallZ + 0.5f; z <= BackstopZ - 0.5f; z += (z < 2f ? 1.5f : 4f))
                for (float x = -RoomWidth * 0.5f + 0.6f; x <= RoomWidth * 0.5f - 0.6f; x += 1.6f)
                    foreach (var y in new[] { 0.3f, 1.4f, 2.6f }) probes.Add(new Vector3(x, y, z));
            group.probePositions = probes.ToArray();
            var rp = EditorRefs.Child(lights, "ReflectionProbe", new Vector3(0f, 1.6f, -2f));
            var probe = EditorRefs.Ensure<ReflectionProbe>(rp);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked; probe.size = new Vector3(RoomWidth, RoomHeight, 14f); probe.resolution = 128; probe.boxProjection = true;
        }

        /// <summary>Teleport area covering the shooting area behind the firing line (XRI Starter Assets rig, Teleport interaction layer).</summary>
        static void BuildTeleportArea(GameObject root)
        {
            var go = EditorRefs.Child(root, "TeleportArea", new Vector3(0f, 0.005f, (BackWallZ + 0.4f - 0.3f) * 0.5f));
            var bc = EditorRefs.Ensure<BoxCollider>(go); bc.size = new Vector3(RoomWidth - 0.6f, 0.01f, -BackWallZ - 0.8f); bc.center = Vector3.zero;
            var area = EditorRefs.Ensure<TeleportationArea>(go);
            area.interactionLayers = InteractionLayerMask.GetMask(ProjectSettingsSetup.TeleportInteractionLayer);
            area.matchOrientation = MatchOrientation.WorldSpaceUp;
            go.layer = EditorRefs.Layer("Environment");
        }
    }
}
