# -*- coding: utf-8 -*-
"""Tiny animated map units. Each unit = base grid + per-frame row overrides (walk cycles, flame flicker,
bobbing). All face right; the engine mirrors them for leftward moves."""

def frames(base, overrides, shift=None):
    out = []
    for k, ov in enumerate(overrides):
        g = list(base)
        for r, row in ov.items():
            g[r] = row
        if shift and shift[k]:
            dy = shift[k]
            w = len(g[0])
            g = ['.' * w] * dy + g[:-dy] if dy > 0 else g[-dy:] + ['.' * w] * (-dy)
        out.append(g)
    return out

# legs for 8-wide walkers (rows 8, 9): stride / pass / stride(other) / pass
LEGS = [
    {8: "..L.L...", 9: ".dd..dd."},
    {8: "..LL....", 9: "..ddd..."},
    {8: "..L.L...", 9: "..d.dd.."},
    {8: "..LL....", 9: "..ddd..."},
]

def with_legs(extra):
    return [{**LEGS[k], **extra[k]} for k in range(4)]

SCOUT_BASE = [
    "......r.",
    ".....rYr",
    "..FF..R.",
    ".FFkk.W.",
    "..kHk.W.",
    ".ANNNkk.",
    ".ANNND..",
    "..NND...",
    "..L.L...",
    ".dd..dd.",
]
SCOUT = frames(SCOUT_BASE, with_legs([
    {0: "......r.", 1: ".....rYr", 2: "..FF..R."},
    {0: ".......r", 1: ".....rYr", 2: "..FF.rR."},
    {0: ".....r..", 1: ".....YrR", 2: "..FF..R."},
    {0: "........", 1: "......r.", 2: "..FF.rYr"},
]))

SPEAR_BASE = [
    "......m.",
    "......W.",
    "..YY..W.",
    ".YYYk.W.",
    "..kHk.W.",
    ".ANNkkW.",
    "ANYND.W.",
    "ANNND.W.",
    "..L.L.W.",
    ".dd..dd.",
]
SPEARMAN = frames(SPEAR_BASE, with_legs([{}, {}, {}, {}]), shift=[0, 0, 0, 0])

MUSKET_BASE = [
    ".......n",
    "..KK..n.",
    ".KKKK.W.",
    "..kk.W..",
    "..kHW...",
    ".NpNkN..",
    ".NNpNN..",
    ".NNNpN..",
    "..L.L...",
    ".dd..dd.",
]
MUSKETEER = frames(MUSKET_BASE, with_legs([{}, {}, {}, {}]))

SOLDIER_BASE = [
    "........",
    "..VIV...",
    ".VIIIV..",
    "..kkk..n",
    ".NIkV.n.",
    "VNIIVkn.",
    "VVIIVn..",
    ".VVVW...",
    "..V.V...",
    ".nn..nn.",
]
SOLDIER = frames(SOLDIER_BASE, [
    {8: "..V.V...", 9: ".nn..nn."},
    {8: "..VV....", 9: "..nnn..."},
    {8: "..V.V...", 9: "..n.nn.."},
    {8: "..VV....", 9: "..nnn..."},
])

# pack mule with nation-cloth bales (12 x 10)
MULE_BASE = [
    "..........W.",
    ".........wWW",
    ".tttu...wWWd",
    ".ANND..wWW..",
    ".ANKD.wWW...",
    "dwwwwwwwWd..",
    ".wWWWWWWWd..",
    "..WWWWWWd...",
    "..W.W..W.W..",
    "..d.d..d.d..",
]
CARAVAN = frames(MULE_BASE, [
    {8: "..W.W..W.W..", 9: "..d.d..d.d.."},
    {8: "..WW...WW...", 9: "..dd...dd..."},
    {8: ".W..W.W..W..", 9: ".d..d.d..d.."},
    {8: "..WW...WW...", 9: "..dd...dd..."},
])

# trade cog, nation sail (12 x 11); bobs 1px and the bow foam flickers
SHIP_BASE = [
    ".....dNN....",
    ".....dN.....",
    "...ppppP....",
    "...AANND....",
    "...AANND....",
    "...ppppP....",
    "ww...d......",
    "wwwwwwwwwwwd",
    ".WWWWWWWWWd.",
    "..dddddddd..",
    "............",
]
SHIP = frames(SHIP_BASE, [
    {10: "..........zz", 0: ".....dNN...."},
    {10: ".z.......zZ.", 0: ".....dNNN..."},
    {10: "..........Zz", 0: ".....dNN...."},
    {10: "z.........z.", 0: ".....dN....."},
], shift=[0, 0, -1, 0])

# nomad settlers: adult with a bindle + a child (12 x 10)
NOMAD_BASE = [
    "..tT........",
    ".tANu.FF....",
    ".ANNDWFkk...",
    "..DD.WFkH...",
    "......WNNk..",
    "..F...ANND..",
    ".Fk...ANND..",
    ".AN....ND...",
    ".L.L...L.L..",
    "dd.d..dd.dd.",
]
NOMADS = frames(NOMAD_BASE, [
    {8: ".L.L...L.L..", 9: "dd.d..dd.dd."},
    {8: "..LL....LL..", 9: ".ddd...ddd.."},
    {8: ".L.L...L.L..", 9: ".d.dd..d.dd."},
    {8: "..LL....LL..", 9: ".ddd...ddd.."},
], shift=[0, 1, 0, 1])

# light tank (12 x 8), tracks roll
TANK_BASE = [
    "............",
    ".....VVVnnnn",
    "....VINVV...",
    "..VVIIIVVVV.",
    ".VIIIVVVVVVV",
    "nMnMnMnMnMn.",
    ".nnnnnnnnnn.",
    "............",
]
TANK = frames(TANK_BASE, [
    {5: "nMnMnMnMnMn."},
    {5: "MnMnMnMnMnM."},
    {5: "nMnMnMnMnMn."},
    {5: "MnMnMnMnMnM."},
], shift=[0, 0, 0, 0])

UNITS = [
    ('РАЗВЕДЧИК С ФАКЕЛОМ', 'MVP', SCOUT, 180),
    ('КАРАВАН', 'MVP', CARAVAN, 200),
    ('ТОРГОВЫЙ КОРАБЛЬ', 'MVP', SHIP, 320),
    ('ПОСЕЛЕНЦЫ-КОЧЕВНИКИ', 'MVP', NOMADS, 220),
    ('АРМИЯ: КОПЕЙЩИК', 'ЭТАП 2', SPEARMAN, 200),
    ('АРМИЯ: МУШКЕТЁР', 'ЭТАП 2', MUSKETEER, 200),
    ('АРМИЯ: СОЛДАТ XX В.', 'ЭТАП 2', SOLDIER, 180),
    ('АРМИЯ: ТАНК', 'ЭТАП 2', TANK, 120),
]

for _n, _m, _fr, _ms in UNITS:
    for _f in _fr:
        assert len(set(len(r) for r in _f)) == 1, (_n, [len(r) for r in _f])
