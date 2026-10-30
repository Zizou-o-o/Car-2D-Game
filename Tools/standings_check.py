"""Mirror of RacePositionManager progress maths, fed with the real racing line.

Checks two things we can't see without Play mode:
  1. the starting grid ranks front row first (progress is measured from the start line),
  2. driving the line for 3 laps keeps increasing progress by exactly one lap per lap,
     with the hinted nearest-segment search never drifting to the wrong part of the track.
"""
import math
import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}


def transform_of(go_id):
    for fid, d in docs.items():
        if d["class"] == "4" and f"m_GameObject: {{fileID: {go_id}}}" in d["body"]:
            return fid
    return None


def local(tf_id):
    b = docs[tf_id]["body"]
    pos = re.search(r"m_LocalPosition: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", b)
    rot = re.search(r"m_LocalRotation: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), z: (-?[\d.eE+-]+), w: (-?[\d.eE+-]+)", b)
    scale = re.search(r"m_LocalScale: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", b)
    father = re.search(r"m_Father: \{fileID: (\d+)\}", b)
    return ((float(pos.group(1)), float(pos.group(2))) if pos else (0.0, 0.0),
            tuple(float(rot.group(i)) for i in range(1, 5)) if rot else (0.0, 0.0, 0.0, 1.0),
            (float(scale.group(1)), float(scale.group(2))) if scale else (1.0, 1.0),
            father.group(1) if father else "0")


def world_position(go_id):
    tf = transform_of(go_id)
    chain = []
    current = tf
    while current not in (None, "0"):
        chain.append(current)
        current = local(current)[3]

    pos, rot = (0.0, 0.0), (0.0, 0.0, 0.0, 1.0)
    for tf_id in reversed(chain):
        p, q, s, _ = local(tf_id)
        x, y, z, w = rot
        vx, vy = p[0] * s[0], p[1] * s[1]
        tx = 2.0 * (y * 0.0 - z * vy)
        ty = 2.0 * (z * vx - x * 0.0)
        tz = 2.0 * (x * vy - y * vx)
        rx = vx + w * tx + (y * tz - z * ty)
        ry = vy + w * ty + (z * tx - x * tz)
        pos = (pos[0] + rx, pos[1] + ry)
        rot = (w * x + x * w + y * z - z * y,
               w * y - x * z + y * w + z * x,
               w * z + x * y - y * x + z * w,
               w * w - x * x - y * y - z * z)
    return pos


# ---- racing line ----
racepath_guid = "c6231437c9ba4784d8ea4aa171836502"
points = []
for fid, d in docs.items():
    if racepath_guid not in d["body"]:
        continue
    block = re.search(r"points:\n((?:  - .*\n?)+)", d["body"])
    for line in block.group(1).splitlines():
        pm = re.search(r"x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", line)
        if pm:
            points.append((float(pm.group(1)), float(pm.group(2))))

segments = list(zip(points, points[1:] + points[:1]))
lengths = [math.dist(a, b) for a, b in segments]
arcs = [0.0]
for length in lengths[:-1]:
    arcs.append(arcs[-1] + length)
total = sum(lengths)
width = 4.0


def wrap_arc(arc):
    return arc % total


def wrap_signed(distance):
    while distance > total * 0.5:
        distance -= total
    while distance < -total * 0.5:
        distance += total
    return distance


def clamp01(v):
    return max(0.0, min(1.0, v))


def project(point, segment):
    (ax, ay), (bx, by) = segments[segment]
    dx, dy = bx - ax, by - ay
    length_sqr = dx * dx + dy * dy
    t = 0.0 if length_sqr <= 1e-9 else clamp01(((point[0] - ax) * dx + (point[1] - ay) * dy) / length_sqr)
    return (ax + dx * t, ay + dy * t), arcs[segment] + lengths[segment] * t


def sqr_distance(point, segment):
    (ax, ay), (bx, by) = segments[segment]
    dx, dy = bx - ax, by - ay
    length_sqr = dx * dx + dy * dy
    t = 0.0 if length_sqr <= 1e-9 else clamp01(((point[0] - ax) * dx + (point[1] - ay) * dy) / length_sqr)
    return (point[0] - (ax + dx * t)) ** 2 + (point[1] - (ay + dy * t)) ** 2


def nearest_segment(point, hint=-1):
    """Mirror of RacePath.FindClosestSegment (exact segment distance, wrap-aware hint)."""
    best, best_sqr = 0, float("inf")

    if 0 <= hint < len(segments):
        for offset in range(-3, 4):
            index = (hint + offset) % len(segments)
            sqr = sqr_distance(point, index)
            if sqr < best_sqr:
                best_sqr, best = sqr, index

        stale = max(6.0, width * 2.0)
        if best_sqr > stale * stale:
            best, best_sqr = 0, float("inf")
    if best_sqr == float("inf"):
        for index in range(len(segments)):
            sqr = sqr_distance(point, index)
            if sqr < best_sqr:
                best_sqr, best = sqr, index

    closest, arc = project(point, best)
    return best, wrap_arc(arc)


def arc_at(point, hint=-1):
    return nearest_segment(point, hint)[1]


# ---- start line ----
checkpoint_guid = "0d5d92519c5ff7c44853ca83ce1da6ea"
start = None
for fid, d in docs.items():
    if checkpoint_guid in d["body"] and re.search(r"isStart: 1", d["body"]):
        start = world_position(re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"]).group(1))
start_arc = arc_at(start)
print(f"track length {total:.2f}, start arc {start_arc:.2f}, start at {start[0]:.2f},{start[1]:.2f}")

# ---- grid ----
cars = []
for gid, d in docs.items():
    if d["class"] != "1":
        continue
    name = re.search(r"m_Name: (.*)", d["body"]).group(1).strip()
    if name.startswith(("Care", "AI Car")):
        pos = world_position(gid)
        cars.append((name, pos, wrap_signed(arc_at(pos) - start_arc)))

print("\nstarting grid by progress from the start line:")
for name, pos, progress in sorted(cars, key=lambda c: -c[2]):
    print(f"  {name:<10} at {pos[0]:7.2f},{pos[1]:7.2f}   progress {progress:8.2f}")

# ---- drive the line for 3 laps with the hinted search ----
print("\nsimulated laps (hinted nearest-segment search):")

step = 0.5
travelled = 0.0
hint = -1
previous_arc = None
progress = 0.0
worst = 0.0
laps = 0

while travelled <= 3 * total:
    segment = 0
    while segment + 1 < len(segments) and arcs[segment + 1] <= (travelled % total):
        segment += 1
    t = ((travelled % total) - arcs[segment]) / lengths[segment]
    (ax, ay), (bx, by) = segments[segment]
    point = (ax + (bx - ax) * t, ay + (by - ay) * t)

    hint, arc = nearest_segment(point, hint)

    if previous_arc is None:
        progress = wrap_signed(arc - start_arc)
    else:
        delta = wrap_signed(arc - previous_arc)
        progress += delta
        worst = max(worst, abs(delta))

    previous_arc = arc
    travelled += step

    lap_now = int(travelled // total)
    if lap_now > laps:
        laps = lap_now
        print(f"  after {laps} lap(s): travelled {laps * total:7.1f}  progress {progress:8.2f}  "
              f"error {progress - laps * total:+.3f}")

print(f"  expected 3 laps = {3 * total:.2f}, got {progress:.2f}, error {progress - 3 * total:+.3f}")
print(f"  largest single-frame arc step: {worst:.3f} (walk step {step})")
