"""Composes a test map from dual-grid tilesets the way the render tilemap does, over the grass, and reports small
detached opaque blobs (dots) in each path layer.

Usage: python compose.py <tiles folder> <grass.png> <output.png> [scale]. The tiles folder holds gravel_path.png and
stone_path.png; MAP below is the test layout ('G' gravel, 'S' stone, '.' grass).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngio import read_png, write_png  # noqa: E402

T = 64
MASKS = ['0010', '0101', '1011', '0011', '1001', '0111', '1111', '1110',
         '0100', '1100', '1101', '1010', '0000', '0001', '0110', '1000']
PIECE = {m: i for i, m in enumerate(MASKS)}

MAP = [
    '................',
    '.GG.......SS....',
    '.GGG.....SSS....',
    '..GGG...SSS.....',
    '...GGG.SSS......',
    '....GGGSS.......',
    '.....GG.........',
    '................',
]


def load(path):
    w, h, px = read_png(path)
    return px


def piece(tiles, index, x, y):
    return tiles[(index // 4) * T + y][(index % 4) * T + x]


def blend(dst, src):
    a = src[3] / 255.0
    return (dst[0] * (1 - a) + src[0] * a, dst[1] * (1 - a) + src[1] * a, dst[2] * (1 - a) + src[2] * a, 255)


def compose(grass, layers, scale):
    rows = len(MAP)
    cols = len(MAP[0])
    width = (cols - 1) * T
    height = (rows - 1) * T
    canvas = [[piece(grass, PIECE['1111'], x % T, y % T) for x in range(width)] for y in range(height)]
    opaque = []
    for tiles, symbol in layers:
        mask_layer = [[False] * width for _ in range(height)]
        for cy in range(rows - 1):
            for cx in range(cols - 1):
                def filled(r, c):
                    return MAP[r][c] == symbol
                key = ''.join('1' if filled(r, c) else '0' for r, c in ((cy, cx), (cy, cx + 1), (cy + 1, cx), (cy + 1, cx + 1)))
                if key == '0000':
                    continue
                index = PIECE[key]
                for y in range(T):
                    for x in range(T):
                        src = piece(tiles, index, x, y)
                        if src[3] > 0:
                            X, Y = cx * T + x, cy * T + y
                            canvas[Y][X] = blend(canvas[Y][X], src)
                            if src[3] >= 200:
                                mask_layer[Y][X] = True
        opaque.append((symbol, mask_layer))
    for symbol, layer in opaque:
        seen = [[False] * width for _ in range(height)]
        small = []
        for y in range(height):
            for x in range(width):
                if layer[y][x] and not seen[y][x]:
                    stack = [(x, y)]
                    seen[y][x] = True
                    size = 0
                    while stack:
                        px, py = stack.pop()
                        size += 1
                        for nx, ny in ((px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1)):
                            if 0 <= nx < width and 0 <= ny < height and layer[ny][nx] and not seen[ny][nx]:
                                seen[ny][nx] = True
                                stack.append((nx, ny))
                    if size < 200:
                        small.append((x, y, size))
        print(symbol, 'blobs under 200 px:', small[:20], len(small))
    out = [[tuple(int(round(c)) for c in canvas[y // scale][x // scale][:3]) + (255,) for x in range(width * scale)] for y in range(height * scale)]
    return width * scale, height * scale, out


if __name__ == '__main__':
    tiles_dir = sys.argv[1]
    grass = load(sys.argv[2])
    gravel = load(f'{tiles_dir}/gravel_path.png')
    stone = load(f'{tiles_dir}/stone_path.png')
    w, h, px = compose(grass, [(gravel, 'G'), (stone, 'S')], int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    write_png(sys.argv[3], w, h, px)
