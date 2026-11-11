"""Renders Resources/UI/Icons/legend.png (128 x 128, transparent) from legend.svg beside it (Update 19.30).

The SVG's two shapes drawn by hand with Pillow, 8x supersampled for smooth edges, the whole icon centred in its
square. Run: python Assets/KitScripts/legend_icon.py
"""
import math
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "RailTracer", "Resources", "UI", "Icons", "legend.png")
SIZE, SS = 128, 8
RED = (0xC8, 0x10, 0x2E, 255)


def chevron(a, b, c, w=5.0):
    """A polyline A-B-C stroked `w` wide, square caps, mitred at B: its outline."""
    h = w / 2

    def unit(p, q):
        d = (q[0] - p[0], q[1] - p[1])
        n = math.hypot(*d)
        return d[0] / n, d[1] / n

    d1, d2 = unit(a, b), unit(b, c)
    n1, n2 = (-d1[1], d1[0]), (-d2[1], d2[0])
    a2 = (a[0] - d1[0] * h, a[1] - d1[1] * h)
    c2 = (c[0] + d2[0] * h, c[1] + d2[1] * h)

    def miter(s):
        # where the two offset edges (side s) meet
        p = (a2[0] + s * n1[0] * h, a2[1] + s * n1[1] * h)
        q = (c2[0] + s * n2[0] * h, c2[1] + s * n2[1] * h)
        den = d1[0] * d2[1] - d1[1] * d2[0]
        t = ((q[0] - p[0]) * d2[1] - (q[1] - p[1]) * d2[0]) / den
        return p[0] + d1[0] * t, p[1] + d1[1] * t

    return [(a2[0] + n1[0] * h, a2[1] + n1[1] * h), miter(1), (c2[0] + n2[0] * h, c2[1] + n2[1] * h),
            (c2[0] - n2[0] * h, c2[1] - n2[1] * h), miter(-1), (a2[0] - n1[0] * h, a2[1] - n1[1] * h)]


def bezier(p0, p1, p2, p3, n=48):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        pts.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return pts


def drop():
    """M24 23.5 C21 29 15 32.5 15 38 A9 9 0 0 0 33 38 C33 32.5 27 29 24 23.5 Z (the arc runs round the bottom)."""
    pts = bezier((24, 23.5), (21, 29), (15, 32.5), (15, 38))
    pts += [(24 + 9 * math.cos(math.radians(a)), 38 + 9 * math.sin(math.radians(a))) for a in range(180, -1, -4)]
    pts += bezier((33, 38), (33, 32.5), (27, 29), (24, 23.5))
    return pts


shapes = [chevron((10, 16.5), (24, 4.5), (38, 16.5)), chevron((10, 26.5), (24, 14.5), (38, 26.5)), drop()]
xs = [p[0] for s in shapes for p in s]
ys = [p[1] for s in shapes for p in s]
dx, dy = 24 - (min(xs) + max(xs)) / 2, 24 - (min(ys) + max(ys)) / 2  # centre the whole icon in its square
k = SIZE * SS / 48
img = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
for s in shapes:
    draw.polygon([((x + dx) * k, (y + dy) * k) for x, y in s], fill=RED)
img = img.resize((SIZE, SIZE), Image.LANCZOS)
# Straight colour under the soft edge (the edge's alpha fades, its colour stays the red).
px = img.load()
for y in range(SIZE):
    for x in range(SIZE):
        if px[x, y][3]:
            px[x, y] = RED[:3] + (px[x, y][3],)
img.save(os.path.abspath(OUT))
print("legend.png", SIZE, "x", SIZE, "bbox", round(min(xs), 2), round(min(ys), 2), round(max(xs), 2), round(max(ys), 2))
