# -*- coding: utf-8 -*-
import pp, terrain
from city_sprites import ERAS

NAT_NAMES = ['АРДАНИЯ', 'КЕСАРАТ МИРРЫ', 'ТОРН']
CONTEXT_BIOMES = ['steppe', 'plains', 'savanna', 'meadow', 'meadow', 'plains', 'steppe', 'tundra', 'plains', 'desert', 'meadow']

def context_cell(rows, pal, biome, seed, z=3, ps=2, w=40, h=26, nat=None):
    """how it looks in game at zoom z: terrain map pixels are z screen px, sprite pixels are ps screen px"""
    t = terrain.patch(biome, w, h, seed=seed)
    P = terrain.provinces(w, h, seed + 3, 3)
    terrain.draw_borders(t, P)
    img = t.scaled(z)
    spr = pp.sprite(rows, pal)
    sh = pp.with_shadow(spr).scaled(ps)
    # drop shadow offset should be one sprite pixel: with_shadow already offsets by 1 sprite px
    img.blit(sh, (img.w - spr.w * ps) // 2, (img.h - spr.h * ps) // 2 - 2)
    return img

def build():
    K = 4
    lab_w = 176
    col_w = 132
    top = 78
    row_h = 16 * K + 26
    n = len(ERAS)
    W = 24 + lab_w + n * col_w + 16
    ctx_h = 26 * 3 + 30
    H = top + 30 + 4 * row_h + ctx_h + 40
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ГОРОД ПО ЭПОХАМ', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'СПРАЙТ 16×16 · ×4 · КОНТУР АВТОМАТИЧЕСКИЙ · СВЕТ СЛЕВА-СВЕРХУ · ЦВЕТ ДЕРЖАВЫ = 3 ОТТЕНКА (СВЕТ, ОСНОВА, ТЕНЬ)', pp.INK2, 1)
    pp.draw_text(img, 24, 54, 'ВЕРХНИЙ РЯД — МАСКА ДЛЯ ШЕЙДЕРА: ПУРПУРНЫЕ ПИКСЕЛИ ЗАМЕНЯЮТСЯ НА ЦВЕТ ДЕРЖАВЫ. НИЖНИЙ РЯД — КАК В ИГРЕ ПРИ ПРИБЛИЖЕНИИ ×3 (СПРАЙТ ×2).', pp.INK2, 1)
    x0 = 24 + lab_w
    # headers
    for i, (name, era, rows) in enumerate(ERAS):
        cx = x0 + i * col_w + col_w // 2
        pp.draw_text_c(img, cx, top, str(i + 1), pp.INK2, 2)
        pp.draw_text_c(img, cx, top + 16, name, pp.INK, 2, maxw=col_w - 6)
    pals = [('МАСКА', pp.mask_pal(), pp.MASK)] + [(NAT_NAMES[k], pp.nation_pal(c), pp.ramp(c)) for k, c in enumerate(pp.NATIONS)]
    x1 = pp.Img(n * 16, 4 * 16)
    y = top + 34
    for r, (label, pal, rmp) in enumerate(pals):
        img.rect(16, y - 4, W - 32, row_h - 4, pp.BG2 if r % 2 == 0 else pp.BG)
        pp.draw_text(img, 24, y + 14, label, pp.INK, 2 if pp.text_width(label, 2) <= lab_w - 12 else 1)
        for k, c in enumerate(rmp):
            img.rect(24 + k * 18, y + 34, 16, 16, c)
        for i, (name, era, rows) in enumerate(ERAS):
            spr = pp.sprite(rows, pal)
            x1.blit(spr, i * 16, r * 16)
            s = pp.with_shadow(spr).scaled(K)
            img.blit(s, x0 + i * col_w + (col_w - 16 * K) // 2, y)
        y += row_h
    # in-game context row (nation 0)
    pp.draw_text(img, 24, y + 14, 'НА КАРТЕ', pp.INK, 2)
    pp.draw_text(img, 24, y + 34, 'ЗУМ ×3, СПРАЙТ ×2', pp.INK2, 1)
    pal = pp.nation_pal(pp.NATIONS[0])
    for i, (name, era, rows) in enumerate(ERAS):
        c = context_cell(rows, pal, CONTEXT_BIOMES[i], seed=101 + i * 13)
        img.blit(c, x0 + i * col_w + (col_w - c.w) // 2, y + 4)
    img.save('city_eras.png')
    x1.save('city_eras_x1.png')
    return img

if __name__ == '__main__':
    build()
