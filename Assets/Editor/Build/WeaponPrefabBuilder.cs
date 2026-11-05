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
    /// <summary>Assembles the weapon prefabs (pistol, rifle, SMG, AKM, sniper) from the models described in weapons_layout.json.</summary>
    public static class WeaponPrefabBuilder
    {
        [System.Serializable] class Part { public string name; public string file; public float[] pivot; public string role; }
        [System.Serializable] class Palm { public float[] pos; public float[] forward; public float[] up; public string pose; public float compression; }
        [System.Serializable] class GripBox { public float[] center; public float[] size; public float[] euler; }
        [System.Serializable] class ContactSeed { public string name; public float[] center; public float[] size; public float[] euler; }
        [System.Serializable] class PartMaterial { public string match; public string textureSet; public int submesh = -1; public string flat; }
        [System.Serializable] class BoltDef { public string[] carrier; public string[] rotor; public float[] pivot; public float travel; public float lift; }
        [System.Serializable] class ScopeDef { public float[] eyepiece; public float[] objective; public float lensRadius; public float fov; public float zero; }
        [System.Serializable] class WeaponLayout { public string model; public float[] modelEuler; public float[] modelOffset; public string[] hiddenParts; public List<PartMaterial> partMaterials; public BoltDef bolt; public ScopeDef scope; public string textureSet; public List<Part> parts; public float[] muzzle; public float[] grip; public float gripPitchDegrees; public float[] secondaryGrip; public float[] colliderCenter; public float[] colliderSize; public float mass; public Palm primaryPalm; public Palm supportPalm; public GripBox primaryGripCollider; public GripBox supportGripCollider; public List<ContactSeed> contactVolumes; public float[] ejectionPort; public float[] ammoDisplay; public float[] ammoDisplayEuler; }

        public static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        /// <summary>Every weapon id in weapons_layout.json, in build order (the first three sit on the counter, the rest on the gun rack).</summary>
        public static readonly string[] Ids = { "pistol", "rifle", "smg", "akm", "sniper" };

        static WeaponDefinition DefinitionFor(string id) => id switch
        {
            "pistol" => SettingsAssetBuilder.Pistol, "rifle" => SettingsAssetBuilder.Rifle, "smg" => SettingsAssetBuilder.Smg,
            "akm" => SettingsAssetBuilder.Akm, "sniper" => SettingsAssetBuilder.Sniper, _ => null,
        };

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
                var def = DefinitionFor(kv.Key);
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
            foreach (var name in Ids)
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
            GameObject inst_for_contacts = null;
            if (!string.IsNullOrEmpty(layout.model))
            {
                // V3: one FBX (CC0 download processed by Tools/AssetGen/blender_process.py) with named parts; one PBR texture set per weapon.
                var modelPath = $"{RangePaths.ArtWeapons}/{layout.model}";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (asset == null) { SetupLog.Error($"Weapon model missing: {modelPath} (run Tools/AssetGen/fetch_polyhaven.py + blender_process.py)"); Object.DestroyImmediate(root); return null; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Mesh"; inst.transform.SetParent(model.transform, false);
                inst_for_contacts = inst;
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
                if (layout.modelEuler != null && layout.modelEuler.Length >= 3)
                {
                    // downloaded models keep their import axis / unit compensation on the root; turn barrel-forward on top of it
                    inst.transform.localRotation = Quaternion.Euler(V(layout.modelEuler)) * asset.transform.localRotation;
                    inst.transform.localScale = asset.transform.localScale;
                    inst.transform.localPosition = V(layout.modelOffset);
                }
                if (layout.hiddenParts != null)
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                        if (t != inst.transform && System.Array.Exists(layout.hiddenParts, h => !string.IsNullOrEmpty(h) && t.name.Contains(h))) Object.DestroyImmediate(t.gameObject);
                var mat = TextureSetMaterial(layout.textureSet, $"Weapon_{id}");
                var roles = new Dictionary<string, string>();
                if (layout.parts != null) foreach (var p in layout.parts) if (!string.IsNullOrEmpty(p.name)) roles[p.name] = p.role;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = root.layer;
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var mf = t.GetComponent<MeshFilter>();
                        int slots = Mathf.Max(1, mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : mr.sharedMaterials.Length);
                        var mats = new Material[slots]; for (int i = 0; i < mats.Length; i++) mats[i] = PartMaterialFor(layout, id, t.name, i, mat);
                        mr.sharedMaterials = mats; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                    if (roles.TryGetValue(t.name, out var role))
                        switch (role) { case "slide": slide = t; break; case "trigger": trigger = t; break; case "magazine": magazine = t; break; }
                }
                if (inst.GetComponentsInChildren<MeshRenderer>(true).Length == 0) SetupLog.Warn($"{id}: model has no mesh renderers");
                if (layout.bolt != null && layout.bolt.carrier != null && layout.bolt.carrier.Length > 0) BuildBolt(root, inst, layout.bolt);
                trigger = HingeAtTop(root, trigger, "TriggerPivot");
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
            // Two-grip weapons (rifle, SMG) and every weapon that accepts a support hand (the pistol: second hand cups the
            // shooting hand and takes the weapon over when it lets go) accept two interactors.
            grab.selectMode = def != null && (def.twoHanded || def.allowsSupportHand) ? InteractableSelectMode.Multiple : InteractableSelectMode.Single;
            grab.throwOnDetach = true; grab.retainTransformParent = true; grab.attachEaseInTime = 0f; grab.trackScale = false;
            grab.addDefaultGrabTransformers = false; // WeaponHandlingTransformer replaces XRI's general transformer

            var animator = root.AddComponent<WeaponAnimator>();
            animator.Configure(model.transform, slide, trigger, magazine);
            if (s_LastBoltCarrier != null) { animator.ConfigureBolt(s_LastBoltCarrier, s_LastBoltRotor, layout.bolt.travel > 0f ? layout.bolt.travel : 0.075f, layout.bolt.lift > 0f ? layout.bolt.lift : 75f); s_LastBoltCarrier = null; s_LastBoltRotor = null; }
            var weapon = root.AddComponent<Weapon>();
            weapon.Initialize(def, muzzle.transform, animator, flash, audio, light);
            root.AddComponent<WeaponHome>();

            // Weight / handling simulation and hand grip anchors
            var handling = root.AddComponent<WeaponHandlingTransformer>();
            // the shooting hand's orientation is the free-hand fist (OpenXR grip pose), so the held wrist stays where the real wrist is
            // round 3: a layout may orient the shooting hand along its grip instead (knuckle line along the grip axis, so the fingers
            // wrap it); the weapon's orientation in the controller comes from the attach transform either way
            if (layout.primaryPalm != null && (layout.primaryPalm.forward == null || layout.primaryPalm.forward.Length < 3)) { layout.primaryPalm.forward = new[] { HandPresence.DefaultFreePalmForward.x, HandPresence.DefaultFreePalmForward.y, HandPresence.DefaultFreePalmForward.z }; layout.primaryPalm.up = new[] { HandPresence.DefaultFreePalmUp.x, HandPresence.DefaultFreePalmUp.y, HandPresence.DefaultFreePalmUp.z }; }
            var primaryAnchor = BuildAnchor(root, "PrimaryGrip", layout.primaryPalm, GripRole.Primary, V(layout.grip));
            var supportAnchor = layout.supportPalm != null && layout.supportPalm.pos != null ? BuildAnchor(root, "SupportGrip", layout.supportPalm, GripRole.Support, V(layout.secondaryGrip)) : null;
            // The handling transformer holds the weapon by the PALM CENTROIDS (OpenXR grip pose origin ~ palm centroid): the
            // anchor is the palm-skin contact point on the grip surface, the centroid sits one skin thickness further out
            // along the anchor's up (back of hand). XRI's own attach transforms stay at the grip for sockets / far grabs.
            const float kPalmSkin = 0.022f; // HandPresence measures the real value per hand; the transformer only needs a close pivot
            var palmAttach = EditorRefs.Child(root, "PalmAttach", primaryAnchor.transform.localPosition + primaryAnchor.transform.localRotation * Vector3.up * kPalmSkin, attach.transform.localEulerAngles);
            GameObject supportPalmAttach = null;
            if (supportAnchor != null) supportPalmAttach = EditorRefs.Child(root, "SupportPalmAttach", supportAnchor.transform.localPosition + supportAnchor.transform.localRotation * Vector3.up * kPalmSkin);
            handling.Configure(def, palmAttach.transform, supportPalmAttach != null ? supportPalmAttach.transform : (secondary != null ? secondary.transform : null));
            BuildContactVolumes(root, inst_for_contacts != null ? model : null, layout); // the whole model: bolt rig parts live beside the mesh instance
            // Convex grip volumes the fingers close onto (HandPresence contact solve): the pistol grip / handguard, and on the
            // pistol's support grip the whole shooting hand. Solid (non-trigger) so Collider.ClosestPoint is exact; they sit
            // inside the weapon's main collider and are on the Weapon layer, which projectiles ignore.
            primaryAnchor.gripCollider = BuildGripBox(root, "PrimaryGripCollider", layout.primaryGripCollider, V(layout.grip) + new Vector3(0f, -0.035f, 0f), new Vector3(0.03f, 0.07f, 0.05f));
            if (supportAnchor != null) supportAnchor.gripCollider = BuildGripBox(root, "SupportGripCollider", layout.supportGripCollider, V(layout.secondaryGrip), new Vector3(0.05f, 0.05f, 0.12f));
            weapon.SetHandling(handling, primaryAnchor, supportAnchor);
            if (layout.ejectionPort != null && layout.ejectionPort.Length >= 3)
            {
                var port = EditorRefs.Child(root, "EjectionPort", V(layout.ejectionPort));
                weapon.SetEjectionPort(port.transform);
            }
            BuildAmmoDisplay(root, weapon, layout);
            if (layout.scope != null && layout.scope.eyepiece != null && layout.scope.eyepiece.Length >= 3) BuildScope(root, model, weapon, layout.scope);

            var path = $"{RangePaths.Prefabs}/Weapon_{display}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Material TextureSetMaterial(string textureSet, string materialName)
        {
            var texFolder = System.IO.Path.GetDirectoryName($"{RangePaths.ArtWeapons}/{textureSet}").Replace('\\', '/');
            return MaterialLibrary.PbrFromFolder(texFolder, System.IO.Path.GetFileName(textureSet), materialName, 0.5f);
        }

        /// <summary>Per-part material: a part (name contains <c>match</c>, optionally one submesh) may use its own texture set or a flat material.</summary>
        static Material PartMaterialFor(WeaponLayout layout, string id, string partName, int submesh, Material fallback)
        {
            if (layout.partMaterials == null) return fallback;
            foreach (var pm in layout.partMaterials)
            {
                if (pm == null || string.IsNullOrEmpty(pm.match) || !partName.Contains(pm.match) || (pm.submesh >= 0 && pm.submesh != submesh)) continue;
                if (pm.flat == "ScopeGlass") return MaterialLibrary.Flat("ScopeGlass", new Color(0.02f, 0.03f, 0.04f), 0.95f, 0f);
                if (!string.IsNullOrEmpty(pm.textureSet)) return TextureSetMaterial(pm.textureSet, $"Weapon_{id}_{System.IO.Path.GetFileName(pm.textureSet)}");
            }
            return fallback;
        }

        static Transform s_LastBoltCarrier, s_LastBoltRotor;

        /// <summary>
        /// A part whose pivot is not on its mesh (the downloaded sniper's trigger pivots at the model origin, by the butt) gets a
        /// hinge at the top centre of its mesh, aligned with the weapon, so a trigger pull turns it in place and the trigger
        /// finger aims at the trigger itself. Parts already pivoted on their mesh are returned unchanged.
        /// </summary>
        static Transform HingeAtTop(GameObject root, Transform part, string name)
        {
            if (part == null) return null;
            var mf = part.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return part;
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            foreach (var v in mf.sharedMesh.vertices) { var w = root.transform.InverseTransformPoint(part.TransformPoint(v)); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
            var top = new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f);
            // only a pivot far off the part (the sniper's: 28 cm) is moved; the round-3 pistol and rifle keep their accepted setup
            if (Vector3.Distance(root.transform.InverseTransformPoint(part.position), top) < 0.10f) return part;
            var hinge = new GameObject(name).transform; hinge.SetParent(part.parent, false);
            hinge.position = root.transform.TransformPoint(top); hinge.rotation = root.transform.rotation; hinge.gameObject.layer = root.layer;
            part.SetParent(hinge, true);
            SetupLog.Ok($"{root.name}: {part.name} re-hinged at its top ({top.x * 1000f:0}, {top.y * 1000f:0}, {top.z * 1000f:0}) mm");
            return hinge;
        }

        /// <summary>
        /// Bolt rig: a carrier at the bolt's pivot on the bore axis (slides back) holding the cocking piece, and a rotor in it
        /// holding the bolt body with the handle (turns about the bore to lift the handle). Parts keep their world pose.
        /// </summary>
        static void BuildBolt(GameObject root, GameObject meshRoot, BoltDef bolt)
        {
            var carrier = new GameObject("BoltCarrier").transform; carrier.SetParent(meshRoot.transform.parent, false);
            carrier.localPosition = V(bolt.pivot); carrier.localRotation = Quaternion.identity;
            var rotor = new GameObject("BoltRotor").transform; rotor.SetParent(carrier, false);
            int moved = 0;
            foreach (var t in meshRoot.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.Exists(bolt.rotor ?? new string[0], n => t.name == n)) { t.SetParent(rotor, true); moved++; }
                else if (System.Array.Exists(bolt.carrier, n => t.name == n)) { t.SetParent(carrier, true); moved++; }
            }
            foreach (var t in carrier.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
            s_LastBoltCarrier = carrier; s_LastBoltRotor = rotor;
            SetupLog.Ok($"{root.name}: bolt rig with {moved} parts (travel {bolt.travel * 1000f:0} mm, lift {bolt.lift:0} deg)");
        }

        /// <summary>Working scope: the eyepiece lens (VRRange/ScopeLens) and the scope camera at the objective, both on the model so they follow the recoil kick.</summary>
        static void BuildScope(GameObject root, GameObject model, Weapon weapon, ScopeDef scope)
        {
            var shader = Shader.Find("VRRange/ScopeLens");
            if (shader == null) { SetupLog.Error("Shader VRRange/ScopeLens not found; the scope stays dark"); return; }
            var matPath = $"{RangePaths.Materials}/ScopeLens.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); RangePaths.EnsureFolder(RangePaths.Materials); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader; EditorUtility.SetDirty(mat);
            float r = scope.lensRadius > 0f ? scope.lensRadius : 0.007f;
            var lens = EditorRefs.MeshObject(model, "ScopeLens", EditorMeshUtil.Disc("ScopeLensDisc", r), new[] { mat }, V(scope.eyepiece), default, root.layer);
            var lensMr = lens.GetComponent<MeshRenderer>(); lensMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lensMr.receiveShadows = false;
            var camGo = EditorRefs.Child(model, "ScopeCamera", V(scope.objective));
            var cam = EditorRefs.Ensure<Camera>(camGo); cam.enabled = false; cam.fieldOfView = scope.fov > 0f ? scope.fov : 4.5f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; cam.tag = "Untagged";
            EditorRefs.Ensure<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(camGo);
            var ws = EditorRefs.Ensure<WeaponScope>(root);
            ws.Configure(weapon, cam, lensMr, cam.fieldOfView, scope.zero > 0f ? scope.zero : 25f);
            SetupLog.Ok($"{root.name}: scope lens r {r * 1000f:0.0} mm, field {cam.fieldOfView:0.0} deg, zero {(scope.zero > 0f ? scope.zero : 25f):0} m");
        }

        /// <summary>Small 3D ammo counter at the rear of the receiver, readable by the shooter looking down the weapon.</summary>
        static void BuildAmmoDisplay(GameObject root, Weapon weapon, WeaponLayout layout)
        {
            var pos = layout.ammoDisplay != null && layout.ammoDisplay.Length >= 3 ? V(layout.ammoDisplay) : V(layout.grip) + new Vector3(0f, 0.08f, -0.04f);
            var euler = layout.ammoDisplayEuler != null && layout.ammoDisplayEuler.Length >= 3 ? V(layout.ammoDisplayEuler) : new Vector3(-35f, 0f, 0f);
            var go = EditorRefs.Child(root, "AmmoDisplay", pos, euler);
            go.layer = root.layer;
            var tmp = EditorRefs.Ensure<TMPro.TextMeshPro>(go);
            tmp.text = "12 / 12  SEMI"; tmp.fontSize = 1.6f; tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
            tmp.transform.localScale = Vector3.one * 0.1f; // 1.6 pt at 0.1 scale ~ 9 mm glyphs
            tmp.fontStyle = TMPro.FontStyles.Bold; tmp.characterSpacing = 2f;
            var font = UiBuilder.GetFont(); if (font != null) tmp.font = font;
            var mr = go.GetComponent<MeshRenderer>(); if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
            var display = EditorRefs.Ensure<WeaponAmmoDisplay>(go);
            display.Configure(weapon, tmp);
        }

        /// <summary>
        /// Fits one box per contact seed to the visual mesh: every vertex inside the seed box (weapon space, seed rotation) is
        /// shrink-wrapped in the seed's frame, so the grip panels, rails and slide get boxes that match what is rendered.
        /// Boxes live on the HandContact layer (collides with nothing; projectiles and grab casters never query it).
        /// </summary>
        static void BuildContactVolumes(GameObject root, GameObject meshRoot, WeaponLayout layout)
        {
            var holder = EditorRefs.Child(root, "ContactVolumes");
            EditorRefs.DestroyChildren(holder.transform);
            int layer = LayerMask.NameToLayer(ProjectSettingsSetup.HandContactLayer); if (layer < 0) layer = 2;
            holder.layer = layer;
            var wcv = EditorRefs.Ensure<WeaponContactVolumes>(root);
            wcv.volumes.Clear();
            if (layout.contactVolumes == null || layout.contactVolumes.Count == 0 || meshRoot == null) { SetupLog.Warn($"{root.name}: no contact volume seeds in weapons_layout.json"); return; }
            var verts = new List<Vector3>();
            foreach (var mf in meshRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices) verts.Add(root.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            int built = 0;
            foreach (var seed in layout.contactVolumes)
            {
                if (seed == null || seed.center == null || seed.size == null) continue;
                var rot = Quaternion.Euler(V(seed.euler)); var inv = Quaternion.Inverse(rot);
                var c = V(seed.center); var h = V(seed.size) * 0.5f;
                var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity; int n = 0;
                var inside = new List<Vector3>();
                foreach (var v in verts)
                {
                    var l = inv * (v - c);
                    if (Mathf.Abs(l.x) > h.x || Mathf.Abs(l.y) > h.y || Mathf.Abs(l.z) > h.z) continue;
                    min = Vector3.Min(min, l); max = Vector3.Max(max, l); n++; inside.Add(l);
                }
                if (n < 12) { SetupLog.Warn($"{root.name}: contact seed '{seed.name}' encloses only {n} vertices; skipped"); continue; }
                var go = EditorRefs.Child(holder, $"Contact_{seed.name}"); go.layer = layer;
                go.transform.localPosition = c + rot * ((min + max) * 0.5f); go.transform.localRotation = rot;
                var box = EditorRefs.Ensure<BoxCollider>(go); box.isTrigger = true; box.center = Vector3.zero; box.size = Vector3.Max(max - min, Vector3.one * 0.004f);
                // round 3: the convex hull (26-direction polytope) of the same vertices, in the volume's own frame; the hand solver
                // and the acceptance test use it instead of the box, so raked grips and rounded straps are followed
                var mid = (min + max) * 0.5f;
                for (int k = 0; k < inside.Count; k++) inside[k] -= mid;
                EditorRefs.Ensure<ContactHull>(go).Fit(inside);
                wcv.volumes.Add(box); built++;
            }
            SetupLog.Ok($"{root.name}: {built} hand-contact volumes fitted to the mesh ({verts.Count} vertices)");
        }

        static BoxCollider BuildGripBox(GameObject root, string name, GripBox box, Vector3 fallbackCenter, Vector3 fallbackSize)
        {
            var go = EditorRefs.Child(root, name);
            go.layer = root.layer;
            go.transform.localPosition = box != null && box.center != null ? V(box.center) : fallbackCenter;
            go.transform.localRotation = box != null && box.euler != null ? Quaternion.Euler(V(box.euler)) : Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var col = EditorRefs.Ensure<BoxCollider>(go);
            col.center = Vector3.zero; col.size = box != null && box.size != null ? V(box.size) : fallbackSize; col.isTrigger = false;
            return col;
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
            anchor.gripCollider = null;
            anchor.palmCompression = palm != null && palm.compression > 0f ? palm.compression : 0.005f;
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
