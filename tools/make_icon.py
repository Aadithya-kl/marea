"""Generates the Marea icon (waves on a rounded square) as a multi-resolution .ico and a .png."""
import math
import sys

from PIL import Image, ImageDraw

SIZE = 512
OUT = sys.argv[1] if len(sys.argv) > 1 else "."


def draw(size: int) -> Image.Image:
    """Draws the icon at 4x and downsamples it for smooth edges."""
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(s * 0.22)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=(14, 76, 92, 255))
    for k, (lvl, col, amp) in enumerate([(0.50, (64, 196, 196, 255), 0.07), (0.66, (150, 228, 214, 255), 0.05)]):
        pts = []
        for x in range(0, s + 1, 4):
            y = s * lvl + math.sin(x / s * 2 * math.pi * 1.25 + k * 1.3) * s * amp
            pts.append((x, y))
        pts += [(s, s), (0, s)]
        mask = Image.new("L", (s, s), 0)
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        layer = Image.new("RGBA", (s, s), col)
        clip = Image.new("L", (s, s), 0)
        ImageDraw.Draw(clip).rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=255)
        mask = Image.fromarray(__import__("numpy").minimum(__import__("numpy").array(mask), __import__("numpy").array(clip)))
        img.paste(layer, (0, 0), mask)
    return img.resize((size, size), Image.LANCZOS)


big = draw(SIZE)
big.save(f"{OUT}/marea.png")
big.save(f"{OUT}/marea.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
