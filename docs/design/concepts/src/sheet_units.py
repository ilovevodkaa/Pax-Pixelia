# -*- coding: utf-8 -*-
import pp, terrain
from unit_sprites import UNITS
from building_sprites import padded

def build():
    K = 4
    fw = 14 * K + 12          # frame cell
    lab_w = 250
    top = 80
    rh = 12 * K + 22
    W = 24 + lab_w + 4 * fw + 30 + 4 * fw + 20
    H = top + 20 + len(UNITS) * rh + 60
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ЮНИТЫ НА КАРТЕ', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'КАДРЫ ×4 · ВСЕ СМОТРЯТ ВПРАВО, ВЛЕВО — ЗЕРКАЛО · ЦИКЛ ШАГА: ШАГ / ПРОХОД / ШАГ / ПРОХОД', pp.INK2, 1)
    pp.draw_text(img, 24, 54, 'СЛЕВА НА СВЕТЛОМ, СПРАВА НА КАРТЕ (РАВНИНА / МОРЕ). ЦВЕТ ДЕРЖАВЫ: ТУНИКА, ТЮК, ПАРУС, ЩИТ, МУНДИР, ОПОЗНАВАТЕЛЬНЫЙ ЗНАК.', pp.INK2, 1)
    x0 = 24 + lab_w
    pp.draw_text(img, x0, top, 'КАДРЫ 1-4', pp.INK2, 1)
    pp.draw_text(img, x0 + 4 * fw + 30, top, 'НА КАРТЕ', pp.INK2, 1)
    maxw = max(len(fr[0][0]) for _, _, fr, _ in UNITS) + 2
    maxh = max(len(fr[0]) for _, _, fr, _ in UNITS) + 2
    x1 = pp.Img(4 * maxw, len(UNITS) * maxh * 2)
    y = top + 16
    for r, (name, ms, fr, dur) in enumerate(UNITS):
        img.rect(16, y - 4, W - 32, rh - 4, pp.BG2 if r % 2 == 0 else pp.BG)
        pp.draw_text(img, 24, y + 8, name, pp.INK, 2 if pp.text_width(name, 2) < lab_w - 10 else 1)
        pp.draw_text(img, 24, y + 28, '%s · %d МС/КАДР' % (ms, dur), pp.INK2, 1)
        sea = 'КОРАБЛЬ' in name
        bg = terrain.patch('shallow' if sea else 'plains', 4 * 16, 14, seed=70 + r).scaled(K)
        img.blit(bg, x0 + 4 * fw + 30 - 4, y - 2)
        for k, f in enumerate(fr):
            spr = pp.sprite(padded(f), pp.nation_pal(pp.NATIONS[r % 3]))
            s = pp.with_shadow(spr).scaled(K)
            img.blit(s, x0 + k * fw, y)
            img.blit(s, x0 + 4 * fw + 30 + k * 64, y)
            x1.blit(spr, k * maxw, r * maxh)
            x1.blit(pp.sprite(padded(f), pp.mask_pal()), k * maxw, (len(UNITS) + r) * maxh)
        y += rh
    pp.draw_text(img, 24, y + 8, 'ЕДИНАЯ ЧАСТОТА ШАГА 4-5 КАДР/С, КОРАБЛЬ КАЧАЕТСЯ МЕДЛЕННЕЕ. НА ПАУЗЕ ВСЁ ЗАМИРАЕТ НА КАДРЕ 1, ФАКЕЛ И ФЛАГИ ПРОДОЛЖАЮТ ТРЕПЕТАТЬ.', pp.INK2, 1)
    img.save('units.png')
    x1.save('units_x1.png')
    return img

if __name__ == '__main__':
    build()
