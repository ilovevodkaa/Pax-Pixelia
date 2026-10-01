"""Pictures for the Discord status (docs/discord): logo.png 1024² and era0…era10.png 512², exact small palettes.
Run from the repo root: python docs/discord/make_art.py"""
from PIL import Image, ImageDraw, ImageFont

OUT = 'docs/discord/'
BG = (18, 24, 38, 255)


def save_exact(img, path):
    """Palette PNG with the image's own colours (pixel art keeps every colour exactly)."""
    img = img.convert('RGBA')
    cols = [c for _, c in img.getcolors(256)]
    pal = Image.new('P', (1, 1))
    flat = [v for c in cols for v in c[:3]]
    pal.putpalette(flat + [0] * (768 - len(flat)))
    index = {c: i for i, c in enumerate(cols)}
    p = Image.new('P', img.size)
    p.putpalette(flat + [0] * (768 - len(flat)))
    p.putdata([index[c] for c in getattr(img, 'get_flattened_data', img.getdata)()])
    alpha = bytes(c[3] for c in cols)
    p.save(path, optimize=True, **({'transparency': alpha} if min(alpha) < 255 else {}))


def eras():
    sheet = Image.open('game/assets/front/city_eras_x1.png').convert('RGBA')
    for e in range(11):
        spr = sheet.crop((e * 16, 16, e * 16 + 16, 32))   # row 1: the warm nation ramp
        img = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
        ImageDraw.Draw(img).ellipse((0, 0, 511, 511), fill=BG)
        big = spr.resize((320, 320), Image.NEAREST)
        a = big.getchannel('A').point(lambda v: 255 if v > 127 else 0)
        big.putalpha(a)
        shadow = Image.new('RGBA', big.size, (8, 10, 18, 255))
        shadow.putalpha(a)
        img.alpha_composite(shadow, (116, 116))
        img.alpha_composite(big, (96, 96))
        save_exact(img, OUT + f'era{e}.png')


def logo():
    img = Image.new('RGBA', (1024, 1024))
    d = ImageDraw.Draw(img)
    for y in range(1024):   # stepped night sky
        t = (y // 64) / 16
        d.line((0, y, 1023, y), fill=tuple(int(a + (b - a) * t) for a, b in zip((14, 18, 32), (44, 40, 66))) + (255,))
    sky = Image.open('game/assets/front/skyline_eras.png').convert('RGBA')
    px = sky.load()
    for y in range(sky.height):
        for x in range(sky.width):
            r, g, b, _ = px[x, y]
            if r + g + b < 30: px[x, y] = (0, 0, 0, 0)
    w = 356   # up to the rocket; the flying city would be cut at the edge
    sky = sky.crop((0, 0, w, 72))
    bb = sky.getbbox()
    sky = sky.crop((0, bb[1], w, bb[3]))
    sk = sky.resize((1024, int(sky.height * 1024 / w)), Image.NEAREST)
    img.alpha_composite(sk, (0, 1024 - sk.height))
    font = ImageFont.truetype('game/assets/fonts/PixelifySansMrP.ttf', 232)

    def text(s, y):
        x = (1024 - d.textlength(s, font=font)) / 2
        layer = Image.new('RGBA', (1024, 1024))
        ImageDraw.Draw(layer).text((x, y), s, font=font, fill=(255, 255, 255, 255))
        a = layer.getchannel('A').point(lambda v: 255 if v > 127 else 0)
        shadow = Image.new('RGBA', (1024, 1024), (6, 7, 14, 255))
        shadow.putalpha(a)
        img.alpha_composite(shadow, (16, 16))
        grad = Image.new('RGBA', (1024, 1024))
        gd = ImageDraw.Draw(grad)
        top, bottom = a.getbbox()[1], a.getbbox()[3]
        stops = [(255, 230, 150), (246, 192, 86), (226, 148, 54), (192, 106, 40)]
        gd.rectangle((0, 0, 1023, 1023), fill=stops[-1] + (255,))
        for i, c in enumerate(stops[:-1]):
            gd.rectangle((0, 0 if i == 0 else top + i * (bottom - top) // 4, 1023, top + (i + 1) * (bottom - top) // 4), fill=c + (255,))
        grad.putalpha(a)
        img.alpha_composite(grad)

    text('PAX', 110)
    text('PIXELIA', 360)
    save_exact(img, OUT + 'logo.png')


# ---- v2: era scenes (large picture in game) and activity icons (small picture) ----
import math
import random
import re

ACTS = {   # key: (icon of game/scripts/UI/Theme/PixelIconArt.cs, full tone, mid tone)
    'act_pause': ('player-pause-filled', (236, 238, 245), (150, 156, 175)),
    'act_wonder': ('building-bank', (255, 214, 102), (200, 140, 50)),
    'act_research': ('bulb', (130, 220, 255), (60, 140, 210)),
    'act_build': ('hammer', (255, 170, 90), (190, 100, 50)),
    'act_scouts': ('compass', (140, 230, 160), (60, 150, 90)),
    'act_people': ('users', (250, 220, 180), (200, 150, 110)),
    'act_rank': ('trophy', (255, 214, 102), (200, 140, 50)),
    'act_nomad': ('walk', (200, 230, 120), (120, 160, 60)),
    'act_blitz': ('hourglass', (255, 120, 120), (190, 60, 70)),
    'act_date': ('sun', (255, 200, 90), (220, 120, 60)),
    'act_glory': ('crown', (255, 214, 102), (200, 140, 50)),
    'act_land': ('flag', (255, 140, 110), (190, 80, 70)),
}

# per era: sky top, sky bottom, ground
ERA_SKY = [
    ((24, 20, 48), (120, 70, 80), (52, 40, 36)),     # первобытная: закат у костра
    ((40, 70, 120), (230, 180, 120), (150, 120, 70)),  # бронза: пустыня
    ((50, 110, 180), (170, 210, 235), (110, 140, 70)),  # античность
    ((60, 80, 130), (180, 190, 210), (80, 110, 60)),    # средневековье
    ((70, 120, 190), (230, 210, 170), (100, 130, 70)),  # возрождение
    ((90, 90, 110), (200, 170, 140), (90, 100, 60)),    # новое время
    ((70, 70, 80), (170, 150, 130), (70, 70, 60)),      # промышленность: дым
    ((40, 60, 110), (150, 170, 200), (70, 90, 70)),     # новейшее
    ((20, 30, 70), (90, 110, 170), (50, 60, 70)),       # информационная: ночь, огни
    ((14, 20, 50), (60, 90, 160), (40, 50, 70)),        # космическая
    ((30, 10, 60), (170, 90, 200), (60, 40, 90)),       # будущее
]


def icon_art():
    with open('game/scripts/UI/Theme/PixelIconArt.cs', encoding='utf-8') as f:
        src = f.read()
    return {m.group(1): [l.strip() for l in m.group(2).strip().splitlines()]
            for m in re.finditer(r'\["([a-z-]+)"\] = @"(.*?)"', src, re.S)}


def acts():
    art = icon_art()
    for key, (name, full, mid) in ACTS.items():
        rows = art[name]
        h, w = len(rows), max(len(r) for r in rows)
        spr = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        for y, r in enumerate(rows):
            for x, ch in enumerate(r):
                if ch == '#': spr.putpixel((x, y), full + (255,))
                elif ch == '+': spr.putpixel((x, y), mid + (255,))
        k = 300 // max(w, h)
        big = spr.resize((w * k, h * k), Image.NEAREST)
        img = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
        ImageDraw.Draw(img).ellipse((0, 0, 511, 511), fill=BG)
        shadow = Image.new('RGBA', big.size, (6, 8, 16, 255))
        shadow.putalpha(big.getchannel('A'))
        x, y = (512 - big.width) // 2, (512 - big.height) // 2
        img.alpha_composite(shadow, (x + k, y + k))
        img.alpha_composite(big, (x, y))
        save_exact(img, OUT + key + '.png')


def scenes():
    sheet = Image.open('game/assets/front/city_eras_x1.png').convert('RGBA')
    for e in range(11):
        top, bottom, ground = ERA_SKY[e]
        img = Image.new('RGBA', (1024, 1024))
        d = ImageDraw.Draw(img)
        for y in range(1024):   # stepped sky, 32 px bands like the game's pixels
            t = min(1, (y // 64) / 12)
            d.line((0, y, 1023, y), fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
        if e in (0, 8, 9, 10):   # night eras: a few square stars
            rnd = random.Random(e)
            for _ in range(40):
                sx, sy = rnd.randrange(0, 1024, 32), rnd.randrange(0, 480, 32)
                d.rectangle((sx, sy, sx + 15, sy + 15), fill=(230, 230, 255, 255))
        else:   # a blocky sun
            d.rectangle((96, 96, 224, 224), fill=(255, 236, 170, 255))
            d.rectangle((64, 128, 256, 192), fill=(255, 236, 170, 255))
            d.rectangle((128, 64, 192, 256), fill=(255, 236, 170, 255))
        # hills: two stepped layers
        far = tuple(int(c * .75 + b * .25) for c, b in zip(ground, bottom))
        for x in range(0, 1024, 32):
            hgt = 720 + int(40 * math.sin(x / 140 + e))
            d.rectangle((x, hgt // 32 * 32, x + 31, 1023), fill=far + (255,))
        d.rectangle((0, 832, 1023, 1023), fill=ground + (255,))
        d.rectangle((0, 832, 1023, 863), fill=tuple(min(255, c + 25) for c in ground) + (255,))
        # the city of the era, 16 px sprite at ×44, standing on the ground
        spr = sheet.crop((e * 16, 16, e * 16 + 16, 32))
        bb = spr.getbbox()
        k = 44
        big = spr.resize((16 * k, 16 * k), Image.NEAREST)
        a = big.getchannel('A').point(lambda v: 255 if v > 127 else 0)
        big.putalpha(a)
        x = (1024 - 16 * k) // 2
        y = 864 - bb[3] * k
        shadow = Image.new('RGBA', big.size, (0, 0, 0, 255))
        shadow.putalpha(a.point(lambda v: 90 if v else 0))
        img.alpha_composite(shadow, (x + k, y + k // 2))
        img.alpha_composite(big, (x, y))
        save_exact(img, OUT + f'scene{e}.png')


if __name__ == '__main__':
    eras()
    logo()
    acts()
    scenes()
