# -*- coding: utf-8 -*-
import pp, terrain

def build():
    B = terrain.BIOMES
    K4 = 4
    cols = 2
    cw = 16 + 5 * 26 + 12 + 128 + 16 + 128 + 16      # cell width
    ch = 20 + 128 + 28
    top = 64
    W = 24 + cols * cw + 8
    rows = (len(B) + cols - 1) // cols
    H = top + rows * ch + 16
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ПАЛИТРЫ БИОМОВ И СТИЛЬ РЕЛЬЕФА', pp.INK, 3)
    pp.draw_text(img, 24, 40, 'РАМПА 5 ТОНОВ (ТЕНЬ К СИНЕМУ, СВЕТ К ЖЁЛТОМУ) · ПАТЧ 32×32 ×4 · ТОТ ЖЕ ПАТЧ 2×2 ×2 (ШОВ НЕ ВИДЕН)', pp.INK2, 1)
    x1 = pp.Img(7 * 32, 2 * 32)
    lut = pp.Img(5, len(B))
    for i, b in enumerate(B):
        key, name = b[0], b[1]
        cx = 24 + (i % cols) * cw
        cy = top + (i // cols) * ch
        img.rect(cx - 8, cy - 6, cw - 8, ch - 8, pp.BG2)
        pp.draw_text(img, cx, cy, name, pp.INK, 2)
        R = terrain.ramp_for(key)
        for k, c in enumerate(R):
            img.rect(cx + k * 26, cy + 20, 24, 24, c)
            lut.setraw(k, i, c)
        # hex under swatches (tiny)
        p = terrain.patch(key, 32, 32, seed=11 + i * 7)
        px = cx + 5 * 26 + 12
        img.blit(p, px, cy + 20, K4)
        for ty in range(2):
            for tx in range(2):
                img.blit(p, px + 128 + 16 + tx * 64, cy + 20 + ty * 64, 2)
        x1.blit(p, (i % 7) * 32, (i // 7) * 32)
        for k, c in enumerate(R):
            pp.draw_text(img, cx, cy + 52 + k * 11, '%d  #%02X%02X%02X' % (k, c[0], c[1], c[2]), pp.INK2, 1)
        pp.draw_text(img, cx, cy + 52 + 5 * 11 + 4, 'БАЗА = УРОВЕНЬ 2', pp.INK2, 1)
    img.save('terrain_tiles.png')
    x1.save('terrain_tiles_x1.png')
    lut.save('terrain_ramps_lut.png')
    return img

if __name__ == '__main__':
    build()
