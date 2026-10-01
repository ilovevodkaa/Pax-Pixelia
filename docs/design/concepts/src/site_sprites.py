# -*- coding: utf-8 -*-
"""Construction sites on the map: one scaffold per era, 4 frames each (ART_BIBLE §10.2 «леса и молоток»).
Grid 10x12 (+1px automatic outline). The building rises inside it: its 8x8 icon sits at columns 1-8, rows 4-11
(bottom-aligned, the engine draws the icon's bottom rows by progress first, then the scaffold on top).
Every site has a worker in the nation's colours (A/N/D), a lift that moves and the era's materials."""

W, H = 10, 12


def blank():
    return [['.'] * W for _ in range(H)]


def put(g, x, y, ch):
    if ch != '.' and 0 <= x < W and 0 <= y < H:
        g[y][x] = ch


def stamp(g, rows, x, y):
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            put(g, x + i, y + j, ch)


def vline(g, x, y0, y1, ch):
    for y in range(y0, y1 + 1):
        put(g, x, y, ch)


def hline(g, y, x0, x1, ch):
    for x in range(x0, x1 + 1):
        put(g, x, y, ch)


def rows(g):
    return [''.join(r) for r in g]


# a worker hammering to the right (4x5): raised, swing, strike, back. `h` is the hammer head.
def hammerer(h, f):
    return [
        ["..%s." % h, ".Fk.", ".k..", "NND.", "L.L."],
        ["....", ".F.%s" % h, ".kk.", "NND.", "L.L."],
        ["....", ".F..", ".kk.", "NNDk", "L.L%s" % h],
        ["....", ".F.%s" % h, ".kk.", "NND.", ".LL."],
    ][f]


# a worker carrying something on the head, stepping in place (3x4)
def carrier(load, f):
    legs = ["L.L", ".L.", "L.L", ".L."][f]
    return [".%s." % load, ".k.", "NND", legs]


def e0(f):
    """Первобытная: an A-frame of branches lashed with hide, a bundle of reeds hauled on a rope, a stone hammer."""
    g = blank()
    stamp(g, ["....dd....",
              "...dhhd...",
              "...W..W...",
              "..W....W..",
              "..W....W..",
              ".W......W.",
              ".W......W.",
              ".W......W.",
              "W........W",
              "W........W",
              "W........W",
              "JJ......JJ"], 0, 0)
    y = [7, 6, 5, 6][f]
    vline(g, 4, 2, y - 1, 'u')
    stamp(g, ["Tt", "uT"], 4, y)
    stamp(g, hammerer('4', f), 6, 7)
    if f == 2:
        put(g, 9, 10, 'Z')
    return rows(g)


def e1(f):
    """Древний мир: a mud-brick stack, a plank platform on poles, a ramp, a carrier with a brick on the head and a
    brick going up the ramp."""
    g = blank()
    hline(g, 4, 0, 9, 'w')
    vline(g, 0, 4, 11, 'W')
    vline(g, 9, 4, 11, 'W')
    for i, (x, y) in enumerate([(3, 11), (4, 10), (5, 9), (6, 8), (7, 7), (8, 6)]):
        put(g, x, y, 'd')
    stamp(g, ["hj", "ahj"], 0, 9)
    stamp(g, ["ajh"], 0, 11)
    stamp(g, carrier('b', f), [3, 4, 5, 4][f], 0)
    bx, by = [(4, 9), (5, 8), (6, 7), (7, 6)][f]
    put(g, bx, by, 'b')
    return rows(g)


def e2(f):
    """Античность: a treadwheel crane (the wheel turns) lifting a stone block, cut stone waiting."""
    g = blank()
    for x, y in [(1, 7), (2, 6), (3, 5), (4, 4), (5, 3), (6, 2), (7, 1)]:
        put(g, x, y, 'd')
    hline(g, 0, 6, 9, 'W')
    yb = [8, 7, 6, 5][f]
    vline(g, 8, 1, yb - 1, 'u')
    stamp(g, ["23", "34"], 7, yb)
    stamp(g, [".WW.", "W..W", "W..W", ".WW."], 0, 8)
    stamp(g, [["d.", ".d"], [".d", "d."], ["d.", ".d"], [".d", "d."]][f], 1, 9)
    stamp(g, ["12", "23"], 8, 10)
    put(g, 3 if f % 2 == 0 else 2, 7, 'k')   # the walker inside the wheel bobs
    return rows(g)


def e3(f):
    """Средневековье: timber scaffolding in two tiers with braces, a pulley and a bucket of mortar, a mason on top."""
    g = blank()
    vline(g, 0, 2, 11, 'W')
    vline(g, 9, 2, 11, 'W')
    hline(g, 2, 0, 3, 'W')
    put(g, 2, 3, 'n')
    hline(g, 7, 3, 9, 'w')
    for x, y in [(4, 11), (5, 10), (6, 9), (7, 8)]:
        put(g, x, y, 'd')
    yb = [9, 8, 7, 8][f]
    vline(g, 2, 4, yb - 1, 'u')
    stamp(g, ["WW", "dd"], 1, yb)
    stamp(g, hammerer('n', f), 5, 2)
    return rows(g)


def e4(f):
    """Возрождение: a tall derrick with a pulley wheel, a dressed block on the hook, plaster tubs, a mason."""
    g = blank()
    vline(g, 1, 0, 11, 'W')
    hline(g, 0, 1, 7, 'W')
    put(g, 2, 1, 'd')
    put(g, 3, 2, 'd')
    put(g, 7, 1, 'n')
    stamp(g, [["n.", ".n"], [".n", "n."], ["n.", ".n"], [".n", "n."]][f], 6, 1)
    yb = [7, 6, 5, 6][f]
    vline(g, 7, 3, yb - 1, 'u')
    stamp(g, ["pP", "PP"], 6, yb)
    stamp(g, ["pP", "WW"], 8, 10)
    stamp(g, hammerer('n', f), 2, 7)
    return rows(g)


def e5(f):
    """Эпоха пара: an iron frame and a steam crane at the foot that puffs, a girder on the chain."""
    g = blank()
    vline(g, 9, 1, 11, 'n')
    hline(g, 1, 3, 9, 'n')
    for x in (4, 6, 8):
        put(g, x, 2, 'M')
    stamp(g, ["MMn", "MnM", "nnn"], 0, 9)
    put(g, 1, 8, 'n')
    puffs = [["..", "Z."], [".z", "Z."], ["z.", ".z"], ["Z.", ".."]][f]
    stamp(g, puffs, 0, 6)
    put(g, 1, 7, 'n')
    yb = [8, 7, 6, 7][f]
    vline(g, 5, 2, yb - 1, 'n')
    stamp(g, ["MMMn"], 4, yb)
    return rows(g)


def e6(f):
    """Индустриальная: riveted steel girders, a jib crane with a hook, a riveter whose sparks fly."""
    g = blank()
    vline(g, 0, 0, 11, 'M')
    vline(g, 9, 3, 11, 'M')
    hline(g, 0, 0, 8, 'M')
    hline(g, 3, 4, 9, 'M')
    for x in (2, 4, 6):
        put(g, x, 0, 'n')
    put(g, 6, 3, 'n')
    put(g, 8, 3, 'n')
    yb = [8, 7, 6, 7][f]
    vline(g, 7, 1, yb - 1, 'n')
    stamp(g, ["nMn"], 6, yb)
    stamp(g, hammerer('n', f), 1, 7)
    if f == 2:
        stamp(g, ["y.", ".r"], 5, 9)
    return rows(g)


def e7(f):
    """Атомная: concrete formwork and a yellow tower crane, the skip of concrete swinging on its hook."""
    g = blank()
    vline(g, 8, 0, 11, 'Y')
    vline(g, 9, 1, 11, 'T')
    hline(g, 0, 0, 9, 'Y')
    put(g, 9, 0, 'T')
    stamp(g, ["88", "77"], 8, 10)
    x = [2, 3, 4, 3][f]
    vline(g, x, 1, 4, 'n')
    stamp(g, ["787", "888"], x - 1, 5)
    stamp(g, ["7", "8", "7"], 0, 9)
    return rows(g)


def e8(f):
    """Информационная: a tower crane with a red light blinking at the top, glass panels going up on the hook."""
    g = blank()
    vline(g, 8, 1, 11, 'Y')
    vline(g, 9, 2, 11, 'T')
    hline(g, 1, 0, 9, 'Y')
    put(g, 8, 0, 'l' if f < 2 else 'n')
    put(g, 0, 2, 'n')
    yb = [8, 6, 4, 6][f]
    vline(g, 3, 2, yb - 1, 'n')
    stamp(g, ["gG", "Gq"], 2, yb)
    stamp(g, hammerer('n', f), 4, 7)
    return rows(g)


def e9(f):
    """Космическая: a robot arm welding (cyan sparks) and a drone bringing a panel."""
    g = blank()
    stamp(g, ["nMn", ".M.", ".m."], 0, 9)
    arm = [[(1, 8), (2, 7), (3, 6), (4, 6)], [(1, 8), (2, 7), (3, 7), (4, 7)],
           [(1, 8), (2, 7), (3, 6), (4, 5)], [(1, 8), (2, 7), (3, 7), (4, 7)]][f]
    for x, y in arm:
        put(g, x, y, 'm')
    tx, ty = arm[-1]
    put(g, tx + 1, ty, 'C' if f % 2 == 0 else 'c')
    put(g, tx + 1, ty - 1, 'c' if f % 2 == 0 else '.')
    dx = [5, 6, 7, 6][f]
    stamp(g, ["m.m", "nMn", ".g."], dx, [0, 1, 0, 1][f])
    return rows(g)


def e10(f):
    """Будущее: a hologram wireframe of the building and a swarm of nanobots weaving it."""
    g = blank()
    for x in range(1, 9):
        put(g, x, 3, 'c')
    vline(g, 1, 3, 11, 'c')
    vline(g, 8, 3, 11, 'c')
    put(g, 4, 2, 'c'); put(g, 5, 2, 'c')
    put(g, 3, 1, 'C' if f % 2 else 'c'); put(g, 6, 1, 'c' if f % 2 else 'C')
    swarm = [[(2, 5), (6, 7), (4, 9), (7, 4)], [(3, 6), (7, 8), (5, 9), (6, 4)],
             [(4, 5), (6, 6), (3, 9), (5, 4)], [(3, 4), (5, 7), (6, 9), (4, 6)]][f]
    for x, y in swarm:
        put(g, x, y, 'C')
    put(g, 0, [11, 10, 9, 10][f], 'C')
    return rows(g)


ERA_SITES = [
    ('Первобытная', e0), ('Древний мир', e1), ('Античность', e2), ('Средневековье', e3), ('Возрождение', e4),
    ('Эпоха пара', e5), ('Индустриальная', e6), ('Атомная', e7), ('Информационная', e8), ('Космическая', e9),
    ('Будущее', e10),
]

SITES = [(name, [fn(f) for f in range(4)]) for name, fn in ERA_SITES]

for _n, _fr in SITES:
    for _rows in _fr:
        assert len(_rows) == H and all(len(r) == W for r in _rows), (_n, [len(r) for r in _rows])

# the dust of a finished building (no outline, no shadow): two frames, drawn for a moment over the new icon
PUFF = [
    ["..........",
     "..........",
     "..........",
     "...z..z...",
     ".zZz.zZz..",
     "zZZzzZZzz.",
     ".zZZZZZz..",
     "..zzZzz...",
     "..........",
     "..........",
     "..........",
     ".........."],
    ["..z....z..",
     ".z..z.z..z",
     "z........z",
     "..........",
     "z........z",
     "..........",
     "z........z",
     "..........",
     ".z......z.",
     "..........",
     "..........",
     ".........."],
]
