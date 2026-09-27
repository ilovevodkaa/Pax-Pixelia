# -*- coding: utf-8 -*-
"""quick preview of sprite lists at big scale: python preview.py module ATTR scale out.png"""
import sys, importlib, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp

def main():
    mod, attr, k, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    items = getattr(importlib.import_module(mod), attr)
    sprites = []
    for it in items:
        rows = it[-1] if isinstance(it, tuple) else it
        for n in pp.NATIONS[:2]:
            sprites.append(pp.with_shadow(pp.sprite(rows, pp.nation_pal(n))))
    w = max(s.w for s in sprites) + 2
    h = max(s.h for s in sprites) + 2
    cols = len(items)
    img = pp.Img(cols * w, 4 * h, pp.BG)
    for i, s in enumerate(sprites):
        c, r = i // 2, i % 2
        img.blit(s, c * w + 1, r * h + 1)
        # on terrain colour
        img.rect(c * w, (r + 2) * h, w, h, (144, 156, 88, 255) if r == 0 else (64, 102, 60, 255))
        img.blit(s, c * w + 1, (r + 2) * h + 1)
    img.scaled(k).save(out)

main()
