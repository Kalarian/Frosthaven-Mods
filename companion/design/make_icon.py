"""Draws the companion's icon (web/icon.ico): a gold six-armed snowflake on a dark rounded square, at every size
Windows asks for. Run it again only to change the icon: python companion/design/make_icon.py"""
import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "web" / "icon.ico"
BG, GOLD = (20, 24, 30, 255), (227, 201, 139, 255)
SIZES = [16, 24, 32, 48, 64, 128, 256]


def draw(size=1024):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=size * 0.2, fill=BG)
    c, arm, w = size / 2, size * 0.36, max(2, round(size * 0.07))
    for k in range(6):
        a = math.radians(90 + 60 * k)
        tip = (c + arm * math.cos(a), c - arm * math.sin(a))
        d.line([(c, c), tip], fill=GOLD, width=w)
        # a pair of short branches two thirds of the way out
        bx, by = c + arm * 0.62 * math.cos(a), c - arm * 0.62 * math.sin(a)
        for side in (-1, 1):
            b = a + side * math.radians(45)
            d.line([(bx, by), (bx + arm * 0.3 * math.cos(b), by - arm * 0.3 * math.sin(b))], fill=GOLD, width=w)
    r = size * 0.07
    d.ellipse([c - r, c - r, c + r, c + r], fill=GOLD)
    return im


if __name__ == "__main__":
    big = draw()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    big.save(OUT, sizes=[(s, s) for s in SIZES])
    big.resize((256, 256), Image.LANCZOS).save(OUT.with_suffix(".png"))
    print(f"wrote {OUT}")
