#!/usr/bin/env python3
"""Downloads CC0 PBR material sets from ambientCG (https://ambientcg.com, Creative Commons CC0 1.0) and converts
them to Unity URP Lit inputs under Assets/Art/Textures/PBR:
  <name>_albedo.jpg (sRGB colour), <name>_normal.jpg (OpenGL-convention normal, matches Unity),
  <name>_mask.png (R = metalness, A = smoothness = 1 - roughness), <name>_ao.jpg (ambient occlusion).
Run from the repo root:  python3 -I Tools/AssetGen/fetch_ambientcg.py   (re-running skips existing sets)
The asset IDs below are recorded in ASSET_CREDITS.md.
"""
import os, io, sys, zipfile, urllib.request, shutil
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "Textures", "PBR")
os.makedirs(OUT, exist_ok=True)

# ambientCG asset id -> project material set name
SETS = {
    "Concrete034": "pbr_floor_concrete",      # polished grey concrete floor
    "Concrete031": "pbr_wall_concrete",       # smooth wall concrete
    "Concrete046": "pbr_backstop_concrete",   # rough concrete (berm / service areas)
    "Rubber004": "pbr_rubber",                # backstop / mats / counter foam
    "Metal032": "pbr_metal_brushed",          # rails, trolleys, fixtures
    "Metal046A": "pbr_metal_dark",            # weapon metal, dark fixtures
    "Wood094": "pbr_wood",                    # counters, benches
    "Plastic010": "pbr_polymer",              # weapon grips / polymer parts
    "Fabric030": "pbr_acoustic_fabric",       # booth dividers / acoustic panels
    "Tiles107": "pbr_floor_tiles",            # waiting area floor
    "Carpet016": "pbr_carpet",                # observation area
}

def fetch(asset_id):
    url = f"https://ambientcg.com/get?file={asset_id}_1K-JPG.zip"
    req = urllib.request.Request(url, headers={"User-Agent": "vr-range-asset-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()

def convert(asset_id, name, data):
    z = zipfile.ZipFile(io.BytesIO(data))
    names = z.namelist()
    def find(suffix):
        for n in names:
            if n.lower().endswith(suffix.lower()): return n
        return None
    color, normal, rough, metal, ao = (find(s) for s in ("_Color.jpg", "_NormalGL.jpg", "_Roughness.jpg", "_Metalness.jpg", "_AmbientOcclusion.jpg"))
    if not color or not normal or not rough:
        print(f"  {asset_id}: missing maps in zip ({names[:6]})"); return False
    def img(n): return Image.open(io.BytesIO(z.read(n))).convert("L" if n in (rough, metal, ao) else "RGB")
    img(color).save(os.path.join(OUT, f"{name}_albedo.jpg"), quality=88)
    img(normal).save(os.path.join(OUT, f"{name}_normal.jpg"), quality=92)
    r = img(rough); size = r.size
    m = img(metal) if metal else Image.new("L", size, 0)
    smooth = Image.eval(r, lambda v: 255 - v)
    mask = Image.merge("RGBA", (m, m, m, smooth))
    mask.save(os.path.join(OUT, f"{name}_mask.png"), optimize=True)
    if ao: img(ao).save(os.path.join(OUT, f"{name}_ao.jpg"), quality=85)
    with open(os.path.join(OUT, f"{name}_SOURCE.txt"), "w") as f:
        f.write(f"Source: ambientCG asset {asset_id} (https://ambientcg.com/a/{asset_id}), 1K JPG set\nLicense: CC0 1.0 Universal (public domain dedication)\n")
    print(f"  {asset_id} -> {name} ({size[0]}x{size[1]}, metal map: {'yes' if metal else 'no'}, ao: {'yes' if ao else 'no'})")
    return True

def main():
    ok = 0
    for asset_id, name in SETS.items():
        if os.path.exists(os.path.join(OUT, f"{name}_mask.png")):
            print(f"  {name}: already present"); ok += 1; continue
        try:
            data = fetch(asset_id)
            if convert(asset_id, name, data): ok += 1
        except Exception as e:
            print(f"  {asset_id}: download failed: {e}")
    print(f"done: {ok}/{len(SETS)} sets")

if __name__ == "__main__":
    main()
