#!/usr/bin/env python3
"""Keeps the downloaded CC0 models within Quest 2 polygon budgets: every FBX under Assets/Art/Props (limit 7000 tris) and the
weapon parts under Assets/Art/Weapons/Models (limit per part below) are decimated in place with Blender's collapse decimator
(UVs preserved) when they exceed the limit. Usage: PYTHONPATH=<bpy> python3 Tools/AssetGen/decimate_models.py [--dry]
"""
import bpy, os, sys, glob
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DRY = "--dry" in sys.argv
PROP_LIMIT = 4000  # per model (all objects together)
PROP_LIMITS = {"mounted_fluorescent_lights": 1800, "caged_hanging_light": 2600, "ammo_box": 1400, "clipboard": 1200, "security_camera_01": 2200,
               "cardboard_box_01": 2200, "plastic_crate_01": 2600, "metal_stool_01": 2600, "wall_clock": 1800, "metal_toolbox": 3000,
               "metal_trash_can": 3600, "wooden_military_crate": 3000, "drawer_cabinet": 5000, "bolt_action_rifle_7_62": 6000, "akm_decor": 5400,
               "steel_frame_shelves_01": 4400, "metal_office_desk": 7000, "projector_screen": 3600, "hand_truck": 3600, "tool_cart": 3600, "power_box_01": 2600}
WEAPON_PART_LIMITS = {"Magazine": 2500, "Body": 6500, "Barrel": 3600, "Slide": 2300}
def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
def process(path, limit_for, model_limit=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    before = sum(tris(o) for o in meshes); changed = False
    model_ratio = min(1.0, model_limit / before) if model_limit and before > 0 else 1.0
    for o in meshes:
        lim = limit_for(o.name); t = tris(o)
        ratio = min(model_ratio, lim / t if t > 0 else 1.0)
        if ratio >= 0.999 or t < 60: continue
        ratio = max(0.05, ratio)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        m = o.modifiers.new("dec", 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = ratio; m.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=m.name); changed = True
    after = sum(tris(o) for o in meshes)
    rel = os.path.relpath(path, ROOT)
    if changed and not DRY:
        bpy.ops.object.select_all(action='DESELECT')
        for o in bpy.data.objects:
            if o.type in ('MESH', 'EMPTY'): o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                                 bake_space_transform=False, mesh_smooth_type='OFF', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    print(f"{'DEC ' if changed else 'ok  '} {rel}: {before} -> {after} tris")
    return before, after
total_b = total_a = 0
for path in sorted(glob.glob(os.path.join(ROOT, "Assets", "Art", "Props", "*", "*.fbx"))):
    name = os.path.basename(os.path.dirname(path))
    b, a = process(path, lambda n: 10**9, PROP_LIMITS.get(name, PROP_LIMIT)); total_b += b; total_a += a
for path in sorted(glob.glob(os.path.join(ROOT, "Assets", "Art", "Weapons", "Models", "*", "*.fbx"))):
    b, a = process(path, lambda n: WEAPON_PART_LIMITS.get(n, 5000)); total_b += b; total_a += a
print(f"TOTAL {total_b} -> {total_a}")
