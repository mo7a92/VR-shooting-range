using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Weapons;

namespace VRRange.Editor
{
    /// <summary>Assembles the three weapon prefabs from the generated OBJ parts described in weapons_layout.json.</summary>
    public static class WeaponPrefabBuilder
    {
        [System.Serializable] class Part { public string name; public string file; public float[] pivot; public string role; }
        [System.Serializable] class Palm { public float[] pos; public float[] forward; public float[] up; public string pose; }
        [System.Serializable] class WeaponLayout { public string model; public string textureSet; public List<Part> parts; public float[] muzzle; public float[] grip; public float gripPitchDegrees; public float[] secondaryGrip; public float[] colliderCenter; public float[] colliderSize; public float mass; public Palm primaryPalm; public Palm supportPalm; public float[] ejectionPort; }

        public static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        public static void Build()
        {
            SetupLog.Section("Weapon prefabs");
            Prefabs.Clear();
            var layoutPath = $"{RangePaths.ArtWeapons}/weapons_layout.json";
            if (!File.Exists(layoutPath)) { SetupLog.Error($"Missing {layoutPath}. Run Tools/AssetGen/gen_weapons.py and reimport."); return; }
            var json = File.ReadAllText(layoutPath);
            var layouts = ParseLayouts(json);
            foreach (var kv in layouts)
            {
                var def = kv.Key == "pistol" ? SettingsAssetBuilder.Pistol : kv.Key == "rifle" ? SettingsAssetBuilder.Rifle : SettingsAssetBuilder.Smg;
                var prefab = BuildWeapon(kv.Key, kv.Value, def);
                if (prefab != null) Prefabs[kv.Key] = prefab;
            }
            SetupLog.Ok($"Weapon prefabs built: {string.Join(", ", Prefabs.Keys)}");
        }

        // Minimal JSON parsing with JsonUtility requires a wrapper; the layout uses a dictionary so we extract per-weapon objects by brace matching.
        static Dictionary<string, WeaponLayout> ParseLayouts(string json)
        {
            var result = new Dictionary<string, WeaponLayout>();
            int w = json.IndexOf("\"weapons\"", System.StringComparison.Ordinal);
            if (w < 0) return result;
            int idx = json.IndexOf('{', w);
            foreach (var name in new[] { "pistol", "rifle", "smg" })
            {
                int k = json.IndexOf($"\"{name}\"", idx, System.StringComparison.Ordinal);
                if (k < 0) continue;
                int start = json.IndexOf('{', k);
                int depth = 0, end = start;
                for (int i = start; i < json.Length; i++) { if (json[i] == '{') depth++; else if (json[i] == '}') { depth--; if (depth == 0) { end = i; break; } } }
                var obj = json.Substring(start, end - start + 1).Replace("null", "[]");
                try { result[name] = JsonUtility.FromJson<WeaponLayout>(obj); }
                catch (System.Exception e) { SetupLog.Error($"Layout parse failed for {name}: {e.Message}"); }
            }
            return result;
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static GameObject BuildWeapon(string id, WeaponLayout layout, WeaponDefinition def)
        {
            string display = def != null ? def.displayName : id;
            var root = new GameObject($"Weapon_{display}");
            root.layer = EditorRefs.Layer("Weapon");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = layout.mass > 0 ? layout.mass : 1f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var col = root.AddComponent<BoxCollider>(); col.center = V(layout.colliderCenter); col.size = V(layout.colliderSize);
            root.AddComponent<SurfaceMaterialTag>().surface = SurfaceType.Metal;

            var model = EditorRefs.Child(root, "Model");
            Transform slide = null, trigger = null, magazine = null;
            if (!string.IsNullOrEmpty(layout.model))
            {
                // V3: one FBX (CC0 download processed by Tools/AssetGen/blender_process.py) with named parts; one PBR texture set per weapon.
                var modelPath = $"{RangePaths.ArtWeapons}/{layout.model}";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (asset == null) { SetupLog.Error($"Weapon model missing: {modelPath} (run Tools/AssetGen/fetch_polyhaven.py + blender_process.py)"); Object.DestroyImmediate(root); return null; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Mesh"; inst.transform.SetParent(model.transform, false);
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
                var texFolder = System.IO.Path.GetDirectoryName($"{RangePaths.ArtWeapons}/{layout.textureSet}").Replace('\\', '/');
                var texBase = System.IO.Path.GetFileName(layout.textureSet);
                var mat = MaterialLibrary.PbrFromFolder(texFolder, texBase, $"Weapon_{id}", 0.5f);
                var roles = new Dictionary<string, string>();
                if (layout.parts != null) foreach (var p in layout.parts) if (!string.IsNullOrEmpty(p.name)) roles[p.name] = p.role;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = root.layer;
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var mats = new Material[Mathf.Max(1, mr.sharedMaterials.Length)]; for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        mr.sharedMaterials = mats; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                    if (roles.TryGetValue(t.name, out var role))
                        switch (role) { case "slide": slide = t; break; case "trigger": trigger = t; break; case "magazine": magazine = t; break; }
                }
                if (inst.GetComponentsInChildren<MeshRenderer>(true).Length == 0) SetupLog.Warn($"{id}: model has no mesh renderers");
            }
            else
            foreach (var part in layout.parts)
            {
                var objPath = $"{RangePaths.ArtWeapons}/{part.file}";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(objPath);
                if (mesh == null) { SetupLog.Warn($"Mesh not found: {objPath}"); continue; }
                var mats = MaterialsFor(objPath, mesh.subMeshCount);
                var go = EditorRefs.MeshObject(model, part.name, mesh, mats, V(part.pivot), default, root.layer);
                go.layer = root.layer;
                switch (part.role) { case "slide": slide = go.transform; break; case "trigger": trigger = go.transform; break; case "magazine": magazine = go.transform; break; }
            }

            var muzzle = EditorRefs.Child(root, "Muzzle", V(layout.muzzle));
            var flashPrefab = FxPrefabBuilder.MuzzleFlash;
            ParticleSystem flash = null;
            if (flashPrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(flashPrefab); inst.transform.SetParent(muzzle.transform, false); inst.transform.localPosition = Vector3.forward * 0.01f;
                flash = inst.GetComponent<ParticleSystem>();
            }
            var lightGo = EditorRefs.Child(muzzle, "MuzzleLight", Vector3.forward * 0.05f);
            var light = EditorRefs.Ensure<Light>(lightGo); light.type = LightType.Point; light.color = new Color(1f, 0.75f, 0.4f); light.intensity = 3f; light.range = 1.5f; light.enabled = false; light.shadows = LightShadows.None;

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 1f; audio.maxDistance = 60f; audio.rolloffMode = AudioRolloffMode.Logarithmic;

            var attach = EditorRefs.Child(root, "Attach", V(layout.grip), new Vector3(layout.gripPitchDegrees, 0f, 0f));
            GameObject secondary = null;
            if (layout.secondaryGrip != null && layout.secondaryGrip.Length >= 3) secondary = EditorRefs.Child(root, "SecondaryAttach", V(layout.secondaryGrip));

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = false;
            grab.attachTransform = attach.transform;
            if (secondary != null) grab.secondaryAttachTransform = secondary.transform;
            grab.selectMode = def != null && def.twoHanded ? InteractableSelectMode.Multiple : InteractableSelectMode.Single;
            grab.throwOnDetach = true; grab.retainTransformParent = true; grab.attachEaseInTime = 0f; grab.trackScale = false;
            grab.addDefaultGrabTransformers = false; // WeaponHandlingTransformer replaces XRI's general transformer

            var animator = root.AddComponent<WeaponAnimator>();
            animator.Configure(model.transform, slide, trigger, magazine);
            var weapon = root.AddComponent<Weapon>();
            weapon.Initialize(def, muzzle.transform, animator, flash, audio, light);
            root.AddComponent<WeaponHome>();

            // Weight / handling simulation and hand grip anchors
            var handling = root.AddComponent<WeaponHandlingTransformer>();
            handling.Configure(def, attach.transform, secondary != null ? secondary.transform : null);
            var primaryAnchor = BuildAnchor(root, "PrimaryGrip", layout.primaryPalm, GripRole.Primary, V(layout.grip));
            var supportAnchor = layout.supportPalm != null && layout.supportPalm.pos != null ? BuildAnchor(root, "SupportGrip", layout.supportPalm, GripRole.Support, V(layout.secondaryGrip)) : null;
            weapon.SetHandling(handling, primaryAnchor, supportAnchor);
            if (layout.ejectionPort != null && layout.ejectionPort.Length >= 3)
            {
                var port = EditorRefs.Child(root, "EjectionPort", V(layout.ejectionPort));
                weapon.SetEjectionPort(port.transform);
            }

            var path = $"{RangePaths.Prefabs}/Weapon_{display}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static WeaponGripAnchor BuildAnchor(GameObject root, string name, Palm palm, GripRole role, Vector3 fallbackPos)
        {
            var go = EditorRefs.Child(root, name);
            Vector3 pos = palm != null && palm.pos != null ? V(palm.pos) : fallbackPos;
            Vector3 fwd = palm != null && palm.forward != null ? V(palm.forward) : new Vector3(-0.55f, -0.35f, 0.76f);
            Vector3 up = palm != null && palm.up != null ? V(palm.up) : new Vector3(0.83f, -0.1f, 0.55f);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.LookRotation(fwd.normalized, up.normalized);
            var anchor = EditorRefs.Ensure<WeaponGripAnchor>(go);
            anchor.role = role; anchor.authoredForRightHand = true;
            anchor.pose = SettingsAssetBuilder.Pose(palm != null ? palm.pose : (role == GripRole.Primary ? "PistolGrip" : "RifleSupport"));
            return anchor;
        }

        /// <summary>OBJ files list `usemtl` names in a deterministic order; map them to generated materials.</summary>
        static Material[] MaterialsFor(string objPath, int subMeshCount)
        {
            var names = new List<string>();
            try { foreach (var line in File.ReadLines(objPath)) if (line.StartsWith("usemtl ")) names.Add(line.Substring(7).Trim()); }
            catch (System.Exception e) { SetupLog.Warn($"Could not read {objPath}: {e.Message}"); }
            var mats = new Material[Mathf.Max(1, subMeshCount)];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = i < names.Count ? names[i] : "Gunmetal";
                mats[i] = n switch
                {
                    "Polymer" => MaterialLibrary.Pbr("pbr_polymer", "WeaponPolymer", 10f, 0.35f, new Color(0.55f, 0.55f, 0.57f)),
                    "Grip" => MaterialLibrary.Pbr("pbr_polymer", "WeaponGripTexture", 40f, 0.25f, new Color(0.35f, 0.35f, 0.36f)),
                    "Rubber" => MaterialLibrary.Pbr("pbr_rubber", "WeaponRubber", 8f),
                    "Metal" => MaterialLibrary.Pbr("pbr_metal_brushed", "WeaponMetal", 8f),
                    "Dark" => MaterialLibrary.Flat("WeaponDark", new Color(0.04f, 0.04f, 0.045f), 0.5f, 0.6f),
                    "White" => MaterialLibrary.Flat("SightWhite", new Color(0.95f, 0.95f, 0.9f), 0.3f, 0f, new Color(0.6f, 0.6f, 0.55f)),
                    "Emissive" => MaterialLibrary.Flat("ReticleRed", new Color(1f, 0.1f, 0.1f), 0.5f, 0f, new Color(4f, 0.2f, 0.2f)),
                    _ => MaterialLibrary.Pbr("pbr_metal_dark", "WeaponGunmetal", 8f, 0.6f, new Color(0.92f, 0.94f, 0.97f)),
                };
            }
            return mats;
        }
    }
}
