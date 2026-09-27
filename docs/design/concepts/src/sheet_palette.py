# -*- coding: utf-8 -*-
"""Master palette: every sprite colour grouped by material ramp, with its key letter."""
import pp

GROUPS = [
    ('КОНТУР', 'K'), ('КАМЕНЬ', '12345'), ('САМАН, ПЕСЧАНИК', 'ahjJ'), ('ДЕРЕВО', 'wWd'), ('СОЛОМА', 'tTu'),
    ('КИРПИЧ, ЧЕРЕПИЦА', 'bBv'), ('ШТУКАТУРКА', 'pP'), ('СТАЛЬ', 'mMn'), ('БЕТОН', '6789'), ('СТЕКЛО', 'gGqQ'),
    ('ОГНИ', 'yY'), ('ЗЕЛЕНЬ', 'eEf'), ('ДЫМ, ПАР', 'zZxX'), ('ОГОНЬ', 'rR'), ('КОЖА, ВОЛОСЫ, ТКАНЬ', 'kHFL'),
    ('ХАКИ', 'IV'), ('ВОДА', 'oO'), ('ШЕРСТЬ, СНЕГ', 'sS'), ('СВЕЧЕНИЕ', 'cCil'), ('ДЕРЖАВА (МАСКА)', 'AND'),
]

def build():
    pal = pp.mask_pal()
    S = 28
    cols = 2
    cw = 420
    rows = (len(GROUPS) + cols - 1) // cols
    W, H = 24 + cols * cw, 70 + rows * (S + 14) + 10
    img = pp.Img(W, H, pp.BG)
    pp.draw_text(img, 24, 18, 'ОБЩАЯ ПАЛИТРА СПРАЙТОВ', pp.INK, 3)
    pp.draw_text(img, 24, 42, 'БУКВА = КЛЮЧ В ASCII-СПРАЙТАХ (SRC/*_SPRITES.PY), ПОДЧЁРКНУТАЯ = СТРОЧНАЯ. В КАЖДОЙ РАМПЕ СВЕТ ТЕПЛЕЕ, ТЕНЬ ХОЛОДНЕЕ.', pp.INK2, 1)
    x1 = pp.Img(sum(len(g[1]) for g in GROUPS), 1)
    k = 0
    for i, (name, keys) in enumerate(GROUPS):
        cx = 24 + (i % cols) * cw
        cy = 64 + (i // cols) * (S + 14)
        pp.draw_text(img, cx, cy + 10, name, pp.INK, 1)
        for j, ch in enumerate(keys):
            c = pal[ch]
            x = cx + 150 + j * (S + 4)
            img.rect(x, cy, S, S, c)
            lum = c[0] * .3 + c[1] * .59 + c[2] * .11
            tc = (255, 255, 255, 255) if lum < 128 else pp.INK
            pp.draw_text(img, x + 2, cy + 2, ch.upper() if ch.isalpha() else ch, tc, 1)
            if ch.isalpha() and ch.islower():
                img.rect(x + 2, cy + 8, 3, 1, tc)
            x1.setraw(k, 0, c); k += 1
    img.save('palette.png')
    x1.save('palette_x1.png')

if __name__ == '__main__':
    build()
