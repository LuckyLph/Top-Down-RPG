"""Generates the terrain materials' textures: grass, gravel and stone at 32 px per cell, each repeating every 4 cells
(128 px).

Usage: python -I materials.py <output folder>. Writes, per material, <name>_albedo.png and <name>_height.png, and for
gravel and stone <name>_field.png. They go into Assets/Textures/Terrain, where TerrainTextureDefaults imports them with
point filtering, no compression, no mipmaps and sRGB off (the shader reads raw bytes and treats albedo as sRGB itself).
A rerun writes byte-identical files.

Texel rows are written top first, as PNGs are, and Unity reads them bottom first, so every offset stored here is in
Unity's orientation: x to the right, y up.

Albedo: the material's interior as it looks away from any edge (chips, stones, tufts), alpha 255.
Field (gravel, stone): R, G = offset in whole pixels from the texel to the centre pixel of its chip or stone, plus 128.
B = the base shade (dirt or mortar without the chip on top), as 128 plus the amount added to the material's base
colour (BASE_COLOURS, which the terrain style's base colours must match). A, times 16 (at least 1): for gravel, whose
chips are drawn whole or left out, the chip's reach, the distance from its centre pixel to its farthest pixel, the same
over the whole chip, so the shader can keep only chips that lie wholly clear of the rim; for stone, which is cut along
the rim, the distance from the texel to the edge of its stone. A = 0 marks a texel that belongs to no chip, so the
shader uses the albedo as it is. A gravel chip's footprint includes the shadow it casts on the dirt, so a chip left out
takes its shadow with it.
Height: R = smooth height in [0, 1] whose slope stays under 0.2 per pixel (soft mode scales it by a few pixels, keeping
the slope under 1). G = one random value per chip or stone (0.5 for grass), constant over its cell.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pathtiles import (CellField, GRAVEL_CHIPS, STONE_PALETTE, clamp, hash01, lerp, max_slope,  # noqa: E402
                       periodic_noise, shade, wrap_delta)
from pngio import write_png  # noqa: E402

P = 128
LIGHT = (-0.6, -0.8)
HEIGHT_TERMS = [(1, 0, 1.0), (0, 1, 0.9), (1, 1, 0.7), (2, -1, 0.5), (-1, 2, 0.45), (3, 1, 0.25)]
BASE_COLOURS = {'gravel': (146, 122, 88), 'stone': (98, 88, 76)}


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


def height_image(height, per_cell=None, owner=None):
    out = blank()
    for y in range(P):
        for x in range(P):
            index = None if owner is None else owner(x, y)
            g = 0.5 if index is None else per_cell[index]
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
    """Dark green with slow tint patches and small tufts of blades: dark strokes with lighter tips, like the marks in
    today's grass edge, clustering here and there."""

    base = (35, 114, 40)
    dark = (24, 88, 30)
    light = (62, 146, 60)
    glyphs = [
        ['+.+',
         '#.#',
         '.#.'],
        ['+.+.+',
         '#.#.#',
         '.#.#.'],
        ['.+',
         '+#',
         '#.'],
        ['+...+',
         '#.+.#',
         '.#.#.'],
        ['+.',
         '#+',
         '.#'],
        ['.+.',
         '+#+',
         '#.#'],
    ]

    def __init__(self, seed):
        rng = random.Random(seed)
        self.tint = periodic_noise(rng, [(1, 0, 1.4), (0, 1, 1.1), (2, 1, 0.9), (1, -2, 0.9), (3, 2, 0.5)], P)
        density = periodic_noise(rng, [(2, 1, 0.14), (-1, 2, 0.12), (3, -2, 0.08)], P)
        self.field = CellField(rng, 12, 0.42, P)
        self.marks = {}
        for sx, sy in self.field.seeds:
            keep = rng.random() < 0.62 + density[int(sy) % P][int(sx) % P]
            glyph = rng.choice(self.glyphs)
            if not keep:
                continue
            ox = int(sx) - len(glyph[0]) // 2
            oy = int(sy) - len(glyph) // 2
            for gy, row in enumerate(glyph):
                for gx, mark in enumerate(row):
                    if mark != '.':
                        self.marks[((ox + gx) % P, (oy + gy) % P)] = mark
        self.height = smooth_height(rng)

    def albedo(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                grit = (hash01(x, y, 3) - 0.5) * 3
                colour = shade(self.base, self.tint[y][x] + grit)
                mark = self.marks.get((x, y))
                if mark == '#':
                    colour = shade(self.dark, grit)
                elif mark == '+':
                    colour = shade(self.light, grit)
                out[y][x] = to_bytes(colour)
        return out

    def height_map(self):
        return height_image(self.height)


class Gravel:
    """Packed dirt with slow tint patches, scattered with small rounded chips: each a tilted ellipse about two to five
    pixels across, lit from the top left, casting a one-pixel shadow on the dirt to its bottom right."""

    dirt = BASE_COLOURS['gravel']

    def __init__(self, seed):
        rng = random.Random(seed)
        self.tint = periodic_noise(rng, [(1, 0, 4.0), (0, 1, 3.0), (2, 2, 3.0), (3, -1, 2.0)], P)
        density = periodic_noise(rng, [(3, 2, 0.08), (-2, 3, 0.08), (4, -1, 0.06)], P)
        seeds = CellField(rng, 27, 0.4, P).seeds
        light = GRAVEL_CHIPS['light']
        grey = GRAVEL_CHIPS['grey']
        dark = GRAVEL_CHIPS['dark']
        self.chips = []
        for sx, sy in seeds:
            keep = rng.random() < 0.72 + density[int(sy) % P][int(sx) % P]
            roll = rng.random()
            colour = lerp(light, grey, rng.random()) if roll < 0.84 else dark
            radius = rng.uniform(1.15, 2.1)
            stretch = rng.uniform(0.8, 1.25)
            angle = rng.uniform(0, math.pi)
            entry = (sx, sy, radius * stretch, radius / stretch, angle, shade(colour, rng.uniform(-10, 10)))
            if keep:
                self.chips.append(entry)
        self.random = [rng.random() for _ in self.chips]
        self.height = smooth_height(rng)
        self.owner = [[None] * P for _ in range(P)]
        self.kind = [[None] * P for _ in range(P)]
        self._place()

    def _inside(self, chip, px, py):
        """How far pixel centre (px, py) lies inside a chip's ellipse: below 1 inside, with its offset from the centre."""
        sx, sy, rx, ry, angle, _ = chip
        dx = wrap_delta(px - sx, P)
        dy = wrap_delta(py - sy, P)
        c = math.cos(angle)
        s = math.sin(angle)
        u = (dx * c + dy * s) / rx
        v = (-dx * s + dy * c) / ry
        return math.hypot(u, v), dx, dy

    def _place(self):
        buckets = {}
        for index, (sx, sy, rx, ry, _, _) in enumerate(self.chips):
            buckets.setdefault((int(sx) // 8, int(sy) // 8), []).append(index)
        best = [[(9.0, None, 0.0, 0.0)] * P for _ in range(P)]
        for y in range(P):
            for x in range(P):
                bx, by = x // 8, y // 8
                for ny in (by - 1, by, by + 1):
                    for nx in (bx - 1, bx, bx + 1):
                        for index in buckets.get((nx % (P // 8), ny % (P // 8)), ()):
                            q, dx, dy = self._inside(self.chips[index], x + 0.5, y + 0.5)
                            if q < best[y][x][0]:
                                best[y][x] = (q, index, dx, dy)
        self.best = best
        for y in range(P):
            for x in range(P):
                q, index, dx, dy = best[y][x]
                if index is not None and q <= 1.0:
                    self.owner[y][x] = index
                    self.kind[y][x] = 'chip'
        for y in range(P):
            for x in range(P):
                if self.owner[y][x] is not None:
                    continue
                up_left = self.owner[(y - 1) % P][(x - 1) % P]
                if up_left is not None and self.kind[(y - 1) % P][(x - 1) % P] == 'chip':
                    self.owner[y][x] = up_left
                    self.kind[y][x] = 'shadow'

    def base_amount(self, x, y):
        return self.tint[y][x] + (hash01(x, y, 7) - 0.5) * 7

    def albedo(self):
        out = blank()
        for y in range(P):
            for x in range(P):
                amount = self.base_amount(x, y)
                base = shade(self.dirt, amount)
                kind = self.kind[y][x]
                if kind is None:
                    out[y][x] = to_bytes(base)
                    continue
                if kind == 'shadow':
                    out[y][x] = to_bytes(shade(base, -16))
                    continue
                q, index, dx, dy = self.best[y][x]
                colour = self.chips[index][5]
                grit = (hash01(x, y, 7) - 0.5) * 5
                edge = clamp((q - 0.35) / 0.65)
                out[y][x] = to_bytes(shade(colour, bevel_light(dx, dy) * 22 * edge + grit))
        return out

    def offset(self, x, y):
        """Whole-pixel offset from texel (x, y) to the centre pixel of the chip that owns it."""
        sx, sy = self.chips[self.owner[y][x]][0], self.chips[self.owner[y][x]][1]
        return centre_offset(x, y, wrap_delta(x + 0.5 - sx, P), wrap_delta(y + 0.5 - sy, P))

    def field_map(self):
        reach = {}
        for y in range(P):
            for x in range(P):
                if self.owner[y][x] is not None:
                    ox, oy = self.offset(x, y)
                    reach[self.owner[y][x]] = max(reach.get(self.owner[y][x], 0.0), math.hypot(ox, oy))
        out = blank()
        for y in range(P):
            for x in range(P):
                amount = int(round(self.base_amount(x, y)))
                index = self.owner[y][x]
                if index is None:
                    out[y][x] = (128, 128, 128 + amount, 0)
                    continue
                ox, oy = self.offset(x, y)
                out[y][x] = (128 + ox, 128 + oy, 128 + amount, int(clamp(math.ceil(reach[index] * 16), 1, 255)))
        return out

    def height_map(self):
        return height_image(self.height, self.random, lambda x, y: self.owner[y][x])


class Stone:
    """Cobbles in dark mortar: one-pixel mortar lines, each stone lit from the top left along its own cell edges."""

    mortar = BASE_COLOURS['stone']

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
        return height_image(self.height, self.random, lambda x, y: self.field.at(x, y)[0])


if __name__ == '__main__':
    out = sys.argv[1]
    materials = (('grass', Grass(20261012)), ('gravel', Gravel(20261013)), ('stone', Stone(20261014)))
    for name, material in materials:
        write_png(f'{out}/{name}_albedo.png', P, P, material.albedo())
        write_png(f'{out}/{name}_height.png', P, P, material.height_map())
        if hasattr(material, 'field_map'):
            write_png(f'{out}/{name}_field.png', P, P, material.field_map())
        print('wrote', name)
