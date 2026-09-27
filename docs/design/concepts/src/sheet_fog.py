# -*- coding: utf-8 -*-
"""Fog-of-war and weather studies, 64x32 map-pixel swatches."""
import math
import pp, terrain
from city_sprites import ERAS

W, H = 64, 32

# the game's fog tones: 4 cloud levels x (flat, lit top edge, shaded bottom edge)  (render.js FOGP)
FOG = [((14, 15, 18), (20, 21, 25), (11, 12, 14)),
       ((20, 21, 25), (29, 31, 36), (15, 16, 19)),
       ((27, 29, 34), (40, 42, 49), (20, 21, 25)),
       ((36, 38, 45), (56, 59, 68), (27, 29, 34))]

def rgba(c, a=255):
    return (c[0], c[1], c[2], a)

def cloud_level(x, y, seed, soft=False):
    d = pp.fbm(x, y, 64, 4, 3, seed)
    d2 = pp.fbm(x, y, 64, 9, 2, seed + 1)
    v = d * 0.75 + d2 * 0.25
    return max(0, min(3, int((v - 0.36) / 0.065)))

def cloud_px(x, y, seed):
    L = cloud_level(x, y, seed)
    up = cloud_level(x - 1, y - 1, seed)
    dn = cloud_level(x + 1, y + 1, seed)
    if up < L:
        return rgba(FOG[L][1])
    if dn < L:
        return rgba(FOG[L][2])
    return rgba(FOG[L][0])

_PUFF = {}
def puffs(seed):
    """cumulus clusters: one wide base puff (level 1), a crown of mid puffs (level 2) on its upper half and
    1-2 small bright tops (level 3) toward the light; clusters sit on a jittered 26px grid"""
    if seed in _PUFF:
        return _PUFF[seed]
    out = []
    for gy in range(-1, 3):
        for gx in range(-1, 4):
            cx = gx * 26 + 6 + pp.h2(gx, gy, seed + 1) * 14
            cy = gy * 22 + 4 + pp.h2(gx, gy, seed + 2) * 12
            R = 10 + pp.h2(gx, gy, seed + 3) * 4
            out.append((1, cx, cy, R))
            out.append((1, cx + R * 0.8, cy + 2, R * 0.7))
            out.append((1, cx - R * 0.7, cy + 3, R * 0.6))
            n2 = 3 + int(pp.h2(gx, gy, seed + 4) * 3)
            for i in range(n2):
                a = 3.6 + i * (2.4 / n2)                  # spread over the upper arc
                out.append((2, cx + math.cos(a) * R * 0.45, cy + math.sin(a) * R * 0.35 - 1, R * (0.42 + 0.12 * pp.h2(i, gx, seed))))
            for i in range(1 + int(pp.h2(gx, gy, seed + 6) * 2)):
                out.append((3, cx - R * 0.3 + i * 4, cy - R * 0.35 + i, R * 0.26))
    out.sort(key=lambda p: p[0])
    _PUFF[seed] = out
    return out

def puff_level(x, y, seed):
    L = 0
    for (l, cx, cy, r) in puffs(seed):
        if l > L and (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
            L = l
    return L

def cloud_px_puffy(x, y, seed):
    """union of round puffs: rims only on the outer silhouette of each level (no 'bubbles')"""
    L = puff_level(x, y, seed)
    if puff_level(x - 1, y - 1, seed) < L or puff_level(x, y - 1, seed) < L:
        return rgba(FOG[L][1])
    if puff_level(x + 1, y + 1, seed) < L:
        return rgba(FOG[L][2])
    return rgba(FOG[L][0])

def stale(c):
    m = (c[0] + c[1] + c[2]) / 3
    r, g, b = [v + (m - v) * 0.6 for v in c[:3]]
    return (int(r * .6), int(g * .6), int(b * .6), 255)

def frontier(base, seed, edge_x, amp=8.0, band=9.0, puffy=False, sea=False):
    """left of the (noisy) frontier = explored land, right = cloud; ordered-dither frays both sides"""
    out = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            ex = edge_x + (pp.fbm(x, y, 64, 3, 2, seed + 70) - 0.5) * amp * 2
            dv = x - ex                          # >0 : inside the fog
            t = (dv + band / 2) / band
            cp = cloud_px_puffy(x, y, seed) if puffy else cloud_px(x, y, seed)
            c = base.get(x, y)
            if t >= 1:
                out.setraw(x, y, cp)
            elif t > pp.bayer(x, y):
                L = cloud_level(x, y, seed)
                out.setraw(x, y, rgba(FOG[L][1]))
            elif dv > -band * 1.5:
                # darkening halo on the explored side: two posterised steps, dithered between them
                q = (dv + band * 1.5) / (band * 1.5)          # 0..1 toward the fog
                out.setraw(x, y, pp.mul(c, 0.66 if q > 0.5 + (pp.bayer(x, y) - 0.5) * 0.5 else 0.84 if q > 0.2 else 1.0))
            else:
                out.setraw(x, y, c)
    return out

def snow_creep(base_key='meadow', seed=5):
    t = terrain.patch(base_key, W, H, seed=seed)
    Hf = terrain.heightfield(W, H, seed, k=3)
    snow = terrain.ramp_for('snow')
    out = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            s = terrain.shade(Hf, x, y, W, H, 3.2)
            # snow line runs from full cover (left) to none (right); it sticks first to high and shaded ground
            cover = (1 - x / W) * 1.5 - 0.35 + (Hf[y][x] - 0.5) * 0.9 - s * 0.35 + (pp.fbm(x, y, 64, 8, 2, seed + 7) - 0.5) * 0.7
            c = t.get(x, y)
            if cover > 0.5 + (pp.bayer(x, y) - 0.5) * 0.12:
                lvl = 2 + (1 if s > 0.05 else -1 if s < -0.05 else 0)
                c = snow[max(0, min(4, lvl + 1))]
            out.setraw(x, y, c)
    return out

def rain(base, seed, frame, heavy=False, dark=0.78):
    out = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            c = base.get(x, y)
            out.setraw(x, y, (int(c[0] * dark), int(c[1] * dark), int(c[2] * (dark + 0.06)), 255))
    # cloud shadows
    for y in range(H):
        for x in range(W):
            if pp.fbm(x + frame * 3, y, 64, 3, 2, seed + 4) > 0.58:
                c = out.get(x, y)
                out.setraw(x, y, pp.mul(c, 0.82))
    n = 90 if heavy else 46
    for i in range(n):
        x0 = int(pp.h2(i, 1, seed) * (W + 16)) - 8 - frame * 2
        y0 = int(pp.h2(i, 2, seed) * H) + frame * 6
        ln = 4 if heavy else 3
        for k in range(ln):
            x, y = (x0 - (k + 1) // 2) % W, (y0 + k) % H
            out.put(x, y, (206, 220, 236, 230 if k == ln - 1 else 150))
    # splashes
    for i in range(12 if heavy else 6):
        x = int(pp.h2(i, 5 + frame, seed) * W); y = int(pp.h2(i, 6 + frame, seed) * H)
        out.put(x, y, (220, 232, 242, 160))
    return out

def lightning(img, x0, seed):
    x, y = x0, 0
    path = []
    while y < H * 0.8:
        path.append((x, y))
        y += 1
        if pp.h2(x, y, seed) < 0.35:
            x += 1 if pp.h2(y, x, seed + 1) < 0.6 else -1
        if y == 12:
            bx, by = x, y
    # flash glow
    for (px, py) in path:
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                d = abs(dx) + abs(dy)
                if d <= 3:
                    img.put(px + dx, py + dy, (170, 190, 255, 26))
    for (px, py) in path:
        img.setraw(px, py, (255, 255, 255, 255))
        img.put(px + 1, py, (180, 200, 255, 200))
    # a short fork
    fx, fy = path[10]
    for k in range(6):
        img.setraw(fx - k // 2 - 1, fy + k, (230, 238, 255, 255))

def smog(seed=9):
    base = terrain.patch('plains', W, H, seed=seed)
    for y in range(H):
        for x in range(W):
            base.setraw(x, y, pp.mix(base.get(x, y), (120, 116, 100), 0.25))
    city = pp.sprite(ERAS[6][2], pp.nation_pal(pp.NATIONS[0]))
    out = pp.Img(W, H)
    out.blit(base, 0, 0)
    out.blit(pp.with_shadow(city), 8, 12)
    out.blit(pp.with_shadow(pp.sprite(ERAS[5][2], pp.nation_pal(pp.NATIONS[0]))), 30, 15)
    # haze: brown-grey, density from noise and distance to the chimneys; ordered dither, no alpha noise
    for y in range(H):
        for x in range(W):
            n = pp.fbm(x + y * 0.5, y, 64, 4, 2, seed + 3)
            src = math.exp(-((x - 20) ** 2 + (y - 10) ** 2) / 500.0)
            dens = n * 0.6 + src * 0.7 - 0.15
            if dens > pp.bayer(x, y) * 0.9 + 0.2:
                out.put(x, y, (132, 120, 98, 150))
            if dens > 0.75:
                out.put(x, y, (96, 88, 74, 120))
    return out

def drought(seed=13):
    base = terrain.patch('plains', W, H, seed=seed)
    P = terrain.provinces(W, H, seed + 2, 16)
    out = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            c = pp.mix(base.get(x, y), (196, 170, 112), 0.55)
            out.setraw(x, y, c)
    for y in range(H):
        for x in range(W):
            p = P[y][x]
            if (x + 1 < W and P[y][x + 1] != p) or (y + 1 < H and P[y + 1][x] != p):
                out.setraw(x, y, (132, 100, 62, 255))
                if y > 0:
                    out.setraw(x, y - 1, pp.mix(out.get(x, y - 1), (232, 212, 160), 0.6))
    return out

def night(seed=21):
    base = terrain.patch('meadow', W, H, seed=seed)
    out = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            c = base.get(x, y)
            out.setraw(x, y, (int(c[0] * 0.22 + 8), int(c[1] * 0.26 + 10), int(c[2] * 0.40 + 26), 255))
    # road between two towns, faint
    for x in range(10, 52):
        y = int(16 + math.sin(x * 0.15) * 4)
        out.put(x, y, (200, 170, 100, 70))
    for (cx, cy, r) in ((12, 14, 6), (50, 18, 5), (32, 24, 2)):
        # warm glow first (ordered dither, no alpha smear)
        for dy in range(-r - 3, r + 4):
            for dx in range(-2 * r - 4, 2 * r + 5):
                dd = math.sqrt((dx / 1.6) ** 2 + dy ** 2) / (r + 3)
                if dd < 1 and (1 - dd) * 0.9 > pp.bayer(cx + dx, cy + dy):
                    c = out.get(cx + dx, cy + dy)
                    out.setraw(cx + dx, cy + dy, pp.mix(c, (120, 84, 46), 0.55))
        # street grid: lights on a 2px lattice, denser and whiter in the core
        for dy in range(-r, r + 1):
            for dx in range(-2 * r, 2 * r + 1):
                dd = math.sqrt((dx / 1.6) ** 2 + dy ** 2) / r
                if dd > 1:
                    continue
                on = (dx % 2 == 0 or dy % 2 == 0) and pp.h2(cx + dx, cy + dy, seed) < 1.05 - dd
                if on:
                    out.setraw(cx + dx, cy + dy, (255, 238, 170, 255) if dd < 0.35 else (240, 182, 84, 255) if dd < 0.7 else (178, 118, 52, 255))
    return out

def build():
    K = 4
    sw, sh = W * K, H * K
    top = 76
    items = []
    land = terrain.patch('meadow', W, H, seed=3)
    P = terrain.provinces(W, H, 8, 4)
    terrain.draw_borders(land, P)
    sea = terrain.patch('shallow', W, H, seed=4)
    items.append(('НЕИЗВЕДАНО: ОБЛАКА (ТЕКУЩИЙ СТИЛЬ)', 'MVP', lambda: _full(cloud_px, 31)))
    items.append(('НЕИЗВЕДАНО: «КУЧЕВЫЕ» ОБЛАКА (ПРЕДЛОЖЕНИЕ)', 'MVP', lambda: _full(cloud_px_puffy, 31)))
    items.append(('КРАЙ ТУМАНА НАД СУШЕЙ: РВАНЫЙ ДИЗЕРИНГ', 'MVP', lambda: frontier(land, 31, 30)))
    items.append(('КРАЙ ТУМАНА НАД МОРЕМ', 'MVP', lambda: frontier(sea, 33, 26, amp=9, puffy=True)))
    items.append(('ИЗВЕДАНО, НО НЕ ВИДНО (УСТАРЕЛО)', 'MVP', lambda: _stale(land)))
    items.append(('СНЕГ НАСТУПАЕТ (ЗИМА)', 'ПОЗЖЕ', lambda: snow_creep()))
    items.append(('ДОЖДЬ, КАДР 1', 'ПОЗЖЕ', lambda: rain(land, 17, 0)))
    items.append(('ДОЖДЬ, КАДР 2', 'ПОЗЖЕ', lambda: rain(land, 17, 1)))
    items.append(('ГРОЗА', 'ПОЗЖЕ', lambda: _storm(land)))
    items.append(('СМОГ НАД ЗАВОДАМИ', 'ПОЗЖЕ', lambda: smog()))
    items.append(('ЗАСУХА', 'ПОЗЖЕ', lambda: drought()))
    items.append(('НОЧНЫЕ ОГНИ (ИНДУСТРИАЛЬНАЯ+)', 'ПОЗЖЕ', lambda: night()))
    cols = 3
    cw, ch = sw + 36, sh + 44
    Wd = 24 + cols * cw
    Hd = top + ((len(items) + cols - 1) // cols) * ch + 20
    img = pp.Img(Wd, Hd, pp.BG)
    pp.draw_text(img, 24, 18, 'ТУМАН ВОЙНЫ И ПОГОДА', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'СВОТЧИ 64×32 ПИКСЕЛЯ КАРТЫ · ×4 · ТУМАН: 4 СЕРЫХ УРОВНЯ × (ПЛОСКИЙ, СВЕТЛЫЙ КРАЙ СВЕРХУ-СЛЕВА, ТЕНЬ СНИЗУ-СПРАВА)', pp.INK2, 1)
    pp.draw_text(img, 24, 54, 'ВСЕ ПЕРЕХОДЫ — УПОРЯДОЧЕННЫЙ ДИЗЕРИНГ БАЙЕРА 4×4, НИКАКОЙ ПОЛУПРОЗРАЧНОСТИ НА КРАЯХ', pp.INK2, 1)
    x1 = pp.Img(cols * W, ((len(items) + cols - 1) // cols) * H)
    for i, (name, ms, fn) in enumerate(items):
        cx = 24 + (i % cols) * cw
        cy = top + (i // cols) * ch
        sw_img = fn()
        img.blit(sw_img, cx, cy + 16, K)
        x1.blit(sw_img, (i % cols) * W, (i // cols) * H)
        pp.draw_text(img, cx, cy, name, pp.INK, 1)
        pp.draw_text(img, cx + sw - pp.text_width(ms), cy, ms, pp.INK2, 1)
    # tone chips
    img.save('fog_and_weather.png')
    x1.save('fog_and_weather_x1.png')
    return img

def _full(fn, seed):
    o = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            o.setraw(x, y, fn(x, y, seed))
    return o

def _stale(land):
    o = pp.Img(W, H)
    for y in range(H):
        for x in range(W):
            c = land.get(x, y)
            o.setraw(x, y, stale(c) if x >= W // 2 + int((pp.fbm(x, y, 64, 3, 1, 5) - .5) * 10) else c)
    return o

def _storm(land):
    o = rain(land, 23, 2, heavy=True, dark=0.55)
    lightning(o, 40, 7)
    return o

if __name__ == '__main__':
    build()
