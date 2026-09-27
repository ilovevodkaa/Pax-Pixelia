# -*- coding: utf-8 -*-
"""Procedural pixel flags (18x12) and coats of arms (16x16) — what the nation creator can compose.
A flag = division pattern + 2-3 tinctures + optional charge at a position. Heraldic rule of tincture:
a charge is always metal on colour or colour on metal, so it stays readable at 1px scale."""
import math
import pp

T = {  # tinctures
    'or': (232, 194, 88), 'argent': (240, 237, 228), 'gules': (182, 58, 48), 'azure': (48, 86, 160),
    'vert': (58, 122, 70), 'sable': (38, 34, 38), 'purpure': (112, 66, 136), 'tenne': (204, 116, 46),
    'celeste': (104, 156, 206),
}
METALS = {'or', 'argent'}

CH = {
    'sun': ["#..#..#", ".#.#.#.", "..###..", "#######", "..###..", ".#.#.#.", "#..#..#"],
    'star': ["..#..", "#####", ".###.", ".#.#.", "#...#"],
    'crescent': [".###.", "##...", "#....", "##...", ".###."],
    'tree': ["..#..", ".###.", "#####", ".###.", "#####", "..#..", "..#.."],
    'tower': ["#.#.#", "#####", ".###.", ".#.#.", ".###.", "#####"],
    'eye': ["..###..", ".#...#.", "#..#..#", ".#...#.", "..###.."],
    'gear': [".#.#.", "#####", "##.##", "#####", ".#.#."],
    'mount': ["...#...", "..###..", ".#####.", "#######"],
    'bird': ["#..#..#", "##.#.##", ".#####.", "..###..", "..#.#.."],
    'wheat': ["#.#.#", ".###.", "..#..", "..#..", "..#.."],
    'swords': ["#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#"],
    'key': [".##....", "#..####", ".##.#.#"],
}

def field(kind, w, h, a, b, c):
    """returns a function (x,y)->tincture name for the division pattern"""
    if kind == 'solid':
        return lambda x, y: a
    if kind == 'bi_h':
        return lambda x, y: a if y < h // 2 else b
    if kind == 'bi_v':
        return lambda x, y: a if x < w // 2 else b
    if kind == 'tri_v':
        return lambda x, y: a if x < w // 3 else b if x < 2 * w // 3 else c
    if kind == 'tri_h':
        return lambda x, y: a if y < h // 3 else b if y < 2 * h // 3 else c
    if kind == 'nordic':
        cx, cy = 6, h // 2
        return lambda x, y: (c if abs(x - cx + 0.5) < 1 or abs(y - cy + 0.5) < 1 else b) if (abs(x - cx + 0.5) < 2 or abs(y - cy + 0.5) < 2) else a
    if kind == 'saltire':
        return lambda x, y: b if abs(x * h / w - y) < 1.4 or abs(x * h / w - (h - 1 - y)) < 1.4 else a
    if kind == 'bend':
        return lambda x, y: a if x * h / w + y < h else b
    if kind == 'hoist':
        return lambda x, y: c if x < (h / 2 - abs(y - h / 2 + 0.5)) * 1.1 else (a if y < h // 2 else b)
    if kind == 'canton':
        return lambda x, y: c if (x < 8 and y < 6) else (a if (y // 2) % 2 == 0 else b)
    if kind == 'bordure':
        return lambda x, y: b if (x < 2 or y < 2 or x >= w - 2 or y >= h - 2) else a
    if kind == 'quarterly':
        return lambda x, y: a if ((x < w // 2) == (y < h // 2)) else b
    if kind == 'fess':
        return lambda x, y: b if h // 3 <= y < 2 * h // 3 else a
    if kind == 'serrated':
        return lambda x, y: b if x < 5 + (1 if (y % 4) in (1, 2) else 0) - (1 if (y % 4) == 0 else 0) else a
    if kind == 'pall':
        return lambda x, y: c if (abs(y - h / 2 + 0.5) < 1.2 and x >= w / 2 - 2) or abs(x * 0.9 - (h / 2 - abs(y - h / 2 + 0.5))) < 1.3 and x < w / 2 else (a if y < h // 2 else b)
    raise KeyError(kind)

FLAGS = [
    # division, tinctures (a, b, c), charge, (cx, cy) centre of charge, charge tincture, caption
    ('tri_v', ('NAT', 'argent', 'or'), None, None, None, 'ТРИКОЛОР'),
    ('bi_h', ('argent', 'NAT', None), 'sun', (4, 3), 'or', 'ДВЕ ПОЛОСЫ + СОЛНЦЕ'),
    ('nordic', ('NAT', 'argent', 'or'), None, None, None, 'СЕВЕРНЫЙ КРЕСТ'),
    ('canton', ('NAT', 'argent', 'azure'), 'star', (4, 3), 'argent', 'КАНТОН + ПОЛОСЫ'),
    ('hoist', ('NAT', 'sable', 'argent'), 'star', (2, 6), 'gules', 'КЛИН У ДРЕВКА'),
    ('saltire', ('NAT', 'argent', None), None, None, None, 'АНДРЕЕВСКИЙ КРЕСТ'),
    ('bend', ('or', 'NAT', None), 'tree', (5, 4), 'vert', 'ПО ДИАГОНАЛИ + ДРЕВО'),
    ('solid', ('NAT', None, None), 'crescent', (9, 6), 'argent', 'ПОЛУМЕСЯЦ'),
    ('bordure', ('argent', 'NAT', None), 'tower', (9, 6), 'NAT', 'КАЙМА + БАШНЯ'),
    ('quarterly', ('NAT', 'argent', None), None, None, None, 'ЧЕТВЕРТИ'),
    ('fess', ('NAT', 'or', None), 'eye', (9, 6), 'sable', 'ПОЯС + ОКО'),
    ('serrated', ('NAT', 'argent', None), 'gear', (12, 6), 'argent', 'ЗУБЧАТЫЙ КРАЙ + ШЕСТЕРНЯ'),
]

def tinct(name, nat):
    if name == 'NAT':
        return tuple(nat)
    return T[name]

def stamp(img, mask, cx, cy, col, fn=None):
    h = len(mask); w = len(mask[0])
    x0, y0 = int(round(cx - w / 2)), int(round(cy - h / 2))
    for y, row in enumerate(mask):
        for x, v in enumerate(row):
            if v == '#':
                img.setraw(x0 + x, y0 + y, col + (255,) if len(col) == 3 else col)

def flag(spec, nat, w=18, h=12, wave=None):
    kind, (a, b, c), ch, pos, cht, cap = spec
    f = field(kind, w, h, a, b, c)
    img = pp.Img(w, h)
    for y in range(h):
        for x in range(w):
            col = tinct(f(x, y), nat)
            img.setraw(x, y, col + (255,))
    if ch:
        cc = tinct(cht, nat)
        # rule of tincture: make sure charge contrasts with what is under it
        under = tinct(f(*pos), nat)
        if sum(abs(cc[i] - under[i]) for i in range(3)) < 120:
            cc = T['argent'] if sum(under) < 420 else T['sable']
        stamp(img, CH[ch], pos[0] + 0.5, pos[1] + 0.5, cc)
    if wave is not None:
        # cloth folds: a travelling sine darkens/brightens columns in 2 posterised steps, 1px vertical sag
        out = pp.Img(w, h + 2)
        for x in range(w):
            s = math.sin(x * 0.55 - wave * 1.6)
            dy = int(round((s + 1) * 0.5 * (x / w) * 2))
            k = 1.10 if s > 0.55 else 0.86 if s < -0.55 else 1.0
            for y in range(h):
                out.setraw(x, y + dy, pp.mul(img.get(x, y), k))
        return out
    return img

def outlined(img, col=pp.K):
    o = pp.Img(img.w + 2, img.h + 2)
    for y in range(o.h):
        for x in range(o.w):
            inside = img.get(x - 1, y - 1)[3] > 0
            if inside:
                o.setraw(x, y, img.get(x - 1, y - 1))
            else:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if img.get(x - 1 + dx, y - 1 + dy)[3] > 0:
                        o.setraw(x, y, col)
                        break
    return o

# ---------------------------------------------------------------- coats of arms
def shield_mask(shape, w=14, h=15):
    def inside(x, y):
        u = (x + 0.5) / w * 2 - 1      # -1..1
        v = (y + 0.5) / h               # 0..1
        if shape == 'heater':
            if v < 0.5:
                return True
            t = (v - 0.5) / 0.5
            return abs(u) < (1 - t ** 1.7) * 1.0 + 0.02
        if shape == 'french':
            if v < 0.78:
                return True
            t = (v - 0.78) / 0.22
            return abs(u) < 1 - t * 0.85 or (abs(u) < 0.2)
        if shape == 'iberian':
            if v < 0.5:
                return True
            t = (v - 0.5) / 0.5
            return u * u + t * t < 1.0
        if shape == 'round':
            return u * u + ((v - 0.5) * 2) ** 2 < 1.0
        if shape == 'kite':
            if v < 0.14:
                return u * u * 0.8 + ((0.14 - v) / 0.2) ** 2 < 0.8
            return abs(u) < 1 - ((v - 0.14) / 0.86) ** 1.6
        if shape == 'tournament':
            if v < 0.12:
                return abs(u) < 0.9 and not (0.15 < u < 0.55 and v < 0.1)
            if v < 0.62:
                return abs(u) < 1.0 - (0.1 if u > 0.4 and 0.2 < v < 0.34 else 0)
            t = (v - 0.62) / 0.38
            return abs(u) < math.cos(t * math.pi / 2)
        return True
    return [[inside(x, y) for x in range(w)] for y in range(h)]

COA = [
    ('heater', 'bi_v', ('NAT', 'or'), 'tower', 'argent', 'СКОШЕННЫЙ ЩИТ'),
    ('french', 'chief', ('azure', 'NAT'), 'star', 'or', 'ФРАНЦУЗСКИЙ'),
    ('iberian', 'bend', ('NAT', 'argent'), 'bird', 'sable', 'ИСПАНСКИЙ'),
    ('round', 'plain', ('NAT', None), 'sun', 'or', 'КРУГЛЫЙ'),
    ('kite', 'bi_h', ('argent', 'NAT'), 'mount', 'sable', 'НОРМАНДСКИЙ'),
    ('tournament', 'plain', ('NAT', None), 'key', 'or', 'ТУРНИРНЫЙ'),
]

def coat(spec, nat):
    shape, div, (a, b), ch, cht, cap = spec
    w, h = 14, 15
    m = shield_mask(shape, w, h)
    img = pp.Img(16, 16)
    A = tinct(a, nat); B = tinct(b, nat) if b else A
    def col(x, y):
        if div == 'bi_v':
            return A if x < w // 2 else B
        if div == 'bi_h':
            return A if y < h // 2 else B
        if div == 'bend':
            return A if x - y * w / h > -2 else B
        if div == 'quarterly':
            return A if ((x < w // 2) == (y < h // 2)) else B
        if div == 'chief':
            return A if y < 5 else B
        return A
    for y in range(h):
        for x in range(w):
            if m[y][x]:
                img.setraw(x + 1, y, col(x, y) + (255,))
    # gold rim: inner edge pixels
    rim = []
    for y in range(h):
        for x in range(w):
            if not m[y][x]:
                continue
            edge = any(not (0 <= x + dx < w and 0 <= y + dy < h and m[y + dy][x + dx]) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if edge:
                rim.append((x, y))
    # lighting: top-left rim bright gold, bottom-right rim dark gold
    for x, y in rim:
        lit = (x < w / 2 and y < h * 0.7) or y == 0
        img.setraw(x + 1, y, (246, 214, 110, 255) if lit else (168, 120, 44, 255))
    # interior shading: 1px inside the rim on the bottom-right gets a shade
    for y in range(h):
        for x in range(w):
            if m[y][x] and (x, y) not in rim:
                nb_rim = ((x + 1, y) in rim and x >= w / 2) or ((x, y + 1) in rim and y > h * 0.5)
                if nb_rim:
                    img.setraw(x + 1, y, pp.mul(img.get(x + 1, y), 0.78))
    if ch:
        # charge colour contrasting with the centre
        stamp(img, CH[ch], 8, 7, T[cht])
    return pp.with_shadow(pp_outline(img))

def pp_outline(img):
    o = pp.Img(img.w, img.h)
    o.blit(img, 0, 0)
    for y in range(img.h):
        for x in range(img.w):
            if img.get(x, y)[3]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if img.get(x + dx, y + dy)[3]:
                    o.setraw(x, y, pp.K)
                    break
    return o

def pole_flag(spec, nat, wave):
    """map sprite: flag on a pole (like the scout's target flag), 1px dark outline"""
    f = flag(spec, nat, 9, 6, wave)
    img = pp.Img(12, 14)
    for y in range(14):
        img.setraw(1, y, (122, 86, 52, 255))
    img.setraw(1, 0, (232, 194, 88, 255))
    img.blit(f, 2, 1)
    return pp.with_shadow(pp_outline(img))

def build():
    K = 4
    top = 76
    fw, fh = 20 * K + 44, 14 * K + 34
    per = 6
    W = 24 + per * fw + 8
    H = top + 2 * fh + 30 + 18 * K + 60 + 16 * K + 60 + 14 * K + 50
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ФЛАГИ И ГЕРБЫ ИЗ КОНСТРУКТОРА', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'ФЛАГ 18×12 = ДЕЛЕНИЕ ПОЛЯ + 2-3 ЦВЕТА + ФИГУРА + ПОЗИЦИЯ. ПРАВИЛО: МЕТАЛЛ (ЗОЛОТО, СЕРЕБРО) НА ЦВЕТЕ ИЛИ ЦВЕТ НА МЕТАЛЛЕ.', pp.INK2, 1)
    pp.draw_text(img, 24, 54, 'ОСНОВНОЙ ЦВЕТ ФЛАГА = ЦВЕТ ДЕРЖАВЫ НА КАРТЕ. ГЕРБ 16×16: ФОРМА ЩИТА + ДЕЛЕНИЕ + ФИГУРА, ЗОЛОТОЙ КАНТ С БЛИКОМ СЛЕВА-СВЕРХУ.', pp.INK2, 1)
    nats = [pp.NATIONS[0], pp.NATIONS[1], pp.NATIONS[2], (140, 92, 172), (200, 142, 46), (58, 140, 182)]
    x1 = pp.Img(per * 20, 2 * 14 + 16 + 3 * 16)
    for i, spec in enumerate(FLAGS):
        nat = nats[i % len(nats)]
        f = outlined(flag(spec, nat))
        cx = 24 + (i % per) * fw
        cy = top + (i // per) * fh
        img.blit(pp.with_shadow(f).scaled(K), cx, cy)
        x1.blit(f, (i % per) * 20, (i // per) * 14)
        pp.draw_text(img, cx, cy + 14 * K + 6, spec[5], pp.INK, 1)
    y = top + 2 * fh + 4
    pp.draw_text(img, 24, y, 'ГЕРБЫ 16×16', pp.INK, 2)
    for i, spec in enumerate(COA):
        nat = nats[(i + 2) % len(nats)]
        c = coat(spec, nat)
        cx = 24 + i * fw
        img.blit(c.scaled(K), cx, y + 22)
        x1.blit(c, i * 17, 2 * 14 + 2)
        pp.draw_text(img, cx, y + 22 + 17 * K + 4, spec[5], pp.INK, 1)
    y2 = y + 22 + 17 * K + 24
    pp.draw_text(img, 24, y2, 'НА КАРТЕ: ФЛАГ НА ДРЕВКЕ, 4 КАДРА «ВЕТРА» (ТОЧКА НАЗНАЧЕНИЯ РАЗВЕДЧИКА, СТОЛИЦА, СОБЫТИЯ)', pp.INK, 1)
    for k in range(4):
        p = pole_flag(FLAGS[0], nats[0], k)
        img.blit(p.scaled(K), 24 + k * (13 * K + 16), y2 + 16)
        p2 = pole_flag(FLAGS[2], nats[1], k)
        img.blit(p2.scaled(K), 24 + 4 * (13 * K + 16) + 40 + k * (13 * K + 16), y2 + 16)
        x1.blit(p, k * 13, 2 * 14 + 20)
        x1.blit(p2, 60 + k * 13, 2 * 14 + 20)
    # constructor: how one flag is assembled, step by step
    xs = 24
    y2 = y2 + 16 + 15 * K + 18
    pp.draw_text(img, xs, y2, 'КОНСТРУКТОР НАЦИИ: ФЛАГ СОБИРАЕТСЯ ЗА 4 ШАГА (ПОЛЕ > ДЕЛЕНИЕ > ФИГУРА > ВЕТЕР НА КАРТЕ)', pp.INK, 1)
    nat = nats[0]
    steps = [('ПОЛЕ', ('solid', ('argent', None, None), None, None, None, '')),
             ('ДЕЛЕНИЕ', ('bi_h', ('argent', 'NAT', None), None, None, None, '')),
             ('ФИГУРА', FLAGS[1]),
             ('ВЕТЕР', None)]
    for k, (cap, spec) in enumerate(steps):
        f = flag(FLAGS[1], nat, wave=1) if spec is None else flag(spec, nat)
        o = outlined(f)
        img.blit(o.scaled(K), xs + k * 110, y2 + 16)
        pp.draw_text(img, xs + k * 110, y2 + 16 + o.h * K + 6, cap, pp.INK2, 1)
        if k < 3:
            pp.draw_text(img, xs + k * 110 + 90, y2 + 36, '>', pp.INK2, 2)
    img = img.crop(0, 0, img.w, min(img.h, y2 + 16 + 16 * K + 34))
    img.save('flags_coa.png')
    x1.save('flags_coa_x1.png')
    return img

if __name__ == '__main__':
    build()
