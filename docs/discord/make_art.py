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


if __name__ == '__main__':
    eras()
    logo()
