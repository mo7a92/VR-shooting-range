#!/usr/bin/env python3
"""Generates the three weapon models (pistol, rifle, SMG) as OBJ part files plus a layout JSON that the Unity
prefab builder uses to assemble them (pivots, materials, muzzle, grips). Units: metres, +Z forward, +Y up.
These are original parametric models built from chamfered boxes and cylinders: not real firearm blueprints.
Run from the repo root:  python3 -I Tools/AssetGen/gen_weapons.py
"""
import os, json, math

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "Weapons")
os.makedirs(OUT, exist_ok=True)
TEXEL = 4.0  # UV units per metre

class Mesh:
    def __init__(self): self.v = []; self.vt = []; self.vn = []; self.f = []  # faces: list of (mat, [(vi,ti,ni)...])
    def add_poly(self, pts, mat):
        # pts: list of (x,y,z) in CCW order when viewed from outside (we fix orientation by centroid test)
        n = self._normal(pts)
        base = len(self.v)
        for p in pts: self.v.append(p)
        self.vn.append(n); ni = len(self.vn)
        # planar UVs by dominant axis
        ax = max(range(3), key=lambda i: abs(n[i]))
        for p in pts:
            if ax == 0: uv = (p[2], p[1])
            elif ax == 1: uv = (p[0], p[2])
            else: uv = (p[0], p[1])
            self.vt.append((uv[0] * TEXEL, uv[1] * TEXEL))
        idx = [(base + i + 1, base + i + 1, ni) for i in range(len(pts))]
        # fan triangulate
        for i in range(1, len(pts) - 1):
            self.f.append((mat, [idx[0], idx[i], idx[i + 1]]))
    @staticmethod
    def _normal(pts):
        ax, ay, az = pts[0]; bx, by, bz = pts[1]; cx, cy, cz = pts[2]
        ux, uy, uz = bx - ax, by - ay, bz - az; vx, vy, vz = cx - ax, cy - ay, cz - az
        n = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        l = math.sqrt(sum(c * c for c in n)) or 1.0
        return tuple(c / l for c in n)
    def write(self, path, name):
        mats = sorted(set(m for m, _ in self.f))
        with open(path, "w") as fh:
            fh.write(f"# VR Range generated weapon part: {name}\nmtllib weapons.mtl\no {name}\n")
            for x, y, z in self.v: fh.write(f"v {x:.5f} {y:.5f} {z:.5f}\n")
            for u, v in self.vt: fh.write(f"vt {u:.4f} {v:.4f}\n")
            for x, y, z in self.vn: fh.write(f"vn {x:.4f} {y:.4f} {z:.4f}\n")
            for m in mats:
                fh.write(f"usemtl {m}\n")
                for mat, tri in self.f:
                    if mat != m: continue
                    fh.write("f " + " ".join(f"{a}/{b}/{c}" for a, b, c in tri) + "\n")
        print("wrote", os.path.relpath(path, ROOT), f"{len(self.f)} tris")

def oriented(poly, center):
    """Ensure polygon winding faces away from the given center."""
    n = Mesh._normal(poly)
    cx = sum(p[0] for p in poly) / len(poly); cy = sum(p[1] for p in poly) / len(poly); cz = sum(p[2] for p in poly) / len(poly)
    d = (cx - center[0]) * n[0] + (cy - center[1]) * n[1] + (cz - center[2]) * n[2]
    return poly if d >= 0 else list(reversed(poly))

def transform(pts, rot=(0, 0, 0), pos=(0, 0, 0)):
    rx, ry, rz = (math.radians(a) for a in rot)
    out = []
    for x, y, z in pts:
        # rotate X then Y then Z
        y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
        x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
        x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
        out.append((x + pos[0], y + pos[1], z + pos[2]))
    return out

def chamfer_box(mesh, center, size, chamfer=0.0, mat="Gunmetal", rot=(0, 0, 0)):
    sx, sy, sz = (s / 2 for s in size); c = min(chamfer, sx * 0.9, sy * 0.9, sz * 0.9)
    polys = []
    if c <= 1e-6:
        corners = [(x * sx, y * sy, z * sz) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
        def P(ix, iy, iz): return corners[(ix > 0) * 4 + (iy > 0) * 2 + (iz > 0)]
        polys = [
            [P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1)], [P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)],
            [P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1)], [P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)],
            [P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)], [P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1)],
        ]
    else:
        # 24 vertices: each corner has one vertex pulled in along each axis
        def V(sgn, axis):
            p = [sgn[0] * sx, sgn[1] * sy, sgn[2] * sz]
            p[axis] -= sgn[axis] * c
            return tuple(p)
        signs = [(x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
        for axis in range(3):
            for s in (-1, 1):
                quad = [V(sg, axis) for sg in signs if sg[axis] == s]
                # order quad around the face
                o = [i for i in range(3) if i != axis]
                quad.sort(key=lambda p: math.atan2(p[o[1]] * s if axis != 1 else p[o[1]], p[o[0]]))
                polys.append(quad)
        # edge faces: for each pair of axes (a,b) and signs
        for a in range(3):
            for b in range(a + 1, 3):
                for sa in (-1, 1):
                    for sb in (-1, 1):
                        quad = []
                        for sg in signs:
                            if sg[a] == sa and sg[b] == sb: quad += [V(sg, a), V(sg, b)]
                        # quad has 4 verts: order them
                        cx_ = sum(p[0] for p in quad) / 4; cy_ = sum(p[1] for p in quad) / 4; cz_ = sum(p[2] for p in quad) / 4
                        n = (sa if a == 0 else (sb if b == 0 else 0), sa if a == 1 else (sb if b == 1 else 0), sa if a == 2 else (sb if b == 2 else 0))
                        # sort by angle in a plane perpendicular to n
                        t1 = (n[1], -n[0], 0) if abs(n[2]) < 0.9 else (1, 0, 0)
                        t2 = (n[1] * t1[2] - n[2] * t1[1], n[2] * t1[0] - n[0] * t1[2], n[0] * t1[1] - n[1] * t1[0])
                        quad.sort(key=lambda p: math.atan2(sum((p[i] - (cx_, cy_, cz_)[i]) * t2[i] for i in range(3)), sum((p[i] - (cx_, cy_, cz_)[i]) * t1[i] for i in range(3))))
                        polys.append(quad)
        for sg in signs:
            polys.append([V(sg, 0), V(sg, 1), V(sg, 2)])
    for poly in polys:
        poly = oriented(poly, (0, 0, 0))
        mesh.add_poly(transform(poly, rot, center), mat)

def cylinder(mesh, center, radius, length, axis="z", segments=16, mat="Gunmetal", rot=(0, 0, 0), caps=True, radius2=None):
    r2 = radius if radius2 is None else radius2
    ring0, ring1 = [], []
    for i in range(segments):
        a = 2 * math.pi * i / segments; ca, sa = math.cos(a), math.sin(a)
        if axis == "z": ring0.append((ca * radius, sa * radius, -length / 2)); ring1.append((ca * r2, sa * r2, length / 2))
        elif axis == "y": ring0.append((ca * radius, -length / 2, sa * radius)); ring1.append((ca * r2, length / 2, sa * r2))
        else: ring0.append((-length / 2, ca * radius, sa * radius)); ring1.append((length / 2, ca * r2, sa * r2))
    polys = []
    for i in range(segments):
        j = (i + 1) % segments
        polys.append([ring0[i], ring0[j], ring1[j], ring1[i]])
    if caps:
        polys.append(list(reversed(ring0))); polys.append(list(ring1))
    for poly in polys:
        poly = oriented(poly, (0, 0, 0))
        mesh.add_poly(transform(poly, rot, center), mat)

def box(mesh, center, size, mat="Gunmetal", rot=(0, 0, 0)): chamfer_box(mesh, center, size, 0.0, mat, rot)

# ---------------- weapons ----------------
layout = {"materials": {"Gunmetal": "pbr_metal_dark", "Polymer": "pbr_polymer", "Rubber": "pbr_rubber", "Metal": "pbr_metal_brushed", "Dark": "flat", "Grip": "pbr_polymer", "White": "flat", "Emissive": "flat"}, "weapons": {}}

def part(weapon, name, build, pivot=(0, 0, 0), role="static"):
    """Build a part mesh with geometry expressed relative to the part pivot (world = pivot + local)."""
    m = Mesh()
    build(m)
    m.write(os.path.join(OUT, f"{weapon}_{name}.obj"), f"{weapon}_{name}")
    layout["weapons"][weapon]["parts"].append({"name": name, "file": f"{weapon}_{name}.obj", "pivot": list(pivot), "role": role})

def rel(pivot, p): return (p[0] - pivot[0], p[1] - pivot[1], p[2] - pivot[2])

def pistol():
    w = "pistol"; layout["weapons"][w] = {"parts": [], "muzzle": [0, 0.04, 0.145], "grip": [0, -0.045, -0.02], "gripPitchDegrees": 0, "secondaryGrip": None,
                                        "colliderCenter": [0, 0.0, 0.03], "colliderSize": [0.035, 0.14, 0.2], "mass": 0.9,
                                        # palm anchors: pos, forward (direction of the proximal finger segments), up (back of hand), hand pose name
                                        "primaryPalm": {"pos": [0.022, -0.045, -0.018], "forward": [-0.55, -0.35, 0.76], "up": [0.83, -0.1, 0.55], "pose": "PistolGrip"},
                                        "ejectionPort": [0.018, 0.05, 0.045]}
    def body(m):
        chamfer_box(m, (0, 0.012, 0.03), (0.028, 0.026, 0.16), 0.004, "Polymer")           # frame
        chamfer_box(m, (0, -0.045, -0.018), (0.03, 0.1, 0.044), 0.006, "Polymer", rot=(-14, 0, 0))  # grip, raked back
        box(m, (0, -0.028, 0.045), (0.012, 0.004, 0.05), "Polymer")                          # guard bottom
        box(m, (0, -0.012, 0.067), (0.012, 0.036, 0.004), "Polymer")                         # guard front
        box(m, (0, 0.0, 0.088), (0.02, 0.012, 0.03), "Polymer")                              # accessory rail block
        box(m, (0, 0.045, -0.062), (0.008, 0.012, 0.01), "Gunmetal")                         # hammer
        box(m, (0, 0.06, -0.045), (0.026, 0.008, 0.008), "Dark")                             # rear sight
        for sx in (-1, 1): cylinder(m, (sx * 0.0135, 0.06, -0.049), 0.0012, 0.002, "z", 8, "White")  # rear sight dots
        for sx in (-1, 1): box(m, (sx * 0.0152, -0.045, -0.018), (0.002, 0.07, 0.03), "Grip", rot=(-14, 0, 0))  # stippled grip panels
        box(m, (0, 0.004, -0.045), (0.024, 0.008, 0.03), "Polymer", rot=(-14, 0, 0))         # beavertail
        cylinder(m, (-0.016, -0.012, -0.012), 0.004, 0.003, "x", 10, "Dark")                 # magazine release
        box(m, (-0.0155, 0.02, -0.02), (0.002, 0.005, 0.03), "Dark")                         # slide stop lever
        for i in range(3): box(m, (0, -0.007, 0.078 + i * 0.012), (0.02, 0.003, 0.004), "Dark")  # rail slots
        for i in range(3): box(m, (0, -0.028 + i * 0.012, 0.06 + i * 0.004), (0.012, 0.004, 0.012), "Polymer")  # rounded guard front
    def slide(m):
        chamfer_box(m, (0, 0.042, 0.03), (0.03, 0.03, 0.18), 0.006, "Gunmetal")
        box(m, (0.0145, 0.045, 0.045), (0.004, 0.012, 0.03), "Dark")                         # ejection port
        for i in range(5): box(m, (0, 0.042, -0.045 + i * 0.006), (0.032, 0.02, 0.002), "Dark")  # rear serrations
        box(m, (0, 0.061, 0.11), (0.004, 0.008, 0.006), "Dark")                               # front sight
        cylinder(m, (0, 0.062, 0.1135), 0.0012, 0.002, "z", 8, "White")                         # front sight dot
        for i in range(4): box(m, (0, 0.042, 0.09 + i * 0.006), (0.032, 0.02, 0.002), "Dark")  # front serrations
        box(m, (0.0165, 0.045, 0.03), (0.002, 0.004, 0.012), "Metal")                           # extractor
        cylinder(m, (0, 0.042, 0.125), 0.0075, 0.012, "z", 12, "Metal")                      # barrel end
        cylinder(m, (0, 0.042, 0.1305), 0.0045, 0.002, "z", 10, "Dark")                      # bore
    def trigger(m):
        box(m, (0, -0.011, 0.0), (0.006, 0.022, 0.006), "Gunmetal")                          # relative to pivot at trigger top
    def magazine(m):
        chamfer_box(m, (0, -0.045, 0.0), (0.02, 0.085, 0.03), 0.003, "Gunmetal", rot=(-14, 0, 0))
        box(m, (0, -0.088, 0.008), (0.026, 0.008, 0.038), "Polymer", rot=(-14, 0, 0))        # baseplate
    part(w, "body", body)
    part(w, "slide", slide, role="slide")
    part(w, "trigger", trigger, pivot=(0, -0.003, 0.04), role="trigger")
    part(w, "magazine", magazine, pivot=(0, -0.012, -0.012), role="magazine")

def rifle():
    w = "rifle"; layout["weapons"][w] = {"parts": [], "muzzle": [0, 0.03, 0.735], "grip": [0, -0.06, -0.05], "gripPitchDegrees": 0, "secondaryGrip": [0, 0.0, 0.30],
                                       "colliderCenter": [0, 0.0, 0.15], "colliderSize": [0.06, 0.2, 0.95], "mass": 3.2,
                                       "primaryPalm": {"pos": [0.022, -0.065, -0.052], "forward": [-0.55, -0.35, 0.76], "up": [0.83, -0.1, 0.55], "pose": "RiflePrimary"},
                                       "supportPalm": {"pos": [-0.012, 0.0, 0.30], "forward": [0.3, 0.95, 0.0], "up": [-0.95, 0.3, 0.0], "pose": "RifleSupport"},
                                       "ejectionPort": [0.03, 0.03, 0.02]}
    def body(m):
        chamfer_box(m, (0, 0.02, 0.0), (0.05, 0.06, 0.26), 0.006, "Gunmetal")                 # upper receiver
        chamfer_box(m, (0, -0.015, -0.02), (0.044, 0.04, 0.2), 0.004, "Polymer")             # lower receiver
        box(m, (0, 0.055, 0.0), (0.022, 0.01, 0.26), "Gunmetal")                              # top rail
        for i in range(13): box(m, (0, 0.062, -0.12 + i * 0.02), (0.022, 0.004, 0.01), "Dark")  # rail teeth
        cylinder(m, (0, 0.03, 0.30), 0.026, 0.30, "z", 8, "Polymer")                          # handguard (octagonal)
        for i in range(6): cylinder(m, (0.026, 0.03, 0.18 + i * 0.045), 0.006, 0.004, "x", 8, "Dark")   # vents right
        for i in range(6): cylinder(m, (-0.026, 0.03, 0.18 + i * 0.045), 0.006, 0.004, "x", 8, "Dark")  # vents left
        box(m, (0, 0.058, 0.30), (0.02, 0.008, 0.28), "Gunmetal")                             # handguard top rail
        cylinder(m, (0, 0.03, 0.52), 0.009, 0.40, "z", 12, "Metal")                           # barrel
        cylinder(m, (0, 0.03, 0.705), 0.013, 0.06, "z", 12, "Dark")                           # muzzle device
        box(m, (0, 0.05, 0.445), (0.02, 0.03, 0.02), "Gunmetal")                              # gas block / front sight base
        box(m, (0, 0.072, 0.445), (0.004, 0.012, 0.006), "Dark")                              # front sight post
        cylinder(m, (0, 0.03, -0.22), 0.017, 0.2, "z", 10, "Gunmetal")                        # buffer tube
        chamfer_box(m, (0, 0.0, -0.34), (0.04, 0.075, 0.22), 0.006, "Polymer")                # stock
        box(m, (0, 0.0, -0.455), (0.045, 0.11, 0.02), "Rubber")                               # butt pad
        chamfer_box(m, (0, -0.065, -0.055), (0.032, 0.1, 0.04), 0.005, "Polymer", rot=(-20, 0, 0))  # pistol grip
        box(m, (0, -0.04, 0.02), (0.014, 0.004, 0.07), "Polymer")                             # trigger guard bottom
        box(m, (0, -0.022, 0.052), (0.014, 0.036, 0.004), "Polymer")                          # trigger guard front
        # red dot optic with turrets, lens and emissive dot
        box(m, (0, 0.068, -0.03), (0.03, 0.012, 0.05), "Gunmetal")
        cylinder(m, (0, 0.092, -0.03), 0.016, 0.06, "z", 12, "Gunmetal")
        cylinder(m, (0, 0.092, 0.001), 0.013, 0.002, "z", 12, "Dark")
        cylinder(m, (0, 0.092, -0.061), 0.012, 0.002, "z", 12, "Dark")
        cylinder(m, (0, 0.092, -0.056), 0.0025, 0.002, "z", 8, "Emissive")                    # reticle dot (seen from behind)
        cylinder(m, (0, 0.11, -0.03), 0.006, 0.008, "y", 10, "Dark"); cylinder(m, (0.018, 0.092, -0.03), 0.006, 0.008, "x", 10, "Dark")  # turrets
        # receiver details
        box(m, (0.026, 0.028, 0.02), (0.003, 0.02, 0.07), "Dark")                               # ejection port cover
        cylinder(m, (0.028, 0.03, -0.04), 0.006, 0.014, "x", 10, "Gunmetal")                    # forward assist
        box(m, (-0.024, 0.0, -0.06), (0.004, 0.006, 0.02), "Metal", rot=(0, 0, 0))              # selector lever
        box(m, (-0.024, 0.01, 0.03), (0.004, 0.02, 0.012), "Dark")                              # bolt release
        cylinder(m, (0.024, -0.02, 0.04), 0.004, 0.004, "x", 10, "Dark")                        # magazine release
        for i in range(3): box(m, (0, -0.066 + i * 0.016, -0.038), (0.034, 0.003, 0.004), "Dark", rot=(-20, 0, 0))  # grip finger grooves
        for i in range(5): box(m, (0, 0.003, 0.2 + i * 0.045), (0.006, 0.004, 0.025), "Dark")   # M-LOK slots (bottom of handguard)
        for i in range(4): box(m, (0, 0.012, -0.16 - i * 0.025), (0.02, 0.004, 0.006), "Dark")  # stock adjustment notches
        box(m, (0, 0.045, -0.36), (0.03, 0.016, 0.14), "Polymer")                               # cheek riser
        cylinder(m, (0, 0.052, 0.38), 0.004, 0.13, "z", 8, "Metal")                             # gas tube
        for i in range(3): box(m, (0, 0.03, 0.69 + i * 0.012), (0.03, 0.004, 0.006), "Dark")     # flash hider slots
        for i in range(3): box(m, (0, -0.075 + i * 0.03, 0.03), (0.032, 0.002, 0.066), "Dark", rot=(-8, 0, 0))  # magazine ribs (visual on body for alignment)
        box(m, (0.0, -0.03, 0.44), (0.02, 0.008, 0.004), "Metal")                               # sling loop
    def charging(m):
        box(m, (0, 0.0, 0.0), (0.03, 0.012, 0.03), "Gunmetal"); box(m, (0, 0.0, 0.012), (0.012, 0.008, 0.01), "Dark")
    def trigger(m): box(m, (0, -0.012, 0.0), (0.007, 0.024, 0.007), "Gunmetal")
    def magazine(m):
        chamfer_box(m, (0, -0.07, 0.0), (0.03, 0.14, 0.065), 0.004, "Gunmetal", rot=(-8, 0, 0))
        box(m, (0, -0.138, -0.012), (0.034, 0.006, 0.07), "Polymer", rot=(-8, 0, 0))
    part(w, "body", body)
    part(w, "charging_handle", charging, pivot=(0, 0.055, -0.14), role="slide")
    part(w, "trigger", trigger, pivot=(0, -0.025, 0.02), role="trigger")
    part(w, "magazine", magazine, pivot=(0, -0.035, 0.03), role="magazine")

def smg():
    w = "smg"; layout["weapons"][w] = {"parts": [], "muzzle": [0, 0.02, 0.42], "grip": [0, -0.07, -0.06], "gripPitchDegrees": 0, "secondaryGrip": [0, -0.05, 0.18],
                                     "colliderCenter": [0, 0.0, 0.05], "colliderSize": [0.06, 0.2, 0.75], "mass": 2.4,
                                     "primaryPalm": {"pos": [0.022, -0.075, -0.062], "forward": [-0.55, -0.35, 0.76], "up": [0.83, -0.1, 0.55], "pose": "SmgGrip"},
                                     "supportPalm": {"pos": [0.02, -0.055, 0.18], "forward": [-0.6, -0.3, 0.74], "up": [0.8, -0.2, 0.56], "pose": "RifleSupport"},
                                     "ejectionPort": [0.03, 0.03, 0.08]}
    def body(m):
        chamfer_box(m, (0, 0.02, 0.05), (0.05, 0.065, 0.30), 0.006, "Gunmetal")               # receiver
        cylinder(m, (0, 0.02, 0.27), 0.024, 0.16, "z", 12, "Gunmetal")                        # barrel shroud
        for i in range(4):
            for a in (0, 90, 180, 270):
                ang = math.radians(a); cx = math.cos(ang) * 0.024; cy = math.sin(ang) * 0.024
                cylinder(m, (cx, 0.02 + cy, 0.21 + i * 0.035), 0.005, 0.004, "x" if a in (0, 180) else "y", 8, "Dark")
        cylinder(m, (0, 0.02, 0.37), 0.008, 0.08, "z", 12, "Metal")                           # barrel
        cylinder(m, (0, 0.02, 0.405), 0.011, 0.03, "z", 12, "Dark")                           # muzzle
        chamfer_box(m, (0, -0.02, -0.03), (0.045, 0.05, 0.18), 0.004, "Polymer")              # lower housing
        chamfer_box(m, (0, -0.075, -0.065), (0.032, 0.1, 0.04), 0.005, "Polymer", rot=(-18, 0, 0))  # pistol grip
        cylinder(m, (0, -0.055, 0.18), 0.013, 0.09, "y", 10, "Polymer")                       # vertical foregrip
        box(m, (0, -0.045, 0.0), (0.014, 0.004, 0.07), "Polymer"); box(m, (0, -0.027, 0.032), (0.014, 0.036, 0.004), "Polymer")  # guard
        box(m, (0, 0.056, 0.05), (0.022, 0.01, 0.28), "Gunmetal")                             # top rail
        for i in range(13): box(m, (0, 0.063, -0.07 + i * 0.02), (0.022, 0.004, 0.01), "Dark")
        box(m, (0, 0.072, 0.33), (0.004, 0.012, 0.006), "Dark"); box(m, (0, 0.07, -0.08), (0.026, 0.008, 0.008), "Dark")  # sights
        box(m, (0.02, 0.025, -0.25), (0.008, 0.012, 0.22), "Gunmetal"); box(m, (-0.02, 0.025, -0.25), (0.008, 0.012, 0.22), "Gunmetal")  # stock bars
        box(m, (0, 0.02, -0.37), (0.05, 0.09, 0.015), "Rubber")                               # butt plate
        box(m, (0, 0.02, -0.11), (0.05, 0.03, 0.02), "Gunmetal")                              # stock hinge
        cylinder(m, (0, 0.02, -0.11), 0.012, 0.054, "x", 10, "Dark")                            # hinge pin
        for i in range(4): cylinder(m, (0, -0.03 - i * 0.02, 0.18), 0.0145, 0.004, "y", 10, "Dark")  # foregrip ridges
        box(m, (-0.026, 0.035, 0.1), (0.002, 0.006, 0.09), "Dark")                              # charging handle slot
        box(m, (0.026, 0.028, 0.08), (0.002, 0.018, 0.045), "Dark")                             # ejection port
        cylinder(m, (0.024, -0.03, 0.08), 0.004, 0.004, "x", 10, "Dark")                        # magazine release
        box(m, (-0.024, -0.005, -0.03), (0.004, 0.006, 0.018), "Metal")                         # safety lever
        cylinder(m, (0, 0.07, -0.08), 0.006, 0.006, "z", 12, "Dark")                            # rear aperture
        cylinder(m, (0, 0.02, 0.41), 0.012, 0.004, "z", 12, "Metal")                            # muzzle thread collar
        for sx in (-1, 1): box(m, (sx * 0.0165, -0.075, -0.062), (0.002, 0.07, 0.03), "Grip", rot=(-18, 0, 0))  # grip panels
    def charging(m): box(m, (0, 0.0, 0.0), (0.02, 0.01, 0.025), "Gunmetal"); box(m, (-0.012, 0.0, 0.0), (0.006, 0.014, 0.012), "Dark")
    def trigger(m): box(m, (0, -0.012, 0.0), (0.007, 0.024, 0.007), "Gunmetal")
    def magazine(m):
        chamfer_box(m, (0, -0.085, 0.0), (0.024, 0.17, 0.045), 0.003, "Gunmetal")
        box(m, (0, -0.172, 0.0), (0.028, 0.006, 0.05), "Polymer")
    part(w, "body", body)
    part(w, "charging_handle", charging, pivot=(-0.03, 0.035, 0.1), role="slide")
    part(w, "trigger", trigger, pivot=(0, -0.03, 0.0), role="trigger")
    part(w, "magazine", magazine, pivot=(0, -0.045, 0.06), role="magazine")

def write_mtl():
    with open(os.path.join(OUT, "weapons.mtl"), "w") as fh:
        for name, (kd) in {"Gunmetal": (0.18, 0.19, 0.21), "Polymer": (0.12, 0.12, 0.13), "Rubber": (0.1, 0.1, 0.1), "Metal": (0.6, 0.61, 0.63), "Dark": (0.05, 0.05, 0.06), "Grip": (0.1, 0.1, 0.1), "White": (0.95, 0.95, 0.95), "Emissive": (1.0, 0.1, 0.1)}.items():
            fh.write(f"newmtl {name}\nKd {kd[0]} {kd[1]} {kd[2]}\nKs 0.2 0.2 0.2\nNs 40\n\n")

if __name__ == "__main__":
    pistol(); rifle(); smg(); write_mtl()
    with open(os.path.join(OUT, "weapons_layout.json"), "w") as fh: json.dump(layout, fh, indent=2)
    print("wrote weapons_layout.json")
