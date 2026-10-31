#!/usr/bin/env python3
"""Downloads CC0 models from Poly Haven (https://polyhaven.com, CC0 1.0) as FBX + JPG textures and converts the
textures to Unity URP Lit inputs next to the model:
  <asset>/<asset>.fbx, <asset>/<asset>_albedo.jpg, _normal.jpg (OpenGL convention = Unity), _mask.png (R = metal,
  A = smoothness = 1 - roughness), _ao.jpg, optional _emission.png.
Run from the repo root:  python3 -I Tools/AssetGen/fetch_polyhaven.py [asset ...]   (re-running skips existing assets)
Everything downloaded is CC0 (recorded in ASSET_CREDITS.md). Weapons go to Assets/Art/Weapons/Models, props to Assets/Art/Props.
"""
import os, sys, json, urllib.request
from io import BytesIO
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
WEAPONS = {"service_pistol": "2k"}
PROPS = {  # asset id -> texture resolution
    "mounted_fluorescent_lights": "1k", "caged_hanging_light": "1k",
    "steel_frame_shelves_01": "1k", "metal_office_desk": "1k", "metal_stool_01": "1k", "painted_wooden_bench": "1k",
    "plastic_monobloc_chair_01": "1k", "worn_metal_rack": "1k", "tool_cart": "1k", "fire_alarm": "1k", "security_camera_01": "1k", "wall_clock": "1k",
    "WetFloorSign_01": "1k", "metal_trash_can": "1k", "cardboard_box_01": "1k", "plastic_crate_01": "1k",
    "ammo_box": "1k", "wooden_military_crate": "1k", "metal_toolbox": "1k",
    "clipboard": "1k", "hand_truck": "1k", "television_02": "1k", "power_box_01": "1k", "projector_screen": "1k", "bolt_action_rifle_7_62": "1k",
}

def get(url, timeout=180):
    req = urllib.request.Request(url, headers={"User-Agent": "vr-range-asset-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=timeout) as r: return r.read()

def api(path): return json.loads(get("https://api.polyhaven.com/" + path, 60))

def tex_url(files, key, res):
    if key not in files: return None
    e = files[key].get(res) or files[key].get("1k") or next(iter(files[key].values()))
    return (e.get("jpg") or e.get("png") or {}).get("url")

def fetch(asset, res, out_root):
    out = os.path.join(out_root, asset)
    if os.path.exists(os.path.join(out, asset + ".fbx")):
        print(f"  {asset}: present"); return True
    files = api("files/" + asset)
    info = api("info/" + asset)
    fbx = files.get("fbx", {}).get("1k", {}).get("fbx", {}).get("url")
    if not fbx: print(f"  {asset}: no fbx"); return False
    os.makedirs(out, exist_ok=True)
    open(os.path.join(out, asset + ".fbx"), "wb").write(get(fbx))
    def save_img(key, suffix, mode="RGB", q=88, fmt="jpg"):
        u = tex_url(files, key, res)
        if not u: return None
        im = Image.open(BytesIO(get(u))).convert(mode)
        p = os.path.join(out, f"{asset}_{suffix}.{fmt}")
        if fmt == "jpg": im.save(p, quality=q)
        else: im.save(p, optimize=True)
        return im
    diff = save_img("Diffuse", "albedo")
    nor = save_img("nor_gl", "normal", q=92)
    ru, mu, au = tex_url(files, "Rough", res), tex_url(files, "Metal", res), tex_url(files, "AO", res)
    size = diff.size if diff else (1024, 1024)
    rough = Image.open(BytesIO(get(ru))).convert("L") if ru else Image.new("L", size, 128)
    metal = Image.open(BytesIO(get(mu))).convert("L").resize(rough.size) if mu else Image.new("L", rough.size, 0)
    smooth = Image.eval(rough, lambda v: 255 - v)
    Image.merge("RGBA", (metal, metal, metal, smooth)).save(os.path.join(out, f"{asset}_mask.png"), optimize=True)
    if au: Image.open(BytesIO(get(au))).convert("L").save(os.path.join(out, f"{asset}_ao.jpg"), quality=85)
    if "Emission" in files: save_img("Emission", "emission", fmt="png")
    authors = ", ".join(info.get("authors", {}).keys())
    dims = info.get("dimensions")
    with open(os.path.join(out, "SOURCE.txt"), "w") as f:
        f.write(f"Source: Poly Haven model '{asset}' (https://polyhaven.com/a/{asset}) by {authors}\nLicense: CC0 1.0 Universal\nTextures: {res} JPG set converted to URP inputs by Tools/AssetGen/fetch_polyhaven.py\nDimensions (mm): {dims}\n")
    print(f"  {asset}: fbx + {res} textures (normal: {'yes' if nor else 'no'}, ao: {'yes' if au else 'no'}, emission: {'yes' if 'Emission' in files else 'no'}) by {authors}", flush=True)
    return True

if __name__ == "__main__":
    wanted = sys.argv[1:]
    ok = True
    for asset, res in WEAPONS.items():
        if wanted and asset not in wanted: continue
        try: ok &= fetch(asset, res, os.path.join(ROOT, "Assets", "Art", "Weapons", "Models"))
        except Exception as e: print(f"  {asset}: FAILED {e}", flush=True); ok = False
    for asset, res in PROPS.items():
        if wanted and asset not in wanted: continue
        try: ok &= fetch(asset, res, os.path.join(ROOT, "Assets", "Art", "Props"))
        except Exception as e: print(f"  {asset}: FAILED {e}", flush=True); ok = False
    sys.exit(0 if ok else 1)
