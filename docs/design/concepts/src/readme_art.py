"""Art for the README (run: python docs/design/concepts/src/readme_art.py): a pixel banner (title, the city through 11 eras on a strip of land) and an animated GIF of
the construction sites of every era. Built from the concept sprite sources so it matches the game."""
import sys, os
sys.dont_write_bytecode = True
SRC = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SRC)
import pp, terrain
from city_sprites import ERAS
from building_sprites import BUILDINGS, padded
from site_sprites import SITES
from PIL import Image

OUT = os.path.normpath(os.path.join(SRC, '..', '..', '..', 'media'))
NAT = pp.nation_pal(pp.NATIONS[0])


def to_pil(img):
    return Image.frombytes('RGBA', (img.w, img.h), bytes(img.px))


def banner():
    W, H = 1280, 400
    ground_y, band_y = 262, 344
    img = pp.Img(W, H, (20, 24, 33, 255))
    # night above, dawn at the horizon, dithered
    stops = [(0.0, (14, 17, 28)), (0.55, (36, 34, 52)), (1.0, (150, 92, 70))]
    for y in range(ground_y):
        t = y / (ground_y - 1)
        for x in range(W):
            tt = min(1, max(0, t + (pp.bayer(x, y) - .5) * .05))
            for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
                if t0 <= tt <= t1:
                    u = (tt - t0) / (t1 - t0)
                    img.setraw(x, y, tuple(int(c0[i] + (c1[i] - c0[i]) * u) for i in range(3)) + (255,))
                    break
    for k in range(180):
        x = int(pp.h2(k, 3, 11) * W); y = int(pp.h2(k, 7, 13) * ground_y * .6)
        b = 120 + int(pp.h2(k, 9, 17) * 130)
        img.setraw(x, y, (b, b, min(255, b + 25), 255))
        if pp.h2(k, 5, 19) > .93:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): img.setraw(x + dx, y + dy, (b // 2, b // 2, b // 2 + 20, 255))
    # one continuous meadow with the sea at both ends
    land = terrain.patch('meadow', W // 4 + 1, (band_y - ground_y) // 4 + 1, seed=31).scaled(4)
    img.blit(land, 0, ground_y)
    sea = terrain.patch('shallow', 14, (band_y - ground_y) // 4 + 1, seed=5).scaled(4)
    img.blit(sea, 0, ground_y); img.blit(sea, W - 56, ground_y)
    for x in range(W): img.setraw(x, ground_y, (40, 52, 34, 255))
    # the road from era to era
    K = 4
    step = (W - 200) // (len(ERAS) - 1)
    xs = [100 + i * step for i in range(len(ERAS))]
    for x in range(xs[0], xs[-1]):
        if (x // 6) % 2 == 0:
            for dy in range(3): img.setraw(x, ground_y + 40 + dy, (196, 168, 112, 255))
    # the city of every era
    for i, (label, era, rows) in enumerate(ERAS):
        spr = pp.with_shadow(pp.sprite(rows, NAT)).scaled(K)
        img.blit(spr, xs[i] - spr.w // 2, ground_y + 40 - spr.h + 6)
    # the band of era names
    img.rect(0, band_y, W, H - band_y, (24, 26, 32, 255))
    img.rect(0, band_y, W, 2, (201, 166, 91, 255))
    for i, (label, era, rows) in enumerate(ERAS):
        pp.draw_text_c(img, xs[i], band_y + 14, era.upper(), (232, 222, 200, 255), 1)
        pp.draw_text_c(img, xs[i], band_y + 30, ['I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII', 'IX', 'X', 'XI'][i], (201, 166, 91, 255), 1)
    # the title with a hard pixel shadow
    for (dx, dy, c) in ((6, 6, (8, 9, 14, 255)), (0, 0, (247, 222, 160, 255))):
        pp.draw_text_c(img, W // 2 + dx, 46 + dy, 'PAX PIXELIA', c, 8)
    for (dx, dy, c) in ((2, 2, (8, 9, 14, 255)), (0, 0, (236, 226, 206, 255))):
        pp.draw_text_c(img, W // 2 + dx, 122 + dy, 'ГЛОБАЛЬНАЯ СТРАТЕГИЯ В РЕАЛЬНОМ ВРЕМЕНИ · ОТ КОЧЕВОГО ПЛЕМЕНИ ДО ЗВЁЗД', c, 2)
    img.rect(W // 2 - 180, 154, 360, 2, (201, 166, 91, 255))
    to_pil(img).save(os.path.join(OUT, 'banner.png'))


def sites_gif():
    K = 5
    cell_w, cell_h = 12 * K + 28, 14 * K + 40
    cols = 6
    rows_n = 2
    W, H = cols * cell_w + 24, rows_n * cell_h + 24
    bld = [0, 7, 5, 6, 0, 7, 5, 6, 0, 7, 5]
    frames = []
    for f in range(16):
        img = pp.Img(W, H, (34, 38, 44, 255))
        for i, (name, fr) in enumerate(SITES):
            r, c = divmod(i, cols)
            if i == 11:
                break
            x0, y0 = 12 + c * cell_w, 12 + r * cell_h
            ground = terrain.patch('plains', (cell_w - 8) // K + 1, 4, seed=40 + i).scaled(K)
            img.blit(ground, x0 + 4, y0 + 14 * K - 2 * K)
            prog = ((f + i * 3) % 16) / 15.0
            b = pp.sprite(padded(BUILDINGS[bld[i]][1]), NAT)
            shown = min(9, int(10 * prog))
            cut = pp.Img(b.w, b.h)
            for yy in range(10 - shown, 10):
                for xx in range(b.w):
                    cc = b.get(xx, yy)
                    if cc[3]:
                        cut.setraw(xx, yy, cc)
            canvas = pp.Img(12, 14)
            canvas.blit(cut, 1, 4)
            canvas.blit(pp.sprite(padded(fr[f % 4]), NAT), 0, 0)
            img.blit(pp.with_shadow(canvas).scaled(K), x0 + (cell_w - 12 * K) // 2, y0)
            pp.draw_text_c(img, x0 + cell_w // 2, y0 + 14 * K + 10, name.upper(), (230, 220, 200, 255), 1)
        # the last cell: a finished building and its dust
        x0, y0 = 12 + 5 * cell_w, 12 + 1 * cell_h
        ground = terrain.patch('plains', (cell_w - 8) // K + 1, 4, seed=99).scaled(K)
        img.blit(ground, x0 + 4, y0 + 14 * K - 2 * K)
        canvas = pp.Img(12, 14)
        canvas.blit(pp.sprite(padded(BUILDINGS[7][1]), NAT), 1, 4)
        img.blit(pp.with_shadow(canvas).scaled(K), x0 + (cell_w - 12 * K) // 2, y0)
        pp.draw_text_c(img, x0 + cell_w // 2, y0 + 14 * K + 10, 'ГОТОВО', (247, 222, 160, 255), 1)
        frames.append(to_pil(img).convert('P', palette=Image.ADAPTIVE, colors=255))
    frames[0].save(os.path.join(OUT, 'construction.gif'), save_all=True, append_images=frames[1:], duration=220, loop=0, disposal=2, optimize=False)


if __name__ == '__main__':
    banner()
    sites_gif()
    print('ok')
