#!/usr/bin/env python3
"""Draw the desktop mouse cursors: a cat's paw (open, and closed while the button is down), and a circling arrow for
while the camera is being turned.

    pip install pillow
    python3 Tools/make_cursors.py

Writes them for the web page (TemplateData/cursor-*.png, plus @2x versions for sharp high-DPI screens) and for the
desktop build (Resources/Cursors/*.png, read by CursorStyle.cs). The hotspots are in CursorStyle.cs and style.css.
"""
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
WEB = os.path.join(ROOT, "Assets/WebGLTemplates/CasinoMares/TemplateData")
DESKTOP = os.path.join(ROOT, "Assets/CasinoMares/Resources/Cursors")

SIZE = 32          # The cursor's size in CSS pixels.
DESIGN = 100.0     # Shapes are laid out in a 100 x 100 box.
SUPER = 16         # Drawn this many times larger, then scaled down for smooth edges.

OUTLINE = (52, 34, 38, 255)
PAD = (246, 178, 190, 255)
PAD_LIGHT = (255, 226, 231, 255)
PAD_PRESSED = (232, 146, 166, 255)
WHITE = (255, 252, 247, 255)


def canvas(scale):
    return Image.new("L", (SIZE * scale * SUPER, SIZE * scale * SUPER), 0)


def px(value, scale):
    return value / DESIGN * SIZE * scale * SUPER


def rotate(point, degrees, center=(50.0, 50.0)):
    a = math.radians(degrees)
    x, y = point[0] - center[0], point[1] - center[1]
    return (center[0] + x * math.cos(a) - y * math.sin(a), center[1] + x * math.sin(a) + y * math.cos(a))


def ellipse(mask, center, rx, ry, angle, grow, scale):
    """A filled ellipse, turned by angle degrees, grown on every side by grow (design units)."""
    points = []
    for i in range(96):
        t = 2 * math.pi * i / 96
        x, y = (rx + grow) * math.cos(t), (ry + grow) * math.sin(t)
        a = math.radians(angle)
        points.append((px(center[0] + x * math.cos(a) - y * math.sin(a), scale),
                       px(center[1] + x * math.sin(a) + y * math.cos(a), scale)))
    ImageDraw.Draw(mask).polygon(points, fill=255)


def layer(mask, color):
    image = Image.new("RGBA", mask.size, color)
    image.putalpha(ImageChops.multiply(mask, Image.new("L", mask.size, color[3])))
    return image


def finish(image, scale):
    """Adds a soft drop shadow and scales the drawing down to the cursor's size."""
    alpha = image.getchannel("A")
    shadow = Image.new("RGBA", image.size, (0, 0, 0, 0))
    offset = int(px(2.2, scale))
    shadow_alpha = Image.new("L", image.size, 0)
    shadow_alpha.paste(alpha, (offset, offset))
    shadow_alpha = shadow_alpha.filter(ImageFilter.GaussianBlur(px(2.0, scale)))
    shadow.putalpha(shadow_alpha.point(lambda v: int(v * 0.45)))
    out = Image.alpha_composite(shadow, image)
    return out.resize((SIZE * scale, SIZE * scale), Image.LANCZOS)


def paw(scale, closed=False):
    """
    A paw print leaning toward the top left, where the hotspot is (the tip of the front toe). Closed, the toes curl
    in against the pad, a little smaller and pinker, as if the paw were pressing down.
    """
    lean = -22.0
    # (center, rx, ry, angle) upright; the print is then leaned and fitted into the box.
    toes = [((25.0, 47.0), 8.6, 10.6, -24.0), ((39.5, 31.0), 9.0, 11.4, -8.0),
            ((60.5, 31.0), 9.0, 11.4, 8.0), ((75.0, 47.0), 8.6, 10.6, 24.0)]
    pad_lobes = [((50.0, 68.0), 21.0, 15.0, 0.0), ((38.5, 77.0), 11.5, 10.0, 0.0),
                 ((61.5, 77.0), 11.5, 10.0, 0.0), ((50.0, 79.0), 12.0, 9.0, 0.0)]
    open_shapes = [(rotate(c, lean), rx, ry, a + lean) for c, rx, ry, a in toes + pad_lobes]
    tip = rotate((39.5 - 1.5, 31.0 - 11.4), lean)

    # Fit the open paw (outline included) into the box, leaving room for the shadow at the bottom right. The
    # closed one uses the same fit, so the pad stays put and only the toes move.
    grow = 4.6
    xs = [c[0] + s * (max(rx, ry) + grow) for c, rx, ry, _ in open_shapes for s in (-1, 1)]
    ys = [c[1] + s * (max(rx, ry) + grow) for c, rx, ry, _ in open_shapes for s in (-1, 1)]
    fit = (DESIGN - 6.0) / max(max(xs) - min(xs), max(ys) - min(ys))
    def place(p):
        return ((p[0] - min(xs)) * fit + 0.5, (p[1] - min(ys)) * fit + 0.5)

    if closed:
        palm = (50.0, 66.0)
        toes = [((c[0] + (palm[0] - c[0]) * 0.3, c[1] + (palm[1] - c[1]) * 0.36), rx * 0.86, ry * 0.8, a * 1.4)
                for c, rx, ry, a in toes]
        pad_lobes = [(c, rx * 0.96, ry * 0.94, a) for c, rx, ry, a in pad_lobes]
    shapes = [(place(rotate(c, lean)), rx * fit, ry * fit, a + lean) for c, rx, ry, a in toes + pad_lobes]
    tip = place(tip)

    outline, fill, light = canvas(scale), canvas(scale), canvas(scale)
    for center, rx, ry, angle in shapes:
        ellipse(outline, center, rx, ry, angle, grow * fit, scale)
        ellipse(fill, center, rx, ry, angle, 0.0, scale)
        # A soft gloss on the upper-left of each bean.
        a = math.radians(angle)
        gloss = (center[0] - 0.3 * rx * math.cos(a) + 0.35 * ry * math.sin(a),
                 center[1] - 0.3 * rx * math.sin(a) - 0.35 * ry * math.cos(a))
        ellipse(light, gloss, rx * 0.42, ry * 0.32, angle, 0.0, scale)

    light = light.filter(ImageFilter.GaussianBlur(px(1.6, scale)))
    image = layer(outline, OUTLINE)
    image = Image.alpha_composite(image, layer(fill, PAD_PRESSED if closed else PAD))
    image = Image.alpha_composite(image, layer(ImageChops.multiply(light, fill), PAD_LIGHT))
    if not closed:
        print("paw hotspot (1x px):", round(tip[0] / DESIGN * SIZE, 1), round(tip[1] / DESIGN * SIZE, 1))
    return finish(image, scale)


def orbit(scale):
    """Two arrows chasing each other round a circle, with a dot in the middle (the hotspot)."""
    center, radius, width = (50.0, 50.0), 28.0, 9.5

    def arc(mask, start, end, grow):
        draw = ImageDraw.Draw(mask)
        r = radius
        box = [px(center[0] - r - width / 2 - grow, scale), px(center[1] - r - width / 2 - grow, scale),
               px(center[0] + r + width / 2 + grow, scale), px(center[1] + r + width / 2 + grow, scale)]
        draw.pieslice(box, start, end, fill=255)
        inner = r - width / 2 + grow
        hole = [px(center[0] - inner, scale), px(center[1] - inner, scale),
                px(center[0] + inner, scale), px(center[1] + inner, scale)]
        draw.ellipse(hole, fill=0)

    def head(mask, angle, grow):
        # An arrowhead at angle (degrees, clockwise from +x as in PIL), pointing on round the circle clockwise.
        a = math.radians(angle)
        base = (center[0] + radius * math.cos(a), center[1] + radius * math.sin(a))
        along = (-math.sin(a), math.cos(a))
        out = (math.cos(a), math.sin(a))
        length, half = 15.0 + grow * 1.6, 12.0 + grow
        back = 2.0 + grow * 0.8
        points = [
            (base[0] + along[0] * length, base[1] + along[1] * length),
            (base[0] + out[0] * half - along[0] * back, base[1] + out[1] * half - along[1] * back),
            (base[0] - out[0] * half - along[0] * back, base[1] - out[1] * half - along[1] * back),
        ]
        ImageDraw.Draw(mask).polygon([(px(x, scale), px(y, scale)) for x, y in points], fill=255)

    def draw(mask, grow):
        for start in (200.0, 20.0):
            # Arcs union'd one by one: the hole of one mustn't erase the other.
            part = canvas(scale)
            arc(part, start - grow * 1.5, start + 118.0, grow)
            head(part, start + 118.0, grow)
            mask.paste(ImageChops.lighter(mask, part))
        ellipse(mask, center, 5.0, 5.0, 0.0, grow, scale)

    outline, fill = canvas(scale), canvas(scale)
    draw(outline, 5.0)
    draw(fill, 0.0)
    image = layer(outline, OUTLINE)
    image = Image.alpha_composite(image, layer(fill, WHITE))
    return finish(image, scale)


def main():
    os.makedirs(DESKTOP, exist_ok=True)
    for web, desktop, make in (("paw", "Paw", paw), ("paw-closed", "PawClosed", lambda scale: paw(scale, closed=True)),
                               ("rotate", "Rotate", orbit)):
        one, two = make(1), make(2)
        one.save(os.path.join(WEB, f"cursor-{web}.png"))
        two.save(os.path.join(WEB, f"cursor-{web}@2x.png"))
        one.save(os.path.join(DESKTOP, f"{desktop}.png"))
        print(web, "done")


if __name__ == "__main__":
    main()
