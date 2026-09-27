# -*- coding: utf-8 -*-
"""Pax Pixelia concept art toolkit: tiny PNG encoder, RGBA canvas, sprite grids, outline,
drop shadow, nation colour ramps, pixel font. Python standard library only."""
import zlib, struct, colorsys, math, os

OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))

# ---------------------------------------------------------------- PNG
def write_png(path, w, h, px):
    """px: bytearray of w*h*4 RGBA."""
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += px[y * stride:(y + 1) * stride]
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    data = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b''))
    with open(path, 'wb') as f:
        f.write(data)

def hexc(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)

# ---------------------------------------------------------------- canvas
class Img:
    def __init__(self, w, h, fill=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.px = bytearray(bytes(fill) * (w * h)) if len(fill) == 4 else bytearray(bytes(tuple(fill) + (255,)) * (w * h))

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 4
            return tuple(self.px[i:i + 4])
        return (0, 0, 0, 0)

    def put(self, x, y, c):
        """alpha-over blend"""
        if not (0 <= x < self.w and 0 <= y < self.h):
            return
        a = c[3] if len(c) > 3 else 255
        if a <= 0:
            return
        i = (y * self.w + x) * 4
        if a >= 255:
            self.px[i:i + 4] = bytes((c[0], c[1], c[2], 255))
            return
        dr, dg, db, da = self.px[i:i + 4]
        fa = a / 255.0
        oa = fa + da / 255.0 * (1 - fa)
        if oa <= 0:
            return
        def mix(s, d):
            return int(round((s * fa + d * (da / 255.0) * (1 - fa)) / oa))
        self.px[i:i + 4] = bytes((mix(c[0], dr), mix(c[1], dg), mix(c[2], db), int(round(oa * 255))))

    def setraw(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 4
            self.px[i:i + 4] = bytes((c[0], c[1], c[2], c[3] if len(c) > 3 else 255))

    def rect(self, x, y, w, h, c):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.put(xx, yy, c)

    def blit(self, src, x, y, scale=1):
        for sy in range(src.h):
            for sx in range(src.w):
                c = src.get(sx, sy)
                if c[3] == 0:
                    continue
                if scale == 1:
                    self.put(x + sx, y + sy, c)
                else:
                    for yy in range(scale):
                        for xx in range(scale):
                            self.put(x + sx * scale + xx, y + sy * scale + yy, c)

    def scaled(self, k):
        o = Img(self.w * k, self.h * k)
        o.blit(self, 0, 0, k)
        return o

    def crop(self, x, y, w, h):
        o = Img(w, h)
        for yy in range(h):
            for xx in range(w):
                o.setraw(xx, yy, self.get(x + xx, y + yy))
        return o

    def save(self, name):
        path = name if os.path.isabs(name) else os.path.join(OUT, name)
        write_png(path, self.w, self.h, self.px)
        return path

# ---------------------------------------------------------------- colour helpers
def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3)) + (255,)

def mul(c, f):
    return tuple(max(0, min(255, int(round(c[i] * f)))) for i in range(3)) + (255,)

def hue_toward(h, target, amt):
    """move hue h (0..1) toward target along the shortest way by amt (in turns)"""
    d = (target - h + 0.5) % 1.0 - 0.5
    step = max(-abs(amt), min(abs(amt), d))
    return (h + step) % 1.0

def ramp(base, n=3):
    """Nation ramp with hue shifting: light -> toward yellow (60deg), dark -> toward blue-violet (250deg).
    Returns (light, base, dark) or 5 steps (hilight, light, base, dark, deep)."""
    r, g, b = [v / 255.0 for v in base[:3]]
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    def mk(dh_target, dh, ds, dv):
        hh = hue_toward(h, dh_target, dh) if s > 0.05 else h
        ss = max(0.0, min(1.0, s * ds))
        vv = max(0.0, min(1.0, v * dv))
        rr, gg, bb = colorsys.hsv_to_rgb(hh, ss, vv)
        return (int(rr * 255 + .5), int(gg * 255 + .5), int(bb * 255 + .5), 255)
    base = tuple(base[:3]) + (255,)
    if n == 3:
        return (mk(1 / 6, 0.035, 0.78, 1.26), base, mk(0.70, 0.04, 1.08, 0.64))
    return (mk(1 / 6, 0.06, 0.55, 1.42), mk(1 / 6, 0.035, 0.78, 1.22), base,
            mk(0.70, 0.04, 1.08, 0.70), mk(0.70, 0.07, 1.15, 0.46))

# ---------------------------------------------------------------- master palette
K = hexc('1d191b')          # outline / deepest dark
PAL = {
    'K': K,
    # warm stone
    '1': hexc('efebe1'), '2': hexc('cfc7b6'), '3': hexc('a29a89'), '4': hexc('736c60'), '5': hexc('4c463f'),
    # adobe / sandstone
    'a': hexc('ecd3a2'), 'h': hexc('cfad78'), 'j': hexc('a4804c'), 'J': hexc('75582f'),
    # wood
    'w': hexc('c29060'), 'W': hexc('8e623c'), 'd': hexc('5e3f29'),
    # thatch / straw
    't': hexc('eed27e'), 'T': hexc('c9a24f'), 'u': hexc('8e6d35'),
    # brick / terracotta
    'b': hexc('dc8d66'), 'B': hexc('ae5f3e'), 'v': hexc('7a3f2c'),
    # plaster
    'p': hexc('f6f0e3'), 'P': hexc('dccfb6'),
    # steel
    'm': hexc('c9d1d9'), 'M': hexc('8c96a1'), 'n': hexc('56606c'),
    # cool concrete
    '6': hexc('e3e7e9'), '7': hexc('b5bdc3'), '8': hexc('828b94'), '9': hexc('545d67'),
    # glass
    'g': hexc('c6eef8'), 'G': hexc('72b9d5'), 'q': hexc('3d7399'), 'Q': hexc('25405d'),
    # lights
    'y': hexc('fff1a0'), 'Y': hexc('f2b43a'),
    # vegetation
    'e': hexc('a6c95e'), 'E': hexc('66943f'), 'f': hexc('3b5f2f'),
    # smoke / steam
    'z': hexc('f0efea'), 'Z': hexc('bcbbb5'), 'x': hexc('7a7873'), 'X': hexc('4e4c49'),
    # fire
    'r': hexc('ffb241'), 'R': hexc('e35d2b'),
    # misc
    'i': hexc('ffffff'), 'l': hexc('ff4d3d'), 'c': hexc('9ff7ff'), 'C': hexc('35cde6'),
    'o': hexc('6f9fbf'), 'O': hexc('4a7aa0'),   # water
    's': hexc('f4f7fa'), 'S': hexc('c9d6e2'),   # wool / snow
    'k': hexc('eab58b'), 'H': hexc('b98260'), 'F': hexc('4d3326'), 'L': hexc('4a3d35'),   # skin, hair, trousers
    'I': hexc('9a9a5e'), 'V': hexc('66683d'),   # olive drab
    '_': (0, 0, 0, 80),                        # soft ground shadow (never outlined)
}
# keys that never get an outline (smoke, steam, glow, sparks)
NO_OUTLINE = set('zZxXcC_')

# nation colours from the game's data table (Ардания, Кесарат Мирры, Торн)
NATIONS = [(190, 72, 60), (72, 112, 182), (112, 152, 58)]
# marker ramp for palette swap in the engine (magenta ramp is never used by the art itself)
MASK = (hexc('ff80ff'), hexc('ff00ff'), hexc('800080'))

def nation_pal(base):
    A, N, D = ramp(base)
    p = dict(PAL)
    p['A'], p['N'], p['D'] = A, N, D
    return p

def mask_pal():
    p = dict(PAL)
    p['A'], p['N'], p['D'] = MASK
    return p

# ---------------------------------------------------------------- sprites
def sprite(rows, pal, outline=True, outline_col=K, no_outline=NO_OUTLINE):
    h = len(rows)
    w = max(len(r) for r in rows)
    im = Img(w, h)
    solid = [[False] * w for _ in range(h)]
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch in '. ':
                continue
            if ch not in pal:
                raise KeyError('palette key %r (row %d: %s)' % (ch, y, row))
            im.setraw(x, y, pal[ch])
            if ch not in no_outline:
                solid[y][x] = True
    im.solid = [row[:] for row in solid]
    if outline:
        edge = []
        for y in range(h):
            for x in range(w):
                if im.get(x, y)[3]:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < w and 0 <= yy < h and solid[yy][xx]:
                        edge.append((x, y))
                        break
        for x, y in edge:
            im.setraw(x, y, outline_col)
            im.solid[y][x] = True
    return im

def with_shadow(spr, alpha=110, dx=1, dy=1):
    """sprite on a canvas 1px bigger with a flat drop shadow (like the map overlay)"""
    o = Img(spr.w + dx, spr.h + dy)
    solid = getattr(spr, 'solid', None)
    for y in range(spr.h):
        for x in range(spr.w):
            if (solid[y][x] if solid else spr.get(x, y)[3] > 0):
                o.put(x + dx, y + dy, (0, 0, 0, alpha))
    o.blit(spr, 0, 0)
    return o

# ---------------------------------------------------------------- pixel font (5 rows, optional accent row above)
_G = {
 'А': '.#./#.#/###/#.#/#.#', 'Б': '###/#../###/#.#/###', 'В': '##./#.#/##./#.#/##.',
 'Г': '###/#../#../#../#..', 'Д': '.###./.#.#./.#.#./#####/#...#', 'Е': '###/#../##./#../###',
 'Ё': '###/#../##./#../###', 'Ж': '#.#.#/#.#.#/.###./#.#.#/#.#.#', 'З': '##./..#/.#./..#/##.',
 'И': '#..#/#..#/#.##/##.#/#..#', 'Й': '#..#/#..#/#.##/##.#/#..#', 'К': '#..#/#.#./##../#.#./#..#',
 'Л': '.###/.#.#/.#.#/.#.#/#..#', 'М': '#...#/##.##/#.#.#/#...#/#...#', 'Н': '#.#/#.#/###/#.#/#.#',
 'О': '.##./#..#/#..#/#..#/.##.', 'П': '####/#..#/#..#/#..#/#..#', 'Р': '##./#.#/##./#../#..',
 'С': '.##/#../#../#../.##', 'Т': '###/.#./.#./.#./.#.', 'У': '#.#/#.#/.##/..#/##.',
 'Ф': '.#./###/#.#/###/.#.', 'Х': '#.#/#.#/.#./#.#/#.#', 'Ц': '#.#./#.#./#.#./####/...#',
 'Ч': '#.#/#.#/.##/..#/..#', 'Ш': '#.#.#/#.#.#/#.#.#/#.#.#/#####', 'Щ': '#.#.#./#.#.#./#.#.#./######/.....#',
 'Ъ': '##../.#../.##./.#.#/.##.', 'Ы': '#...#/#...#/##..#/#.#.#/##..#', 'Ь': '#../#../##./#.#/##.',
 'Э': '##./..#/.##/..#/##.', 'Ю': '#..#./#.#.#/###.#/#.#.#/#..#.', 'Я': '.###/#..#/.###/.#.#/#..#',
 '0': '###/#.#/#.#/#.#/###', '1': '.#./##./.#./.#./###', '2': '##./..#/.#./#../###',
 '3': '##./..#/.#./..#/##.', '4': '#.#/#.#/###/..#/..#', '5': '###/#../##./..#/##.',
 '6': '.##/#../###/#.#/###', '7': '###/..#/.#./.#./.#.', '8': '###/#.#/###/#.#/###',
 '9': '###/#.#/###/..#/##.', ' ': '../../../../..', '.': '././././#',
 ',': './././#/#', '-': '../../##/../..', '×': '.../#.#/.#./#.#/...', '/': '..#/..#/.#./#../#..',
 '(': '.#/#./#./#./.#', ')': '#./.#/.#/.#/#.', ':': './#/./#/.', '+': '.../.#./###/.#./...',
 '«': '..../.#.#/#.#./.#.#/....', '»': '..../#.#./.#.#/#.#./....', '%': '#..#/..#./.#../#..#/....',
 'X': '#.#/#.#/.#./#.#/#.#', 'x': '.../#.#/.#./#.#/...', '№': '#..#.#/##.#.#/#.##../#..#.#/#..#..',
 '—': '..../..../####/..../....', '?': '##./..#/.#./.../.#.', '!': '#/#/#/./#', '"': '#.#/#.#/.../.../...', '·': '././#/./.', '=': '.../###/.../###/...', '_': '.../.../.../.../###', '#': '#.#/###/#.#/###/#.#', '<': '..#/.#./#../.#./..#', '>': '#../.#./..#/.#./#..', '*': '#.#/.#./#.#/.../...',
}
_LAT = {'A': 'А', 'B': 'В', 'C': 'С', 'E': 'Е', 'H': 'Н', 'K': 'К', 'M': 'М', 'O': 'О', 'P': 'Р', 'T': 'Т', 'X': 'Х'}
for _l, _c in _LAT.items():
    _G[_l] = _G[_c]
_G.update({'D': '##./#.#/#.#/#.#/##.', 'F': '###/#../##./#../#..', 'G': '.##/#../#.#/#.#/.##', 'I': '###/.#./.#./.#./###',
           'J': '..#/..#/..#/#.#/.#.', 'L': '#../#../#../#../###', 'N': '#..#/##.#/#.##/#..#/#..#', 'Q': '.#./#.#/#.#/#.#/.##',
           'R': '##./#.#/##./#.#/#.#', 'S': '.##/#../.#./..#/##.', 'U': '#.#/#.#/#.#/#.#/###', 'V': '#.#/#.#/#.#/#.#/.#.',
           'W': '#...#/#...#/#.#.#/##.##/#...#', 'Y': '#.#/#.#/.#./.#./.#.', 'Z': '###/..#/.#./#../###'})
_ACC = {'Й': '.##.', 'Ё': '#.#'}
FONT = {}
for ch, s in _G.items():
    rows = s.split('/')
    FONT[ch] = rows

def draw_text(img, x, y, s, col=(40, 42, 46, 255), scale=1, sp=1):
    """y = top of the cap height (accent row drawn above)."""
    cx = x
    for ch in s:
        key = ch if ch in FONT else ch.upper()
        g = FONT.get(key, FONT['?'])
        for ry, row in enumerate(g):
            for rx, v in enumerate(row):
                if v == '#':
                    img.rect(cx + rx * scale, y + ry * scale, scale, scale, col)
        acc = _ACC.get(key)
        if acc:
            for rx, v in enumerate(acc):
                if v == '#':
                    img.rect(cx + rx * scale, y - 2 * scale, scale, scale, col)
        cx += (len(g[0]) + sp) * scale
    return cx

def text_width(s, scale=1, sp=1):
    w = 0
    for ch in s:
        key = ch if ch in FONT else ch.upper()
        g = FONT.get(key, FONT['?'])
        w += (len(g[0]) + sp) * scale
    return max(0, w - sp * scale)

def draw_text_c(img, cx, y, s, col=(40, 42, 46, 255), scale=1, maxw=None):
    """centred; drops to scale 1 if it does not fit maxw"""
    sc = scale
    while maxw and sc > 1 and text_width(s, sc) > maxw:
        sc -= 1
    draw_text(img, cx - text_width(s, sc) // 2, y, s, col, sc)
    return sc

# sheet colours
BG = (218, 219, 221, 255)       # light gray sheet background
BG2 = (206, 207, 210, 255)      # panel
INK = (38, 40, 44, 255)
INK2 = (104, 108, 116, 255)

def upscale_nn(img, k):
    return img.scaled(k)

# ---------------------------------------------------------------- deterministic noise (for terrain/fog studies)
def h2(ix, iy, s):
    h = (ix * 374761393 + iy * 668265263 + s * 1442695041) & 0xffffffff
    h = ((h ^ (h >> 13)) * 1274126177) & 0xffffffff
    h ^= h >> 16
    return (h & 0xffffffff) / 4294967296.0

def vnoise(x, y, period, s):
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    x0 = ix % period; x1 = (x0 + 1) % period
    y0 = iy % period; y1 = (y0 + 1) % period
    a, b = h2(x0, y0, s), h2(x1, y0, s)
    c, d = h2(x0, y1, s), h2(x1, y1, s)
    return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy

def fbm(x, y, size, k, octaves, s):
    """tileable fbm over a size x size square, k cells across"""
    u, v, amp, tot, nm, p = x / size * k, y / size * k, 0.5, 0.0, 0.0, k
    for o in range(octaves):
        tot += amp * vnoise(u, v, p, s + o * 101)
        nm += amp
        amp *= 0.5; u *= 2; v *= 2; p *= 2
    return tot / nm

BAYER4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5]
def bayer(x, y):
    return (BAYER4[((y & 3) << 2) | (x & 3)] + 0.5) / 16.0
