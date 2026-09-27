# -*- coding: utf-8 -*-
"""Biome ramps + patch generator (hillshade from the top-left, posterised levels, narrow ordered-dither
bands between levels, clustered biome details instead of per-pixel speckle)."""
import math, colorsys
import pp

# base colours = the game's BiomeColor table (Data.cs / core.js) + water depths
BIOMES = [
    # key, name, base rgb, relief amplitude, detail
    ('forest',  'ЛЕС',          (64, 102, 60),  0.55, 'trees'),
    ('meadow',  'ЛУГА',         (110, 144, 74), 0.45, 'tufts'),
    ('plains',  'РАВНИНА',      (144, 156, 88), 0.30, 'fields'),
    ('steppe',  'СТЕПЬ',        (170, 160, 100), 0.30, 'streaks'),
    ('desert',  'ПУСТЫНЯ',      (200, 180, 128), 0.35, 'dunes'),
    ('tundra',  'ТУНДРА',       (128, 134, 112), 0.40, 'moss'),
    ('snow',    'СНЕГ, ЛЕДНИК', (214, 221, 228), 0.80, 'ice'),
    ('mount',   'ГОРЫ',         (122, 116, 108), 1.00, 'rock'),
    ('swamp',   'БОЛОТО',       (74, 88, 64),   0.20, 'pools'),
    ('jungle',  'ДЖУНГЛИ',      (48, 98, 56),   0.55, 'canopy'),
    ('savanna', 'САВАННА',      (172, 154, 86), 0.30, 'acacia'),
    ('taiga',   'ТАЙГА',        (58, 84, 66),   0.55, 'conifers'),
    ('shallow', 'МЕЛКОВОДЬЕ',   (64, 99, 120),  0.00, 'caustics'),
    ('deep',    'ГЛУБОКАЯ ВОДА', (37, 59, 82),  0.00, 'swell'),
]
BY_KEY = {b[0]: b for b in BIOMES}

def terrain_ramp(base, cool=0.035, warm=0.022):
    """5 steps, index 0 = deepest shadow ... 4 = highlight. Shadows drift toward blue-violet and gain a
    little saturation, highlights drift toward yellow and lose saturation (classic hue-shifted ramp)."""
    r, g, b = [v / 255.0 for v in base]
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    out = []
    for step, vm, sm in ((-2, 0.70, 1.10), (-1, 0.85, 1.05), (0, 1.0, 1.0), (1, 1.13, 0.90), (2, 1.26, 0.78)):
        if s < 0.08:
            hh = 0.60 if step < 0 else h     # near-greys (snow): shadows go blue
            ss = s + (0.06 * -step if step < 0 else 0)
        elif step < 0:
            hh = pp.hue_toward(h, 0.68, cool * -step); ss = s * sm
        elif step > 0:
            hh = pp.hue_toward(h, 1 / 6, warm * step); ss = s * sm
        else:
            hh, ss = h, s
        rr, gg, bb = colorsys.hsv_to_rgb(hh, max(0, min(1, ss)), max(0, min(1, v * vm)))
        out.append((int(rr * 255 + .5), int(gg * 255 + .5), int(bb * 255 + .5), 255))
    return out

WATER = {
    'shallow': [(47, 77, 100, 255), (56, 89, 112, 255), (64, 99, 120, 255), (80, 116, 134, 255), (112, 150, 162, 255)],
    'deep':    [(22, 34, 52, 255), (28, 44, 64, 255), (37, 59, 82, 255), (47, 77, 100, 255), (74, 104, 126, 255)],
}

def ramp_for(key):
    if key in WATER:
        return WATER[key]
    base = BY_KEY[key][2]
    if key == 'snow':
        return [(150, 164, 188, 255), (178, 192, 212, 255), (206, 216, 228, 255), (228, 234, 240, 255), (246, 248, 250, 255)]
    if key == 'mount':
        # rock + snowcap highlight
        return [(74, 68, 66, 255), (98, 92, 86, 255), (122, 116, 108, 255), (152, 146, 134, 255), (236, 238, 242, 255)]
    return terrain_ramp(base)

def quant(v, x, y, band=0.34):
    """posterise v (float level) to an int; only a narrow band around each boundary is ordered-dithered"""
    fl = math.floor(v)
    f = v - fl
    lo, hi = 0.5 - band / 2, 0.5 + band / 2
    if f < lo:
        return fl
    if f > hi:
        return fl + 1
    t = (f - lo) / band
    return fl + 1 if t > pp.bayer(x, y) else fl

def heightfield(w, h, seed, k=3, amp=1.0, ridged=False, tile=True):
    size = max(w, h)
    H = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if ridged:
                n = pp.fbm(x, y, size, k, 4, seed)
                n = 1 - abs(n * 2 - 1)
                n = n * n
            else:
                n = pp.fbm(x, y, size, k, 4, seed)
            H[y][x] = n * amp
    return H

def shade(H, x, y, w, h, gain):
    xl, xr = (x - 1) % w, (x + 1) % w
    yu, yd = (y - 1) % h, (y + 1) % h
    gx = H[y][xr] - H[y][xl]
    gy = H[yd][x] - H[yu][x]
    return (gx + gy) * gain          # >0 : faces the top-left light

def patch(key, w=32, h=32, seed=7, relief=None, detail=True):
    """map-pixel patch (1 px = 1 map px). Returns pp.Img"""
    _, _, base, amp, det = BY_KEY[key]
    if relief is not None:
        amp = relief
    R = ramp_for(key)
    img = pp.Img(w, h)
    lvl = [[2] * w for _ in range(h)]
    if key in ('shallow', 'deep'):
        size = max(w, h)
        for y in range(h):
            for x in range(w):
                n = pp.fbm(x, y, size, 2, 3, seed + 3)
                v = 1.55 + (n - 0.5) * 1.6 if key == 'deep' else 2.0 + (n - 0.5) * 1.4
                lvl[y][x] = max(0, min(4, quant(v, x, y, 0.3)))
    else:
        ridged = False
        Hf = heightfield(w, h, seed, k=3 if key != 'mount' else 4, amp=1.0, ridged=ridged)
        tone = heightfield(w, h, seed + 50, k=4)
        gain = {'mount': 7.0, 'snow': 3.2}.get(key, 3.2) * amp
        for y in range(h):
            for x in range(w):
                s = shade(Hf, x, y, w, h, gain)
                v = 2.0 + s * 1.6 + (tone[y][x] - 0.5) * 0.9
                if key == 'mount':
                    v = 2.0 + s * 1.6 + (Hf[y][x] - 0.5) * 1.2
                    v = min(v, 3.3)
                    if Hf[y][x] > 0.64:
                        v = 4.2 if s > -0.15 else 1.6     # snow cap: lit side white, shadow side blue-grey rock
                lvl[y][x] = max(0, min(4, quant(v, x, y)))
    # base fill
    for y in range(h):
        for x in range(w):
            img.setraw(x, y, R[lvl[y][x]])
    if detail:
        DETAIL.get(det, lambda *a: None)(img, lvl, R, w, h, seed)
    return img

# ---------------------------------------------------------------- details
def jitter_points(w, h, cell, seed, keep=1.0, margin=0):
    pts = []
    for gy in range(-1, h // cell + 2):
        for gx in range(-1, w // cell + 2):
            if pp.h2(gx, gy, seed + 11) > keep:
                continue
            x = int(gx * cell + pp.h2(gx, gy, seed) * cell)
            y = int(gy * cell + pp.h2(gx, gy, seed + 1) * cell)
            pts.append((x, y))
    pts.sort(key=lambda p: (p[1], p[0]))       # draw back to front
    return pts

def put_wrap(img, x, y, c):
    img.setraw(x % img.w, y % img.h, c)

def d_trees(img, lvl, R, w, h, seed):
    # ground under canopy = step 1, crowns 3x3 with lit top-left, contact shadow step 0
    for y in range(h):
        for x in range(w):
            img.setraw(x, y, R[max(0, min(1, lvl[y][x] - 1))])
    crown = [".LM.", "LMMD", "MMDD", ".DD."]
    for (x, y) in jitter_points(w, h, 4, seed, 0.92):
        big = pp.h2(x, y, seed + 5) < 0.35
        shape = crown if big else [".L.", "LMD", ".D."]
        sh = len(shape)
        for dy in range(sh):                                 # shadow first (down-right)
            for dx, ch in enumerate(shape[dy]):
                if ch != '.':
                    put_wrap(img, x + dx + 1, y + dy + 1, R[0])
        for dy in range(sh):
            for dx, ch in enumerate(shape[dy]):
                if ch == 'L': put_wrap(img, x + dx, y + dy, R[4])
                elif ch == 'M': put_wrap(img, x + dx, y + dy, R[3])
                elif ch == 'D': put_wrap(img, x + dx, y + dy, R[2])

def d_conifers(img, lvl, R, w, h, seed):
    for y in range(h):
        for x in range(w):
            img.setraw(x, y, R[max(0, min(2, lvl[y][x] - 1))])
    shape = [".L.", ".LD", "LMD", "MDD"]
    for (x, y) in jitter_points(w, h, 5, seed + 3, 0.9):
        for dy, row in enumerate(shape):
            for dx, ch in enumerate(row):
                if ch not in '.T':
                    put_wrap(img, x + dx + 1, y + dy + 1, R[0])
        for dy, row in enumerate(shape):
            for dx, ch in enumerate(row):
                if ch == 'L': put_wrap(img, x + dx, y + dy, R[4])
                elif ch == 'M': put_wrap(img, x + dx, y + dy, R[3])
                elif ch == 'D': put_wrap(img, x + dx, y + dy, R[2])
                elif ch == 'T': put_wrap(img, x + dx, y + dy, (70, 56, 44, 255))
        if pp.h2(x, y, seed + 9) < 0.12:
            put_wrap(img, x + 1, y, (226, 232, 238, 255))

def d_canopy(img, lvl, R, w, h, seed):
    for y in range(h):
        for x in range(w):
            img.setraw(x, y, R[0])
    shape = [".LLM.", "LLMMD", "LMMDD", "MMDDD", ".DDD."]
    small = [".LM.", "LMMD", "MDDD", ".DD."]
    for (x, y) in jitter_points(w, h, 4, seed + 7, 1.0):
        s = shape if pp.h2(x, y, seed) < 0.5 else small
        for dy, row in enumerate(s):
            for dx, ch in enumerate(row):
                if ch == 'L': put_wrap(img, x + dx, y + dy, R[4])
                elif ch == 'M': put_wrap(img, x + dx, y + dy, R[3])
                elif ch == 'D': put_wrap(img, x + dx, y + dy, R[2])
        if pp.h2(x, y, seed + 2) < 0.12:                    # rare flowering tree
            put_wrap(img, x + 1, y + 1, (214, 170, 92, 255))

def d_tufts(img, lvl, R, w, h, seed):
    for (x, y) in jitter_points(w, h, 5, seed + 21, 0.7):
        put_wrap(img, x, y, R[min(4, lvl[y % h][x % w] + 1)])
        put_wrap(img, x + 1, y, R[min(4, lvl[y % h][x % w] + 1)])
        put_wrap(img, x, y + 1, R[max(0, lvl[y % h][x % w] - 1)])
    for (x, y) in jitter_points(w, h, 9, seed + 22, 0.5):   # flowers
        put_wrap(img, x, y, (236, 226, 160, 255))

def d_fields(img, lvl, R, w, h, seed):
    # faint wind-combed rows (fertile plains), only on flat tone
    for y in range(h):
        for x in range(w):
            if (y + (x // 6)) % 4 == 0 and lvl[y][x] == 2 and pp.h2(x // 8, y // 6, seed) < 0.5:
                img.setraw(x, y, R[3])

def d_streaks(img, lvl, R, w, h, seed):
    for y in range(h):
        for x in range(w):
            ph = math.sin(x * 0.35 + y * 0.9 + pp.fbm(x, y, max(w, h), 2, 1, seed + 5) * 6)
            if ph > 0.97 and lvl[y][x] >= 2 and pp.h2(x // 5, y, seed) < 0.6:
                img.setraw(x, y, R[min(4, lvl[y][x] + 1)])
            elif ph < -0.985:
                img.setraw(x, y, R[max(0, lvl[y][x] - 1)])

def d_dunes(img, lvl, R, w, h, seed):
    """barchan-like ridges: ~8px wavelength, 1px lit crest, 2px lit slope facing top-left, 1px lee shadow"""
    size = max(w, h)
    for y in range(h):
        for x in range(w):
            warp = pp.fbm(x, y, size, 2, 2, seed + 8)
            u = x * 0.055 + y * 0.105 + warp * 1.6
            f = u - math.floor(u)
            if f < 0.10:
                c = R[4]
            elif f < 0.30:
                c = R[3]
            elif f > 0.90:
                c = R[1]
            else:
                c = R[2]
            img.setraw(x, y, c)
    for (x, y) in jitter_points(w, h, 13, seed + 9, 0.35):     # rare rock outcrop
        put_wrap(img, x, y, R[0]); put_wrap(img, x + 1, y, R[1])

def d_moss(img, lvl, R, w, h, seed):
    size = max(w, h)
    for y in range(h):
        for x in range(w):
            n = pp.fbm(x, y, size, 5, 2, seed + 30)
            if n > 0.60:
                img.setraw(x, y, R[1])
            elif n < 0.34:
                img.setraw(x, y, R[3])
    for (x, y) in jitter_points(w, h, 8, seed + 31, 0.4):
        put_wrap(img, x, y, (226, 230, 234, 255))           # frost patch
        put_wrap(img, x + 1, y, (226, 230, 234, 255))
        put_wrap(img, x + 1, y + 1, (190, 198, 208, 255))

def d_ice(img, lvl, R, w, h, seed):
    for (x, y) in jitter_points(w, h, 9, seed + 40, 0.5):   # sastrugi: 3px shadow + 1px sparkle
        if lvl[y % h][x % w] >= 2:
            for k in range(3):
                put_wrap(img, x + k, y, R[1])
            put_wrap(img, x, y - 1, (255, 255, 255, 255))

def d_rock(img, lvl, R, w, h, seed):
    for (x, y) in jitter_points(w, h, 6, seed + 50, 0.5):
        if lvl[y % h][x % w] <= 2:
            put_wrap(img, x, y, R[0])
            put_wrap(img, x + 1, y, R[1])

def d_pools(img, lvl, R, w, h, seed):
    size = max(w, h)
    water = [(52, 74, 78, 255), (70, 96, 98, 255), (104, 132, 124, 255)]
    for y in range(h):
        for x in range(w):
            n = pp.fbm(x, y, size, 5, 2, seed + 60)
            if n > 0.6:
                img.setraw(x, y, water[0] if n > 0.66 else water[1])
                if n > 0.66 and pp.fbm(x - 1, y - 1, size, 5, 2, seed + 60) <= 0.66:
                    img.setraw(x, y, water[2])                   # lit rim top-left
    for (x, y) in jitter_points(w, h, 4, seed + 61, 0.6):     # reeds
        put_wrap(img, x, y, R[4])
        put_wrap(img, x, y + 1, R[3])

def d_acacia(img, lvl, R, w, h, seed):
    for (x, y) in jitter_points(w, h, 9, seed + 70, 0.75):
        dark = (86, 104, 52, 255); mid = (112, 130, 62, 255); lit = (140, 156, 76, 255)
        for dx in range(5):
            put_wrap(img, x + dx + 1, y + 3, R[0])               # shadow on ground
        for dx in range(5):
            put_wrap(img, x + dx, y, lit if dx < 2 else mid)
        for dx in range(1, 4):
            put_wrap(img, x + dx, y + 1, dark)
        put_wrap(img, x + 2, y + 2, (92, 70, 44, 255))

def d_caustics(img, lvl, R, w, h, seed):
    for y in range(h):
        for x in range(w):
            a = math.sin(x * 0.8 + math.sin(y * 0.55) * 2.2 + seed)
            b = math.sin(y * 0.7 + math.sin(x * 0.45) * 2.0)
            if a * b > 0.9:
                img.setraw(x, y, R[4] if lvl[y][x] >= 2 else R[3])
    for (x, y) in jitter_points(w, h, 8, seed + 80, 0.5):
        put_wrap(img, x, y, (170, 200, 206, 255))
        put_wrap(img, x + 1, y, (132, 168, 180, 255))

def d_swell(img, lvl, R, w, h, seed):
    for (x, y) in jitter_points(w, h, 7, seed + 90, 0.7):
        put_wrap(img, x, y, R[3])
        put_wrap(img, x + 1, y, R[3])
        put_wrap(img, x + 2, y, R[4] if pp.h2(x, y, seed) < 0.3 else R[3])
        put_wrap(img, x + 1, y + 1, R[1])

DETAIL = {'trees': d_trees, 'conifers': d_conifers, 'canopy': d_canopy, 'tufts': d_tufts, 'fields': d_fields,
          'streaks': d_streaks, 'dunes': d_dunes, 'moss': d_moss, 'ice': d_ice, 'rock': d_rock,
          'pools': d_pools, 'acacia': d_acacia, 'caustics': d_caustics, 'swell': d_swell}

# ---------------------------------------------------------------- province borders on a patch
def provinces(w, h, seed, n=3):
    pts = [(pp.h2(i, 1, seed) * w, pp.h2(i, 2, seed) * h) for i in range(n)]
    P = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            jx = x + (pp.fbm(x, y, max(w, h), 5, 2, seed + 9) - 0.5) * 6
            jy = y + (pp.fbm(x, y, max(w, h), 5, 2, seed + 10) - 0.5) * 6
            P[y][x] = min(range(n), key=lambda i: (pts[i][0] - jx) ** 2 + (pts[i][1] - jy) ** 2)
    return P

def draw_borders(img, P, f=0.72):
    w, h = img.w, img.h
    for y in range(h):
        for x in range(w):
            p = P[y][x]
            if (x + 1 < w and P[y][x + 1] != p) or (y + 1 < h and P[y + 1][x] != p):
                c = img.get(x, y)
                img.setraw(x, y, pp.mul(c, f))

def tint(img, rgb, a):
    for y in range(img.h):
        for x in range(img.w):
            c = img.get(x, y)
            img.setraw(x, y, pp.mix(c, rgb, a))
