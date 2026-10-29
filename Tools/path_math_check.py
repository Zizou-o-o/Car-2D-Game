# Mirror of RacePath's sampling maths, to check wrapping / indexing / curvature
# outside Unity before trusting it in the game.
import math

points = [(0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0)]
closed = True


def point_at(i):
    n = len(points)
    if not closed:
        return points[max(0, min(i, n - 1))]
    return points[i % n]


def build():
    segs = len(points) if closed else len(points) - 1
    cum = [0.0] * (segs + 1)
    total = 0.0
    for i in range(segs):
        cum[i] = total
        a, b = point_at(i), point_at(i + 1)
        total += math.dist(a, b)
    cum[segs] = total
    return segs, cum, total


seg_count, cumulative, total_length = build()


def wrap(arc):
    if total_length <= 1e-5:
        return 0.0
    if closed:
        return arc % total_length
    return max(0.0, min(arc, total_length))


def locate(arc):
    low, high, seg = 0, seg_count - 1, 0
    while low <= high:
        mid = (low + high) // 2
        if cumulative[mid] <= arc:
            seg = mid
            low = mid + 1
        else:
            high = mid - 1
    length = cumulative[seg + 1] - cumulative[seg]
    t = max(0.0, min(1.0, (arc - cumulative[seg]) / length)) if length > 1e-6 else 0.0
    return seg, t


def project(world, segment):
    a, b = point_at(segment), point_at(segment + 1)
    ab = (b[0] - a[0], b[1] - a[1])
    l2 = ab[0] ** 2 + ab[1] ** 2
    if l2 <= 1e-8:
        t = 0.0
    else:
        t = max(0.0, min(1.0, ((world[0] - a[0]) * ab[0] + (world[1] - a[1]) * ab[1]) / l2))
    return cumulative[segment] + t * math.sqrt(l2)


def sqr_dist(world, segment):
    a, b = point_at(segment), point_at(segment + 1)
    ab = (b[0] - a[0], b[1] - a[1])
    l2 = ab[0] ** 2 + ab[1] ** 2
    t = 0.0 if l2 <= 1e-8 else max(0.0, min(1.0, ((world[0] - a[0]) * ab[0] + (world[1] - a[1]) * ab[1]) / l2))
    px, py = a[0] + ab[0] * t, a[1] + ab[1] * t
    return (world[0] - px) ** 2 + (world[1] - py) ** 2


def closest(world, hint=-1):
    best, best_sqr = 0, float("inf")
    if 0 <= hint < seg_count:
        for i in range(max(0, hint - 3), min(seg_count - 1, hint + 3) + 1):
            d = sqr_dist(world, i)
            if d < best_sqr:
                best_sqr, best = d, i
        stale = max(6.0, 4.0 * 2)
        if best_sqr > stale * stale:
            best_sqr = float("inf")
            for i in range(seg_count):
                d = sqr_dist(world, i)
                if d < best_sqr:
                    best_sqr, best = d, i
    else:
        for i in range(seg_count):
            d = sqr_dist(world, i)
            if d < best_sqr:
                best_sqr, best = d, i
    return best


def tangent(arc):
    seg, _ = locate(wrap(arc))
    a, b = point_at(seg), point_at(seg + 1)
    d = math.hypot(b[0] - a[0], b[1] - a[1])
    return ((b[0] - a[0]) / d, (b[1] - a[1]) / d)


def curvature(arc):
    span = max(0.4, min(2.0, total_length * 0.03))
    t0, t1 = tangent(arc), tangent(arc + span)
    dot = max(-1.0, min(1.0, t0[0] * t1[0] + t0[1] * t1[1]))
    return math.acos(dot) / span


def sample(world, ahead, hint=-1):
    nearest = closest(world, hint)
    arc = project(world, nearest)
    d = wrap(arc + ahead)
    seg, t = locate(d)
    a, b = point_at(seg), point_at(seg + 1)
    return ((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t), d, seg, nearest, curvature(d))


def show(label, world, ahead, hint=-1):
    pos, d, seg, near, curv = sample(world, ahead, hint)
    radius = 1.0 / curv if curv > 1e-4 else float("inf")
    print(f"{label:<42} aim=({pos[0]:6.2f},{pos[1]:6.2f}) seg={seg} nearest={near} "
          f"curv={curv:5.3f} radius={radius:6.2f}")


print(f"segments={seg_count} total={total_length}")
show("straight, 3 units ahead", (5.0, -2.0), 3.0)
show("around the far corner, 5 ahead", (9.0, 9.0), 5.0)
show("exactly on point 0, 10 ahead", (0.0, 0.0), 10.0)
show("exactly on point 0, 3 ahead", (0.0, 0.0), 3.0)
show("hint wraps the start line", (0.2, -0.1), 2.0, hint=3)
show("hint is stale (teleported)", (10.2, 10.2), 2.0, hint=0)

# sample along a whole lap: every point must be on the line, and the distance must wrap
ok = True
for step in range(0, 400):
    arc = step * total_length / 400.0
    seg, t = locate(wrap(arc))
    a, b = point_at(seg), point_at(seg + 1)
    x = a[0] + (b[0] - a[0]) * t
    y = a[1] + (b[1] - a[1]) * t
    on_edge = (abs(x) < 1e-6 or abs(x - 10) < 1e-6 or abs(y) < 1e-6 or abs(y - 10) < 1e-6)
    if not on_edge:
        ok = False
        print("point off the square:", arc, (x, y))
print("every sampled point lies on the square:", ok)

# curvature on the straights must be 0, at the corners it must be positive
print("curvature mid straight (arc 5):", round(curvature(5.0), 6))
print("curvature at corner (arc 9.8):", round(curvature(9.8), 6))
