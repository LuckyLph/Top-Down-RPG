"""Generates the terrain-blending spike's material textures: grass, gravel and stone at 32 px per cell, each repeating
every 4 cells (128 px).

Usage: python materials.py <output folder>. Writes, per material, <name>_albedo.png and <name>_height.png, and for
gravel and stone <name>_field.png. They go into Assets/Dev/TerrainSpike/Textures and are imported with point
filtering, no compression, no mipmaps and sRGB off (the shader reads raw bytes and treats albedo as sRGB itself).

Texel rows are written top first, as PNGs are, and Unity reads them bottom first, so every offset stored here is in
Unity's orientation: x to the right, y up.

Albedo: the material's interior as it looks away from any edge (chips, stones, tufts), alpha 255.
Field (gravel, stone): R, G = offset in whole pixels from the texel to the centre pixel of its chip or stone, plus 128.
B = the base shade (dirt or mortar without the chip on top), as 128 plus the amount added to the material's base
colour. A = the distance from the texel to the edge of its chip's or stone's cell, times 16 (at least 1); 0 marks a
texel that belongs to no chip, so the shader uses the albedo as it is.
Height: R = smooth height in [0, 1] whose slope stays under 0.2 per pixel (soft mode scales it by a few pixels, keeping
the slope under 1). G = one random value per chip or stone (0.5 for grass), constant over its cell.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pathtiles import (CellField, GRAVEL_CHIPS, STONE_PALETTE, clamp, hash01, lerp, max_slope,  # noqa: E402
                       periodic_noise, shade)
from pngio import write_png  # noqa: E402

P = 128
LIGHT = (-0.6, -0.8)
HEIGHT_TERMS = [(1, 0, 1.0), (0, 1, 0.9), (1, 1, 0.7), (2, -1, 0.5), (-1, 2, 0.45), (3, 1, 0.25)]


def blank():
    return [[(0, 0, 0, 255)] * P for _ in range(P)]


def to_bytes(colour, alpha=255):
    return tuple(int(round(clamp(c, 0, 255))) for c in colour) + (alpha,)


def smooth_height(rng):
    """Smooth periodic height in [0, 1], normalised so its slope stays under 0.2 per pixel."""
    assert max_slope(HEIGHT_TERMS, P) / sum(a for _, _, a in HEIGHT_TERMS) < 0.2
    total = sum(a for _, _, a in HEIGHT_TERMS)
    grid = periodic_noise(rng, HEIGHT_TERMS, P)
    return [[0.5 + 0.5 * grid[y][x] / total for x in range(P)] for y in range(P)]


def height_image(height, per_cell=None, field=None):
    out = blank()
    for y in range(P):
        for x in range(P):
            g = 0.5 if per_cell is None else per_cell[field.at(x, y)[0]]
            out[y][x] = (int(round(height[y][x] * 255)), int(round(g * 255)), 0, 255)
    return out


def centre_offset(x, y, dx, dy):
    """Whole-pixel offset from texel (x, y) to the pixel holding its seed, in Unity's orientation (y up)."""
    ox = int(math.floor(x + 0.5 - dx)) - x
    oy = int(math.floor(y + 0.5 - dy)) - y
    return ox, -oy


def bevel_light(dx, dy):
    length = math.hypot(dx, dy) or 1.0
    return dx / length * LIGHT[0] + dy / length * LIGHT[1]


class Grass:
    """Flat dark green with slow tint patches and sparse small tufts, like the interior of grass.png."""

    base = (35, 114, 40)
    dark = (27, 100, 32)
    light = (54, 136, 56)
    glyphs = [
        [(-1, 0), (0, 1), (1, 0)],
        [(-2, 0), (-1, 1), (0, 0), (1, 1), (2, 0)],
        [(0, 0), (0, 1)],
        [(-1, 0), (1, 0), (0, 1), (0, 2)],
    ]

    def __init__(self, seed):
        rng = random.Random(seed)
        self.tint = periodic_noise(rng, [(1, 0, 1.1), (0, 1, 0.9), (2, 1, 0.7), (1, -2, 0.7), (3, 2, 0.4)], P)
        self.field = CellField(rng, 10, 0.45, P)
        self.tufts = {}
        for index, (sx, sy) in enumerate(self.field.seeds):
            if rng.random() < 0.5:
                self.tufts[index] = (rng.choice(self.glyphs), int(sx), int(sy), rng.random() < 0.5)
        self.height = smooth_height(rng)
        self.marks = {}
        for glyph, sx, sy, lit in self.tufts.values():
            for gx, gy in glyph:
                self.marks[((sx + gx) % P, (sy - gy) % P)] = 'dark'
            if lit:
                top = max(gy for _, gy in glyph)
                self.marks[(sx % P, (sy - top - 1) % P)] = 'light'

    def albedo(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                grit = (hash01(x, y, 3) - 0.5) * 3
                colour = shade(self.base, self.tint[y][x] + grit)
                mark = self.marks.get((x, y))
                if mark == 'dark':
                    colour = shade(self.dark, grit)
                elif mark == 'light':
                    colour = shade(self.light, grit)
                out[y][x] = to_bytes(colour)
        return out

    def height_map(self):
        return height_image(self.height)


class Gravel:
    """Packed dirt with slow tint patches, half covered in small chips, as in pathtiles.py's gravel at half the size."""

    dirt = (146, 122, 88)

    def __init__(self, seed):
        rng = random.Random(seed)
        self.tint = periodic_noise(rng, [(1, 0, 4.0), (0, 1, 3.0), (2, 2, 3.0), (3, -1, 2.0)], P)
        density = periodic_noise(rng, [(3, 2, 0.06), (-2, 3, 0.06), (4, -1, 0.05)], P)
        self.field = CellField(rng, 25, 0.4, P)
        light = GRAVEL_CHIPS['light']
        grey = GRAVEL_CHIPS['grey']
        dark = GRAVEL_CHIPS['dark']
        self.chips = []
        for sx, sy in self.field.seeds:
            keep = rng.random() < 0.7 + density[int(sy) % P][int(sx) % P]
            roll = rng.random()
            colour = lerp(light, grey, rng.random()) if roll < 0.86 else dark
            self.chips.append((keep, shade(colour, rng.uniform(-9, 9)), rng.uniform(0.45, 0.85)))
        self.random = [rng.random() for _ in self.field.seeds]
        self.height = smooth_height(rng)

    def base_amount(self, x, y):
        return self.tint[y][x] + (hash01(x, y, 7) - 0.5) * 7

    def albedo(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                amount = self.base_amount(x, y)
                base = shade(self.dirt, amount)
                index, dx, dy, border = self.field.at(x, y)
                keep, colour, gap = self.chips[index]
                if not keep:
                    out[y][x] = to_bytes(base)
                elif border < gap:
                    out[y][x] = to_bytes(shade(base, -10 if dx + dy > 0 else -3))
                else:
                    grit = (hash01(x, y, 7) - 0.5) * 7
                    bevel = clamp(1 - (border - gap) / 0.9)
                    out[y][x] = to_bytes(shade(colour, bevel_light(dx, dy) * 18 * bevel + grit * 0.6))
        return out

    def field_map(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                amount = int(round(self.base_amount(x, y)))
                index, dx, dy, border = self.field.at(x, y)
                if not self.chips[index][0]:
                    out[y][x] = (128, 128, 128 + amount, 0)
                    continue
                ox, oy = centre_offset(x, y, dx, dy)
                out[y][x] = (128 + ox, 128 + oy, 128 + amount, int(clamp(round(border * 16), 1, 255)))
        return out

    def height_map(self):
        return height_image(self.height, self.random, self.field)


class Stone:
    """Cobbles in dark mortar, as in pathtiles.py's stone at half the size: one-pixel mortar lines, each stone lit from
    the top left along its own cell edges."""

    mortar = (98, 88, 76)

    def __init__(self, seed):
        rng = random.Random(seed)
        self.field = CellField(rng, 18, 0.32, P)
        self.colours = [rng.choice(STONE_PALETTE) for _ in self.field.seeds]
        self.random = [rng.random() for _ in self.field.seeds]
        self.height = smooth_height(rng)

    def albedo(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                grit = (hash01(x, y, 11) - 0.5) * 7
                index, dx, dy, border = self.field.at(x, y)
                if border < 0.5:
                    out[y][x] = to_bytes(shade(self.mortar, grit))
                    continue
                lit = bevel_light(dx, dy)
                bevel = clamp(1 - (border - 0.5) / 1.25)
                radial = clamp(math.hypot(dx, dy) / 3.5)
                amount = lit * (18 * bevel + 4 * radial) + grit
                if bevel > 0 and lit < -0.35:
                    amount -= 8 * bevel
                out[y][x] = to_bytes(shade(self.colours[index], amount))
        return out

    def field_map(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                grit = int(round((hash01(x, y, 11) - 0.5) * 7))
                index, dx, dy, border = self.field.at(x, y)
                ox, oy = centre_offset(x, y, dx, dy)
                out[y][x] = (128 + ox, 128 + oy, 128 + grit, int(clamp(round(border * 16), 1, 255)))
        return out

    def height_map(self):
        return height_image(self.height, self.random, self.field)


if __name__ == '__main__':
    out = sys.argv[1]
    materials = (('grass', Grass(20261012)), ('gravel', Gravel(20261013)), ('stone', Stone(20261014)))
    for name, material in materials:
        write_png(f'{out}/{name}_albedo.png', P, P, material.albedo())
        write_png(f'{out}/{name}_height.png', P, P, material.height_map())
        if hasattr(material, 'field_map'):
            write_png(f'{out}/{name}_field.png', P, P, material.field_map())
        print('wrote', name)
