using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>
    /// Tactical gloves for the XR Hands rig (0.4.1, owner: "the hands are ugly; gloved hands would be nicer"). The rig, its
    /// joints, skin weights and proportions stay exactly as tuned in rounds 2-4 (the hand-grip solver and the acceptance test
    /// depend on them); only the surface changes:
    /// <list type="bullet">
    /// <item>the sample mesh's UVs are one planar projection with palm and back on the same texels, so a copy of the mesh gets
    /// three islands: back of the hand, palm side, and the flat wrist cut (the inside of the cuff);</item>
    /// <item>a glove texture set is painted procedurally into that layout from the bind-pose geometry (every texel knows its 3D
    /// point, side and bone): black stretch fabric, coyote synthetic-leather palm and finger pads with flex seams at every
    /// joint, a moulded knuckle guard and finger-back pads, reinforced fingertips, stitched seams, and a ribbed wrist cuff
    /// with a hook-and-loop strap.</item>
    /// </list>
    /// Output: <c>Assets/Art/Hands/Glove/</c> (mesh assets + albedo / normal / mask / AO PNGs), CC0 (generated here).
    /// </summary>
    public static class GloveBuilder
    {
        public const string Folder = RangePaths.ArtHands + "/Glove";
        const int Size = 2048;
        const float UvScale = 0.75f; // the source projection spans u 0..0.63, v 0..1; each side gets half the atlas
        static readonly Vector2 CapUv = new Vector2(0.99f, 0.02f);

        public static string MeshPath(bool left) => $"{Folder}/Glove{(left ? "Left" : "Right")}Hand.asset";
        public static string TexBase(bool left) => left ? "glove_left" : "glove_right";

        [MenuItem("VR Range/Build/Gloves (hand mesh + textures)", priority = 60)]
        public static void BuildAll()
        {
            SetupLog.Section("Gloves");
            RangePaths.EnsureFolder(Folder);
            foreach (var left in new[] { true, false }) Build(left);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            ConfigureImports();
            SetupLog.Ok("Glove meshes and texture sets built");
        }

        public static void RunBatch() { SetupLog.Begin("Gloves"); BuildAll(); }

        static void ConfigureImports()
        {
            foreach (var left in new[] { true, false })
                foreach (var suffix in new[] { "_albedo", "_normal", "_mask", "_ao" })
                {
                    var p = $"{Folder}/{TexBase(left)}{suffix}.png";
                    if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                    ti.textureType = suffix == "_normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = suffix == "_albedo";
                    ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.maxTextureSize = 2048; ti.wrapMode = TextureWrapMode.Clamp;
                    var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(android);
                    ti.SaveAndReimport();
                }
        }

        /// <summary>Puts the glove mesh and material on a hand model instance (scene builder and the scene patch). False when the glove assets are missing.</summary>
        public static bool ApplyTo(GameObject handModel, bool left)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(left));
            if (mesh == null) { BuildAll(); mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(left)); }
            if (mesh == null) { SetupLog.Warn("Glove mesh missing; the hand keeps its skin"); return false; }
            var mat = MaterialLibrary.PbrFromFolder(Folder, TexBase(left), left ? "HandGloveLeft" : "HandGloveRight", 0.3f);
            foreach (var smr in handModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.sharedMesh = mesh;
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)]; for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats; smr.updateWhenOffscreen = true;
                EditorUtility.SetDirty(smr);
            }
            return true;
        }

        enum Side { Back, Palm, Cap }

        static void Build(bool left)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(left ? RangePaths.HandLeftModel : RangePaths.HandRightModel);
            var smr = asset.GetComponentInChildren<SkinnedMeshRenderer>();
            var src = smr.sharedMesh;
            var bones = smr.bones;
            var v = src.vertices; var n = src.normals; var uv = src.uv; var bw = src.boneWeights; var tri = src.triangles;

            // palm direction: the fingertips curl toward the palm
            Vector3 PosOf(string suffix) { foreach (var b in bones) if (b.name.EndsWith(suffix)) return asset.transform.InverseTransformPoint(b.position); return Vector3.zero; }
            var palmBone = PosOf("_Palm");
            float tipsY = (PosOf("_IndexTip").y + PosOf("_MiddleTip").y + PosOf("_RingTip").y) / 3f;
            float palmSign = Mathf.Sign(tipsY - palmBone.y); // -1: the palm faces -y
            float wristZ = PosOf("_Wrist").z, minZ = float.MaxValue; foreach (var p in v) minZ = Mathf.Min(minZ, p.z);

            // ---- split the mesh into back / palm / cap islands ----
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUv = new List<Vector2>(); var outUvOld = new List<Vector2>(); var outBw = new List<BoneWeight>(); var outSide = new List<Side>();
            var remap = new Dictionary<(int, Side), int>(); var outTri = new List<int>(tri.Length);
            for (int i = 0; i < tri.Length; i += 3)
            {
                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                var fn = (n[a] + n[b] + n[c]).normalized; // the imported vertex normals (the winding's cross product is not reliable here)
                float zc = (v[a].z + v[b].z + v[c].z) / 3f;
                Side side = fn.z < -0.8f && zc < minZ + 0.012f ? Side.Cap : (fn.y * palmSign > 0f ? Side.Palm : Side.Back);
                foreach (int k in new[] { a, b, c })
                {
                    if (!remap.TryGetValue((k, side), out int idx))
                    {
                        idx = outV.Count; remap[(k, side)] = idx;
                        outV.Add(v[k]); outN.Add(n[k]); outBw.Add(bw[k]); outUvOld.Add(uv[k]); outSide.Add(side);
                        outUv.Add(side == Side.Cap ? CapUv : side == Side.Back ? new Vector2(uv[k].x * UvScale, uv[k].y) : new Vector2(0.5f + uv[k].x * UvScale, uv[k].y));
                    }
                    outTri.Add(idx);
                }
            }
            var mesh = new Mesh { name = $"Glove{(left ? "Left" : "Right")}Hand", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(outV); mesh.SetNormals(outN); mesh.SetUVs(0, outUv); mesh.SetTriangles(outTri, 0);
            mesh.boneWeights = outBw.ToArray(); mesh.bindposes = src.bindposes;
            mesh.RecalculateTangents(); mesh.bounds = src.bounds;
            var path = MeshPath(left);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, path);
            SetupLog.Ok($"{mesh.name}: {outV.Count} vertices ({v.Length} in the source, split into back / palm / cap islands)");

            // ---- rasterise the bind-pose attributes into the atlas ----
            var P = new Vector3[Size * Size]; var N = new Vector3[Size * Size]; var S = new sbyte[Size * Size]; var B = new int[Size * Size]; var OldUv = new Vector2[Size * Size];
            for (int i = 0; i < S.Length; i++) S[i] = -1;
            for (int i = 0; i < outTri.Count; i += 3)
            {
                int a = outTri[i], b = outTri[i + 1], c = outTri[i + 2];
                if (outSide[a] == Side.Cap) continue;
                Vector2 ta = outUv[a] * Size, tb = outUv[b] * Size, tc = outUv[c] * Size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.x, Mathf.Min(tb.x, tc.x)))), x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(ta.x, Mathf.Max(tb.x, tc.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.y, Mathf.Min(tb.y, tc.y)))), y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(ta.y, Mathf.Max(tb.y, tc.y))));
                float area = (tb.x - ta.x) * (tc.y - ta.y) - (tc.x - ta.x) * (tb.y - ta.y);
                if (Mathf.Abs(area) < 1e-9f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var q = new Vector2(x + 0.5f, y + 0.5f);
                        float w0 = ((tb.x - q.x) * (tc.y - q.y) - (tc.x - q.x) * (tb.y - q.y)) / area;
                        float w1 = ((tc.x - q.x) * (ta.y - q.y) - (ta.x - q.x) * (tc.y - q.y)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                        int id = y * Size + x;
                        var nn = (outN[a] * w0 + outN[b] * w1 + outN[c] * w2).normalized;
                        // overlapping triangles of one island (planar projection of a curved surface): keep the one facing the projection most
                        if (S[id] >= 0 && Mathf.Abs(N[id].y) > Mathf.Abs(nn.y)) continue;
                        P[id] = outV[a] * w0 + outV[b] * w1 + outV[c] * w2; N[id] = nn; S[id] = (sbyte)outSide[a];
                        OldUv[id] = outUvOld[a] * w0 + outUvOld[b] * w1 + outUvOld[c] * w2;
                        var wb = w0 >= w1 && w0 >= w2 ? outBw[a] : w1 >= w2 ? outBw[b] : outBw[c]; B[id] = wb.boneIndex0;
                    }
            }

            // ---- glove design ----
            var boneName = new string[bones.Length]; for (int i = 0; i < bones.Length; i++) boneName[i] = bones[i].name.Substring(2);
            var bonePos = new Vector3[bones.Length]; for (int i = 0; i < bones.Length; i++) bonePos[i] = asset.transform.InverseTransformPoint(bones[i].position);
            var joints = new List<(Vector3 at, Vector3 dir)>(); // flex joints (and the finger direction there) for the palm creases
            for (int i = 0; i < bones.Length; i++)
            {
                if (!(boneName[i].EndsWith("Proximal") || boneName[i].EndsWith("Intermediate") || boneName[i].EndsWith("Distal"))) continue;
                int child = -1; for (int k = 0; k < bones.Length; k++) if (bones[k].parent == bones[i]) child = k;
                if (child < 0) continue;
                var d = bonePos[child] - bonePos[i]; joints.Add((bonePos[i], new Vector3(d.x, 0f, d.z).normalized));
            }
            var knuckles = new List<Vector3>(); for (int i = 0; i < bones.Length; i++) if (boneName[i].EndsWith("Proximal") && !boneName[i].StartsWith("Thumb")) knuckles.Add(bonePos[i]);
            var fingerBackPads = new List<(Vector3 a, Vector3 b)>();
            for (int i = 0; i < bones.Length; i++)
                if (boneName[i].EndsWith("Proximal") && !boneName[i].StartsWith("Thumb"))
                {
                    var finger = boneName[i].Replace("Proximal", ""); int j = System.Array.IndexOf(boneName, finger + "Intermediate");
                    if (j >= 0) fingerBackPads.Add((Vector3.Lerp(bonePos[i], bonePos[j], 0.25f), Vector3.Lerp(bonePos[i], bonePos[j], 0.75f)));
                }
            float cuffTop = wristZ + 0.022f; // cuff from the cut up to just past the wrist joint
            var height = new float[Size * Size]; var albedo = new Color32[Size * Size]; var mask = new Color32[Size * Size]; var ao = new Color32[Size * Size];
            var skinAo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtHands}/{(left ? "skin_left" : "skin_right")}_ao.jpg");
            Color[] aoPx = null; int aoW = 0, aoH = 0;
            if (skinAo != null) { var readable = Readable(skinAo); aoPx = readable.GetPixels(); aoW = readable.width; aoH = readable.height; Object.DestroyImmediate(readable); }

            Color fabric = new Color(0.085f, 0.088f, 0.092f), leather = new Color(0.36f, 0.29f, 0.2f), leatherDark = new Color(0.25f, 0.2f, 0.14f), rubber = new Color(0.025f, 0.025f, 0.027f), cuffCol = new Color(0.2f, 0.22f, 0.16f), thread = new Color(0.42f, 0.38f, 0.3f);
            for (int id = 0; id < S.Length; id++)
            {
                if (S[id] < 0) continue;
                var p = P[id]; var nrm = N[id]; bool palm = S[id] == (sbyte)Side.Palm;
                string bn = boneName[B[id]];
                bool digit = bn.Contains("Proximal") || bn.Contains("Intermediate") || bn.Contains("Distal") || bn.Contains("Tip");
                bool thumb = bn.StartsWith("Thumb");
                float facing = Mathf.Abs(nrm.y);                      // 1 = facing the projection (palm / back), 0 = the side wall
                float sideWall = 1f - Smooth(0.25f, 0.55f, facing);   // the seam between palm and back runs here
                float h = 0f; Color col = fabric; float smooth = 0.28f;

                // fabric weave: two crossed fine sinusoids (≈1.2 mm period) in the model's plane
                float weave = Mathf.Sin(p.x * 5200f) * Mathf.Sin(p.z * 5200f);
                h += weave * 0.12f;
                col *= 1f + weave * 0.05f + Hash(p * 9000f) * 0.06f;

                if (p.z < cuffTop)
                {
                    // ribbed cuff, slightly flared, with a hook-and-loop strap across the back
                    float rib = Mathf.Abs(Mathf.Sin((p.x + p.y) * 900f));
                    col = cuffCol * (0.92f + 0.08f * rib); h = 0.2f + rib * 0.25f; smooth = 0.22f;
                    if (!palm && p.z > minZ + 0.012f && p.z < cuffTop - 0.006f)
                    {
                        col = new Color(0.09f, 0.09f, 0.085f) * (0.85f + 0.3f * Hash(p * 40000f)); h = 0.55f + 0.2f * Hash(p * 40000f); smooth = 0.12f;
                    }
                    if (Mathf.Abs(p.z - cuffTop) < 0.0015f) { h -= 0.5f; col *= 0.7f; } // hem at the top of the cuff
                    // the last 12 mm before the cut: plain, so the stretched projection of the cut's walls cannot show
                    if (p.z < minZ + 0.012f) { col = cuffCol * 0.8f; h = 0.2f; smooth = 0.2f; }
                }
                else if (palm && facing > 0.35f)
                {
                    // synthetic leather palm: pads between the flex creases, darker reinforced tips
                    float suede = Hash(p * 15000f);
                    col = Color.Lerp(leather, leatherDark, 0.3f * suede); smooth = 0.38f; h = 0.35f + 0.05f * suede;
                    float crease = 1f;
                    foreach (var (j, dir) in joints)
                    {
                        // a line across the finger at the joint: close along the finger axis, within the finger's width
                        var rel = new Vector3(p.x - j.x, 0f, p.z - j.z); float along = Vector3.Dot(rel, dir), across = (rel - dir * along).magnitude;
                        if (across < 0.012f) crease = Mathf.Min(crease, Smooth(0.0012f, 0.0035f, Mathf.Abs(along)));
                    }
                    h -= (1f - crease) * 0.45f; col *= 0.75f + 0.25f * crease;
                    if (bn.Contains("Distal") || bn.Contains("Tip")) { col = Color.Lerp(col, leatherDark, 0.55f); h += 0.12f; }
                    if (!digit && !thumb) { float heel = 1f - Smooth(wristZ + 0.035f, wristZ + 0.05f, p.z); col = Color.Lerp(col, leatherDark, 0.35f * heel); h += 0.1f * heel; } // heel pad
                }
                else if (!palm && facing > 0.3f)
                {
                    // moulded knuckle guard across the MCP joints, segmented per finger
                    float guard = 0f;
                    foreach (var k in knuckles) { float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(k.x, k.z - 0.004f)); guard = Mathf.Max(guard, 1f - Smooth(0.007f, 0.0115f, d)); }
                    float band = 1f - Smooth(0.009f, 0.013f, Mathf.Abs(p.z - (knuckles.Count > 0 ? knuckles[0].z : 0f)));
                    guard = Mathf.Max(guard * 0.95f, band * Smooth(-0.04f, -0.03f, p.x) * (1f - Smooth(0.035f, 0.045f, p.x)) * 0.6f);
                    if (guard > 0.05f) { col = Color.Lerp(col, rubber, Smooth(0.05f, 0.4f, guard)); h = Mathf.Max(h, 0.3f + guard * 0.9f); smooth = Mathf.Lerp(smooth, 0.55f, guard); }
                    // finger back pads on the proximal phalanges
                    foreach (var (a, b2) in fingerBackPads)
                    {
                        float d = DistSeg(new Vector2(p.x, p.z), new Vector2(a.x, a.z), new Vector2(b2.x, b2.z));
                        float pad = 1f - Smooth(0.0045f, 0.0065f, d);
                        if (pad > 0.02f) { col = Color.Lerp(col, rubber, pad); h = Mathf.Max(h, 0.3f + pad * 0.55f); smooth = Mathf.Lerp(smooth, 0.5f, pad); }
                    }
                    // stretch panels between the knuckles and the cuff: a faint vent pattern
                    if (!digit && guard < 0.05f && p.z < (knuckles.Count > 0 ? knuckles[0].z : 0f) - 0.015f && p.z > cuffTop + 0.005f)
                    {
                        float vent = Mathf.Abs(Mathf.Sin(p.z * 1300f));
                        h -= (1f - Smooth(0.0f, 0.15f, vent)) * 0.25f;
                    }
                }
                // seams: along the palm / back join (side wall) and the cuff top, with stitches
                float seam = Mathf.Max(1f - Smooth(0.0f, 0.18f, Mathf.Abs(sideWall - 0.5f)) * 1f, p.z >= cuffTop ? 1f - Smooth(0.0008f, 0.002f, Mathf.Abs(p.z - cuffTop)) : 0f);
                if (seam > 0.01f)
                {
                    h -= seam * 0.35f;
                    float st = Mathf.Sin((p.z + p.x) * 2600f) > 0.6f ? 1f : 0f;
                    col = Color.Lerp(col, thread, seam * st * 0.6f); h += seam * st * 0.25f;
                }
                height[id] = h;
                albedo[id] = (Color32)new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), 1f);
                mask[id] = (Color32)new Color(0f, 0f, 0f, Mathf.Clamp01(smooth));
                float occl = 1f;
                if (aoPx != null) { var o = OldUv[id]; int ax = Mathf.Clamp((int)(o.x * aoW), 0, aoW - 1), ay = Mathf.Clamp((int)(o.y * aoH), 0, aoH - 1); occl = Mathf.Lerp(1f, aoPx[ay * aoW + ax].r, 0.8f); }
                occl *= Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(h + 0.6f)); // grooves and seams are darker
                ao[id] = (Color32)new Color(occl, occl, occl, 1f);
            }
            // cap island: the inside of the cuff (dark lining)
            for (int y = 0; y < 48; y++) for (int x = Size - 48; x < Size; x++) { int id = y * Size + x; S[id] = 2; albedo[id] = new Color32(8, 8, 9, 255); mask[id] = new Color32(0, 0, 0, 30); ao[id] = new Color32(90, 90, 90, 255); height[id] = 0f; }

            Dilate(albedo, S, 6); Dilate(mask, S, 6); Dilate(ao, S, 6); DilateF(height, S, 6);
            // tangent-space normal from the height field (texel = UV step; strength in texel units)
            var normal = new Color32[Size * Size];
            const float strength = 2.2f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int id = y * Size + x;
                    float hl = height[y * Size + Mathf.Max(0, x - 1)], hr = height[y * Size + Mathf.Min(Size - 1, x + 1)], hd = height[Mathf.Max(0, y - 1) * Size + x], hu = height[Mathf.Min(Size - 1, y + 1) * Size + x];
                    var nn = new Vector3(-(hr - hl) * strength, -(hu - hd) * strength, 1f).normalized;
                    normal[id] = new Color32((byte)((nn.x * 0.5f + 0.5f) * 255), (byte)((nn.y * 0.5f + 0.5f) * 255), (byte)((nn.z * 0.5f + 0.5f) * 255), 255);
                }
            Save(albedo, $"{Folder}/{TexBase(left)}_albedo.png"); Save(normal, $"{Folder}/{TexBase(left)}_normal.png");
            Save(mask, $"{Folder}/{TexBase(left)}_mask.png"); Save(ao, $"{Folder}/{TexBase(left)}_ao.png");
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Hash(Vector3 p) { float h = Mathf.Sin(Vector3.Dot(new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z)), new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f; return h - Mathf.Floor(h); }
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude)); return Vector2.Distance(p, a + ab * t); }

        static void Dilate(Color32[] px, sbyte[] filled, int passes)
        {
            var f = (sbyte[])filled.Clone();
            for (int pass = 0; pass < passes; pass++)
            {
                var nf = (sbyte[])f.Clone();
                for (int y = 1; y < Size - 1; y++) for (int x = 1; x < Size - 1; x++)
                {
                    int id = y * Size + x; if (f[id] >= 0) continue;
                    foreach (int o in new[] { id - 1, id + 1, id - Size, id + Size }) if (f[o] >= 0) { px[id] = px[o]; nf[id] = f[o]; break; }
                }
                f = nf;
            }
        }
        static void DilateF(float[] px, sbyte[] filled, int passes)
        {
            var f = (sbyte[])filled.Clone();
            for (int pass = 0; pass < passes; pass++)
            {
                var nf = (sbyte[])f.Clone();
                for (int y = 1; y < Size - 1; y++) for (int x = 1; x < Size - 1; x++)
                {
                    int id = y * Size + x; if (f[id] >= 0) continue;
                    foreach (int o in new[] { id - 1, id + 1, id - Size, id + Size }) if (f[o] >= 0) { px[id] = px[o]; nf[id] = f[o]; break; }
                }
                f = nf;
            }
        }

        static Texture2D Readable(Texture2D t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
            var r = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, true); r.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); r.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); return r;
        }

        static void Save(Color32[] px, string path)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
