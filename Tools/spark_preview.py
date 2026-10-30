"""Render the generated CarEffects textures to PNGs so the shape can be eyeballed."""
import math
import struct
import zlib


def write_png(path, width, height, pixels):
    raw = b"".join(b"\x00" + bytes(row) for row in pixels)
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


SIZE = 64
half_length = SIZE * 0.30
radius = SIZE * 0.12
center = (SIZE - 1) * 0.5

alphas = [[0.0] * SIZE for _ in range(SIZE)]
for y in range(SIZE):
    for x in range(SIZE):
        dx = x - center
        dy = y - center
        closest = max(-half_length, min(half_length, dx))
        distance = math.hypot(dx - closest, dy) / radius
        alpha = max(0.0, min(1.0, 1.0 - distance))
        alpha = alpha * alpha * (3.0 - 2.0 * alpha)
        core = max(0.0, min(1.0, 1.0 - abs(dy) / (radius * 0.6)))
        core *= core
        alpha = max(alpha, core * 0.95)
        alpha *= max(0.0, min(1.0, 1.0 - (abs(dx) / (half_length + radius)) ** 8))
        alphas[y][x] = alpha


def shade(base):
    rows = []
    for y in range(SIZE - 1, -1, -1):  # PNG rows top-down, texture y is bottom-up
        row = bytearray()
        for x in range(SIZE):
            a = alphas[y][x]
            r = int(base[0] * a * 255)
            g = int(base[1] * a * 255)
            b = int(base[2] * a * 255)
            row += bytes((r, g, b, 255))
        rows.append(row)
    return rows


def scale(rows, factor):
    out = []
    for row in rows:
        wide = bytearray()
        for i in range(0, len(row), 4):
            wide += row[i:i + 4] * factor
        for _ in range(factor):
            out.append(wide)
    return out


# orange spark on a dark background
write_png("Tools/spark_preview.png", SIZE * 6, SIZE * 6, scale(shade((1.0, 0.72, 0.18)), 6))
# the same shape as white-hot
write_png("Tools/spark_preview_white.png", SIZE * 6, SIZE * 6, scale(shade((1.0, 1.0, 1.0)), 6))

# a few coverage numbers
lit = sum(1 for row in alphas for a in row if a > 0.05)
print(f"pixels with alpha > 0.05: {lit} / {SIZE * SIZE} ({100.0 * lit / (SIZE * SIZE):.1f}%)")
print(f"streak length in texture: {2 * (half_length + radius):.1f} / {SIZE} px")
print(f"streak width in texture:  {2 * radius:.1f} / {SIZE} px")
print(f"on screen at 0.12 world size: {2 * (half_length + radius) / SIZE * 0.12:.3f} long x "
      f"{2 * radius / SIZE * 0.12:.3f} wide units")
