# -*- coding: utf-8 -*-
import pp, terrain
from building_sprites import BUILDINGS, padded
from city_sprites import ERAS

HOME = ['plains', 'forest', 'mount', 'shallow', 'meadow', 'meadow', 'plains', 'steppe', 'mount', 'plains', 'shallow', 'meadow']

def province_closeup(pal, z=5, w=56, h=30, seed=404):
    """a province close-up at zoom z: city (sprite x3) + building icons (x2) on their plots"""
    t = terrain.patch('meadow', w, h, seed=seed)
    # a patch of forest + a strip of shore to host the sawmill and the pier
    f = terrain.patch('forest', w, h, seed=seed + 1)
    sea = terrain.patch('shallow', w, h, seed=seed + 2)
    for y in range(h):
        for x in range(w):
            n = pp.fbm(x, y, 64, 4, 2, seed + 5)
            if x > w - 9 + (n - 0.5) * 8:
                t.setraw(x, y, sea.get(x, y) if x > w - 7 + (n - 0.5) * 8 else (208, 196, 150, 255))
            elif x < 12 + (n - 0.5) * 10 and y < 16:
                t.setraw(x, y, f.get(x, y))
    P = terrain.provinces(w, h, seed + 3, 2)
    terrain.draw_borders(t, P)
    img = t.scaled(z)
    city = pp.with_shadow(pp.sprite(ERAS[1][2], pal)).scaled(3)
    img.blit(city, img.w // 2 - city.w // 2 - 20, img.h // 2 - city.h // 2 - 6)
    spots = [(0, 18, 21), (1, 9, 9), (3, 45, 5), (4, 31, 22), (5, 37, 3), (6, 17, 12), (7, 25, 24), (10, 46, 18), (2, 3, 22)]
    spots = [(k, x * z, y * z) for k, x, y in spots]
    for k, x, y in spots:
        s = pp.with_shadow(pp.sprite(padded(BUILDINGS[k][1]), pal)).scaled(2)
        img.blit(s, x, y)
    return img

def build():
    K = 4
    cw, chh = 150, 76
    top = 76
    n = len(BUILDINGS)
    per = 6
    W = 24 + per * cw + 16
    close = province_closeup(pp.nation_pal(pp.NATIONS[0]))
    H = top + 2 * (chh + 64) + close.h + 70
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ПОСТРОЙКИ В ПРОВИНЦИИ', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'ИКОНКА 8×8 + КОНТУР 1 PX = 10×10 · ×4 · СЛЕВА НА СВЕТЛОМ ФОНЕ, СПРАВА НА СВОЁМ БИОМЕ', pp.INK2, 1)
    pp.draw_text(img, 24, 54, 'ЦВЕТ ДЕРЖАВЫ ТОЛЬКО НА «ГОРОДСКИХ» ПОСТРОЙКАХ (РЫНОК, ПОРТ, УНИВЕРСИТЕТ), ОСТАЛЬНОЕ НЕЙТРАЛЬНО', pp.INK2, 1)
    pal = pp.nation_pal(pp.NATIONS[1])
    x1 = pp.Img(n * 10, 2 * 10)
    for i, (name, rows) in enumerate(BUILDINGS):
        cx = 24 + (i % per) * cw
        cy = top + (i // per) * (chh + 64)
        img.rect(cx - 6, cy - 6, cw - 8, chh + 44, pp.BG2)
        spr = pp.sprite(padded(rows), pal)
        x1.blit(spr, i * 10, 0)
        x1.blit(pp.sprite(padded(rows), pp.mask_pal()), i * 10, 10)
        s = pp.with_shadow(spr).scaled(K)
        img.blit(s, cx + 4, cy + 8)
        patch = terrain.patch(HOME[i], 14, 14, seed=31 + i).scaled(K)
        img.blit(patch, cx + 70, cy + 2)
        img.blit(s, cx + 70 + (patch.w - s.w) // 2 + 2, cy + 2 + (patch.h - s.h) // 2 + 2)
        pp.draw_text(img, cx + 2, cy + 70, str(i + 1), pp.INK2, 2)
        words = name.split(' ')
        pp.draw_text(img, cx + 22, cy + 70, words[0], pp.INK, 2 if pp.text_width(words[0], 2) < cw - 34 else 1)
        if len(words) > 1:
            pp.draw_text(img, cx + 22, cy + 86, ' '.join(words[1:]), pp.INK, 2 if pp.text_width(' '.join(words[1:]), 2) < cw - 34 else 1)
    y = top + 2 * (chh + 64) + 6
    pp.draw_text(img, 24, y, 'ПРОВИНЦИЯ ВБЛИЗИ · ЗУМ ×5 · ГОРОД ×3, ПОСТРОЙКИ ×2 НА СВОИХ УЧАСТКАХ', pp.INK, 2)
    img.blit(close, 24, y + 22)
    # notes next to the close-up
    nx = 24 + close.w + 24
    notes = ['ПОСТРОЙКИ ВИДНЫ С ЗУМА ×5.', 'КАЖДАЯ СТОИТ НА «СВОЁМ» РЕЛЬЕФЕ:', 'ЛЕСОПИЛКА У ЛЕСА, ПРИСТАНЬ И ПОРТ', 'У БЕРЕГА, КАМЕНОЛОМНЯ НА ХОЛМЕ.',
             '', 'ТЕНЬ = 1 ПИКСЕЛЬ СПРАЙТА ВПРАВО-ВНИЗ,', 'ЧЁРНЫЙ 45%.', '', 'НИЖНИЙ РЯД В ×1-АТЛАСЕ — МАСКА', 'ДЛЯ ЗАМЕНЫ ЦВЕТА ДЕРЖАВЫ.']
    for k, t in enumerate(notes):
        pp.draw_text(img, nx, y + 26 + k * 12, t, pp.INK2, 1)
    img.save('buildings.png')
    x1.save('buildings_x1.png')
    return img

if __name__ == '__main__':
    build()
