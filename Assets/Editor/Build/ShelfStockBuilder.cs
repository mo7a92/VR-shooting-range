using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// Stock on the two staff-area shelves behind the counter (they were empty): ammo cans, cardboard boxes, plastic crates
    /// and a toolbox. The shelf levels are found by raycasting the shelf mesh; every item is checked to fit between the posts
    /// and under the level above. Items are dynamic (light probes), so adding them needs no re-bake. Idempotent.
    /// </summary>
    public static class ShelfStockBuilder
    {
        public const string GroupName = "ShelfStock";

        // per level: (prop, x along the shelf in metres from its centre, scale, yaw); levels counted from the lowest above the floor
        static readonly (string prop, float x, float scale, float yaw)[][] s_Patterns =
        {
            new[] { ("plastic_crate_01", -0.3f, 1f, 0f), ("plastic_crate_01", 0.06f, 1f, 4f), ("ammo_box", 0.42f, 1f, 90f) },
            new[] { ("ammo_box", -0.4f, 1f, 90f), ("ammo_box", -0.28f, 1f, 92f), ("ammo_box", -0.16f, 1f, 88f), ("cardboard_box_01", 0.22f, 0.62f, 90f) },
            new[] { ("cardboard_box_01", -0.28f, 0.62f, 94f), ("metal_toolbox", 0.2f, 0.8f, 0f) },
            new[] { ("ammo_box", -0.36f, 1f, 90f), ("ammo_box", -0.24f, 1f, 90f), ("ammo_box", 0.25f, 1f, 87f), ("ammo_box", 0.37f, 1f, 93f) },
        };

        public static int Build(Transform sceneRoot)
        {
            var staff = GameObject.Find("StaffArea");
            if (staff == null) { SetupLog.Warn("StaffArea not found; shelf stock skipped"); return 0; }
            var old = staff.transform.Find(GroupName); if (old != null) Object.DestroyImmediate(old.gameObject);
            var group = new GameObject(GroupName); group.transform.SetParent(staff.transform, false);
            int placed = 0, unit = 0;
            foreach (Transform shelf in staff.transform)
            {
                if (!shelf.name.StartsWith("Shelves")) continue;
                var levels = Levels(shelf.gameObject, out var b);
                SetupLog.Ok($"{shelf.name}: shelf levels at {string.Join(", ", levels.Select(l => l.ToString("0.00")))} m");
                for (int li = 0; li < levels.Count - 1 && li < s_Patterns.Length; li++)
                {
                    float level = levels[li], clearance = levels[li + 1] - level - 0.04f; // the next level's own thickness
                    var pattern = s_Patterns[(li + unit) % s_Patterns.Length];
                    foreach (var (prop, x, scale, yaw) in pattern)
                    {
                        float sx = shelf.name.Contains("-") ? -1f : 1f; // mirror the layout on the second unit
                        var worldPos = new Vector3(b.center.x + x * sx, level, b.center.z + 0.02f);
                        var local = staff.transform.InverseTransformPoint(worldPos);
                        var go = PropLibrary.Place(group, prop, local, new Vector3(0f, yaw + (placed % 3 - 1) * 2.5f, 0f), scale, local.y, SurfaceType.Metal, true, false, 0f, $"{shelf.name}_{prop}_{li}_{placed}");
                        if (go == null) continue;
                        var gb = BoundsOf(go);
                        bool fits = gb.max.y <= level + clearance && gb.min.x >= b.min.x + 0.05f && gb.max.x <= b.max.x - 0.05f && gb.min.z >= b.min.z - 0.01f && gb.max.z <= b.max.z + 0.04f;
                        if (!fits) { SetupLog.Info($"{go.name} does not fit ({gb.min:F2}..{gb.max:F2}); left out"); Object.DestroyImmediate(go); continue; }
                        placed++;
                    }
                }
                unit++;
            }
            SetupLog.Ok($"Shelf stock: {placed} items on {unit} shelf units");
            return placed;
        }

        /// <summary>Heights of the shelf surfaces (top faces), found by casting down through a temporary mesh collider.</summary>
        static List<float> Levels(GameObject shelf, out Bounds b)
        {
            b = BoundsOf(shelf);
            var temps = new List<GameObject>();
            foreach (var mf in shelf.GetComponentsInChildren<MeshFilter>())
            {
                var t = new GameObject("ShelfProbe") { hideFlags = HideFlags.HideAndDontSave }; t.layer = 31;
                t.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); t.transform.localScale = mf.transform.lossyScale;
                t.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; temps.Add(t);
            }
            Physics.SyncTransforms();
            var hits = new List<float>();
            foreach (var dx in new[] { -0.2f, 0f, 0.2f })
            {
                var o = new Vector3(b.center.x + dx, b.max.y + 0.05f, b.center.z);
                for (int guard = 0; guard < 20 && Physics.Raycast(o, Vector3.down, out var hit, 3f, 1 << 31); guard++)
                {
                    if (hit.normal.y > 0.9f) hits.Add(hit.point.y);
                    o = hit.point + Vector3.down * 0.03f; // through the shelf board to the next one
                }
            }
            foreach (var t in temps) Object.DestroyImmediate(t);
            // merge the three columns, keep surfaces above the floor (the lowest board sits a few cm up)
            var levels = new List<float>();
            foreach (var h in hits.OrderBy(h => h)) if (h > 0.05f && (levels.Count == 0 || h - levels[levels.Count - 1] > 0.08f)) levels.Add(h);
            levels.Add(b.max.y + 0.3f); // a virtual level above the top board: items on top may stand up to 30 cm
            return levels;
        }

        static Bounds BoundsOf(GameObject go)
        {
            Bounds? r = null;
            foreach (var rr in go.GetComponentsInChildren<Renderer>(true)) { if (r == null) r = rr.bounds; else { var x = r.Value; x.Encapsulate(rr.bounds); r = x; } }
            return r ?? new Bounds(go.transform.position, Vector3.zero);
        }
    }
}
