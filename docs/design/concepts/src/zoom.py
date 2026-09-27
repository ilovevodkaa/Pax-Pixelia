import sys, importlib, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pp
mod, attr, idx, k, out = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5]
items = getattr(importlib.import_module(mod), attr)
ids = [int(v) for v in idx.split(',')]
sp = [pp.with_shadow(pp.sprite(items[i][-1] if isinstance(items[i], tuple) else items[i], pp.nation_pal(pp.NATIONS[0]))) for i in ids]
w = max(s.w for s in sp) + 2; h = max(s.h for s in sp) + 2
img = pp.Img(len(sp) * w, h * 2, pp.BG)
for j, s in enumerate(sp):
    img.blit(s, j * w + 1, 1)
    img.rect(j * w, h, w, h, (144, 156, 88, 255))
    img.blit(s, j * w + 1, h + 1)
img.scaled(k).save(out)
