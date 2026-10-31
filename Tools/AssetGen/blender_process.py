#!/usr/bin/env python3
"""Prepares the downloaded CC0 weapon / hand assets for Unity with Blender-as-a-module (pip install bpy).
Run from the repo root:  PYTHONPATH=<bpy site-packages> python3 Tools/AssetGen/blender_process.py <scratch_dir> [weapons|hands|decor|all]

Outputs (all CC0 sources, see ASSET_CREDITS.md):
  Assets/Art/Weapons/Models/pistol/pistol.fbx      Poly Haven "service_pistol" (Mateusz Sadek)         parts Body, Slide, Trigger, Hammer, Magazine
  Assets/Art/Weapons/Models/rifle/rifle.fbx        OpenGameArt "M4A1 Assault Rifle" (3dmodelscc0.com)   parts Body, Barrel, ChargingHandle, Magazine, Trigger, Sight, Stock...
  Assets/Art/Weapons/Models/smg/smg.fbx            OpenGameArt "Micro Uzi" (TimDev), bevelled + baked textures   parts Body, Magazine
  Assets/Art/Hands/LeftHand.fbx / RightHand.fbx    Unity XR Hands sample rig, subdivided once, smooth-shaded, baked AO skin textures
  Assets/Art/Props/akm_decor/akm_decor.fbx         OpenGameArt "AKM" (weathered, 2K textures) for the wall rack
Convention: every weapon is exported with the barrel pointing along Blender -Y (= Unity +Z), up = +Z (= Unity +Y),
origin at the model's grip reference (centre of the pistol grip at trigger-finger height). Part bounds are written to
<model>/anchors.json so weapons_layout.json can be authored from numbers.
"""
import bpy, bmesh, os, sys, json, math
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SCRATCH = sys.argv[1] if len(sys.argv) > 1 else "/tmp"
MODE = sys.argv[2] if len(sys.argv) > 2 else "all"
WEAPONS = os.path.join(ROOT, "Assets", "Art", "Weapons", "Models")
HANDS = os.path.join(ROOT, "Assets", "Art", "Hands")
PROPS = os.path.join(ROOT, "Assets", "Art", "Props")

def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def meshes(): return [o for o in bpy.data.objects if o.type == 'MESH']
def select_only(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0] if objs else None
def world_verts(o): return [o.matrix_world @ v.co for v in o.data.vertices]
def bounds(objs):
    pts = [p for o in objs for p in world_verts(o)]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx
def apply_all(objs):
    select_only(objs); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
def unity(v):  # Blender (x, y, z) -> Unity (x, y, z): Unity.x = Blender.x, Unity.y = Blender.z, Unity.z = -Blender.y
    return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]

def grip_region(objs, trigger=None):
    """Vertices of the pistol-grip area: below the trigger top (+2 cm) and just behind the trigger (or the lowest 45 % if no trigger)."""
    mn, mx = bounds(objs); size = mx - mn
    pts = [p for o in objs if o.name not in ("Magazine",) for p in world_verts(o)]
    if trigger is not None:
        tmn, tmx = bounds([trigger])
        ymax = tmx.z + 0.02
        region = [p for p in pts if p.z <= ymax and tmn.y <= p.y <= tmn.y + 0.10]   # Blender: -Y is forward, so "behind" = larger y
    else:
        region = [p for p in pts if p.z <= mn.z + size.z * 0.45]
    if len(region) < 20: region = [p for p in pts if p.z <= mn.z + size.z * 0.45]
    return region

def orient_and_centre(objs, long_axis_hint=None):
    """Yaw the model (all sources are Z-up) so the barrel points along -Y, then put the grip reference at the origin."""
    mn, mx = bounds(objs); size = mx - mn
    axis = long_axis_hint if long_axis_hint is not None else (0 if size.x >= size.y else 1)
    pts = [p for o in objs for p in world_verts(o)]
    zcut = mn.z + size.z * 0.3
    low = [p for p in pts if p.z <= zcut] or pts
    grip = sum(low, Vector()) / len(low); centre = (mn + mx) / 2
    sign = 1.0 if (centre[axis] - grip[axis]) >= 0 else -1.0   # muzzle lies on the far side from the grip
    muzzle_dir = Vector((sign, 0, 0)) if axis == 0 else Vector((0, sign, 0))
    yaw = math.atan2(-muzzle_dir.x, -muzzle_dir.y)  # signed angle that maps muzzle_dir onto -Y
    rot = Matrix.Rotation(yaw, 4, 'Z')
    for o in objs: o.matrix_world = rot @ o.matrix_world
    apply_all(objs)
    trigger = next((o for o in objs if o.name == "Trigger"), None)
    region = grip_region(objs, trigger)
    g = sum(region, Vector()) / len(region)
    grip_point = Vector((0.0, g.y, g.z))
    for o in objs: o.matrix_world = Matrix.Translation(-grip_point) @ o.matrix_world
    apply_all(objs)
    return grip_point

def seat_magazine(objs):
    """Moves a loose Magazine part into the grip (x centred, z centred on the grip region, floor plate just below the body)."""
    mag = next((o for o in objs if o.name == "Magazine"), None)
    body = next((o for o in objs if o.name == "Body"), None)
    if mag is None or body is None: return
    region = grip_region([body], next((o for o in objs if o.name == "Trigger"), None))
    g = sum(region, Vector()) / len(region)
    bmn, bmx = bounds([body]); mmn, mmx = bounds([mag])
    target_bottom = bmn.z - 0.004
    delta = Vector((0 - (mmn.x + mmx.x) * 0.5, g.y - (mmn.y + mmx.y) * 0.5, target_bottom - mmn.z))
    mag.matrix_world = Matrix.Translation(delta) @ mag.matrix_world
    apply_all([mag])

def write_anchors(objs, path, extra=None):
    mn, mx = bounds(objs)
    data = {"bounds_min": unity(mn), "bounds_max": unity(mx), "parts": {}}
    # fix unity min/max ordering after axis flip
    for k in ("bounds_min", "bounds_max"): pass
    for o in objs:
        a, b = bounds([o]); ua, ub = unity(a), unity(b)
        data["parts"][o.name] = {"min": [min(ua[i], ub[i]) for i in range(3)], "max": [max(ua[i], ub[i]) for i in range(3)], "tris": sum(len(p.vertices) - 2 for p in o.data.polygons)}
    ua, ub = unity(mn), unity(mx)
    data["bounds_min"] = [min(ua[i], ub[i]) for i in range(3)]; data["bounds_max"] = [max(ua[i], ub[i]) for i in range(3)]
    if extra: data.update(extra)
    json.dump(data, open(path, "w"), indent=1)
    print("  anchors ->", os.path.relpath(path, ROOT)); print("  ", json.dumps(data["bounds_min"]), json.dumps(data["bounds_max"]))
    for k, v in data["parts"].items(): print("   part", k, v["min"], v["max"], v["tris"])

def export_fbx(objs, path, armature=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(objs)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'ARMATURE', 'EMPTY'} if armature else {'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
                             mesh_smooth_type='OFF', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
                             path_mode='STRIP', embed_textures=False, use_armature_deform_only=False)
    print("  fbx ->", os.path.relpath(path, ROOT), "(%d objects)" % len(objs))

def shade_smooth_by_angle(o, angle_deg=35):
    select_only([o])
    try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_deg))
    except Exception:
        try: bpy.ops.object.shade_auto_smooth(angle=math.radians(angle_deg))
        except Exception: bpy.ops.object.shade_smooth()

def make_bake_image(name, size):
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False); img.colorspace_settings.name = 'Non-Color'; return img

def bake_ao(objs, size=1024, samples=24):
    """Bakes ambient occlusion of the given objects into one image (needs UVs). Returns the image."""
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = samples
    sc.render.bake.margin = 6; sc.render.bake.use_selected_to_active = False
    img = make_bake_image("ao_bake", size)
    for o in objs:
        if not o.data.materials:
            o.data.materials.append(bpy.data.materials.new("bake_" + o.name))
        for m in o.data.materials:
            if m is None: continue
            m.use_nodes = True
            node = m.node_tree.nodes.new("ShaderNodeTexImage"); node.image = img; m.node_tree.nodes.active = node
    select_only(objs)
    bpy.ops.object.bake(type='AO')
    return img

def image_to_pil(img):
    import numpy as np
    from PIL import Image
    w, h = img.size; px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    return Image.fromarray((np.clip(px[::-1, :, :3], 0, 1) * 255).astype('uint8'))

# ------------------------------------------------------------------ pistol
def process_pistol():
    reset(); src = os.path.join(WEAPONS, "service_pistol", "service_pistol.fbx")
    bpy.ops.import_scene.fbx(filepath=src)
    keep = {"service_pistol_pistol_a": "Body", "service_pistol_slide_a": "Slide", "service_pistol_trigger_a": "Trigger", "service_pistol_hammer_a": "Hammer", "service_pistol_magazine_loaded": "Magazine"}
    for o in list(bpy.data.objects):
        if o.type != 'MESH' or o.name not in keep: bpy.data.objects.remove(o, do_unlink=True)
    objs = meshes()
    for o in objs: o.name = keep[o.name]; o.data.name = o.name
    apply_all(objs)
    orient_and_centre(objs)
    seat_magazine(objs)
    orient_and_centre(objs)
    out = os.path.join(WEAPONS, "pistol"); os.makedirs(out, exist_ok=True)
    export_fbx(objs, os.path.join(out, "pistol.fbx"))
    write_anchors(objs, os.path.join(out, "anchors.json"), {"source": "Poly Haven service_pistol (CC0)"})
    # textures: reuse the converted Poly Haven set under the pistol folder name
    import shutil
    for suf in ("albedo.jpg", "normal.jpg", "mask.png", "ao.jpg"):
        s = os.path.join(WEAPONS, "service_pistol", "service_pistol_" + suf)
        if os.path.exists(s): shutil.copy(s, os.path.join(out, "pistol_" + suf))
    shutil.copy(os.path.join(WEAPONS, "service_pistol", "SOURCE.txt"), os.path.join(out, "SOURCE.txt"))

# ------------------------------------------------------------------ rifle (M4A1)
def process_rifle():
    reset(); src = os.path.join(SCRATCH, "dl", "oga", "m4a1", "M4A1", "M4A1.fbx")
    bpy.ops.import_scene.fbx(filepath=src)
    objs = meshes()
    # flatten hierarchy (empties carry transforms); keep world transforms
    for o in objs:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in list(bpy.data.objects):
        if o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
    rename = {"Base": "Body", "Barrel": "Barrel", "Charging_Handle": "ChargingHandle", "Magazine": "Magazine", "Trigger": "Trigger", "Stock": "Stock", "Sight": "Sight", "Sight_2": "SightRear", "Firemode_Selector": "Selector", "Ejector_2": "EjectorA", "Ejector_Lid": "EjectorLid", "Switch1": "SwitchA", "Switch2": "SwitchB"}
    for o in objs:
        if o.name in rename: o.name = rename[o.name]; o.data.name = o.name
    apply_all(objs)
    # the model is in metres already (0.86 m long); orient barrel -Y, grip at origin
    orient_and_centre(objs)
    # merge tiny parts into the body to reduce draw calls (keep animated parts separate)
    animated = {"Body", "Barrel", "ChargingHandle", "Magazine", "Trigger"}
    body = next(o for o in objs if o.name == "Body")
    tiny = [o for o in objs if o.name not in animated]
    if tiny:
        select_only([body] + tiny); bpy.context.view_layer.objects.active = body; bpy.ops.object.join()
    objs = meshes()
    out = os.path.join(WEAPONS, "rifle"); os.makedirs(out, exist_ok=True)
    export_fbx(objs, os.path.join(out, "rifle.fbx"))
    write_anchors(objs, os.path.join(out, "anchors.json"), {"source": "OpenGameArt M4A1 Assault Rifle (CC0, via 3dmodelscc0.com)"})
    # textures -> URP inputs
    from PIL import Image
    base = os.path.dirname(src)
    Image.open(os.path.join(base, "M4A1_Base_Color.png")).convert("RGB").save(os.path.join(out, "rifle_albedo.jpg"), quality=90)
    Image.open(os.path.join(base, "M4A1_Normal.png")).convert("RGB").save(os.path.join(out, "rifle_normal.jpg"), quality=92)
    rough = Image.open(os.path.join(base, "M4A1_Roughness.png")).convert("L"); metal = Image.open(os.path.join(base, "M4A1_Metallic.png")).convert("L").resize(rough.size)
    Image.merge("RGBA", (metal, metal, metal, Image.eval(rough, lambda v: 255 - v))).save(os.path.join(out, "rifle_mask.png"), optimize=True)
    open(os.path.join(out, "SOURCE.txt"), "w").write("Source: OpenGameArt 'M4A1 Assault Rifle' https://opengameart.org/content/m4a1-assault-rifle (origin https://www.3dmodelscc0.com/model/m4a1-assault-rifle)\nLicense: CC0 1.0 Universal\nProcessed with Tools/AssetGen/blender_process.py (parts flattened/renamed, oriented barrel +Z, grip at origin; 2K textures converted to URP inputs)\n")

# ------------------------------------------------------------------ smg (Micro Uzi)
def process_smg():
    reset(); src = os.path.join(SCRATCH, "dl", "oga", "uzi.blend")
    bpy.ops.wm.open_mainfile(filepath=src)
    for o in list(bpy.data.objects):
        if o.type != 'MESH' or o.name not in ("Body", "Mag"): bpy.data.objects.remove(o, do_unlink=True)
    objs = meshes()
    for o in objs:
        o.name = "Body" if o.name == "Body" else "Magazine"; o.data.name = o.name
        o.data.materials.clear()
    apply_all(objs)
    # scale: Micro Uzi ~ 0.25 m long (stock folded). Current long axis in Blender units:
    mn, mx = bounds(objs); size = mx - mn; L = max(size.x, size.y)
    s = 0.25 / L
    for o in objs: o.matrix_world = Matrix.Scale(s, 4) @ o.matrix_world
    apply_all(objs)
    # bevel the hard box edges a little and smooth-shade so it stops looking like a block model
    for o in objs:
        m = o.modifiers.new("bevel", 'BEVEL'); m.width = 0.0012; m.segments = 2; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(40)
        select_only([o]); bpy.ops.object.modifier_apply(modifier=m.name)
        shade_smooth_by_angle(o, 40)
    orient_and_centre(objs, long_axis_hint=None)
    # bake AO for texture generation (UVs exist in the source)
    img = bake_ao(objs, size=1024, samples=24)
    out = os.path.join(WEAPONS, "smg"); os.makedirs(out, exist_ok=True)
    ao = image_to_pil(img).convert("L")
    import numpy as np
    from PIL import Image, ImageFilter
    a = np.array(ao, dtype=np.float32) / 255.0
    rng = np.random.default_rng(7)
    noise = np.array(Image.fromarray((rng.random((128, 128)) * 255).astype('uint8')).resize(ao.size, Image.BILINEAR).filter(ImageFilter.GaussianBlur(1.5)), dtype=np.float32) / 255.0
    grain = np.array(Image.fromarray((rng.random((1024, 1024)) * 255).astype('uint8')).resize(ao.size), dtype=np.float32) / 255.0
    base = 0.11 + 0.05 * noise + 0.03 * grain  # parkerized dark steel
    albedo = np.stack([base * (0.55 + 0.45 * a) * c for c in (1.0, 1.0, 1.04)], axis=-1)
    Image.fromarray((np.clip(albedo, 0, 1) * 255).astype('uint8')).save(os.path.join(out, "smg_albedo.jpg"), quality=90)
    rough = np.clip(0.42 + 0.18 * noise + 0.25 * (1 - a) + 0.06 * grain, 0, 1)
    smooth = ((1 - rough) * 255).astype('uint8'); metal = np.full(a.shape, 230, dtype='uint8')
    Image.merge("RGBA", tuple(Image.fromarray(x) for x in (metal, metal, metal, smooth))).save(os.path.join(out, "smg_mask.png"), optimize=True)
    ao.save(os.path.join(out, "smg_ao.jpg"), quality=85)
    for o in objs: o.data.materials.clear()
    export_fbx(objs, os.path.join(out, "smg.fbx"))
    write_anchors(objs, os.path.join(out, "anchors.json"), {"source": "OpenGameArt Micro Uzi by TimDev (CC0), bevelled, textures baked here"})
    open(os.path.join(out, "SOURCE.txt"), "w").write("Source: OpenGameArt 'Micro Uzi - Untextured' by TimDev https://opengameart.org/content/micro-uzi-untextured\nLicense: CC0 1.0 Universal\nProcessed with Tools/AssetGen/blender_process.py (silencer removed, scaled to 0.25 m, bevelled, smooth-shaded, AO baked and PBR textures generated)\n")

# ------------------------------------------------------------------ hands
def process_hand(side):
    reset(); src = os.path.join(HANDS, f"{side}Hand.fbx")
    bpy.ops.import_scene.fbx(filepath=src, ignore_leaf_bones=False, automatic_bone_orientation=False)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    # one level of Catmull-Clark (2.7k -> ~10.8k tris) applied before the armature modifier; weights are interpolated
    sub = mesh.modifiers.new("subsurf", 'SUBSURF'); sub.levels = 1; sub.render_levels = 1
    select_only([mesh])
    while mesh.modifiers[0] != sub: bpy.ops.object.modifier_move_up(modifier=sub.name)
    bpy.ops.object.modifier_apply(modifier=sub.name)
    bpy.ops.object.shade_smooth()
    # AO bake for the skin texture
    img = bake_ao([mesh], size=1024, samples=24)
    ao = image_to_pil(img).convert("L")
    import numpy as np
    from PIL import Image, ImageFilter
    a = np.array(ao, dtype=np.float32) / 255.0
    rng = np.random.default_rng(3 if side == "Left" else 4)
    n1 = np.array(Image.fromarray((rng.random((64, 64)) * 255).astype('uint8')).resize(ao.size, Image.BICUBIC), dtype=np.float32) / 255.0
    n2 = np.array(Image.fromarray((rng.random((512, 512)) * 255).astype('uint8')).resize(ao.size, Image.BILINEAR).filter(ImageFilter.GaussianBlur(0.8)), dtype=np.float32) / 255.0
    skin = np.array([0.76, 0.57, 0.46], dtype=np.float32)       # light-medium skin tone (sRGB-ish)
    flush = np.array([0.62, 0.36, 0.30], dtype=np.float32)      # knuckles / creases
    t = np.clip((1 - a) * 1.4 + 0.15 * n1, 0, 1)[..., None]
    col = skin * (1 - t) + flush * t
    col = col * (0.92 + 0.08 * n2[..., None]) * (0.55 + 0.45 * a[..., None])
    out = HANDS
    Image.fromarray((np.clip(col, 0, 1) * 255).astype('uint8')).save(os.path.join(out, f"skin_{side.lower()}_albedo.jpg"), quality=90)
    rough = np.clip(0.52 + 0.1 * n2 + 0.22 * (1 - a), 0, 1)
    smooth = ((1 - rough) * 255).astype('uint8'); metal = np.zeros(a.shape, dtype='uint8')
    Image.merge("RGBA", tuple(Image.fromarray(x) for x in (metal, metal, metal, smooth))).save(os.path.join(out, f"skin_{side.lower()}_mask.png"), optimize=True)
    ao.save(os.path.join(out, f"skin_{side.lower()}_ao.jpg"), quality=85)
    mesh.data.materials.clear()
    print(f"  {side} hand: tris={sum(len(p.vertices)-2 for p in mesh.data.polygons)} bones={len(arm.data.bones)}")
    export_fbx([arm, mesh], os.path.join(out, f"{side}Hand.fbx"), armature=True)

# ------------------------------------------------------------------ AKM decor
def process_akm():
    reset(); src = os.path.join(SCRATCH, "dl", "oga", "akm54.blend")
    bpy.ops.wm.open_mainfile(filepath=src)
    objs = [o for o in meshes()]
    for o in list(bpy.data.objects):
        if o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
    apply_all(objs); orient_and_centre(objs)
    out = os.path.join(PROPS, "akm_decor"); os.makedirs(out, exist_ok=True)
    from PIL import Image
    import numpy as np
    def save(name, fn, mode="RGB", q=90):
        img = bpy.data.images.get(name)
        if img is None: return None
        w, h = img.size; px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
        im = Image.fromarray((np.clip(px[:, :, :3], 0, 1) * 255).astype('uint8')).resize((2048, 2048), Image.LANCZOS)
        if mode == "L": im = im.convert("L")
        if fn: im.save(os.path.join(out, fn), quality=q)
        return im
    save("Material_BaseColor.png", "akm_decor_albedo.jpg"); save("Material_Normal.png", "akm_decor_normal.jpg", q=92); save("Material_AmbientOcclusion.png", "akm_decor_ao.jpg", "L", 85)
    rough = save("Material_Roughness.png", None, "L"); metal = save("Material_Metallic.png", None, "L")
    Image.merge("RGBA", (metal, metal, metal, Image.eval(rough, lambda v: 255 - v))).save(os.path.join(out, "akm_decor_mask.png"), optimize=True)
    for o in objs: o.data.materials.clear(); o.name = "AKM"
    export_fbx(objs, os.path.join(out, "akm_decor.fbx"))
    write_anchors(objs, os.path.join(out, "anchors.json"), {"source": "OpenGameArt AKM (CC0)"})
    open(os.path.join(out, "SOURCE.txt"), "w").write("Source: OpenGameArt 'AKM' https://opengameart.org/content/akm\nLicense: CC0 1.0 Universal\nProcessed with Tools/AssetGen/blender_process.py (textures unpacked and downsized to 2K, oriented barrel +Z)\n")

if __name__ == "__main__":
    if MODE in ("weapons", "all"):
        print("== pistol"); process_pistol()
        print("== rifle"); process_rifle()
        print("== smg"); process_smg()
    if MODE in ("hands", "all"):
        for side in ("Left", "Right"): print("== hand", side); process_hand(side)
    if MODE in ("decor", "all"):
        print("== akm"); process_akm()
    print("done")
