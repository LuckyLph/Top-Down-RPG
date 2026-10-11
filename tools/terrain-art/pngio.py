"""Minimal PNG reading (8-bit, any colour type, no interlacing) and RGBA writing, with the standard library only."""
import struct
import zlib


def read_png(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    pos = 8
    idat = b''
    width = height = 0
    color_type = bit_depth = 0
    palette = None
    trns = None
    while pos < len(data):
        length = struct.unpack('>I', data[pos:pos + 4])[0]
        kind = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b'IHDR':
            width, height, bit_depth, color_type = struct.unpack('>IIBB', body[:10])
        elif kind == b'PLTE':
            palette = [tuple(body[i:i + 3]) for i in range(0, len(body), 3)]
        elif kind == b'tRNS':
            trns = body
        elif kind == b'IDAT':
            idat += body
    assert bit_depth == 8, bit_depth
    channels = {6: 4, 2: 3, 3: 1, 0: 1, 4: 2}[color_type]
    raw = zlib.decompress(idat)
    stride = width * channels
    rows = []
    prev = bytearray(stride)
    i = 0
    for _ in range(height):
        f = raw[i]
        i += 1
        line = bytearray(raw[i:i + stride])
        i += stride
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = prev[x]
            c = prev[x - channels] if x >= channels else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        rows.append(line)
        prev = line
    pixels = []
    for line in rows:
        row = []
        for x in range(width):
            if color_type == 6:
                row.append(tuple(line[x * 4:x * 4 + 4]))
            elif color_type == 2:
                row.append(tuple(line[x * 3:x * 3 + 3]) + (255,))
            elif color_type == 3:
                idx = line[x]
                alpha = trns[idx] if trns is not None and idx < len(trns) else 255
                row.append(palette[idx] + (alpha,))
            elif color_type == 4:
                row.append((line[x * 2],) * 3 + (line[x * 2 + 1],))
            else:
                row.append((line[x],) * 3 + (255,))
        pixels.append(row)
    return width, height, pixels


def write_png(path, width, height, pixels):
    raw = bytearray()
    for row in pixels:
        raw.append(0)
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)

    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += chunk(b'IEND', b'')
    open(path, 'wb').write(out)
