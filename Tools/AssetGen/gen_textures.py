#!/usr/bin/env python3
"""Procedural PBR textures, target faces, decals, FX sprites and signage for the VR range (numpy + Pillow).
All images are generated here (no downloaded content). Run from the repo root:
    python3 -I Tools/AssetGen/gen_textures.py
Outputs PNGs under Assets/Art/{Textures,Signs,FX}. URP Lit expects: albedo (sRGB), normal (OpenGL +Y),
mask = metallic in R, smoothness in A (we write RGB=metallic, A=smoothness).
"""
import os, math, numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX = os.path.join(ROOT, "Assets", "Art", "Textures")
SIGNS = os.path.join(ROOT, "Assets", "Art", "Signs")
FX = os.path.join(ROOT, "Assets", "Art", "FX")
for d in (TEX, SIGNS, FX): os.makedirs(d, exist_ok=True)
rng = np.random.default_rng(42)

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
def font(size, bold=True):
    try: return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)
    except Exception: return ImageFont.load_default(size=size)

# ---------------- noise helpers ----------------
def value_noise(size, cells, seed=None):
    r = np.random.default_rng(seed) if seed is not None else rng
    g = r.random((cells, cells)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    return np.asarray(img).astype(np.float32) / 255.0

def fbm(size, base_cells=4, octaves=6, gain=0.5, seed=1):
    out = np.zeros((size, size), np.float32); amp = 1.0; total = 0.0
    for o in range(octaves):
        cells = min(size, base_cells * (2 ** o))
        out += value_noise(size, cells, seed + o * 17) * amp; total += amp; amp *= gain
    return out / total

def tileable(a):
    """Make a square array seamlessly tileable by blending with its half-offset copy."""
    s = a.shape[0]; h = s // 2
    b = np.roll(np.roll(a, h, 0), h, 1)
    y, x = np.mgrid[0:s, 0:s]
    wx = np.clip(np.minimum(x, s - 1 - x) / (s * 0.25), 0, 1); wy = np.clip(np.minimum(y, s - 1 - y) / (s * 0.25), 0, 1)
    w = (wx * wy).astype(np.float32)
    return a * w + b * (1 - w)

def normal_from_height(h, strength=4.0):
    dx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    dy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    nx = -dx * strength; ny = dy * strength; nz = np.ones_like(h)  # OpenGL convention: +Y up in texture space
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)

def save_rgb(path, arr):
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").save(path, optimize=True); print("wrote", os.path.relpath(path, ROOT))
def save_rgba(path, arr):
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA").save(path, optimize=True); print("wrote", os.path.relpath(path, ROOT))
def colorize(h, c0, c1):
    c0 = np.array(c0, np.float32); c1 = np.array(c1, np.float32)
    return c0[None, None, :] * (1 - h[..., None]) + c1[None, None, :] * h[..., None]
def mask_map(metallic, smooth):
    s = metallic.shape[0]
    out = np.zeros((s, s, 4), np.float32)
    out[..., 0] = metallic * 255; out[..., 1] = metallic * 255; out[..., 2] = metallic * 255; out[..., 3] = smooth * 255
    return out

def material(name, albedo, height, metallic, smooth, normal_strength=4.0):
    save_rgb(os.path.join(TEX, f"{name}_albedo.png"), albedo)
    save_rgb(os.path.join(TEX, f"{name}_normal.png"), normal_from_height(height, normal_strength))
    save_rgba(os.path.join(TEX, f"{name}_mask.png"), mask_map(metallic, smooth))

# ---------------- surfaces ----------------
def concrete_floor(size=1024):
    base = tileable(fbm(size, 4, 7, 0.55, 1))
    grain = tileable(fbm(size, 64, 3, 0.5, 9))
    stains = tileable(fbm(size, 2, 4, 0.6, 21))
    h = base * 0.6 + grain * 0.4
    alb = colorize(h, (118, 116, 112), (158, 155, 150)) - (stains[..., None] > 0.62) * 18
    # expansion joints every 256px
    y, x = np.mgrid[0:size, 0:size]
    joint = ((x % 512) < 3) | ((y % 512) < 3)
    alb[joint] *= 0.55; h = h.copy(); h[joint] -= 0.3
    material("floor_concrete", alb, h, np.zeros_like(h), 0.35 + 0.2 * (1 - stains), 3.0)

def painted_block_wall(size=1024):
    y, x = np.mgrid[0:size, 0:size]
    bh, bw = 128, 256  # block size px
    row = y // bh; offset = (row % 2) * (bw // 2)
    mortar = (((x + offset) % bw) < 8) | ((y % bh) < 8)
    n = tileable(fbm(size, 8, 5, 0.5, 3))
    h = n * 0.5 + 0.5; h[mortar] = 0.1
    alb = colorize(n, (196, 198, 194), (222, 224, 220)); alb[mortar] = (150, 150, 148)
    # subtle colour band at the bottom quarter (painted dado)
    material("wall_block_painted", alb, h, np.zeros_like(h), np.full_like(h, 0.25), 3.5)

def acoustic_ceiling(size=1024):
    y, x = np.mgrid[0:size, 0:size]
    tile = 512
    grid = ((x % tile) < 6) | ((y % tile) < 6)
    n = tileable(fbm(size, 32, 4, 0.5, 5))
    holes = (np.sin(x * math.pi / 8) ** 2 > 0.93) & (np.sin(y * math.pi / 8) ** 2 > 0.93)
    h = 0.8 + n * 0.2; h[holes] = 0.3; h[grid] = 0.0
    alb = colorize(n, (228, 228, 224), (244, 244, 240)); alb[holes] *= 0.6; alb[grid] = (120, 120, 122)
    material("ceiling_acoustic", alb, h, (grid * 0.6).astype(np.float32), np.full_like(h, 0.2), 2.0)

def rubber_backstop(size=1024):
    g = tileable(fbm(size, 128, 2, 0.5, 11)); n = tileable(fbm(size, 8, 4, 0.5, 12))
    h = g * 0.7 + n * 0.3
    alb = colorize(h, (26, 26, 28), (58, 56, 54))
    material("backstop_rubber", alb, h, np.zeros_like(h), np.full_like(h, 0.15), 5.0)

def wood_laminate(size=1024):
    y, x = np.mgrid[0:size, 0:size]
    warp = tileable(fbm(size, 3, 4, 0.5, 31)) * 60
    rings = (np.sin((y + warp) * 0.12) * 0.5 + 0.5)
    fine = tileable(fbm(size, 2, 6, 0.6, 32))
    h = rings * 0.4 + fine * 0.6
    alb = colorize(h, (96, 62, 34), (168, 118, 72))
    material("counter_wood", alb, h, np.zeros_like(h), np.full_like(h, 0.6), 1.5)

def brushed_metal(size=512):
    y, x = np.mgrid[0:size, 0:size]
    streak = tileable(fbm(size, 1, 1, 0.5, 41)) * 0 + value_noise(size, size, 43)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))).astype(np.float32) / 255
    streak = np.repeat(streak[:, :1], size, 1) * 0 + np.asarray(Image.fromarray((value_noise(size, 512, 44) * 255).astype(np.uint8)).resize((size, size), Image.BILINEAR)).astype(np.float32) / 255
    h = 0.5 + (streak - 0.5) * 0.3
    hs = np.asarray(Image.fromarray((h * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur((12, 0)))).astype(np.float32) / 255
    alb = colorize(hs, (150, 152, 156), (200, 202, 206))
    material("metal_brushed", alb, hs, np.full_like(hs, 0.9), np.full_like(hs, 0.72), 1.0)

def polymer(size=512):
    g = tileable(fbm(size, 96, 2, 0.5, 51)); n = tileable(fbm(size, 6, 3, 0.5, 52))
    h = g * 0.8 + n * 0.2
    alb = colorize(h, (28, 29, 31), (46, 47, 50))
    material("polymer_dark", alb, h, np.zeros_like(h), 0.3 + 0.1 * n, 3.0)

def gunmetal(size=512):
    n = tileable(fbm(size, 12, 5, 0.5, 61)); scratches = tileable(fbm(size, 200, 1, 0.5, 62)) > 0.985
    h = 0.5 + (n - 0.5) * 0.3; h[scratches] += 0.3
    alb = colorize(n, (40, 42, 46), (70, 72, 78)); alb[scratches] += 50
    material("gunmetal", alb, h, np.full_like(h, 0.85), 0.55 + 0.15 * n, 1.5)

def skin(size=512):
    """Subtle skin albedo (pores + mottling) for the hand models, mid skin tone; smoothness ~0.4."""
    pores = tileable(fbm(size, 96, 2, 0.5, 101)); mottle = tileable(fbm(size, 6, 4, 0.55, 102))
    h = pores * 0.35 + mottle * 0.65
    alb = colorize(h, (178, 128, 104), (214, 168, 140))
    material("skin", alb, pores, np.zeros_like(h), np.full_like(h, 0.42), 1.2)

def acoustic_panel(size=512):
    g = tileable(fbm(size, 48, 3, 0.5, 71))
    alb = colorize(g, (172, 166, 150), (196, 190, 174))
    material("divider_panel", alb, g, np.zeros_like(g), np.full_like(g, 0.1), 2.5)

# ---------------- targets, decals, fx ----------------
def target_face(size=1024):
    """Bullseye face for a 0.6 m square target. Rings match ScoringRules radii: 5/12/20/28 cm of 30 cm half-width."""
    img = Image.new("RGB", (size, size), (238, 234, 222))
    d = ImageDraw.Draw(img)
    paper = (np.asarray(img).astype(np.float32) + (fbm(size, 16, 3, 0.5, 81)[..., None] - 0.5) * 14)
    img = Image.fromarray(np.clip(paper, 0, 255).astype(np.uint8)); d = ImageDraw.Draw(img)
    c = size / 2; half = 0.30
    rings = [(0.28, (40, 40, 40), 2), (0.20, (40, 40, 40), 5), (0.12, (40, 40, 40), 8), (0.05, (40, 40, 40), 10)]
    fills = [(0.28, (235, 232, 220)), (0.20, (250, 250, 248)), (0.12, (235, 232, 220)), (0.05, (30, 30, 30))]
    for r, col in fills:
        px = r / half * c; d.ellipse([c - px, c - px, c + px, c + px], fill=col)
    for r, col, _ in rings:
        px = r / half * c; d.ellipse([c - px, c - px, c + px, c + px], outline=col, width=5)
    f = font(44)
    for r, col, pts in rings[:3]:
        px = r / half * c
        for ang in (90, 270):
            x = c + math.cos(math.radians(ang)) * (px - 42); y = c - math.sin(math.radians(ang)) * (px - 42)
            d.text((x, y), str(pts), fill=col, font=f, anchor="mm")
    d.text((c, c), "10", fill=(230, 230, 230), font=font(40), anchor="mm")
    d.text((c, size - 50), "VR RANGE  •  TRAINING TARGET  •  60 cm", fill=(90, 90, 90), font=font(26, False), anchor="mm")
    d.text((70, 60), "LANE", fill=(90, 90, 90), font=font(26), anchor="lm")
    img.save(os.path.join(TEX, "target_face_albedo.png")); print("wrote target_face_albedo.png")
    # steel variant
    s = Image.new("RGB", (size, size), (0, 0, 0)); ds = ImageDraw.Draw(s)
    ds.ellipse([4, 4, size - 4, size - 4], fill=(120, 122, 126))
    for r, col, _ in rings:
        px = r / half * c; ds.ellipse([c - px, c - px, c + px, c + px], outline=(250, 250, 250), width=6)
    s.save(os.path.join(TEX, "target_steel_albedo.png")); print("wrote target_steel_albedo.png")

def bullet_hole(size=256):
    y, x = np.mgrid[0:size, 0:size]; cx = cy = size / 2
    r = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / (size / 2)
    jag = fbm(size, 8, 3, 0.5, 91) * 0.25
    hole = np.clip(1 - (r + jag - 0.45) / 0.12, 0, 1)
    ring = np.clip(1 - np.abs(r + jag * 0.5 - 0.62) / 0.18, 0, 1)
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., :3] = 18; rgba[..., 3] = np.clip(hole + ring * 0.75, 0, 1) * 255
    rgba[..., :3] += ring[..., None] * 35 * (1 - hole[..., None])
    save_rgba(os.path.join(FX, "bullet_hole.png"), rgba)
    # bright impact marker (target overlay)
    m = np.zeros((128, 128, 4), np.float32); yy, xx = np.mgrid[0:128, 0:128]; rr = np.sqrt((xx - 64) ** 2 + (yy - 64) ** 2) / 64
    m[..., 0] = 255; m[..., 1] = 40; m[..., 2] = 30; m[..., 3] = np.clip(1 - (rr - 0.55) / 0.15, 0, 1) * 255
    inner = rr < 0.35; m[inner, :3] = 20
    save_rgba(os.path.join(FX, "impact_marker.png"), m)

def muzzle_flash(size=256):
    y, x = np.mgrid[0:size, 0:size]; cx = cy = size / 2
    dx = (x - cx) / (size / 2); dy = (y - cy) / (size / 2)
    r = np.sqrt(dx * dx + dy * dy); a = np.arctan2(dy, dx)
    star = 0.55 + 0.45 * np.cos(a * 6) ** 2
    core = np.clip(1 - r / (0.3 + 0.5 * star), 0, 1) ** 1.5
    rgba = np.zeros((size, size, 4), np.float32)
    rgba[..., 0] = 255; rgba[..., 1] = 160 + 80 * core; rgba[..., 2] = 60 + 160 * core ** 3; rgba[..., 3] = core * 255
    save_rgba(os.path.join(FX, "muzzle_flash.png"), rgba)
    # soft particle dot for sparks/dust
    s = np.zeros((64, 64, 4), np.float32); yy, xx = np.mgrid[0:64, 0:64]; rr = np.sqrt((xx - 32) ** 2 + (yy - 32) ** 2) / 32
    s[..., :3] = 255; s[..., 3] = np.clip(1 - rr, 0, 1) ** 2 * 255
    save_rgba(os.path.join(FX, "particle_soft.png"), s)

def light_panel(size=256):
    y, x = np.mgrid[0:size, 0:size]
    a = np.full((size, size, 3), 250, np.float32)
    frame = (x < 10) | (x >= size - 10) | (y < 10) | (y >= size - 10)
    a[frame] = (170, 172, 176)
    louvre = ((x // 16) % 2 == 0) & ~frame
    a[louvre] -= 25
    save_rgb(os.path.join(TEX, "light_panel_albedo.png"), a)

# ---------------- signs ----------------
def sign(name, lines, size=(1024, 512), bg=(250, 212, 40), fg=(20, 20, 20), border=(20, 20, 20), title=None, title_bg=(200, 30, 30)):
    img = Image.new("RGB", size, bg); d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size[0] - 1, size[1] - 1], outline=border, width=18)
    y = 60
    if title:
        d.rectangle([18, 18, size[0] - 19, 150], fill=title_bg)
        d.text((size[0] / 2, 84), title, fill=(255, 255, 255), font=font(74), anchor="mm"); y = 200
    n = len(lines)
    fs = max(28, min(70, int((size[1] - y - 40) / max(n, 1) * 0.7)))
    for i, line in enumerate(lines):
        d.text((size[0] / 2, y + i * (fs + 18) + fs / 2), line, fill=fg, font=font(fs, i < 2), anchor="mm")
    img.save(os.path.join(SIGNS, f"{name}.png")); print("wrote", f"Signs/{name}.png")

def lane_number(n, size=256):
    img = Image.new("RGB", (size, size), (30, 32, 36)); d = ImageDraw.Draw(img)
    d.rectangle([8, 8, size - 9, size - 9], outline=(240, 200, 40), width=10)
    d.text((size / 2, size / 2 - 10), str(n), fill=(255, 255, 255), font=font(150), anchor="mm")
    d.text((size / 2, size - 34), "LANE", fill=(240, 200, 40), font=font(28), anchor="mm")
    img.save(os.path.join(SIGNS, f"lane_{n}.png")); print("wrote", f"Signs/lane_{n}.png")

def distance_label(m, size=(512, 256)):
    img = Image.new("RGB", size, (235, 235, 230)); d = ImageDraw.Draw(img)
    d.rectangle([6, 6, size[0] - 7, size[1] - 7], outline=(30, 30, 30), width=8)
    d.text((size[0] / 2, size[1] / 2), f"{m} m", fill=(30, 30, 30), font=font(150), anchor="mm")
    img.save(os.path.join(SIGNS, f"distance_{m}m.png")); print("wrote", f"Signs/distance_{m}m.png")

UI = os.path.join(ROOT, "Assets", "Art", "UI")
os.makedirs(UI, exist_ok=True)

def ui_sprites():
    """9-sliceable rounded panel / button / outline sprites (white, tinted at runtime) + icons."""
    def rounded(size, radius, fill, outline=None, outline_w=0):
        img = Image.new("RGBA", (size, size), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
        d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=fill, outline=outline, width=outline_w)
        return img
    rounded(64, 14, (255, 255, 255, 255)).save(os.path.join(UI, "ui_panel_rounded.png"))
    rounded(64, 12, (255, 255, 255, 255), (255, 255, 255, 255), 0).save(os.path.join(UI, "ui_button.png"))
    rounded(64, 12, (0, 0, 0, 0), (255, 255, 255, 255), 3).save(os.path.join(UI, "ui_outline.png"))
    # soft shadow
    sh = Image.new("RGBA", (96, 96), (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle([12, 12, 83, 83], radius=16, fill=(0, 0, 0, 160))
    sh.filter(ImageFilter.GaussianBlur(6)).save(os.path.join(UI, "ui_shadow.png"))
    # icons: target, pistol, rifle, timer, trophy (simple glyphs)
    def icon(name, draw_fn):
        img = Image.new("RGBA", (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(img); draw_fn(d); img.save(os.path.join(UI, f"icon_{name}.png"))
    icon("target", lambda d: [d.ellipse([8 + i * 18, 8 + i * 18, 120 - i * 18, 120 - i * 18], outline=(255, 255, 255, 255), width=8) for i in range(3)])
    icon("timer", lambda d: (d.ellipse([14, 20, 114, 120], outline=(255, 255, 255, 255), width=8), d.line([64, 70, 64, 40], fill=(255, 255, 255, 255), width=8), d.line([64, 70, 86, 82], fill=(255, 255, 255, 255), width=8), d.rectangle([52, 8, 76, 20], fill=(255, 255, 255, 255))))
    icon("trophy", lambda d: (d.rounded_rectangle([30, 14, 98, 70], radius=14, fill=(255, 255, 255, 255)), d.rectangle([56, 70, 72, 92], fill=(255, 255, 255, 255)), d.rectangle([36, 92, 92, 108], fill=(255, 255, 255, 255))))
    icon("move", lambda d: (d.line([20, 64, 108, 64], fill=(255, 255, 255, 255), width=8), d.polygon([(108, 64), (84, 44), (84, 84)], fill=(255, 255, 255, 255)), d.polygon([(20, 64), (44, 44), (44, 84)], fill=(255, 255, 255, 255))))
    print("wrote UI sprites")

def main():
    ui_sprites()
    concrete_floor(); painted_block_wall(); acoustic_ceiling(); rubber_backstop(); wood_laminate()
    brushed_metal(); polymer(); gunmetal(); acoustic_panel(); skin(); target_face(); bullet_hole(); muzzle_flash(); light_panel()
    sign("sign_safety", ["EYE AND EAR PROTECTION", "REQUIRED BEYOND THIS POINT", "", "Keep weapons pointed downrange"], title="SAFETY")
    sign("sign_firing_line", ["FIRING LINE", "DO NOT CROSS WHILE THE RANGE IS HOT"], bg=(200, 30, 30), fg=(255, 255, 255), border=(255, 255, 255))
    sign("sign_rules", ["1. Treat every weapon as loaded", "2. Finger off the trigger until ready", "3. Fire only at your lane's target", "4. Return weapons to the counter", "5. Reset targets between sessions"], title="RANGE RULES", bg=(245, 245, 240), title_bg=(30, 60, 120))
    sign("sign_controls", ["GRIP button: pick up / drop weapon", "TRIGGER: fire     A / X: reload", "B / Y: toggle semi / auto", "LEFT STICK: teleport     RIGHT STICK: snap turn", "Point at a panel and pull trigger to press buttons"], title="VR CONTROLS", bg=(235, 240, 250), title_bg=(30, 90, 170))
    sign("sign_welcome", ["WELCOME TO THE", "VR SHOOTING RANGE", "", "Pick a weapon from the counter", "Choose a mode on the panel"], bg=(30, 32, 36), fg=(240, 240, 240), border=(240, 200, 40))
    sign("sign_counter", ["WEAPON COUNTER", "Pistol   •   Rifle   •   SMG", "Return weapons here"], size=(1024, 384), bg=(30, 60, 120), fg=(255, 255, 255), border=(255, 255, 255))
    for n in range(1, 6): lane_number(n)
    for m in (5, 10, 15, 25): distance_label(m)

if __name__ == "__main__":
    main()
