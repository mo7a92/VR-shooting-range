using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Builds simple, properly UV-mapped meshes (box / cylinder / quad) and stores them as assets so scenes reference persistent data.</summary>
    public static class EditorMeshUtil
    {
        static readonly Dictionary<string, Mesh> s_Cache = new Dictionary<string, Mesh>();
        public static void ResetCache() => s_Cache.Clear();

        /// <summary>
        /// Box with world-size based UV tiling (texelsPerMetre) so textures do not stretch. Large faces are subdivided
        /// (about one vertex per metre) so per-vertex additional lights (the Quest preset) light walls and floors
        /// visibly, and a lightmap UV set is generated for baking.
        /// </summary>
        public static Mesh Box(string name, Vector3 size, float uvPerMetre = 1f, float maxQuadSize = 1f)
        {
            string path = $"{RangePaths.Meshes}/{name}.asset";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.name = name;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 normal, Vector3 up, Vector3 right, float w, float hgt)
            {
                int sx = Mathf.Clamp(Mathf.CeilToInt(w / Mathf.Max(0.05f, maxQuadSize)), 1, 48), sy = Mathf.Clamp(Mathf.CeilToInt(hgt / Mathf.Max(0.05f, maxQuadSize)), 1, 48);
                int b = v.Count;
                Vector3 c = Vector3.Scale(normal, h);
                Vector3 origin = c - right * w * 0.5f - up * hgt * 0.5f;
                for (int j = 0; j <= sy; j++)
                    for (int i = 0; i <= sx; i++)
                    {
                        float fx = (float)i / sx, fy = (float)j / sy;
                        v.Add(origin + right * (w * fx) + up * (hgt * fy)); n.Add(normal);
                        uv.Add(new Vector2(w * uvPerMetre * fx, hgt * uvPerMetre * fy));
                    }
                for (int j = 0; j < sy; j++)
                    for (int i = 0; i < sx; i++)
                    {
                        int a = b + j * (sx + 1) + i, bb = a + 1, cc = a + sx + 1, d = cc + 1;
                        t.AddRange(new[] { a, cc, bb, bb, cc, d });
                    }
            }
            Face(Vector3.forward, Vector3.up, Vector3.left, size.x, size.y);
            Face(Vector3.back, Vector3.up, Vector3.right, size.x, size.y);
            Face(Vector3.right, Vector3.up, Vector3.forward, size.z, size.y);
            Face(Vector3.left, Vector3.up, Vector3.back, size.z, size.y);
            Face(Vector3.up, Vector3.forward, Vector3.right, size.x, size.z);
            Face(Vector3.down, Vector3.back, Vector3.right, size.x, size.z);
            mesh.Clear();
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            LightmapUVs(mesh);
            Save(mesh, path, create); s_Cache[path] = mesh;
            return mesh;
        }

        /// <summary>Non-overlapping UV2 for lightmaps (the tiled UV0 of boxes/cylinders overlaps between faces).</summary>
        static void LightmapUVs(Mesh mesh)
        {
            try { Unwrapping.GenerateSecondaryUVSet(mesh, new UnwrapParam { angleError = 8f, areaError = 15f, hardAngle = 88f, packMargin = 1f / 256f }); }
            catch (System.Exception e) { Debug.LogWarning($"[VRRange] lightmap UVs for {mesh.name} failed: {e.Message}"); }
        }

        /// <summary>Single-sided quad in the XY plane facing +Z... actually facing -Z (towards a viewer looking at +Z): normal = -Z.</summary>
        public static Mesh Quad(string name, float width, float height, bool normalPositiveZ = false)
        {
            string path = $"{RangePaths.Meshes}/{name}.asset";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.name = name;
            float w = width * 0.5f, h = height * 0.5f;
            mesh.Clear();
            mesh.SetVertices(new List<Vector3> { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0) });
            var nrm = normalPositiveZ ? Vector3.forward : Vector3.back;
            mesh.SetNormals(new List<Vector3> { nrm, nrm, nrm, nrm });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(normalPositiveZ ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.SetUVs(1, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            Save(mesh, path, create); s_Cache[path] = mesh;
            return mesh;
        }

        public static Mesh Cylinder(string name, float radius, float length, int segments = 16, bool alongZ = true)
        {
            string path = $"{RangePaths.Meshes}/{name}.asset";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.name = name;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments; var rad = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 p0 = rad * radius + Vector3.back * length * 0.5f, p1 = rad * radius + Vector3.forward * length * 0.5f;
                if (!alongZ) { p0 = new Vector3(p0.x, p0.z, p0.y); p1 = new Vector3(p1.x, p1.z, p1.y); rad = new Vector3(rad.x, rad.z, rad.y); }
                v.Add(p0); v.Add(p1); n.Add(rad); n.Add(rad);
                uv.Add(new Vector2((float)i / segments, 0)); uv.Add(new Vector2((float)i / segments, 1));
                if (i < segments) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            // caps
            for (int side = 0; side < 2; side++)
            {
                Vector3 nrm = (side == 0 ? Vector3.back : Vector3.forward); Vector3 c = nrm * length * 0.5f;
                if (!alongZ) { nrm = new Vector3(nrm.x, nrm.z, nrm.y); c = new Vector3(c.x, c.z, c.y); }
                int center = v.Count; v.Add(c); n.Add(nrm); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.PI * 2f * i / segments; var rad = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    Vector3 p = rad * radius + (alongZ ? c : Vector3.zero); if (!alongZ) p = new Vector3(rad.x * radius, c.y, rad.y * radius);
                    v.Add(p); n.Add(nrm); uv.Add(new Vector2(0.5f + rad.x * 0.5f, 0.5f + rad.y * 0.5f));
                    if (i < segments) { int k = center + 1 + i; if (side == 0) t.AddRange(new[] { center, k + 1, k }); else t.AddRange(new[] { center, k, k + 1 }); }
                }
            }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            LightmapUVs(mesh);
            Save(mesh, path, create); s_Cache[path] = mesh;
            return mesh;
        }

        static void Save(Mesh mesh, string path, bool create)
        {
            if (create) { RangePaths.EnsureFolder(RangePaths.Meshes); AssetDatabase.CreateAsset(mesh, path); }
            else EditorUtility.SetDirty(mesh);
        }
    }
}
