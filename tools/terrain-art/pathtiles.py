"""Generates the stone and gravel dual-grid path tilesets (4x4 pieces of 64 px).

Usage: python pathtiles.py <output folder>. Writes gravel_path.png and stone_path.png, which go over the files of the
same name in Assets/Tiles/tilesets (their .meta files, and so their slices, stay as they are).

Each piece is drawn from an exact signed distance to the data cells around its corner (rounded outer corners,
filleted inner ones), pushed by smooth noise that repeats every 64 px and is too gentle to pinch off islands.
Stones and pebbles come from fields that also repeat every 64 px, so pieces that meet agree along their edge.
The edge waves with zero amplitude are kept on purpose: each wave draws its phase from the shared random source,
so removing one would reshuffle the chip and stone layout that was tuned after it.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngio import write_png  # noqa: E402

T = 64
MASKS = ['0010', '0101', '1011', '0011', '1001', '0111', '1111', '1110',
         '0100', '1100', '1101', '1010', '0000', '0001', '0110', '1000']
FLIPS = [(-1, -1), (1, -1), (-1, 1), (1, 1)]
LIGHT = (-0.6, -0.8)
GRAVEL_EDGE = [(1, 1, 0.7), (2, -1, 0.35), (1, 2, 0.3), (3, 2, 0.0), (-2, 3, 0.0)]
STONE_EDGE = [(1, 1, 0.6), (2, -1, 0.3), (1, 2, 0.25), (3, 2, 0.0), (-2, 3, 0.0)]


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else hi if v > hi else v


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def shade(c, amount):
    return tuple(clamp(c[i] + amount, 0, 255) for i in range(3))


def sd_quarter(x, y):
    if x >= 0 and y >= 0:
        return -min(x, y)
    return math.hypot(min(x, 0.0), min(y, 0.0))


def convex(u, v, r):
    return sd_quarter(u - r, v - r) - r


def shape_sd(mask, u, v, r_out, r_in):
    filled = [k for k in range(4) if mask[k] == '1']
    if not filled:
        return 100.0
    if len(filled) == 4:
        return -100.0
    if len(filled) == 1:
        sx, sy = FLIPS[filled[0]]
        return convex(sx * u, sy * v, r_out)
    if len(filled) == 2:
        s = set(filled)
        if s == {0, 1}:
            return v
        if s == {2, 3}:
            return -v
        if s == {0, 2}:
            return u
        if s == {1, 3}:
            return -u
        return min(convex(FLIPS[k][0] * u, FLIPS[k][1] * v, r_out) for k in filled)
    empty = [k for k in range(4) if mask[k] == '0'][0]
    sx, sy = FLIPS[empty]
    return -convex(sx * u, sy * v, r_in)


def periodic_noise(rng, terms):
    comps = [(kx, ky, amp, rng.uniform(0, 2 * math.pi)) for kx, ky, amp in terms]
    grid = [[0.0] * T for _ in range(T)]
    for y in range(T):
        for x in range(T):
            grid[y][x] = sum(a * math.sin(2 * math.pi * (kx * x + ky * y) / T + ph) for kx, ky, a, ph in comps)
    return grid


def max_slope(terms):
    return sum(abs(a) * 2 * math.pi * math.hypot(kx, ky) / T for kx, ky, a in terms)


def hash01(x, y, salt):
    h = (x * 374761393 + y * 668265263 + salt * 1442695041) & 0xffffffff
    h = ((h ^ (h >> 13)) * 1274126177) & 0xffffffff
    return ((h ^ (h >> 16)) & 0xffff) / 65535.0


def wrap_delta(d):
    return d - T * round(d / T)


class CellField:
    """Jittered seeds on a grid that repeats every 64 px, and for every pixel its nearest seed, the offset to it and
    the distance to the edge of its cell."""

    def __init__(self, rng, cells, jitter):
        size = T / cells
        self.seeds = []
        for j in range(cells):
            for i in range(cells):
                self.seeds.append((((i + 0.5 + rng.uniform(-jitter, jitter)) * size) % T,
                                   ((j + 0.5 + rng.uniform(-jitter, jitter)) * size) % T))
        self.table = [[self._nearest(x, y) for x in range(T)] for y in range(T)]

    def _nearest(self, x, y):
        px, py = x + 0.5, y + 0.5
        near = []
        for index, (sx, sy) in enumerate(self.seeds):
            dx = wrap_delta(px - sx)
            dy = wrap_delta(py - sy)
            near.append((dx * dx + dy * dy, dx, dy, index))
        near.sort(key=lambda n: n[0])
        d1, dx1, dy1, index = near[0]
        border = 1e9
        for d2, dx2, dy2, _ in near[1:10]:
            gap = math.hypot(dx2 - dx1, dy2 - dy1)
            if gap > 1e-6:
                border = min(border, (d2 - d1) / (2 * gap))
        return index, dx1, dy1, border

    def at(self, x, y):
        return self.table[y % T][x % T]


class Style:
    """One tileset's look: edge geometry, the bands at its border and its interior."""

    def __init__(self, seed, r_out, r_in, edge_terms, rim, outline, shadow, shadow_alpha, shadow_width):
        rng = random.Random(seed)
        self.rng = rng
        self.r_out = r_out
        self.r_in = r_in
        assert max_slope(edge_terms) < 0.95, max_slope(edge_terms)
        self.edge = periodic_noise(rng, edge_terms)
        self.rim = rim
        self.outline = outline
        self.shadow = shadow
        self.shadow_alpha = shadow_alpha
        self.shadow_width = shadow_width

    def distance(self, mask, x, y):
        u = x - 32 + 0.5
        v = y - 32 + 0.5
        return shape_sd(mask, u, v, self.r_out, self.r_in) + self.edge[y % T][x % T]

    def depth_at(self, mask, x, y, dx, dy):
        """How deep inside the shape the seed of the cell holding pixel (x, y) lies, the seed being (dx, dy) away."""
        return -self.distance(mask, int(math.floor(x + 0.5 - dx)), int(math.floor(y + 0.5 - dy)))

    def bevel(self, mask, x, y, dx, dy, border, inside):
        """The light on a chip or stone pixel: the edge it is nearest, its own cell's or the cut along the rim, faces
        towards or away from the light."""
        if inside < border:
            gx = self.distance(mask, x + 1, y) - self.distance(mask, x - 1, y)
            gy = self.distance(mask, x, y + 1) - self.distance(mask, x, y - 1)
            length = math.hypot(gx, gy) or 1.0
            return gx / length * LIGHT[0] + gy / length * LIGHT[1]
        length = math.hypot(dx, dy) or 1.0
        return dx / length * LIGHT[0] + dy / length * LIGHT[1]

    def interior(self, mask, x, y, depth):
        raise NotImplementedError

    def pixel(self, mask, x, y):
        d = self.distance(mask, x, y)
        if d >= 0.5 + self.shadow_width:
            return (0, 0, 0, 0)
        if d >= 0.5:
            alpha = self.shadow_alpha * (1 - (d - 0.5) / self.shadow_width)
            return self.shadow + (alpha,)
        if d > -0.5:
            alpha = self.shadow_alpha + (1 - self.shadow_alpha) * (0.5 - d)
            return self.outline + (alpha,)
        depth = -d
        colour = self.interior(mask, x, y, depth)
        if depth < 1.5:
            colour = lerp(self.outline, colour, clamp(depth - 0.5))
        return colour + (1.0,)


class Gravel(Style):
    """Packed dirt darkening towards a soft rim, half covered in small chips that cluster here and there. A chip is
    drawn whole or not at all: only when its seed lies clear of the rim."""

    def __init__(self, seed):
        super().__init__(seed, r_out=29.0, r_in=22.0,
                         edge_terms=GRAVEL_EDGE,
                         rim=3.5, outline=(102, 84, 60), shadow=(38, 52, 24), shadow_alpha=0.32, shadow_width=2.0)
        self.dirt = (146, 122, 88)
        self.rim_colour = (126, 104, 76)
        self.tint = periodic_noise(self.rng, [(1, 0, 4.0), (0, 1, 3.0), (2, 2, 3.0), (3, -1, 2.0)])
        density = periodic_noise(self.rng, [(3, 2, 0.05), (-2, 3, 0.05), (4, -1, 0.04)])
        self.field = CellField(self.rng, 13, 0.4)
        light = (180, 164, 132)
        grey = (160, 152, 138)
        dark = (126, 110, 88)
        self.chips = []
        for sx, sy in self.field.seeds:
            keep = self.rng.random() < 0.8 + density[int(sy) % T][int(sx) % T]
            roll = self.rng.random()
            colour = lerp(light, grey, self.rng.random()) if roll < 0.86 else dark
            self.chips.append((keep, shade(colour, self.rng.uniform(-9, 9)), self.rng.uniform(0.6, 1.0)))

    def interior(self, mask, x, y, depth):
        grit = (hash01(x % T, y % T, 7) - 0.5) * 7
        base = shade(self.dirt, self.tint[y % T][x % T] + grit)
        if depth < self.rim:
            return lerp(shade(self.rim_colour, grit), base, (depth / self.rim) ** 1.5)
        index, dx, dy, border = self.field.at(x, y)
        keep, colour, gap = self.chips[index]
        if not keep or self.depth_at(mask, x, y, dx, dy) < self.rim + 2.5:
            return base
        inside = depth - self.rim
        edge = min(border, inside)
        if edge < gap:
            return shade(base, -10 if dx + dy > 0 else -3)
        lit = self.bevel(mask, x, y, dx, dy, border, inside)
        bevel = clamp(1 - (edge - gap) / 1.3)
        return shade(colour, lit * 18 * bevel + grit * 0.6)


class Stone(Style):
    """Cobbles in dark mortar inside a soft earthy rim. Stones whose seed lies near the rim are left out, so no thin
    slivers line it; the rest are cut along a smooth line and bevelled to that cut as to their own edges."""

    def __init__(self, seed):
        super().__init__(seed, r_out=29.0, r_in=22.0,
                         edge_terms=STONE_EDGE,
                         rim=3.0, outline=(76, 68, 58), shadow=(34, 44, 22), shadow_alpha=0.34, shadow_width=2.0)
        self.mortar = (98, 88, 76)
        self.rim_colour = (112, 98, 80)
        self.field = CellField(self.rng, 5, 0.32)
        palette = [(152, 148, 138), (166, 160, 148), (140, 136, 126), (176, 170, 158), (130, 126, 118),
                   (158, 150, 136), (146, 144, 138)]
        self.colours = [self.rng.choice(palette) for _ in self.field.seeds]

    def interior(self, mask, x, y, depth):
        grit = (hash01(x % T, y % T, 11) - 0.5) * 7
        if depth < self.rim:
            return shade(lerp(self.rim_colour, self.mortar, depth / self.rim), grit)
        index, dx, dy, border = self.field.at(x, y)
        if self.depth_at(mask, x, y, dx, dy) < self.rim + 3.5:
            return shade(self.mortar, grit)
        inside = depth - self.rim
        edge = min(border, inside)
        if edge < 1.0:
            return shade(self.mortar, grit)
        lit = self.bevel(mask, x, y, dx, dy, border, inside)
        bevel = clamp(1 - (edge - 1.0) / 2.5)
        radial = clamp(math.hypot(dx, dy) / 7.0)
        amount = lit * (18 * bevel + 4 * radial) + grit
        if bevel > 0 and lit < -0.35:
            amount -= 8 * bevel
        return shade(self.colours[index], amount)


def render(style):
    pixels = [[(0, 0, 0, 0)] * (4 * T) for _ in range(4 * T)]
    for index, mask in enumerate(MASKS):
        ox = (index % 4) * T
        oy = (index // 4) * T
        for y in range(T):
            for x in range(T):
                r, g, b, a = style.pixel(mask, x, y)
                pixels[oy + y][ox + x] = (int(round(r)), int(round(g)), int(round(b)), int(round(a * 255)))
    return pixels


if __name__ == '__main__':
    out = sys.argv[1]
    for name, style in (('gravel_path', Gravel(20261010)), ('stone_path', Stone(20261011))):
        write_png(f'{out}/{name}.png', 4 * T, 4 * T, render(style))
        print('wrote', name)
