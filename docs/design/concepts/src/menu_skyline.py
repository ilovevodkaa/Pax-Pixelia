# -*- coding: utf-8 -*-
"""«Горизонт эпох» for the title screen (MAIN_MENU.md §3.1): 11 monochrome era silhouettes, left to right,
from a nomad camp to a floating disc city. Writes game/assets/front/skyline_eras.png (400×72, a DATA atlas,
not a picture — the colours are chosen by shaders/front/era_sky.gdshader).

Channel encoding (alpha is always 255 so the importer never touches RGB):
  R = plan × 64      0 empty · 1 far (#18181B) · 2 near (#0D0D0E) · 3 lit left face (#1D1D20)
  G = era × 20 + 10 if the pixel is a window (era 0..10)
  B = emitter id × 16
      1 campfire · 2 fire smoke · 3–5 chimneys · 6 cooling-tower steam (2–6 are single marker pixels on an
      empty cell: the shader draws the puffs) · 7 aviation light · 8 rocket flame · 9 rocket body · 10 clock face
Rows: y = 71 − fg, where fg is the height above the bottom screen row; fg 0..2 is the ground drawn by the shader,
so every building stands on fg = 3. The central third (x 133..266) stays at or below fg 49 (46 vp of building)
because the menu buttons sit there.

Run: python menu_skyline.py [out_png]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pp import write_png

W, H = 400, 72
GROUND = 3
EMPTY, FAR, NEAR, LIT = 0, 1, 2, 3
CENTERS = [18, 50, 84, 118, 152, 186, 220, 256, 292, 330, 372]

plan = [[EMPTY] * W for _ in range(H)]    # indexed [fg][x]
win = [[0] * W for _ in range(H)]
emit = [[0] * W for _ in range(H)]
era = [[0] * W for _ in range(H)]
E = 0   # era of the primitives being drawn


def put(x, f, p=NEAR):
    x, f = int(round(x)), int(round(f))
    if 0 <= x < W and GROUND <= f < H:
        if p == FAR and plan[f][x] in (NEAR, LIT):
            return   # far plan never paints over the near one
        plan[f][x] = p
        era[f][x] = E


def rect(x0, x1, f0, f1, p=NEAR):
    for f in range(f0, f1 + 1):
        for x in range(x0, x1 + 1):
            put(x, f, p)


def clear(x0, x1, f0, f1):
    for f in range(f0, f1 + 1):
        for x in range(x0, x1 + 1):
            plan[f][x] = EMPTY; win[f][x] = 0; emit[f][x] = 0


def tri(cx, f0, half, h, p=NEAR):
    """Symmetric triangle: base half-width `half` at row f0, apex h rows higher."""
    for i in range(h):
        w = half * (1 - i / h)
        rect(int(math.floor(cx - w + .5)), int(math.ceil(cx + w - .5)), f0 + i, f0 + i, p)


def dome(cx, f0, r, p=NEAR):
    for i in range(r + 1):
        w = math.sqrt(max(0, r * r - i * i))
        rect(int(round(cx - w)), int(round(cx + w)), f0 + i, f0 + i, p)


def windows(x0, x1, f0, f1, dx=2, df=3, phase=0):
    for f in range(f0, f1 + 1, df):
        for x in range(x0 + phase, x1 + 1, dx):
            if plan[f][x] in (NEAR, LIT):
                win[f][x] = 1


def mark(x, f, eid, p=None):
    emit[f][x] = eid
    era[f][x] = E
    if p is not None:
        plan[f][x] = p


def crenels(x0, x1, f, p=NEAR):
    for x in range(x0, x1 + 1):
        if (x - x0) % 3 != 2:
            put(x, f, p)


def pine(cx, f0, h, p=FAR):
    tri(cx, f0 + 1, max(2, h // 3), h, p)
    put(cx, f0, p)


def house(x0, x1, h, roof=2, p=FAR):
    rect(x0, x1, GROUND, GROUND + h - 1, p)
    tri((x0 + x1) / 2, GROUND + h, (x1 - x0) / 2 + 1, roof, p)


def build():
    global E
    g = GROUND
    # ---- E0 · стоянка (camp): two tents, a campfire and crossed poles ------------------------------
    E = 0
    pine(4, g, 13); pine(9, g, 9); pine(32, g, 8)
    tri(14, g, 6, 9)
    put(12, g + 9); put(16, g + 9); put(11, g + 10); put(17, g + 10)   # crossed poles
    tri(27, g, 4.5, 6)
    rect(13, 15, g, g + 2, EMPTY); win[g][14] = 1; plan[g][14] = NEAR    # dark door with a warm glint
    put(19, g); put(22, g)                                                # hearth stones
    for x, f in ((20, g), (21, g), (20, g + 1), (21, g + 1), (20, g + 2)):
        put(x, f); mark(x, f, 1)
    mark(20, g + 4, 2)
    # ---- E1 · зиккурат ------------------------------------------------------------------------------
    E = 1
    house(35, 38, 4); house(62, 66, 5)
    for i, (x0, x1) in enumerate(((36, 64), (39, 61), (42, 58), (45, 55))):
        rect(x0, x1, g + i * 4, g + i * 4 + 3)
    rect(47, 53, g + 16, g + 19)
    rect(48, 52, g + 20, g + 20)
    for f in range(g, g + 16):
        if f % 2 == 0:
            for x in (49, 50, 51):
                put(x, f, LIT)                                           # the great stair
    win[g + 17][50] = win[g + 18][50] = 1
    for x, f in ((37, g + 4), (63, g + 4), (40, g + 8), (60, g + 8)):
        put(x, f); win[f][x] = 1                                         # torches on the terraces
    # ---- E2 · храм (temple) -------------------------------------------------------------------------
    E = 2
    pine(71, g, 12); house(99, 101, 4)
    rect(69, 99, g, g + 1); rect(70, 98, g + 2, g + 2)
    for x in range(71, 98):
        col = (x - 71) % 4 in (0, 1)
        rect(x, x, g + 3, g + 11, NEAR if col else FAR)
    for f in range(g + 3, g + 8):
        for x in (83, 84, 85):
            if plan[f][x] == FAR:
                plan[f][x] = NEAR; win[f][x] = 1                         # lit doorway of the cella
    rect(70, 98, g + 12, g + 13)
    tri(84, g + 14, 14.5, 5)
    put(69, g + 14); put(99, g + 14); put(84, g + 19)
    # ---- E3 · замок (castle) ------------------------------------------------------------------------
    E = 3
    rect(104, 132, g, g + 11); crenels(104, 132, g + 12)
    rect(100, 106, g, g + 17); crenels(100, 106, g + 18)
    rect(130, 136, g, g + 17); crenels(130, 136, g + 18)
    rect(112, 124, g, g + 22); crenels(112, 124, g + 23)
    rect(115, 121, g + 23, g + 23)
    tri(118, g + 24, 4.5, 6)
    rect(118, 118, g + 30, g + 32); rect(119, 121, g + 31, g + 32, FAR)   # banner
    clear(116, 120, g, g + 4); rect(117, 119, g + 5, g + 5, EMPTY)
    for x in range(116, 121):
        for f in range(g, g + 4):
            plan[f][x] = NEAR; win[f][x] = 1 if (x + f) % 2 == 0 else 0  # gate with firelight
    for x in (103, 133):
        for f in (g + 8, g + 13):
            win[f][x] = win[f + 1][x] = 1                                 # arrow slits
    for x in (115, 121):
        win[g + 15][x] = win[g + 16][x] = win[g + 19][x] = win[g + 20][x] = 1
    # ---- E4 · купол (cathedral + campanile) ---------------------------------------------------------
    E = 4
    rect(139, 165, g, g + 13); rect(138, 166, g + 14, g + 14)
    tri(152, g + 15, 13.5, 3)
    rect(145, 159, g + 15, g + 20)
    dome(152, g + 21, 8)
    rect(151, 153, g + 30, g + 32); put(152, g + 33); put(152, g + 34); put(151, g + 34); put(153, g + 34); put(152, g + 35)
    rect(168, 172, g, g + 30); tri(170, g + 31, 3, 5)
    windows(141, 163, g + 3, g + 9, 4, 3, 1)
    for f in range(g + 3, g + 7):
        win[f][152] = 1
    windows(147, 157, g + 17, g + 18, 3, 1)
    for f in (g + 24, g + 25, g + 18, g + 19, g + 9, g + 10):
        win[f][170] = 1
    # ---- E5 · часы (clock tower + parliament) -------------------------------------------------------
    E = 5
    rect(175, 181, g, g + 11); rect(191, 203, g, g + 11)
    for x in range(175, 204, 4):
        if not 182 <= x <= 190:
            put(x, g + 12); put(x, g + 13); put(x, g + 14)                # pinnacles
    windows(175, 181, g + 2, g + 9, 2, 3, 1); windows(191, 203, g + 2, g + 9, 2, 3, 1)
    rect(182, 190, g, g + 29)
    rect(181, 191, g + 30, g + 30)
    rect(183, 189, g + 31, g + 33)
    tri(186, g + 34, 3.5, 7)
    put(186, g + 41)
    for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1), (2, 0), (-2, 0), (0, 2), (0, -2)):
        mark(186 + dx, g + 25 + dy, 10)                                   # clock face
    for dx, dy in ((0, 0), (0, 1), (1, 0)):
        emit[g + 25 + dy][186 + dx] = 0                                   # the hands stay dark
    windows(184, 188, g + 3, g + 20, 2, 4, 0)
    # ---- E6 · фабрика (factory, three chimneys) -----------------------------------------------------
    E = 6
    rect(205, 237, g, g + 11)
    for s in range(205, 237, 6):
        for x in range(s, min(s + 6, 238)):
            h = int((x - s) * 4 / 5)
            rect(x, x, g + 12, g + 12 + h)
        for f in range(g + 13, g + 16):
            if s + 5 <= 237:
                win[f][s + 5] = 0
    windows(206, 236, g + 2, g + 8, 3, 3, 1)
    for (x0, top, eid) in ((208, 48, 3), (214, 42, 4), (228, 36, 5)):
        rect(x0, x0 + 2, g, top)
        rect(x0 - 1, x0 + 3, top - 1, top)                                 # chimney cap
        mark(x0 + 1, top + 2, eid)
    # ---- E7 · градирня (cooling tower, reactor dome, slab) ------------------------------------------
    E = 7
    for f in range(g, g + 36):
        k = f - g
        w = 6 + 6.5 * ((k - 25) / 25) ** 2 if k < 25 else 6 + (k - 25) * .16
        rect(int(round(249 - w)), int(round(249 + w)), f, f)
    mark(249, g + 38, 6)
    rect(258, 268, g, g + 6); dome(263, g + 7, 5)
    rect(271, 279, g, g + 43)
    rect(273, 277, g + 44, g + 44)
    windows(272, 278, g + 2, g + 41, 2, 3, 0)
    # ---- E8 · башни (skyscrapers) -------------------------------------------------------------------
    E = 8
    rect(283, 291, g, g + 52); rect(284, 290, g + 53, g + 55); rect(285, 289, g + 56, g + 57)
    rect(287, 287, g + 58, g + 62); mark(287, g + 63, 7, NEAR)
    rect(293, 300, g, g + 44); rect(294, 299, g + 45, g + 46); mark(296, g + 47, 7, NEAR); put(297, g + 47)
    for f in range(g, g + 34):
        rect(302, 308 - max(0, f - (g + 28)), f, f)
    windows(284, 290, g + 2, g + 54, 2, 3, 0)
    windows(294, 299, g + 2, g + 43, 2, 3, 1)
    windows(303, 307, g + 2, g + 31, 2, 3, 0)
    # ---- E9 · ракета (launch pad, gantry, rocket) ---------------------------------------------------
    E = 9
    rect(313, 318, g, g + 5); dome(315, g + 6, 2)                          # control bunker
    win[g + 2][315] = win[g + 2][317] = 1
    rect(320, 346, g, g + 3)
    clear(328, 332, g, g + 3)
    for x in range(328, 333):
        for f in range(g, g + 4):
            mark(x, f, 8)                                                 # flame trench (hidden until launch)
    for f in range(g + 4, g + 58):
        for x in range(328, 333):
            put(x, f); mark(x, f, 9)
    for f, (x0, x1) in zip(range(g + 58, g + 66), ((328, 332),) * 3 + ((329, 331),) * 3 + ((330, 330),) * 2):
        for x in range(x0, x1 + 1):
            put(x, f); mark(x, f, 9)
    for i in range(6):                                                    # fins
        for x in (326 + i // 3, 334 - i // 3):
            put(x, g + 4 + i); mark(x, g + 4 + i, 9)
    for f in (g + 20, g + 21, g + 40, g + 41):
        for x in range(328, 333):
            if plan[f][x] == NEAR:
                win[f][x] = 0
    # gantry: two rails with X bracing and service arms
    for f in range(g + 4, g + 62):
        put(337, f); put(341, f)
        k = (f - g - 4) % 8
        put(337 + (k if k <= 4 else 8 - k), f)
    for f in (g + 30, g + 46, g + 58):
        rect(333, 336, f, f)
    rect(336, 342, g + 62, g + 62); mark(339, g + 63, 7, NEAR)
    # ---- E10 · диск (floating city) -----------------------------------------------------------------
    E = 10
    for f in range(g + 24, g + 31):
        k = f - (g + 27)
        w = 18 * math.sqrt(max(0, 1 - (k / 3.6) ** 2))
        rect(int(round(372 - w)), int(round(372 + w)), f, f)
    for x in range(357, 388, 3):
        win[g + 24][x] = 1 if plan[g + 24][x] else 0
        win[g + 25][x + 1] = 1 if plan[g + 25][x + 1] else 0
    rect(365, 367, g + 31, g + 44); rect(371, 374, g + 31, g + 49); rect(378, 380, g + 31, g + 41)
    dome(361, g + 31, 3); dome(384, g + 31, 2)
    rect(372, 373, g + 50, g + 51); mark(372, g + 52, 7, NEAR)
    windows(365, 367, g + 33, g + 43, 2, 3, 1); windows(371, 374, g + 33, g + 48, 2, 3, 0); windows(378, 380, g + 33, g + 40, 2, 3, 1)
    # ---- far-plan fillers between the eras ----------------------------------------------------------
    E = 5
    house(160, 166, 6, 2); E = 3; house(97, 101, 5, 2); house(137, 140, 7, 3)
    E = 6; rect(238, 241, g, g + 9, FAR); E = 7; rect(266, 270, g, g + 14, FAR)
    E = 8; rect(309, 312, g, g + 18, FAR); rect(279, 282, g, g + 24, FAR)
    E = 9; rect(343, 350, g, g + 8, FAR)
    E = 10; rect(390, 396, g, g + 7, FAR)


def lit_faces():
    """Light from the north-west: a near pixel with nothing solid to its left gets the lit tone."""
    for f in range(H):
        for x in range(W):
            if plan[f][x] == NEAR and (x == 0 or plan[f][x - 1] in (EMPTY, FAR)) and not win[f][x]:
                plan[f][x] = LIT


def encode():
    px = bytearray(W * H * 4)
    for f in range(H):
        y = H - 1 - f
        for x in range(W):
            i = (y * W + x) * 4
            px[i] = plan[f][x] * 64
            px[i + 1] = era[f][x] * 20 + (10 if win[f][x] else 0)
            px[i + 2] = emit[f][x] * 16
            px[i + 3] = 255
    return px


def check():
    for f in range(50, H):
        for x in range(133, 267):
            assert plan[f][x] == EMPTY, ('central third too tall', x, f)


if __name__ == '__main__':
    build()
    lit_faces()
    check()
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(here, '../../../../game/assets/front/skyline_eras.png'))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    write_png(out, W, H, encode())
    print('skyline atlas ->', out)
