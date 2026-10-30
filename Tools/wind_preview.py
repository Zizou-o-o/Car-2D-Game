"""Preview the world-space speed wind around the car (positions, size, density)."""
import math
import random
import struct
import zlib


def write_png(path, width, height, pixels):
    raw = b"".join(b"\x00" + b"".join(bytes(px) if not isinstance(px, int) else bytes((px,))
                                          for px in row)
                   for row in pixels)

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


W, H = 700, 400
PPU = 55.0                      # pixels per world unit
CX, CY = W / 2, H / 2
CAR_V = 15.0                    # units/second, heading up (+Y)
RATE = 90.0
LIFE = 0.32
SIZE = 0.45
ALPHA = 0.5
STRENGTH = 1.0

random.seed(7)

# Simulate emission for 2 s, then keep the particles that are still alive.
particles = []                   # (x, y, vx, vy, age, life, size)
emission = 0.0
dt = 1.0 / 60.0
t = 0.0

while t < 2.0:
    emission += RATE * STRENGTH * dt
    count = min(5, int(emission))
    emission -= count
    for _ in range(count):
        side = -1.0 if random.random() < 0.5 else 1.0
        x = side * random.uniform(0.30, 1.10)
        y = random.uniform(-0.7, 1.5)
        factor = random.uniform(0.55, 0.85)
        vx = random.uniform(-0.3, 0.3)
        vy = CAR_V * factor + random.uniform(-0.3, 0.3)
        particles.append([x, y, vx, vy, 0.0, LIFE * random.uniform(0.7, 1.3),
                          SIZE * random.uniform(0.7, 1.35)])

    for p in particles:
        p[0] += (p[2] - 0.0) * dt          # relative to the car
        p[1] += (p[3] - CAR_V) * dt
        p[4] += dt
    particles = [p for p in particles if p[4] < p[5]]
    t += dt

print(f"alive at full speed: {len(particles)} streaks")

pixels = [[(58, 62, 68)] * W for _ in range(H)]


def blend(x, y, a, r=255, g=255, b=255):
    if 0 <= x < W and 0 <= y < H:
        br, bg, bb = pixels[y][x]
        pixels[y][x] = (int(br * (1 - a) + r * a),
                        int(bg * (1 - a) + g * a),
                        int(bb * (1 - a) + b * a))


# road-ish lane markers so the motion direction is readable
for y in range(0, H, 40):
    for x in range(W // 2 - 2, W // 2 + 2):
        blend(x, y, 0.25)


def gradient_alpha(u):
    # matches the ColorOverLifetime gradient: 0 -> 1 at .18 -> 1 at .55 -> 0
    if u < 0.18:
        return u / 0.18
    if u < 0.55:
        return 1.0
    return max(0.0, (1.0 - u) / 0.45)


for (x, y, vx, vy, age, life, size) in particles:
    px = CX + x * PPU
    py = CY - y * PPU
    length = size * 0.77 * PPU
    half = length * 0.5
    a = ALPHA * gradient_alpha(age / life) * 0.9

    # streak lies along the heading (straight up on screen)
    for s in range(int(-half), int(half) + 1):
        fade = 1.0 - (abs(s) / (half + 1)) ** 3
        for w in range(-1, 2):
            blend(int(px + w), int(py + s), a * fade * (1.0 - abs(w) * 0.35))

# the car: 0.29 x 0.64 units
cw, ch = int(0.29 * PPU), int(0.64 * PPU)
cx, cy = int(CX), int(CY)
for y in range(cy - ch // 2, cy + ch // 2):
    for x in range(cx - cw // 2, cx + cw // 2):
        pixels[y][x] = (205, 85, 65)

write_png("Tools/wind_preview.png", W, H, pixels)
print("wrote Tools/wind_preview.png")
