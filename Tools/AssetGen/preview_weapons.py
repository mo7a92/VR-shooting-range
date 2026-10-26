#!/usr/bin/env python3
"""Flat-shaded orthographic preview of the generated weapon OBJ parts (side + top + 3/4 views) for quick visual checks."""
import os, sys, json, math, numpy as np
from PIL import Image, ImageDraw
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
W = os.path.join(ROOT, "Assets", "Art", "Weapons")
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Tools", "_out", "weapons_preview.png")
os.makedirs(os.path.dirname(out), exist_ok=True)
layout = json.load(open(os.path.join(W, "weapons_layout.json")))
COL = {"Gunmetal": (70, 74, 82), "Polymer": (40, 40, 44), "Rubber": (30, 30, 30), "Metal": (170, 172, 176), "Dark": (18, 18, 20)}
def load(path, pivot):
    v, tris, mat = [], [], "Gunmetal"
    for line in open(path):
        p = line.split()
        if not p: continue
        if p[0] == "v": v.append((float(p[1]) + pivot[0], float(p[2]) + pivot[1], float(p[3]) + pivot[2]))
        elif p[0] == "usemtl": mat = p[1]
        elif p[0] == "f": tris.append(([int(x.split("/")[0]) - 1 for x in p[1:]], mat))
    return np.array(v), tris
def render(draw, tris_all, verts_all, ox, oy, scale, view):
    light = np.array([0.3, 0.8, -0.5]); light /= np.linalg.norm(light)
    items = []
    for verts, tris in zip(verts_all, tris_all):
        for idx, mat in tris:
            p = verts[idx]
            if view == "side": xy = p[:, [2, 1]]; depth = -p[:, 0]
            elif view == "top": xy = p[:, [2, 0]]; depth = p[:, 1]
            else:
                a = math.radians(35); b = math.radians(25)
                x = p[:, 2] * math.cos(a) - p[:, 0] * math.sin(a); z = p[:, 2] * math.sin(a) + p[:, 0] * math.cos(a)
                y = p[:, 1] * math.cos(b) - z * math.sin(b); depth = p[:, 1] * math.sin(b) + z * math.cos(b)
                xy = np.stack([x, y], 1); depth = -depth
            n = np.cross(p[1] - p[0], p[2] - p[0]); ln = np.linalg.norm(n)
            if ln == 0: continue
            n /= ln
            shade = 0.45 + 0.55 * max(0.0, float(np.dot(n, light)))
            c = tuple(int(min(255, ch * shade * 1.6)) for ch in COL.get(mat, (120, 120, 120)))
            pts = [(ox + float(q[0]) * scale, oy - float(q[1]) * scale) for q in xy]
            items.append((float(depth.mean()), pts, c))
    items.sort(key=lambda t: t[0])
    for _, pts, c in items: draw.polygon(pts, fill=c)
img = Image.new("RGB", (1800, 1500), (235, 235, 235)); d = ImageDraw.Draw(img)
scale = 900
for row, (name, wdef) in enumerate(layout["weapons"].items()):
    verts_all, tris_all = [], []
    for part in wdef["parts"]:
        v, t = load(os.path.join(W, part["file"]), part["pivot"]); verts_all.append(v); tris_all.append(t)
    y0 = 250 + row * 480
    render(d, tris_all, verts_all, 500, y0, scale, "side"); d.text((60, y0 - 200), f"{name} side", fill=(0, 0, 0))
    render(d, tris_all, verts_all, 1250, y0, scale * 0.6, "top"); d.text((1000, y0 - 200), f"{name} top", fill=(0, 0, 0))
    render(d, tris_all, verts_all, 1250, y0 + 180, scale * 0.45, "iso")
    mz = wdef["muzzle"]; d.ellipse([500 + mz[2] * scale - 5, y0 - mz[1] * scale - 5, 500 + mz[2] * scale + 5, y0 - mz[1] * scale + 5], outline=(255, 0, 0), width=2)
    g = wdef["grip"]; d.ellipse([500 + g[2] * scale - 5, y0 - g[1] * scale - 5, 500 + g[2] * scale + 5, y0 - g[1] * scale + 5], outline=(0, 160, 0), width=2)
img.save(out); print("wrote", out)
