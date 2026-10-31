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
        /// <summary>Fill lights near the firing line that cast soft shadows on PCVR only (set at runtime by PlatformGraphicsSetup).</summary>
        public static readonly System.Collections.Generic.List<Light> PcvrShadowLights = new System.Collections.Generic.List<Light>();
        static Material s_Floor, s_FloorTiles, s_Carpet, s_Wall, s_WallRough, s_Rubber, s_MetalBrushed, s_MetalDark, s_Wood, s_Fabric, s_Dark, s_Yellow, s_Red, s_Glass, s_White, s_Screen;

        public static float LaneX(int lane) => (lane - (LaneCount - 1) * 0.5f) * LaneWidth;

        public static GameObject Build(Transform sceneRoot)
        {
            SetupLog.Section("Environment");
            PcvrShadowLights.Clear(); PropLibrary.Reset();
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
            // ventilation duct along the right wall, above the acoustic panels and below the cable tray, clear of signs, HUD and scoreboards
            const float ductX = 4.1f, ductY = 2.6f, ductR = 0.25f;
            Cyl(root, "Duct", "Duct", ductR, depth - 1f, s_MetalBrushed, new Vector3(ductX, ductY, zc), default, true, SurfaceType.Metal, 14);
            for (float z = BackWallZ + 2f; z < BackstopZ - 1f; z += 6f)
            {
                Box(root, $"DuctVent{z:0}", "DuctVent", new Vector3(0.42f, 0.06f, 0.42f), s_MetalDark, new Vector3(ductX, ductY - ductR - 0.03f, z), default, SurfaceType.Metal, 2f, false);
                Box(root, $"DuctHanger{z:0}", "DuctHanger", new Vector3(0.03f, RoomHeight - ductY - ductR, 0.03f), s_MetalDark, new Vector3(ductX, (RoomHeight + ductY + ductR) * 0.5f, z), default, SurfaceType.Metal, 1f, false);
            }
        }

        static void BuildBooths(GameObject root)
        {
            Box(root, "FiringLine", "FiringLine", new Vector3(RoomWidth, 0.01f, 0.12f), s_Yellow, new Vector3(0f, 0.005f, 0f), default, SurfaceType.Concrete, 1f, false);
            Box(root, "FiringLineText", "FiringLineInner", new Vector3(RoomWidth, 0.011f, 0.02f), s_Dark, new Vector3(0f, 0.005f, 0.07f), default, SurfaceType.Concrete, 1f, false);
            Box(root, "CeilingBeam", "CeilingBeam", new Vector3(RoomWidth, 0.25f, 0.3f), s_MetalDark, new Vector3(0f, RoomHeight - 0.125f, 0.95f), default, SurfaceType.Metal, 1f);
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
                // shooting table: laminate top on a welded steel frame, rubber mat, front lip, magazine shelf underneath
                Box(b, "Shelf", "Shelf", new Vector3(LaneWidth - 0.14f, 0.04f, 0.55f), s_Wood, new Vector3(0f, 1.0f, 0.55f), default, SurfaceType.Wood, 1f);
                Box(b, "ShelfLip", "ShelfLip", new Vector3(LaneWidth - 0.14f, 0.09f, 0.03f), s_MetalDark, new Vector3(0f, 1.045f, 0.815f), default, SurfaceType.Metal, 1f, false);
                Box(b, "ShelfMat", "ShelfMat", new Vector3(LaneWidth - 0.3f, 0.008f, 0.42f), s_Rubber, new Vector3(0f, 1.024f, 0.55f), default, SurfaceType.Rubber, 2f, false);
                Box(b, "ShelfFrameFront", "ShelfFrame", new Vector3(LaneWidth - 0.2f, 0.04f, 0.04f), s_MetalDark, new Vector3(0f, 0.96f, 0.8f), default, SurfaceType.Metal, 1f, false);
                Box(b, "ShelfFrameBack", "ShelfFrame", new Vector3(LaneWidth - 0.2f, 0.04f, 0.04f), s_MetalDark, new Vector3(0f, 0.96f, 0.3f), default, SurfaceType.Metal, 1f, false);
                Box(b, "UnderShelf", "UnderShelf", new Vector3(LaneWidth - 0.3f, 0.02f, 0.4f), s_MetalBrushed, new Vector3(0f, 0.62f, 0.55f), default, SurfaceType.Metal, 1f);
                foreach (var sx in new[] { -1f, 1f })
                {
                    Box(b, $"ShelfLeg{(sx < 0 ? "L" : "R")}F", "ShelfLeg", new Vector3(0.04f, 0.98f, 0.04f), s_MetalDark, new Vector3(sx * (LaneWidth * 0.5f - 0.12f), 0.49f, 0.78f), default, SurfaceType.Metal, 1f, false);
                    Box(b, $"ShelfLeg{(sx < 0 ? "L" : "R")}B", "ShelfLeg", new Vector3(0.04f, 0.98f, 0.04f), s_MetalDark, new Vector3(sx * (LaneWidth * 0.5f - 0.12f), 0.49f, 0.32f), default, SurfaceType.Metal, 1f, false);
                }
                PropLibrary.Place(b, "ammo_box", new Vector3(0.45f, 0.63f, 0.5f), new Vector3(0f, 95f + lane * 11f, 0f), 1f, 0.63f, SurfaceType.Metal, true, true, 0f, "AmmoBox");
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
            // steel ricochet protection along the side walls near the traps
            foreach (var side in new[] { -1f, 1f })
                Box(root, $"SteelGuard{(side < 0 ? "L" : "R")}", "SteelGuard", new Vector3(0.04f, 2.0f, 6.0f), s_MetalDark, new Vector3(side * (RoomWidth * 0.5f - 0.04f), 1.1f, BackstopZ - 4.5f), new Vector3(0f, 0f, side * -6f), SurfaceType.Metal, 1f, false);
        }

        static void BuildCounterAndWaitingArea(GameObject root)
        {
            // ---- rental counter (laminate top, steel kick, foam mat for the weapons) ----
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
            PropLibrary.Place(counter, "clipboard", new Vector3(1.35f, 1.031f, -0.15f), new Vector3(0f, 160f, 0f), 1f, 1.03f + CounterZ * 0f, SurfaceType.Wood, false, true, 0f, "Clipboard");
            PropLibrary.Place(counter, "ammo_box", new Vector3(-1.42f, 1.03f, 0.18f), new Vector3(0f, 75f, 0f), 1f, null, SurfaceType.Metal, true, true, 0f, "AmmoBoxCounter");
            foreach (var x in new[] { -1.0f, 0f, 1.0f })
                PropLibrary.Place(counter, "caged_hanging_light", new Vector3(x, RoomHeight - 0.75f, 0f), Vector3.zero, 1f, null, SurfaceType.Metal, false, true, 0f, $"CounterLamp{x:0}");

            // ---- staff area behind the counter: shelves with stock, gun rack with display rifles, officer desk ----
            var staff = EditorRefs.Child(root, "StaffArea", new Vector3(0f, 0f, CounterZ - 1.1f));
            foreach (var x in new[] { -1.35f, 1.35f }) // leave the centre open so the welcome sign stays visible from the counter
                PropLibrary.Place(staff, "steel_frame_shelves_01", new Vector3(x, 0f, 0f), new Vector3(0f, 0f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, $"Shelves{x:0.0}");
            PropLibrary.Place(staff, "plastic_crate_01", new Vector3(-1.25f, 0f, 0.45f), new Vector3(0f, 12f, 0f), 1f, 0f, SurfaceType.Rubber, true, true, 0f, "Crate1");
            PropLibrary.Place(staff, "plastic_crate_01", new Vector3(-1.25f, 0.27f, 0.45f), new Vector3(0f, -8f, 0f), 1f, 0.265f, SurfaceType.Rubber, true, true, 0f, "Crate2");
            PropLibrary.Place(staff, "wooden_military_crate", new Vector3(0.9f, 0f, 0.55f), new Vector3(0f, 90f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, "WoodCrate");
            PropLibrary.Place(staff, "ammo_box", new Vector3(0.15f, 0f, 0.55f), new Vector3(0f, 20f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "AmmoBoxFloor1");
            PropLibrary.Place(staff, "ammo_box", new Vector3(0.35f, 0f, 0.5f), new Vector3(0f, -30f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "AmmoBoxFloor2");
            PropLibrary.Place(staff, "metal_toolbox", new Vector3(-2.35f, 0f, 0.45f), new Vector3(0f, 100f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "Toolbox");
            var rack = PropLibrary.Place(staff, "worn_metal_rack", new Vector3(-2.4f, 0f, 0f), Vector3.zero, 1f, 0f, SurfaceType.Metal, true, true, 0f, "GunRack");
            // display weapons on the rack (CC0 AKM + bolt-action rifle), barrels up
            PropLibrary.Place(staff, "akm_decor", new Vector3(-2.6f, 1.05f, 0.18f), new Vector3(-90f, 0f, 8f), 1f, null, SurfaceType.Metal, false, true, 0f, "RackRifleAKM");
            PropLibrary.Place(staff, "bolt_action_rifle_7_62", new Vector3(-2.2f, 1.1f, 0.18f), new Vector3(0f, 0f, 90f), 1f, null, SurfaceType.Metal, false, true, 0f, "RackRifleBolt");
            PropLibrary.Place(staff, "cardboard_box_01", new Vector3(-3.3f, 0f, 0.3f), new Vector3(0f, 15f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, "Box1");
            PropLibrary.Place(staff, "cardboard_box_01", new Vector3(-3.35f, 0.52f, 0.28f), new Vector3(0f, -20f, 0f), 1f, 0.515f, SurfaceType.Wood, true, true, 0f, "Box2");

            // ---- range officer station ----
            var ro = EditorRefs.Child(root, "RangeOfficerStation", new Vector3(3.2f, 0f, -3.5f));
            PropLibrary.Place(ro, "metal_office_desk", Vector3.zero, new Vector3(0f, 180f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "Desk");
            PropLibrary.Place(ro, "metal_stool_01", new Vector3(0.1f, 0f, -0.85f), new Vector3(0f, 20f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "Stool");
            PropLibrary.Place(ro, "television_02", new Vector3(-0.4f, 0.79f, 0.1f), new Vector3(0f, 200f, 0f), 1f, 0.7875f, SurfaceType.Metal, true, true, 0f, "Monitor");
            PropLibrary.Place(ro, "clipboard", new Vector3(0.45f, 0.79f, 0.05f), new Vector3(0f, 190f, 0f), 1f, 0.7875f, SurfaceType.Wood, false, true, 0f, "DeskClipboard");
            Box(ro, "Console", "ROConsole", new Vector3(0.5f, 0.06f, 0.3f), s_MetalDark, new Vector3(0.3f, 0.82f, 0.05f), new Vector3(-10f, 0f, 0f), SurfaceType.Metal, 1f, false);
            for (int i = 0; i < 5; i++) Box(ro, $"ConsoleButton{i}", "ConsoleButton", new Vector3(0.05f, 0.02f, 0.05f), i == 0 ? s_Red : (i == 1 ? MaterialLibrary.Flat("ConsoleGreen", new Color(0.1f, 0.7f, 0.2f), 0.4f, 0f, new Color(0.1f, 0.8f, 0.2f)) : s_White), new Vector3(0.12f + i * 0.09f, 0.86f, 0.05f), new Vector3(-10f, 0f, 0f), SurfaceType.Metal, 1f, false);
            PropLibrary.Place(ro, "tool_cart", new Vector3(1.0f, 0f, -1.4f), new Vector3(0f, 90f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "ToolCart");

            // ---- waiting area: benches, chairs, lockers, briefing screen, boxes, bins, door ----
            var wait = EditorRefs.Child(root, "WaitingArea", new Vector3(0f, 0f, BackWallZ));
            foreach (var x in new[] { -3.1f, -1.6f })
                PropLibrary.Place(wait, "painted_wooden_bench", new Vector3(x, 0f, 1.2f), new Vector3(0f, 180f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, $"Bench{x:0.0}");
            for (int i = 0; i < 3; i++)
                PropLibrary.Place(wait, "plastic_monobloc_chair_01", new Vector3(0.6f + i * 0.72f, 0f, 0.9f), new Vector3(0f, 180f + (i - 1) * 7f, 0f), 1f, 0f, SurfaceType.Rubber, true, true, 0f, $"Chair{i}");
            PropLibrary.Place(wait, "projector_screen", new Vector3(-4.0f, 0f, 0.95f), new Vector3(0f, 150f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, "BriefingScreen");
            PropLibrary.Place(wait, "wooden_military_crate", new Vector3(3.0f, 0f, 0.35f), new Vector3(0f, 90f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, "LobbyCrate");
            PropLibrary.Place(wait, "cardboard_box_01", new Vector3(4.1f, 0f, 0.35f), new Vector3(0f, 25f, 0f), 1f, 0f, SurfaceType.Wood, true, true, 0f, "CornerBox");
            PropLibrary.Place(wait, "hand_truck", new Vector3(4.15f, 0f, 1.4f), new Vector3(0f, 250f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "HandTruck");
            PropLibrary.Place(wait, "metal_trash_can", new Vector3(-2.2f, 0f, 0.5f), new Vector3(0f, 180f, 0f), 1f, 0f, SurfaceType.Metal, true, true, 0f, "Bins");
            PropLibrary.Place(wait, "WetFloorSign_01", new Vector3(-2.3f, 0f, 2.4f), new Vector3(0f, 35f, 0f), 1f, 0f, SurfaceType.Rubber, true, true, 0f, "WetFloorSign");
            PropLibrary.Place(wait, "wall_clock", new Vector3(1.0f, 2.45f, 0.04f), new Vector3(0f, 180f, 0f), 1f, null, SurfaceType.Metal, false, true, 0f, "WallClock");
            PropLibrary.Place(wait, "fire_alarm", new Vector3(-2.5f, 1.4f, 0.03f), new Vector3(0f, 180f, 0f), 1f, null, SurfaceType.Metal, false, true, 0f, "FireAlarm");
            Box(wait, "DoorFrame", "DoorFrame", new Vector3(1.1f, 2.2f, 0.06f), s_MetalDark, new Vector3(-RoomWidth * 0.5f + 1.15f, 1.1f, 0.02f), default, SurfaceType.Metal, 1f);
            Box(wait, "Door", "Door", new Vector3(0.95f, 2.1f, 0.04f), MaterialLibrary.Flat("DoorGrey", new Color(0.35f, 0.37f, 0.4f), 0.5f, 0.3f), new Vector3(-RoomWidth * 0.5f + 1.15f, 1.05f, 0.04f), default, SurfaceType.Metal, 1f);
            Box(wait, "DoorHandle", "DoorHandle", new Vector3(0.03f, 0.03f, 0.15f), s_MetalBrushed, new Vector3(-RoomWidth * 0.5f + 1.5f, 1.05f, 0.1f), default, SurfaceType.Metal, 1f, false);
            Box(wait, "ExitSign", "ExitSign", new Vector3(0.4f, 0.15f, 0.04f), MaterialLibrary.Flat("ExitGreen", new Color(0.1f, 0.8f, 0.2f), 0.3f, 0f, new Color(0.2f, 2f, 0.4f)), new Vector3(-RoomWidth * 0.5f + 1.15f, 2.35f, 0.1f), default, SurfaceType.Metal, 1f, false);
            // wall-mounted safety equipment along the range
            foreach (var (side, z) in new[] { (-1f, -5.5f), (1f, -1.6f), (-1f, 6f), (1f, 14f) })
            {
                var ext = Cyl(root, $"Extinguisher{z:0}", "Extinguisher", 0.08f, 0.5f, s_Red, new Vector3(side * (RoomWidth * 0.5f - 0.12f), 1.0f, z), default, false, SurfaceType.Metal, 12);
                Box(root, $"ExtinguisherBracket{z:0}", "ExtBracket", new Vector3(0.1f, 0.06f, 0.2f), s_MetalDark, new Vector3(side * (RoomWidth * 0.5f - 0.08f), 1.2f, z), default, SurfaceType.Metal, 1f, false);
                Cyl(root, $"ExtinguisherNeck{z:0}", "ExtNeck", 0.025f, 0.08f, s_MetalBrushed, new Vector3(side * (RoomWidth * 0.5f - 0.12f), 1.29f, z), default, false, SurfaceType.Metal, 10);
            }
            PropLibrary.Place(root, "power_box_01", new Vector3(RoomWidth * 0.5f - 0.22f, 1.5f, -6.2f), new Vector3(0f, -90f, 0f), 1f, null, SurfaceType.Metal, true, true, 0f, "PowerBox");
            foreach (var (x, z, yaw) in new[] { (-4.3f, -7.1f, 135f), (3.7f, -7.1f, -135f), (-4.3f, 1.2f, 45f), (3.7f, 1.2f, -45f), (0f, 27.5f, 180f) })
                PropLibrary.Place(root, "security_camera_01", new Vector3(x, RoomHeight - 0.3f, z), new Vector3(0f, yaw, 0f), 1f, null, SurfaceType.Metal, false, true, 0f, $"Camera{z:0}{x:0}");
            if (PropLibrary.Missing.Count > 0) SetupLog.Warn("Props missing (run Tools/AssetGen/fetch_polyhaven.py): " + string.Join(", ", PropLibrary.Missing));
            SetupLog.Ok($"{PropLibrary.Placed} CC0 props placed (counter, staff area, officer station, waiting area, safety equipment)");
        }

        /// <summary>Quad signs face -Z at identity rotation (readable by a viewer at smaller z looking downrange); use Y=180 for signs behind the player.</summary>
        static void BuildSigns(GameObject root)
        {
            Sign(root, "SignWelcome", $"{RangePaths.ArtSigns}/sign_welcome.png", new Vector3(0f, 2.3f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 2.0f, 1.0f);
            Sign(root, "SignControls", $"{RangePaths.ArtSigns}/sign_controls.png", new Vector3(-2.7f, 1.9f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignRules", $"{RangePaths.ArtSigns}/sign_rules.png", new Vector3(2.7f, 2.0f, BackWallZ + 0.02f), new Vector3(0f, 180f, 0f), 1.8f, 0.9f);
            Sign(root, "SignSafetyLeft", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.0f, -3.5f), new Vector3(0f, -90f, 0f), 1.4f, 0.7f);
            Sign(root, "SignSafetyRight", $"{RangePaths.ArtSigns}/sign_safety.png", new Vector3(RoomWidth * 0.5f - 0.01f, 2.0f, -5.0f), new Vector3(0f, 90f, 0f), 1.4f, 0.7f);
            // firing-line warning on both side walls (the centre lane is taken by the HUD, lane sign and scoreboard)
            Sign(root, "SignFiringLineLeft", $"{RangePaths.ArtSigns}/sign_firing_line.png", new Vector3(-RoomWidth * 0.5f + 0.01f, 2.4f, 0.3f), new Vector3(0f, -90f, 0f), 1.6f, 0.8f);
            Sign(root, "SignFiringLineRight", $"{RangePaths.ArtSigns}/sign_firing_line.png", new Vector3(RoomWidth * 0.5f - 0.01f, 2.4f, 0.3f), new Vector3(0f, 90f, 0f), 1.6f, 0.8f);
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
            int idx = 0;
            // Ceiling-mounted twin fluorescent fixtures (CC0 prop with emissive tubes) + a baked rectangle light under each.
            // Dense over the shooting area and the waiting room, two rows downrange so the lanes read as a lit corridor.
            void Fixture(float x, float z, float intensity, float yaw = 0f)
            {
                var go = PropLibrary.Place(lights, "mounted_fluorescent_lights", new Vector3(x, RoomHeight - 0.02f, z), new Vector3(180f, yaw, 0f), 1f, null, SurfaceType.Metal, false, true, 2.6f, $"Fixture{idx}");
                if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var lgo = EditorRefs.Child(lights, $"AreaLight{idx}", new Vector3(x, RoomHeight - 0.1f, z), new Vector3(90f, yaw, 0f));
                var l = EditorRefs.Ensure<Light>(lgo);
                l.type = LightType.Rectangle; l.areaSize = new Vector2(0.9f, 0.6f); l.intensity = intensity; l.color = new Color(1f, 0.97f, 0.92f); l.range = 12f;
                l.lightmapBakeType = LightmapBakeType.Baked;
                idx++;
            }
            for (float z = BackWallZ + 1.2f; z < -0.2f; z += 2.4f) foreach (var x in new[] { -3.1f, -0.9f, 1.3f, 3.5f }) Fixture(x, z, 30f);
            for (float z = 2.4f; z < BackstopZ - 1.5f; z += 4f) foreach (var x in new[] { -2.0f, 2.0f }) Fixture(x, z, 24f);
            // booth task lights: a strip over each shooting table
            for (int lane = 0; lane < LaneCount; lane++)
            {
                var lgo = EditorRefs.Child(lights, $"BoothLight{lane}", new Vector3(LaneX(lane), 2.26f, 0.6f), new Vector3(90f, 0f, 0f));
                var l = EditorRefs.Ensure<Light>(lgo);
                l.type = LightType.Rectangle; l.areaSize = new Vector2(1.1f, 0.08f); l.intensity = 14f; l.color = new Color(1f, 0.98f, 0.95f); l.range = 6f; l.lightmapBakeType = LightmapBakeType.Baked;
            }
            // target illumination: baked spots aimed at the 10 m and 15 m target positions of every lane
            foreach (var d in new[] { 10f, 15f })
                for (int lane = 0; lane < LaneCount; lane++)
                {
                    var sgo = EditorRefs.Child(lights, $"TargetSpot{lane}_{d:0}", new Vector3(LaneX(lane), RoomHeight - 0.15f, d - 2.2f), new Vector3(58f, 0f, 0f));
                    var sl = EditorRefs.Ensure<Light>(sgo);
                    sl.type = LightType.Spot; sl.spotAngle = 42f; sl.innerSpotAngle = 25f; sl.range = 8f; sl.intensity = 18f; sl.color = new Color(1f, 0.98f, 0.9f);
                    sl.lightmapBakeType = LightmapBakeType.Baked; sl.shadows = LightShadows.Soft;
                }
            // mixed fill lights keep dynamic objects (weapons, hands, targets, casings) lit; two of them cast soft shadows on PCVR
            int p = 0;
            foreach (var z in new[] { -5.5f, -2.5f, 0.5f, 5f, 12f, 19f, 26f })
            {
                foreach (var x in new[] { -2.4f, 2.4f })
                {
                    var lgo = EditorRefs.Child(lights, $"FillLight{p++}", new Vector3(x, RoomHeight - 0.3f, z));
                    var l = EditorRefs.Ensure<Light>(lgo);
                    l.type = LightType.Point; l.range = 11f; l.intensity = 1.6f; l.color = new Color(1f, 0.96f, 0.9f);
                    l.lightmapBakeType = LightmapBakeType.Mixed; l.shadows = LightShadows.None;
                    if (Mathf.Approximately(z, 0.5f)) PcvrShadowLights.Add(l); // the pair above the firing line: soft shadows on PCVR
                }
            }
            var dgo = EditorRefs.Child(lights, "FillDirectional", new Vector3(0f, 3f, 0f), new Vector3(60f, 30f, 0f));
            var d2 = EditorRefs.Ensure<Light>(dgo); d2.type = LightType.Directional; d2.intensity = 0.45f; d2.color = new Color(0.95f, 0.95f, 1f);
            d2.lightmapBakeType = LightmapBakeType.Mixed; d2.shadows = LightShadows.Soft; d2.shadowStrength = 0.55f;

            var lpg = EditorRefs.Child(lights, "LightProbes"); var group = EditorRefs.Ensure<LightProbeGroup>(lpg);
            var probes = new System.Collections.Generic.List<Vector3>();
            for (float z = BackWallZ + 0.5f; z <= BackstopZ - 0.5f; z += (z < 2f ? 1.2f : 3f))
                for (float x = -RoomWidth * 0.5f + 0.6f; x <= RoomWidth * 0.5f - 0.6f; x += 1.3f)
                    foreach (var y in new[] { 0.3f, 1.1f, 1.8f, 2.7f }) probes.Add(new Vector3(x, y, z));
            group.probePositions = probes.ToArray();
            foreach (var (name, pos, size) in new[] { ("ReflectionProbeFiring", new Vector3(0f, 1.6f, -1.5f), new Vector3(RoomWidth, RoomHeight, 7f)), ("ReflectionProbeWaiting", new Vector3(0f, 1.6f, -6f), new Vector3(RoomWidth, RoomHeight, 4f)), ("ReflectionProbeRange", new Vector3(0f, 1.6f, 14f), new Vector3(RoomWidth, RoomHeight, 30f)) })
            {
                var rp = EditorRefs.Child(lights, name, pos);
                var probe = EditorRefs.Ensure<ReflectionProbe>(rp);
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked; probe.size = size; probe.resolution = 256; probe.boxProjection = true; probe.intensity = 1f;
            }
            // indoor reverb for spatial audio
            var rz = EditorRefs.Child(root, "ReverbZone", new Vector3(0f, 1.5f, 8f));
            var zone = EditorRefs.Ensure<AudioReverbZone>(rz); zone.reverbPreset = AudioReverbPreset.Hangar; zone.minDistance = 20f; zone.maxDistance = 40f;
            SetupLog.Ok($"Lighting: {idx} fluorescent fixtures with baked area lights, {LaneCount} booth strips, {LaneCount * 2} target spots, mixed fills, {probes.Count} light probes, 3 reflection probes");
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
