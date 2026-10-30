#!/usr/bin/env python3
"""Synthesises all game audio (gunshots, mechanics, impacts, UI, ambience) as 16-bit WAV with numpy.
Everything is procedurally generated in this repository: no recorded firearm audio is used.
Run from the repo root:  python3 -I Tools/AssetGen/gen_audio.py
"""
import os, wave, struct, numpy as np

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Audio")
rng = np.random.default_rng(7)

def t(seconds): return np.arange(int(SR * seconds)) / SR
def env(n, attack=0.001, decay=0.2, curve=1.0):
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-5), 0, 1)
    d = np.exp(-x / max(decay, 1e-5)) ** curve
    return a * d
def noise(n): return rng.standard_normal(n)
def lowpass(x, cutoff, order=2):
    # simple one-pole IIR applied 'order' times
    rc = 1.0 / (2 * np.pi * cutoff); dt = 1.0 / SR; a = dt / (rc + dt)
    y = x.copy()
    for _ in range(order):
        out = np.empty_like(y); acc = 0.0
        for i in range(len(y)):
            acc += a * (y[i] - acc); out[i] = acc
        y = out
    return y
def highpass(x, cutoff): return x - lowpass(x, cutoff, 1)
def reverb(x, decay=0.35, taps=((0.011, 0.5), (0.023, 0.35), (0.037, 0.25), (0.053, 0.18), (0.071, 0.12))):
    out = x.copy()
    for delay, gain in taps:
        d = int(delay * SR)
        if d < len(x): out[d:] += x[:-d] * gain * decay / 0.35
    # feedback comb tail
    d = int(0.047 * SR); fb = 0.45 * decay / 0.35
    y = out.copy()
    for i in range(d, len(y)): y[i] += y[i - d] * fb * 0.5
    return y
def normalize(x, peak=0.95):
    m = np.max(np.abs(x)) if len(x) else 1.0
    return x / m * peak if m > 0 else x
def write(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = np.clip(x, -1, 1)
    data = (x * 32767).astype("<i2").tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data)
    print("wrote", os.path.relpath(path, ROOT), f"{len(x)/SR:.2f}s")

def gunshot(length=0.9, crack=1.0, thump=1.0, body_cut=2500, tail=0.3, pitch=1.0):
    n = int(SR * length); x = t(length)
    # supersonic crack: short bright noise burst
    crack_sig = highpass(noise(n), 1500 * pitch) * env(n, 0.0005, 0.012 / pitch) * crack * 2.5
    # muzzle blast body: band-limited noise with fast decay
    body = lowpass(noise(n), body_cut * pitch) * env(n, 0.001, 0.06) * 1.6
    # low thump: decaying sine sweep
    f0 = 120 * pitch
    thump_sig = np.sin(2 * np.pi * (f0 * x - 40 * x * x)) * env(n, 0.001, 0.09) * thump
    sig = crack_sig + body + thump_sig
    sig = reverb(sig, decay=tail)
    sig *= env(n, 0.0, length * 0.45, 0.6) + 0.02
    return normalize(np.tanh(sig * 1.3))

def click(length=0.12, freq=1800, decay=0.02, amount=0.6):
    n = int(SR * length)
    s = (np.sin(2 * np.pi * freq * t(length)) * 0.4 + highpass(noise(n), 1200) * 0.6) * env(n, 0.0005, decay)
    return normalize(s, amount)

def clack(length=0.25, freq=420, decay=0.05):
    n = int(SR * length)
    s = (np.sin(2 * np.pi * freq * t(length)) * 0.5 + lowpass(noise(n), 3000) * 0.7) * env(n, 0.001, decay)
    s += click(length, 2400, 0.01, 0.4)[:n]
    return normalize(reverb(s, 0.15), 0.85)

def ding(length=1.4, freq=2650):
    n = int(SR * length); x = t(length)
    s = np.zeros(n)
    for k, (mult, amp, dec) in enumerate([(1.0, 1.0, 0.5), (2.76, 0.45, 0.25), (5.4, 0.2, 0.12), (8.9, 0.08, 0.06)]):
        s += np.sin(2 * np.pi * freq * mult * x) * amp * np.exp(-x / dec)
    s += highpass(noise(n), 3000) * env(n, 0.0005, 0.01) * 0.6
    return normalize(reverb(s, 0.25), 0.8)

def impact(kind):
    n = int(SR * 0.35); x = t(0.35)
    if kind == "paper":
        s = lowpass(noise(n), 1800) * env(n, 0.001, 0.03) + highpass(noise(n), 4000) * env(n, 0.0005, 0.008) * 0.6
    elif kind == "metal":
        s = ding(0.35, 1900)[:n] * 0.8 + lowpass(noise(n), 2500) * env(n, 0.001, 0.02)
    elif kind == "concrete":
        s = lowpass(noise(n), 4000) * env(n, 0.0005, 0.045) * 1.4 + np.sin(2 * np.pi * 90 * x) * env(n, 0.001, 0.05) * 0.5
    elif kind == "wood":
        s = lowpass(noise(n), 1200) * env(n, 0.001, 0.05) + np.sin(2 * np.pi * 180 * x) * env(n, 0.001, 0.07) * 0.6
    else:  # rubber
        s = lowpass(noise(n), 700) * env(n, 0.002, 0.06) + np.sin(2 * np.pi * 70 * x) * env(n, 0.002, 0.08) * 0.7
    return normalize(reverb(s, 0.2), 0.85)

def ambience(length=8.0):
    n = int(SR * length)
    brown = np.cumsum(noise(n)); brown -= np.linspace(brown[0], brown[-1], n); brown /= np.max(np.abs(brown))
    hum = 0.25 * np.sin(2 * np.pi * 60 * t(length)) + 0.12 * np.sin(2 * np.pi * 120 * t(length))
    s = lowpass(brown, 400) * 0.8 + hum * 0.3
    # seamless loop: crossfade the ends
    f = int(SR * 0.5); ramp = np.linspace(0, 1, f)
    s[:f] = s[:f] * ramp + s[-f:] * (1 - ramp)
    return normalize(s[:-f], 0.6)

def main():
    W = os.path.join(OUT, "Weapons"); I = os.path.join(OUT, "Impacts"); U = os.path.join(OUT, "UI"); A = os.path.join(OUT, "Ambience")
    write(os.path.join(W, "pistol_fire.wav"), gunshot(0.8, crack=1.1, thump=0.7, body_cut=3200, tail=0.28, pitch=1.15))
    write(os.path.join(W, "rifle_fire.wav"), gunshot(1.1, crack=1.3, thump=1.3, body_cut=2200, tail=0.4, pitch=0.9))
    write(os.path.join(W, "smg_fire.wav"), gunshot(0.6, crack=0.9, thump=0.9, body_cut=2800, tail=0.22, pitch=1.05))
    write(os.path.join(W, "dry_fire.wav"), click(0.15, 1500, 0.015, 0.7))
    write(os.path.join(W, "mag_out.wav"), clack(0.3, 380, 0.06))
    write(os.path.join(W, "mag_in.wav"), clack(0.35, 520, 0.045))
    write(os.path.join(W, "fire_mode_switch.wav"), click(0.1, 2600, 0.012, 0.5))
    write(os.path.join(I, "impact_paper.wav"), impact("paper"))
    write(os.path.join(I, "impact_metal.wav"), impact("metal"))
    write(os.path.join(I, "impact_concrete.wav"), impact("concrete"))
    write(os.path.join(I, "impact_wood.wav"), impact("wood"))
    write(os.path.join(I, "impact_rubber.wav"), impact("rubber"))
    write(os.path.join(I, "target_ding.wav"), ding())
    write(os.path.join(U, "ui_click.wav"), click(0.08, 1200, 0.02, 0.5))
    write(os.path.join(U, "target_reset_motor.wav"), normalize(lowpass(noise(int(SR * 0.6)), 500) * env(int(SR * 0.6), 0.05, 0.4) * 0.6 + 0.3 * np.sin(2 * np.pi * 95 * t(0.6)) * env(int(SR * 0.6), 0.05, 0.35), 0.5))
    write(os.path.join(A, "range_ambience_loop.wav"), ambience())
    # mechanical and feedback sounds
    for i, f in enumerate((5200, 6100, 4700)):
        n = int(SR * 0.25); x = t(0.25)
        s = (np.sin(2 * np.pi * f * x) * 0.6 + np.sin(2 * np.pi * f * 2.3 * x) * 0.25) * env(n, 0.0003, 0.035) + highpass(noise(n), 5000) * env(n, 0.0003, 0.006) * 0.5
        write(os.path.join(W, f"casing_bounce_{i + 1}.wav"), normalize(reverb(s, 0.12), 0.45))
    write(os.path.join(W, "slide_rack.wav"), normalize(np.concatenate([clack(0.12, 700, 0.03), clack(0.2, 480, 0.05)]), 0.8))
    write(os.path.join(W, "bolt_release.wav"), normalize(clack(0.25, 320, 0.07), 0.85))
    for i, f in enumerate((880, 880, 880, 1320)):
        n = int(SR * (0.15 if i < 3 else 0.5)); x = np.arange(n) / SR
        s = np.sin(2 * np.pi * f * x) * env(n, 0.005, 0.12 if i < 3 else 0.35) * 0.6
        write(os.path.join(U, f"countdown_{i + 1}.wav"), normalize(s, 0.55))
    n = int(SR * 0.35); x = t(0.35)
    write(os.path.join(U, "menu_confirm.wav"), normalize((np.sin(2 * np.pi * 660 * x) * env(n, 0.005, 0.08) + np.sin(2 * np.pi * 990 * x) * env(n, 0.06, 0.12)) * 0.5, 0.5))
    write(os.path.join(U, "ui_hover.wav"), click(0.05, 2200, 0.01, 0.25))
    n = int(SR * 1.2); x = t(1.2)
    write(os.path.join(U, "round_complete.wav"), normalize(sum(np.sin(2 * np.pi * f * x) * env(n, 0.01 + k * 0.12, 0.5) * 0.4 for k, f in enumerate((523.25, 659.25, 783.99, 1046.5))), 0.6))

if __name__ == "__main__":
    main()
