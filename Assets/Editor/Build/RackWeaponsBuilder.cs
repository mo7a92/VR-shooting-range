using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Weapons;

namespace VRRange.Editor
{
    /// <summary>
    /// The playable AKM and sniper rifle on the staff-area gun rack. Each stands in front of the rack with its butt on the
    /// floor, leaning back against the front lip of the middle shelf: the placement is computed from the weapon's real mesh
    /// vertices so no part of it is inside a shelf, a post or the floor. Each rifle sits in its own socket (its home: a
    /// dropped rifle returns there). Applied by the scene builder and, for an existing scene, by
    /// <see cref="ApplyToOpenScene"/> (no scene rebuild, no re-bake).
    /// </summary>
    public static class RackWeaponsBuilder
    {
        public const string GroupName = "RackWeapons";
        // rack-local placement: x offset from the rack centre, lean angle back toward the rack, shelf lip leaned on
        static readonly (string id, float x, float leanDeg)[] s_Slots = { ("akm", -0.22f, 11f), ("sniper", 0.2f, 9f) };
        const float LeanLipHeight = 0.91f;   // the middle shelf; its front lip is the support
        const float Clearance = 0.004f;      // gap kept to the rack front plane and the floor

        [MenuItem("VR Range/Setup/Update Gun Rack Weapons (open scene)", priority = 40)]
        public static void MenuApply()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>Batch: Unity.exe -batchmode -executeMethod VRRange.Editor.RackWeaponsBuilder.RunBatch -quit (prefabs must be built).</summary>
        public static void RunBatch()
        {
            SetupLog.Begin("Gun rack weapons (scene patch, no rebuild)");
            LoadPrefabs();
            var scene = EditorSceneManager.OpenScene(RangePaths.MainScene, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            SetupLog.Ok($"Scene saved: {RangePaths.MainScene}");
        }

        static void LoadPrefabs()
        {
            foreach (var id in WeaponPrefabBuilder.Ids)
            {
                if (WeaponPrefabBuilder.Prefabs.TryGetValue(id, out var p) && p != null) continue;
                var guid = AssetDatabase.FindAssets("t:Prefab Weapon_", new[] { RangePaths.Prefabs })
                    .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .FirstOrDefault(g => g != null && g.GetComponent<Weapon>() != null && g.GetComponent<Weapon>().Definition != null && g.GetComponent<Weapon>().Definition.weaponId == id);
                if (guid != null) WeaponPrefabBuilder.Prefabs[id] = guid;
            }
        }

        /// <summary>Removes the old static display rifles and any previous rack weapons, then places the playable ones.</summary>
        public static List<Weapon> ApplyToOpenScene()
        {
            LoadPrefabs();
            foreach (var n in new[] { "RackRifleAKM", "RackRifleBolt" })
            {
                var old = GameObject.Find(n);
                if (old != null) { Object.DestroyImmediate(old); SetupLog.Ok($"Removed decor prop {n}"); }
            }
            var rangeRoot = GameObject.Find("--- Range ---");
            var weapons = Build(rangeRoot != null ? rangeRoot.transform : null);
            var gm = Object.FindFirstObjectByType<GameModeManager>();
            if (gm != null)
            {
                var so = new SerializedObject(gm); var list = so.FindProperty("m_Weapons");
                for (int i = list.arraySize - 1; i >= 0; i--) if (list.GetArrayElementAtIndex(i).objectReferenceValue == null) list.DeleteArrayElementAtIndex(i);
                foreach (var w in weapons)
                {
                    bool present = false;
                    for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == w) present = true;
                    if (!present) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = w; }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                SetupLog.Ok($"GameModeManager tracks {list.arraySize} weapons");
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return weapons;
        }

        /// <summary>Places the rack weapons (idempotent: an existing RackWeapons group is replaced).</summary>
        public static List<Weapon> Build(Transform root)
        {
            var weapons = new List<Weapon>();
            var rack = GameObject.Find("GunRack");
            if (rack == null) { SetupLog.Warn("GunRack not found; rack weapons skipped"); return weapons; }
            var existing = root != null ? root.Find(GroupName) : GameObject.Find(GroupName)?.transform;
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var group = new GameObject(GroupName).transform; if (root != null) group.SetParent(root, false);

            var rackVerts = WorldVertices(rack);
            var rb = new Bounds(rackVerts[0], Vector3.zero); foreach (var v in rackVerts) rb.Encapsulate(v);
            // the lip: rack vertices at the shelf height near the front; its top surface and front face
            var lip = rackVerts.Where(v => Mathf.Abs(v.y - LeanLipHeight) < 0.04f && v.z > rb.max.z - 0.03f).ToList();
            float lipTop = lip.Count > 0 ? lip.Max(v => v.y) : LeanLipHeight + 0.02f;
            float frontZ = rb.max.z; // the posts and every lip share the front plane
            float floorY = 0f;
            // the rack and the props standing around it (toolbox, boxes): exact mesh tests against temporary mesh colliders
            var probe = new ObstacleProbe(rack, group, rb);
            var placed = new List<Bounds>();

            foreach (var (id, dx, lean) in s_Slots)
            {
                if (!WeaponPrefabBuilder.Prefabs.TryGetValue(id, out var prefab) || prefab == null) { SetupLog.Warn($"Rack weapon prefab missing: {id}"); continue; }
                // barrel up, leaning back toward the rack (-Z); the right side faces the room (+Z), the top of the gun points +X
                float t = lean * Mathf.Deg2Rad;
                var fwd = new Vector3(0f, Mathf.Cos(t), -Mathf.Sin(t)); var rot = Quaternion.LookRotation(fwd, Vector3.right);
                var weaponGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                weaponGo.transform.SetParent(group, false);
                // try the slot's x first, then step sideways along the bay until the rifle clears the props and the other rifle
                List<Vector3> verts = null; Bounds wb = default; bool clear = false; float usedDx = dx; string lastWhy = null;
                for (int k = 0; k <= 40 && !clear; k++)
                {
                    float tryDx = dx + (k == 0 ? 0f : ((k + 1) / 2) * 0.02f * (k % 2 == 1 ? 1f : -1f));
                    weaponGo.transform.SetPositionAndRotation(new Vector3(rb.center.x + tryDx, 0f, frontZ + 0.3f), rot);
                    var pts = SampledPoints(weaponGo);
                    float dy = floorY + Clearance - pts.Min(v => v.y);
                    // nothing at or below the lip's top surface may be behind the rack's front plane (points along every edge,
                    // so long sparse faces such as a forend are checked too); above the lip the rifle may reach into the bay
                    float dzNeed = pts.Where(v => v.y + dy <= lipTop + 0.002f).Max(v => frontZ + Clearance - v.z);
                    weaponGo.transform.position += new Vector3(0f, dy, dzNeed);
                    verts = SampledPoints(weaponGo);
                    wb = new Bounds(verts[0], Vector3.zero); foreach (var v in verts) wb.Encapsulate(v);
                    bool inBay = wb.min.x >= rb.min.x + 0.03f && wb.max.x <= rb.max.x - 0.03f;
                    var why = probe.Intersects(weaponGo, verts); bool hitsProp = why != null; lastWhy = why;
                    bool hitsRifle = placed.Any(o => { var e = o; e.Expand(0.03f); return e.Intersects(wb); });
                    clear = inBay && !hitsProp && !hitsRifle; usedDx = tryDx;
                }
                if (!clear) SetupLog.Warn($"Rack {id}: no free spot in front of the rack; left at the last tried position (last: {lastWhy ?? "outside the bay / touching the other rifle"})");
                if (clear) SetupLog.Ok($"Rack {id}: no edge of the rifle crosses the rack or a prop, no point of it is inside one ({probe.Describe()})");
                placed.Add(wb);
                float intoBay = frontZ - wb.min.z;
                SetupLog.Ok($"Rack {id}: x offset {usedDx * 100f:0} cm from the rack centre, lean {lean:0} deg, butt {wb.min.y * 1000f:0} mm above the floor, top at {wb.max.y:0.00} m, leans on the {lipTop:0.00} m lip, reaches {intoBay * 1000f:0} mm into the shelf bay above it, x {wb.min.x:0.00}..{wb.max.x:0.00} (posts {rb.min.x:0.00}..{rb.max.x:0.00})");
                if (wb.max.y > 1.38f) SetupLog.Warn($"Rack {id}: top {wb.max.y:0.00} m reaches the next shelf");
                if (wb.min.x < rb.min.x + 0.03f || wb.max.x > rb.max.x - 0.03f) SetupLog.Warn($"Rack {id}: wider than the bay between the posts");

                var socketGo = new GameObject($"RackSocket_{id}"); socketGo.transform.SetParent(group, false);
                socketGo.transform.SetPositionAndRotation(weaponGo.transform.position, weaponGo.transform.rotation);
                var trigger = socketGo.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = 0.12f;
                trigger.center = socketGo.transform.InverseTransformPoint(wb.center) ; // the sphere sits on the middle of the rifle
                var socket = socketGo.AddComponent<XRSocketInteractor>();
                socket.showInteractableHoverMeshes = false; socket.recycleDelayTime = 0.5f;
                var attach = new GameObject("Attach"); attach.transform.SetParent(socketGo.transform, false);
                var grab = weaponGo.GetComponent<XRGrabInteractable>();
                if (grab.attachTransform != null) { attach.transform.localPosition = grab.attachTransform.localPosition; attach.transform.localRotation = grab.attachTransform.localRotation; }
                socket.attachTransform = attach.transform;
                socket.startingSelectedInteractable = grab;
                var home = weaponGo.GetComponent<WeaponHome>(); if (home != null) home.HomeSocket = socket;
                weapons.Add(weaponGo.GetComponent<Weapon>());
            }
            probe.Dispose();
            SetupLog.Ok($"{weapons.Count} weapons on the gun rack");
            return weapons;
        }

        /// <summary>
        /// Exact intersection test of a rifle against the rack and the props near it: temporary mesh colliders on a scratch
        /// layer; a rifle intersects when any of its triangle edges crosses an obstacle surface (back faces included) or any
        /// of its points is inside an obstacle (odd number of surface crossings straight up).
        /// </summary>
        public sealed class ObstacleProbe : System.IDisposable
        {
            const int Layer = 31;
            readonly List<MeshCollider> m_Colliders = new List<MeshCollider>();
            readonly List<Bounds> m_Bounds = new List<Bounds>();
            readonly List<string> m_Names = new List<string>();
            readonly List<bool> m_Closed = new List<bool>();

            public ObstacleProbe(GameObject rack, Transform exclude, Bounds rackBounds)
            {
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    var r = mf.GetComponent<Renderer>();
                    if (mf.sharedMesh == null || r == null || (exclude != null && mf.transform.IsChildOf(exclude)) || mf.GetComponentInParent<Weapon>() != null) continue;
                    var b = r.bounds;
                    if (b.max.x < rackBounds.min.x - 0.4f || b.min.x > rackBounds.max.x + 0.4f || b.max.z < rackBounds.min.z - 0.4f || b.min.z > rackBounds.max.z + 0.8f || b.min.y > 1.6f) continue;
                    if (b.size.x > 3f || b.size.z > 3f) continue; // floor and walls: the floor is checked by height
                    var go = new GameObject("RackProbe_" + mf.name) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); go.transform.localScale = mf.transform.lossyScale; go.layer = Layer;
                    var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
                    // open sheet-metal shelving has no inside: the parity test only applies to closed props
                    m_Colliders.Add(mc); m_Bounds.Add(b); m_Names.Add(mf.name); m_Closed.Add(!mf.transform.IsChildOf(rack.transform));
                }
                Physics.SyncTransforms();
            }

            public string Describe() => $"{m_Colliders.Count} obstacles: {string.Join(", ", m_Names.Distinct())}";

            /// <summary>Null when the rifle is clear, else what it touches.</summary>
            public string Intersects(GameObject weapon, List<Vector3> samples)
            {
                bool hb = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
                try
                {
                    foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!Counts(mf)) continue;
                        var m = mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; var tr = mf.sharedMesh.triangles;
                        var w = new Vector3[vs.Length]; for (int i = 0; i < vs.Length; i++) w[i] = m.MultiplyPoint3x4(vs[i]);
                        for (int i = 0; i < tr.Length; i += 3)
                            for (int e = 0; e < 3; e++)
                                if (Physics.Linecast(w[tr[i + e]], w[tr[i + (e + 1) % 3]], out var hit, 1 << Layer, QueryTriggerInteraction.Ignore)) return $"edge of {mf.name} crosses {hit.collider.name} at {hit.point}";
                    }
                    foreach (var v in samples)
                        for (int k = 0; k < m_Bounds.Count; k++)
                        {
                            if (!m_Closed[k] || !m_Bounds[k].Contains(v)) continue;
                            int crossings = 0; var o = v;
                            for (int guard = 0; guard < 32 && m_Colliders[k].Raycast(new Ray(o, Vector3.up), out var hit, 5f); guard++) { crossings++; o = hit.point + Vector3.up * 0.0005f; }
                            if ((crossings & 1) == 1) return $"point {v} inside {m_Names[k]}";
                        }
                    return null;
                }
                finally { Physics.queriesHitBackfaces = hb; }
            }

            public void Dispose() { foreach (var c in m_Colliders) if (c != null) Object.DestroyImmediate(c.gameObject); m_Colliders.Clear(); }
        }

        /// <summary>Mesh vertices plus points every 8 mm along every triangle edge (world space).</summary>
        public static List<Vector3> SampledPoints(GameObject go, float spacing = 0.008f)
        {
            var list = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!Counts(mf)) continue;
                var m = mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; var tr = mf.sharedMesh.triangles;
                var w = new Vector3[vs.Length]; for (int i = 0; i < vs.Length; i++) { w[i] = m.MultiplyPoint3x4(vs[i]); list.Add(w[i]); }
                for (int i = 0; i < tr.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        var a = w[tr[i + e]]; var b = w[tr[i + (e + 1) % 3]];
                        int n = Mathf.FloorToInt(Vector3.Distance(a, b) / spacing);
                        for (int j = 1; j <= n; j++) list.Add(Vector3.Lerp(a, b, j / (n + 1f)));
                    }
            }
            return list;
        }

        static bool Counts(MeshFilter mf) => mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null && mf.GetComponent<TMPro.TMP_Text>() == null && mf.gameObject.layer != LayerMask.NameToLayer(ProjectSettingsSetup.HandContactLayer);

        public static List<Vector3> WorldVertices(GameObject go)
        {
            var list = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy && mf.gameObject.activeSelf) continue;
                if (mf.GetComponent<MeshRenderer>() == null || mf.GetComponent<TMPro.TMP_Text>() != null || mf.gameObject.layer == LayerMask.NameToLayer(ProjectSettingsSetup.HandContactLayer)) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
            }
            return list;
        }
    }
}
