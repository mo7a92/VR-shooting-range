using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// Builds the indoor range: room shell with real PBR materials, five booths with acoustic/glass dividers, ceiling
    /// rails and baffles, backstop, counter, waiting area, signage, lights, props and the teleport area.
    /// Layout: firing line at z = 0, shooting area behind it (z &lt; 0), targets downrange (z &gt; 0), lanes along x.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public const int LaneCount = 5;
        public const float LaneWidth = 1.6f;
        public const float RoomWidth = LaneCount * LaneWidth + 1.2f;   // 9.2 m
        public const float RoomHeight = 3.2f;
        public const float BackWallZ = -7.5f;
        public const float BackstopZ = 29f;
        public const float RailHeight = TargetPrefabBuilder.RailHeight;
        public const float CounterZ = -3.2f;

        static int s_EnvLayer;
        static Material s_Floor, s_FloorTiles, s_Carpet, s_Wall, s_WallRough, s_Rubber, s_MetalBrushed, s_MetalDark, s_Wood, s_Fabric, s_Dark, s_Yellow, s_Red, s_Glass, s_White, s_Screen;

        public static float LaneX(int lane) => (lane - (LaneCount - 1) * 0.5f) * LaneWidth;

        public static GameObject Build(Transform sceneRoot)
        {
            SetupLog.Section("Environment");
            var root = new GameObject("Environment"); root.transform.SetParent(sceneRoot, false);
            s_EnvLayer = EditorRefs.Layer("Environment");
            s_Floor = MaterialLibrary.Pbr("pbr_floor_concrete", "FloorConcrete", 1f);
            s_FloorTiles = MaterialLibrary.Pbr("pbr_floor_tiles", "FloorTiles", 1f);
            s_Carpet = MaterialLibrary.Pbr("pbr_carpet", "Carpet", 1f);
            s_Wall = MaterialLibrary.Pbr("pbr_wall_concrete", "WallConcrete", 1f, 0.3f, new Color(0.92f, 0.92f, 0.9f));
            s_WallRough = MaterialLibrary.Pbr("pbr_backstop_concrete", "WallRoughConcrete", 1f);
            s_Rubber = MaterialLibrary.Pbr("pbr_rubber", "Rubber", 1f);
            s_MetalBrushed = MaterialLibrary.Pbr("pbr_metal_brushed", "MetalBrushed", 2f);
            s_MetalDark = MaterialLibrary.Pbr("pbr_metal_dark", "MetalDark", 2f);
            s_Wood = MaterialLibrary.Pbr("pbr_wood", "Wood", 1f);
            s_Fabric = MaterialLibrary.Pbr("pbr_acoustic_fabric", "AcousticFabric", 2f, 0.1f, new Color(0.75f, 0.74f, 0.7f));
            s_Dark = MaterialLibrary.Flat("DarkMetal", new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.7f);
            s_Yellow = MaterialLibrary.Flat("SafetyYellow", new Color(0.95f, 0.78f, 0.1f), 0.3f);
            s_Red = MaterialLibrary.Flat("SafetyRed", new Color(0.75f, 0.08f, 0.06f), 0.4f);
            s_White = MaterialLibrary.Flat("PaintWhite", new Color(0.9f, 0.9f, 0.88f), 0.35f);
            s_Screen = MaterialLibrary.Flat("ScreenDark", new Color(0.02f, 0.03f, 0.04f), 0.9f, 0f, new Color(0.05f, 0.12f, 0.2f));
            s_Glass = MaterialLibrary.UnlitTexture("BoothGlass", null, transparent: true, lit: true);
            s_Glass.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f, 0.18f)); s_Glass.SetFloat("_Smoothness", 0.95f);

            BuildShell(root);
            BuildBooths(root);
            BuildDownrange(root);
            BuildCounterAndWaitingArea(root);
            BuildSigns(root);
            BuildLights(root);
            BuildTeleportArea(root);
            SetupLog.Ok("Environment built (PBR shell, 5 booths, rails, baffles, backstop, counter, waiting area, signs, props, lights)");
            return root;
        }

        static GameObject Static(GameObject go, SurfaceType surface) { EditorRefs.Ensure<SurfaceMaterialTag>(go).surface = surface; return go; }
        static GameObject Box(GameObject parent, string name, string meshName, Vector3 size, Material mat, Vector3 pos, Vector3 euler = default, SurfaceType surface = SurfaceType.Concrete, float uv = 1f, bool collider = true)
            => Static(EditorRefs.MeshObject(parent, name, EditorMeshUtil.Box(meshName, size, uv), new[] { mat }, pos, euler, s_EnvLayer, collider, true), surface);
        static GameObject Cyl(GameObject parent, string name, string meshName, float radius, float length, Material mat, Vector3 pos, Vector3 euler = default, bool alongZ = true, SurfaceType surface = SurfaceType.Metal, int segs = 16)
            => Static(EditorRefs.MeshObject(parent, name, EditorMeshUtil.Cylinder(meshName, radius, length, segs, alongZ), new[] { mat }, pos, euler, s_EnvLayer, true, true), surface);

        static void BuildShell(GameObject root)
        {
            float depth = BackstopZ - BackWallZ + 1f; float zc = (BackstopZ + BackWallZ) * 0.5f;
            float rangeDepth = BackstopZ - (-4.5f) + 0.5f; float rangeZc = (BackstopZ + -4.5f) * 0.5f;
            // floors: polished concrete in the range, tiles in the waiting area, a carpet in front of the benches
            Box(root, "FloorRange", "FloorRange", new Vector3(RoomWidth, 0.1f, rangeDepth), s_Floor, new Vector3(0f, -0.05f, rangeZc), default, SurfaceType.Concrete, 0.5f);
            Box(root, "FloorWaiting", "FloorWaiting", new Vector3(RoomWidth, 0.1f, 3.0f + 0.5f), s_FloorTiles, new Vector3(0f, -0.05f, BackWallZ + 1.5f), default, SurfaceType.Concrete, 1f);
            Box(root, "FloorCarpet", "FloorCarpet", new Vector3(4.0f, 0.012f, 1.6f), s_Carpet, new Vector3(-2.2f, 0.006f, BackWallZ + 1.2f), default, SurfaceType.Rubber, 1f);
            Box(root, "Ceiling", "Ceiling", new Vector3(RoomWidth, 0.1f, depth), MaterialLibrary.Pbr("ceiling_acoustic", "CeilingAcoustic", 1f), new Vector3(0f, RoomHeight + 0.05f, zc), default, SurfaceType.Concrete, 0.5f);
            // walls with an acoustic panel dado downrange and a rough concrete base
            foreach (var side in new[] { -1f, 1f })
            {
                string n = side < 0 ? "Left" : "Right";
                Box(root, $"Wall{n}", "WallSide", new Vector3(0.2f, RoomHeight, depth), s_Wall, new Vector3(side * (RoomWidth * 0.5f + 0.1f), RoomHeight * 0.5f, zc), default, SurfaceType.Concrete, 0.5f);
                Box(root, $"WallBase{n}", "WallBase", new Vector3(0.05f, 0.9f, depth), s_WallRough, new Vector3(side * (RoomWidth * 0.5f - 0.025f), 0.45f, zc), default, SurfaceType.Concrete, 0.5f, false);
                for (float z = 1f; z < BackstopZ - 2f; z += 2.4f)
                    Box(root, $"AcousticPanel{n}{z:0}", "AcousticPanel", new Vector3(0.06f, 1.2f, 2.0f), s_Fabric, new Vector3(side * (RoomWidth * 0.5f - 0.03f), 1.7f, z), default, SurfaceType.Wood, 1f, false);
                Box(root, $"CableTray{n}", "CableTray", new Vector3(0.12f, 0.06f, depth - 2f), s_MetalDark, new Vector3(side * (RoomWidth * 0.5f - 0.1f), RoomHeight - 0.25f, zc), default, SurfaceType.Metal, 1f, false);
            }
            Box(root, "WallBack", "WallEnd", new Vector3(RoomWidth + 0.4f, RoomHeight, 0.2f), s_Wall, new Vector3(0f, RoomHeight * 0.5f, BackWallZ - 0.1f), default, SurfaceType.Concrete, 0.5f);
            Box(root, "WallBackDado", "WallBackDado", new Vector3(RoomWidth, 1.1f, 0.04f), s_Wood, new Vector3(0f, 0.55f, BackWallZ + 0.02f), default, SurfaceType.Wood, 1f, false);
            // ventilation duct along the ceiling with hangers and a vent grille every 6 m
            Cyl(root, "Duct", "Duct", 0.3f, depth - 1f, s_MetalBrushed, new Vector3(0f, RoomHeight - 0.45f, zc), default, true, SurfaceType.Metal, 14);
            for (float z = BackWallZ + 2f; z < BackstopZ - 1f; z += 6f)
            {
                Box(root, $"DuctVent{z:0}", "DuctVent", new Vector3(0.5f, 0.06f, 0.5f), s_MetalDark, new Vector3(0f, RoomHeight - 0.78f, z), default, SurfaceType.Metal, 2f, false);
                Box(root, $"DuctHanger{z:0}", "DuctHanger", new Vector3(0.03f, 0.3f, 0.03f), s_MetalDark, new Vector3(0f, RoomHeight - 0.15f, z), default, SurfaceType.Metal, 1f, false);
            }
        }

        static void BuildBooths(GameObject root)
        {
            Box(root, "FiringLine", "FiringLine", new Vector3(RoomWidth, 0.01f, 0.12f), s_Yellow, new Vector3(0f, 0.005f, 0f), default, SurfaceType.Concrete, 1f, false);
            Box(root, "FiringLineText", "FiringLineInner", new Vector3(RoomWidth, 0.011f, 0.02f), s_Dark, new Vector3(0f, 0.005f, 0.07f), default, SurfaceType.Concrete, 1f, false);
            Box(root, "CeilingBeam", "CeilingBeam", new Vector3(RoomWidth, 0.3f, 0.3f), s_MetalDark, new Vector3(0f, RoomHeight - 0.15f, 0.95f), default, SurfaceType.Metal, 1f);
            var frameMesh = EditorMeshUtil.Box("DividerFrame", new Vector3(0.06f, 2.3f, 0.08f), 1f);
            for (int i = 0; i <= LaneCount; i++)
            {
                float x = -LaneCount * LaneWidth * 0.5f + i * LaneWidth;
                var booth = EditorRefs.Child(root, $"Divider{i}", new Vector3(x, 0f, 0f));
                // steel frame posts + lower acoustic panel + upper safety glass + top rail
                Static(EditorRefs.MeshObject(booth, "PostFront", frameMesh, new[] { s_MetalDark }, new Vector3(0f, 1.15f, 0.95f), default, s_EnvLayer, true, true), SurfaceType.Metal);
                Static(EditorRefs.MeshObject(booth, "PostBack", frameMesh, new[] { s_MetalDark }, new Vector3(0f, 1.15f, -0.65f), default, s_EnvLayer, true, true), SurfaceType.Metal);
                Box(booth, "PanelLower", "DividerPanelLower", new Vector3(0.05f, 1.25f, 1.6f), s_Fabric, new Vector3(0f, 0.65f, 0.15f), default, SurfaceType.Wood, 1f);
                Box(booth, "PanelGlass", "DividerGlass", new Vector3(0.012f, 1.0f, 1.55f), s_Glass, new Vector3(0f, 1.78f, 0.15f), default, SurfaceType.Metal, 1f);
                Box(booth, "TopRail", "DividerTopRail", new Vector3(0.07f, 0.05f, 1.7f), s_MetalDark, new Vector3(0f, 2.3f, 0.15f), default, SurfaceType.Metal, 1f, false);
            }
            for (int lane = 0; lane < LaneCount; lane++)
            {
                float x = LaneX(lane);
                var b = EditorRefs.Child(root, $"Booth{lane + 1}", new Vector3(x, 0f, 0f));
                Box(b, "Shelf", "Shelf", new Vector3(LaneWidth - 0.14f, 0.05f, 0.5f), s_Wood, new Vector3(0f, 1.0f, 0.55f), default, SurfaceType.Wood, 1f);
                Box(b, "ShelfLip", "ShelfLip", new Vector3(LaneWidth - 0.14f, 0.1f, 0.03f), s_Wood, new Vector3(0f, 1.055f, 0.79f), default, SurfaceType.Wood, 1f, false);
                Box(b, "ShelfMat", "ShelfMat", new Vector3(LaneWidth - 0.3f, 0.01f, 0.4f), s_Rubber, new Vector3(0f, 1.03f, 0.55f), default, SurfaceType.Rubber, 2f, false);
                Box(b, "ShelfBracketL", "ShelfBracket", new Vector3(0.04f, 0.3f, 0.4f), s_MetalDark, new Vector3(-(LaneWidth * 0.5f - 0.12f), 0.82f, 0.55f), default, SurfaceType.Metal, 1f, false);
                Box(b, "ShelfBracketR", "ShelfBracket", new Vector3(0.04f, 0.3f, 0.4f), s_MetalDark, new Vector3(LaneWidth * 0.5f - 0.12f, 0.82f, 0.55f), default, SurfaceType.Metal, 1f, false);
                // booth light strip + scoreboard housing above the booth
                Box(b, "BoothLight", "BoothLight", new Vector3(LaneWidth - 0.4f, 0.04f, 0.1f), MaterialLibrary.Flat("BoothLightEmissive", Color.white, 0.2f, 0f, new Color(1.8f, 1.8f, 1.7f)), new Vector3(0f, 2.28f, 0.6f), default, SurfaceType.Metal, 1f, false);
                var sb = Box(b, "ScoreboardHousing", "ScoreboardHousing", new Vector3(0.9f, 0.4f, 0.08f), s_MetalDark, new Vector3(0f, 2.62f, 0.95f), default, SurfaceType.Metal, 1f, false);
                Box(sb, "Screen", "ScoreboardScreen", new Vector3(0.84f, 0.34f, 0.01f), s_Screen, new Vector3(0f, 0f, -0.045f), default, SurfaceType.Metal, 1f, false);
                Sign(b, "LaneNumber", $"{RangePaths.ArtSigns}/lane_{lane + 1}.png", new Vector3(0f, 2.62f, 0.9f - 0.5f), new Vector3(0f, 0f, 0f), 0.3f, 0.3f);
                Box(b, "CarrierMotor", "CarrierMotor", new Vector3(0.24f, 0.2f, 0.3f), s_MetalDark, new Vector3(0f, RailHeight - 0.05f, 1.25f), default, SurfaceType.Metal, 1f, false);
                Box(b, "Rail", "TargetRail", new Vector3(0.08f, 0.1f, BackstopZ - 1.0f), s_MetalBrushed, new Vector3(0f, RailHeight + 0.05f, (BackstopZ - 1.0f) * 0.5f + 0.5f), default, SurfaceType.Metal, 1f);
                for (float z = 4f; z < BackstopZ - 2f; z += 6f)
                    Box(b, $"RailHanger{z:0}", "RailHanger", new Vector3(0.04f, RoomHeight - RailHeight - 0.1f, 0.04f), s_MetalDark, new Vector3(0f, (RoomHeight + RailHeight + 0.1f) * 0.5f, z), default, SurfaceType.Metal, 1f, false);
            }
        }

        static void BuildDownrange(GameObject root)
        {
            // distance markers (floor stripes + labels + wall labels)
            foreach (var d in new[] { 5, 10, 15, 25 })
            {
                Box(root, $"DistanceStripe{d}", "DistanceStripe", new Vector3(RoomWidth, 0.006f, 0.06f), s_White, new Vector3(0f, 0.003f, d), default, SurfaceType.Concrete, 1f, false);
                Sign(root, $"DistanceFloorL{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(-RoomWidth * 0.5f + 0.45f, 0.011f, d), new Vector3(90f, 0f, 0f), 0.6f, 0.3f);
                Sign(root, $"DistanceFloorR{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(RoomWidth * 0.5f - 0.45f, 0.011f, d), new Vector3(90f, 0f, 0f), 0.6f, 0.3f);
                Sign(root, $"DistanceWallL{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.5f, d), new Vector3(0f, -90f, 0f), 0.8f, 0.4f);
                Sign(root, $"DistanceWallR{d}", $"{RangePaths.ArtSigns}/distance_{d}m.png", new Vector3(RoomWidth * 0.5f - 0.01f, 2.5f, d), new Vector3(0f, 90f, 0f), 0.8f, 0.4f);
            }
            // ceiling baffles downrange (acoustic)
            for (float z = 3f; z < BackstopZ - 2f; z += 4f)
                for (int k = -2; k <= 2; k++)
                    Box(root, $"Baffle{z:0}_{k}", "Baffle", new Vector3(1.2f, 0.5f, 0.08f), s_Fabric, new Vector3(k * 1.8f, RoomHeight - 0.3f, z), default, SurfaceType.Wood, 1f, false);
            // backstop: rubber curtain, angled steel deflector plates, rubber berm and side bullet traps
            Box(root, "Backstop", "Backstop", new Vector3(RoomWidth + 0.4f, RoomHeight, 0.3f), s_Rubber, new Vector3(0f, RoomHeight * 0.5f, BackstopZ + 0.15f), default, SurfaceType.Rubber, 0.5f);
            for (int i = 0; i < 3; i++)
                Box(root, $"Deflector{i}", "Deflector", new Vector3(RoomWidth, 0.9f, 0.06f), s_MetalDark, new Vector3(0f, 0.6f + i * 0.9f, BackstopZ - 0.35f - i * 0.1f), new Vector3(-35f, 0f, 0f), SurfaceType.Metal, 0.5f);
            Box(root, "BackstopBerm", "Berm", new Vector3(RoomWidth, 0.5f, 1.2f), s_Rubber, new Vector3(0f, 0.25f, BackstopZ - 0.8f), default, SurfaceType.Rubber, 0.5f);
            Box(root, "BackstopTrough", "Trough", new Vector3(RoomWidth, 0.2f, 0.5f), s_MetalDark, new Vector3(0f, 0.1f, BackstopZ - 1.6f), default, SurfaceType.Metal, 1f);
            Box(root, "BackstopLightBar", "BackstopLightBar", new Vector3(RoomWidth - 1f, 0.08f, 0.2f), MaterialLibrary.Flat("BackstopLightEmissive", Color.white, 0.2f, 0f, new Color(1.5f, 1.5f, 1.4f)), new Vector3(0f, RoomHeight - 0.2f, BackstopZ - 1.5f), default, SurfaceType.Metal, 1f, false);
        }

        static void BuildCounterAndWaitingArea(GameObject root)
        {
            var counter = EditorRefs.Child(root, "WeaponCounter", new Vector3(0f, 0f, CounterZ));
            Box(counter, "CounterTop", "CounterTop", new Vector3(3.2f, 0.06f, 0.8f), s_Wood, new Vector3(0f, 0.97f, 0f), default, SurfaceType.Wood, 1f);
            Box(counter, "CounterBase", "CounterBase", new Vector3(3.1f, 0.94f, 0.7f), s_Fabric, new Vector3(0f, 0.47f, 0f), default, SurfaceType.Wood, 1f);
            Box(counter, "CounterKick", "CounterKick", new Vector3(3.1f, 0.12f, 0.72f), s_MetalDark, new Vector3(0f, 0.06f, 0f), default, SurfaceType.Metal, 1f, false);
            Box(counter, "CounterFoam", "CounterFoam", new Vector3(3.0f, 0.03f, 0.6f), s_Rubber, new Vector3(0f, 1.015f, 0f), default, SurfaceType.Rubber, 2f);
            Box(counter, "CounterEdge", "CounterEdge", new Vector3(3.2f, 0.03f, 0.03f), s_MetalBrushed, new Vector3(0f, 1.0f, -0.39f), default, SurfaceType.Metal, 1f, false);
            Sign(counter, "CounterSign", $"{RangePaths.ArtSigns}/sign_counter.png", new Vector3(0f, 2.3f, -0.3f), new Vector3(0f, 180f, 0f), 1.6f, 0.6f);
            Cyl(counter, "SignHangerL", "SignHanger", 0.01f, 0.6f, s_MetalDark, new Vector3(-0.7f, 2.9f, -0.3f), default, false, SurfaceType.Metal, 8);
            Cyl(counter, "SignHangerR", "SignHanger", 0.01f, 0.6f, s_MetalDark, new Vector3(0.7f, 2.9f, -0.3f), default, false, SurfaceType.Metal, 8);
            for (int i = 0; i < 3; i++)
            {
                Box(counter, $"Stand{i}", "WeaponStand", new Vector3(0.14f, 0.05f, 0.1f), s_MetalDark, new Vector3(-0.9f + i * 0.9f, 1.055f, 0f), default, SurfaceType.Metal, 2f, false);
                Box(counter, $"StandLabel{i}", "StandLabel", new Vector3(0.18f, 0.001f, 0.06f), s_White, new Vector3(-0.9f + i * 0.9f, 1.031f, -0.22f), default, SurfaceType.Rubber, 1f, false);
            }
            // range officer station with a monitor and a control console
            var ro = EditorRefs.Child(root, "RangeOfficerStation", new Vector3(3.3f, 0f, -3.4f));
            Box(ro, "Desk", "RODesk", new Vector3(1.2f, 0.75f, 0.7f), s_Wood, new Vector3(0f, 0.375f, 0f), default, SurfaceType.Wood, 1f);
            Box(ro, "Monitor", "ROMonitor", new Vector3(0.55f, 0.35f, 0.03f), s_MetalDark, new Vector3(0f, 1.0f, 0.2f), new Vector3(-8f, 180f, 0f), SurfaceType.Metal, 1f, false);
            Box(ro, "MonitorScreen", "ROMonitorScreen", new Vector3(0.5f, 0.3f, 0.005f), s_Screen, new Vector3(0f, 1.0f, 0.18f), new Vector3(-8f, 180f, 0f), SurfaceType.Metal, 1f, false);
            Box(ro, "Console", "ROConsole", new Vector3(0.5f, 0.06f, 0.3f), s_MetalDark, new Vector3(0f, 0.78f, -0.1f), new Vector3(-10f, 0f, 0f), SurfaceType.Metal, 1f, false);
            for (int i = 0; i < 5; i++) Box(ro, $"ConsoleButton{i}", "ConsoleButton", new Vector3(0.05f, 0.02f, 0.05f), i == 0 ? s_Red : (i == 1 ? MaterialLibrary.Flat("ConsoleGreen", new Color(0.1f, 0.7f, 0.2f), 0.4f, 0f, new Color(0.1f, 0.8f, 0.2f)) : s_White), new Vector3(-0.18f + i * 0.09f, 0.82f, -0.1f), new Vector3(-10f, 0f, 0f), SurfaceType.Metal, 1f, false);
            Box(ro, "Chair", "ROChairSeat", new Vector3(0.5f, 0.08f, 0.5f), s_Fabric, new Vector3(0f, 0.45f, -0.7f), default, SurfaceType.Wood, 1f);
            Box(ro, "ChairBack", "ROChairBack", new Vector3(0.5f, 0.5f, 0.06f), s_Fabric, new Vector3(0f, 0.74f, -0.92f), default, SurfaceType.Wood, 1f, false);
            Cyl(ro, "ChairPost", "ROChairPost", 0.03f, 0.4f, s_MetalBrushed, new Vector3(0f, 0.2f, -0.7f), default, false, SurfaceType.Metal, 10);
            // waiting area: benches, table, vending machine, bins, extinguisher, door, crates, posters
            var wait = EditorRefs.Child(root, "WaitingArea", new Vector3(0f, 0f, BackWallZ));
            foreach (var x in new[] { -3.0f, -1.2f })
            {
                Box(wait, $"Bench{x:0}", "BenchSeat", new Vector3(1.6f, 0.06f, 0.45f), s_Wood, new Vector3(x, 0.45f, 0.5f), default, SurfaceType.Wood, 1f);
                Box(wait, $"BenchBack{x:0}", "BenchBack", new Vector3(1.6f, 0.45f, 0.05f), s_Wood, new Vector3(x, 0.72f, 0.3f), default, SurfaceType.Wood, 1f, false);
                Box(wait, $"BenchLegL{x:0}", "BenchLeg", new Vector3(0.05f, 0.45f, 0.4f), s_MetalDark, new Vector3(x - 0.7f, 0.22f, 0.5f), default, SurfaceType.Metal, 1f, false);
                Box(wait, $"BenchLegR{x:0}", "BenchLeg", new Vector3(0.05f, 0.45f, 0.4f), s_MetalDark, new Vector3(x + 0.7f, 0.22f, 0.5f), default, SurfaceType.Metal, 1f, false);
            }
            Box(wait, "Table", "WaitTable", new Vector3(0.9f, 0.04f, 0.6f), s_Wood, new Vector3(-2.1f, 0.5f, 1.4f), default, SurfaceType.Wood, 1f);
            Cyl(wait, "TableLeg", "WaitTableLeg", 0.03f, 0.48f, s_MetalBrushed, new Vector3(-2.1f, 0.24f, 1.4f), default, false, SurfaceType.Metal, 10);
            var vend = Box(wait, "VendingMachine", "Vending", new Vector3(0.9f, 1.9f, 0.7f), s_MetalDark, new Vector3(3.9f, 0.95f, 0.45f), default, SurfaceType.Metal, 1f);
            Box(vend, "VendingFront", "VendingFront", new Vector3(0.7f, 1.3f, 0.01f), MaterialLibrary.Flat("VendingGlow", new Color(0.2f, 0.4f, 0.9f), 0.6f, 0f, new Color(0.4f, 0.8f, 2.0f)), new Vector3(0f, 0.15f, -0.355f), default, SurfaceType.Metal, 1f, false);
            Cyl(wait, "Bin", "Bin", 0.22f, 0.7f, s_MetalDark, new Vector3(2.8f, 0.35f, 0.5f), default, false, SurfaceType.Metal, 16);
            Cyl(root, "Extinguisher", "Extinguisher", 0.08f, 0.5f, s_Red, new Vector3(RoomWidth * 0.5f - 0.12f, 1.0f, -5.5f), default, false, SurfaceType.Metal, 12);
            Box(root, "ExtinguisherBracket", "ExtBracket", new Vector3(0.1f, 0.06f, 0.2f), s_MetalDark, new Vector3(RoomWidth * 0.5f - 0.08f, 1.2f, -5.5f), default, SurfaceType.Metal, 1f, false);
            Box(wait, "DoorFrame", "DoorFrame", new Vector3(1.1f, 2.2f, 0.06f), s_MetalDark, new Vector3(-RoomWidth * 0.5f + 1.15f, 1.1f, 0.02f), default, SurfaceType.Metal, 1f);
            Box(wait, "Door", "Door", new Vector3(0.95f, 2.1f, 0.04f), MaterialLibrary.Flat("DoorGrey", new Color(0.35f, 0.37f, 0.4f), 0.5f, 0.3f), new Vector3(-RoomWidth * 0.5f + 1.15f, 1.05f, 0.04f), default, SurfaceType.Metal, 1f);
            Box(wait, "DoorHandle", "DoorHandle", new Vector3(0.03f, 0.03f, 0.15f), s_MetalBrushed, new Vector3(-RoomWidth * 0.5f + 1.5f, 1.05f, 0.1f), default, SurfaceType.Metal, 1f, false);
            Box(wait, "ExitSign", "ExitSign", new Vector3(0.4f, 0.15f, 0.04f), MaterialLibrary.Flat("ExitGreen", new Color(0.1f, 0.8f, 0.2f), 0.3f, 0f, new Color(0.2f, 2f, 0.4f)), new Vector3(-RoomWidth * 0.5f + 1.15f, 2.35f, 0.1f), default, SurfaceType.Metal, 1f, false);
            for (int i = 0; i < 4; i++)
                Box(root, $"AmmoCrate{i}", "AmmoCrate", new Vector3(0.4f, 0.22f, 0.28f), MaterialLibrary.Pbr("pbr_polymer", "CrateDark", 2f), new Vector3(2.0f + (i % 2) * 0.45f, 0.11f + (i / 2) * 0.23f, BackWallZ + 0.35f), new Vector3(0f, (i % 2) * 6f, 0f), SurfaceType.Wood, 2f);
            Box(root, "LockerBank", "Lockers", new Vector3(1.6f, 1.9f, 0.45f), s_MetalBrushed, new Vector3(-RoomWidth * 0.5f + 3.2f, 0.95f, BackWallZ + 0.25f), default, SurfaceType.Metal, 1f);
            for (int i = 0; i < 4; i++) Box(root, $"LockerDoorLine{i}", "LockerLine", new Vector3(0.01f, 1.8f, 0.01f), s_Dark, new Vector3(-RoomWidth * 0.5f + 3.2f - 0.6f + i * 0.4f, 0.95f, BackWallZ + 0.48f), default, SurfaceType.Metal, 1f, false);
        }

        /// <summary>Quad signs face -Z at identity rotation (readable by a viewer at smaller z looking downrange); use Y=180 for signs behind the player.</summary>
        static void BuildSigns(GameObject root)
        {
            Sign(root, "SignWelcome", $"{RangePaths.ArtSigns}/sign_welcome.png", new Vector3(0f, 2.3f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 2.0f, 1.0f);
            Sign(root, "SignControls", $"{RangePaths.ArtSigns}/sign_controls.png", new Vector3(-2.7f, 1.9f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignRules", $"{RangePaths.ArtSigns}/sign_rules.png", new Vector3(2.7f, 2.0f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignSafetyLeft", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.0f, -3.5f), new Vector3(0f, -90f, 0f), 1.4f, 0.7f);
            Sign(root, "SignSafetyRight", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(RoomWidth * 0.5f - 0.01f, 2.0f, -5.0f), new Vector3(0f, 90f, 0f), 1.4f, 0.7f);
            Sign(root, "SignFiringLine", $"{RangePaths.ArtSigns}/sign_firing_line.png", new Vector3(0f, 2.75f, 0.77f), Vector3.zero, 2.0f, 1.0f);
            Sign(root, "SignSafetyBackstop", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(0f, 2.3f, BackstopZ - 0.05f), Vector3.zero, 2.8f, 1.4f);
        }

        static void Sign(GameObject parent, string name, string texturePath, Vector3 pos, Vector3 euler, float width, float height)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) { SetupLog.Warn($"Sign texture missing: {texturePath}"); return; }
            var matName = "Sign_" + System.IO.Path.GetFileNameWithoutExtension(texturePath);
            var mat = MaterialLibrary.UnlitTexture(matName, tex, false, true);
            var mesh = EditorMeshUtil.Quad($"SignQuad_{width:0.00}x{height:0.00}".Replace('.', '_'), width, height);
            var go = EditorRefs.MeshObject(parent, name, mesh, new[] { mat }, pos, euler, s_EnvLayer, false, true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(width, height, 0.01f);
            EditorRefs.Ensure<SurfaceMaterialTag>(go).surface = SurfaceType.Wood;
        }

        /// <summary>Ceiling light panels with baked rectangle lights, mixed point lights for the unbaked case, probes and a reflection probe.</summary>
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
                    var panel = EditorRefs.MeshObject(lights, $"LightPanel{idx}", panelMesh, new[] { panelMat }, new Vector3(x, RoomHeight - 0.02f, z), new Vector3(-90f, 0f, 0f), s_EnvLayer, false, true);
                    panel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var lgo = EditorRefs.Child(lights, $"AreaLight{idx}", new Vector3(x, RoomHeight - 0.08f, z), new Vector3(90f, 0f, 0f));
                    var l = EditorRefs.Ensure<Light>(lgo);
                    l.type = LightType.Rectangle; l.areaSize = new Vector2(1.2f, 0.35f); l.intensity = 25f; l.color = new Color(1f, 0.97f, 0.9f); l.range = 12f;
                    l.lightmapBakeType = LightmapBakeType.Baked;
                    idx++;
                }
            }
            int p = 0;
            foreach (var z in new[] { -5.5f, -2.5f, 0.5f, 5f, 12f })
            {
                foreach (var x in new[] { -2.4f, 2.4f })
                {
                    var lgo = EditorRefs.Child(lights, $"FillLight{p++}", new Vector3(x, RoomHeight - 0.3f, z));
                    var l = EditorRefs.Ensure<Light>(lgo);
                    l.type = LightType.Point; l.range = 9f; l.intensity = 1.5f; l.color = new Color(1f, 0.96f, 0.9f);
                    l.lightmapBakeType = LightmapBakeType.Mixed; l.shadows = LightShadows.None;
                }
            }
            var dgo = EditorRefs.Child(lights, "FillDirectional", new Vector3(0f, 3f, 0f), new Vector3(60f, 30f, 0f));
            var d = EditorRefs.Ensure<Light>(dgo); d.type = LightType.Directional; d.intensity = 0.35f; d.color = new Color(0.95f, 0.95f, 1f);
            d.lightmapBakeType = LightmapBakeType.Mixed; d.shadows = LightShadows.Soft; d.shadowStrength = 0.6f;

            var lpg = EditorRefs.Child(lights, "LightProbes"); var group = EditorRefs.Ensure<LightProbeGroup>(lpg);
            var probes = new System.Collections.Generic.List<Vector3>();
            for (float z = BackWallZ + 0.5f; z <= BackstopZ - 0.5f; z += (z < 2f ? 1.5f : 4f))
                for (float x = -RoomWidth * 0.5f + 0.6f; x <= RoomWidth * 0.5f - 0.6f; x += 1.6f)
                    foreach (var y in new[] { 0.3f, 1.4f, 2.6f }) probes.Add(new Vector3(x, y, z));
            group.probePositions = probes.ToArray();
            var rp = EditorRefs.Child(lights, "ReflectionProbe", new Vector3(0f, 1.6f, -2f));
            var probe = EditorRefs.Ensure<ReflectionProbe>(rp);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked; probe.size = new Vector3(RoomWidth, RoomHeight, 14f); probe.resolution = 128; probe.boxProjection = true;
            // indoor reverb for spatial audio
            var rz = EditorRefs.Child(root, "ReverbZone", new Vector3(0f, 1.5f, 8f));
            var zone = EditorRefs.Ensure<AudioReverbZone>(rz); zone.reverbPreset = AudioReverbPreset.Hangar; zone.minDistance = 20f; zone.maxDistance = 40f;
        }

        static void BuildTeleportArea(GameObject root)
        {
            var go = EditorRefs.Child(root, "TeleportArea", new Vector3(0f, 0.005f, (BackWallZ + 0.4f - 0.3f) * 0.5f));
            var bc = EditorRefs.Ensure<BoxCollider>(go); bc.size = new Vector3(RoomWidth - 0.6f, 0.01f, -BackWallZ - 0.8f); bc.center = Vector3.zero;
            var area = EditorRefs.Ensure<TeleportationArea>(go);
            int mask = InteractionLayerMask.GetMask(ProjectSettingsSetup.TeleportInteractionLayer);
            if (mask == 0) mask = 1 << ProjectSettingsSetup.TeleportInteractionLayerIndex;
            area.interactionLayers = mask;
            area.matchOrientation = MatchOrientation.WorldSpaceUp;
            go.layer = s_EnvLayer;
        }
    }
}
