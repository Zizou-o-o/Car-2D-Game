"""Read-only check: are the race path points and checkpoints actually on the road?

Parses the open scene's YAML (the editor owns the file; nothing is written) and does a
point-in-polygon test against every PolygonCollider2D on the Road layer, in world space.
"""
import re
import math

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


def quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def quat_rotate_xy(q, vx, vy):
    x, y, z, w = q
    tx = 2.0 * (y * 0.0 - z * vy)
    ty = 2.0 * (z * vx - x * 0.0)
    tz = 2.0 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz))


def world_transform(tf_id):
    chain = []
    current = tf_id
    while current not in (None, "0"):
        chain.append(current)
        current = local(current)[3]

    pos = (0.0, 0.0)
    rot = (0.0, 0.0, 0.0, 1.0)
    scale = (1.0, 1.0)

    for tf in reversed(chain):
        p, q, s, _ = local(tf)
        rx, ry = quat_rotate_xy(rot, p[0] * scale[0], p[1] * scale[1])
        pos = (pos[0] + rx, pos[1] + ry)
        rot = quat_mul(rot, q)
        scale = (scale[0] * s[0], scale[1] * s[1])

    return pos, rot, scale


def world_points(tf_id, local_points):
    pos, rot, scale = world_transform(tf_id)
    out = []

    for lx, ly in local_points:
        rx, ry = quat_rotate_xy(rot, lx * scale[0], ly * scale[1])
        out.append((pos[0] + rx, pos[1] + ry))

    return out


def parse_paths(body):
    m = re.search(r"m_Points:\n\s*m_Paths:\n(.*?)\n\s*m_UseDelaunayMesh", body, re.S)
    if not m:
        return []

    paths, current = [], None

    for line in m.group(1).splitlines():
        if re.match(r"^\s*-\s*-\s*\{x:", line):
            current = []
            paths.append(current)
        pm = re.search(r"\{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)\}", line)
        if pm and current is not None:
            current.append((float(pm.group(1)), float(pm.group(2))))

    return [p for p in paths if len(p) >= 3]


def point_in_polygon(point, poly):
    x, y = point
    inside = False
    j = len(poly) - 1

    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            if x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                inside = not inside
        j = i

    return inside


road_layer_gos = {gid for gid, d in docs.items()
                  if d["class"] == "1" and re.search(r"m_Layer: 6\b", d["body"])}

road_polys = []
poly_count = 0

for fid, d in docs.items():
    if d["class"] != "60":
        continue

    go = re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"])
    if not go or go.group(1) not in road_layer_gos:
        continue

    tf = transform_of(go.group(1))
    if tf is None:
        continue

    poly_count += 1
    for path in parse_paths(d["body"]):
        road_polys.append(world_points(tf, path))

print(f"road colliders on layer 6: {poly_count}, polygons parsed: {len(road_polys)}")

# path points
racepath_guid = "c6231437c9ba4784d8ea4aa171836502"
path_points = []

for fid, d in docs.items():
    if racepath_guid not in d["body"]:
        continue
    block = re.search(r"points:\n((?:  - .*\n?)+)", d["body"])
    for line in block.group(1).splitlines():
        pm = re.search(r"x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", line)
        if pm:
            path_points.append((float(pm.group(1)), float(pm.group(2))))

off_path = [p for p in path_points
            if not any(point_in_polygon(p, poly) for poly in road_polys)]

print(f"race path points on the road: {len(path_points) - len(off_path)}/{len(path_points)}")
for p in off_path[:20]:
    print(f"   off road at {p[0]:8.2f},{p[1]:8.2f}")

# checkpoints
checkpoint_guid = "0d5d92519c5ff7c44853ca83ce1da6ea"
print("\ncheckpoints:")

rows = []
for fid, d in docs.items():
    if checkpoint_guid not in d["body"]:
        continue
    go = re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"]).group(1)
    tf = transform_of(go)
    pos = world_transform(tf)[0] if tf else (0.0, 0.0)
    index = re.search(r"index: (-?\d+)", d["body"])
    on = any(point_in_polygon(pos, poly) for poly in road_polys)
    rows.append((int(index.group(1)) if index else -1, pos, on))

for index, pos, on in sorted(rows):
    print(f"   index {index:>2}  {pos[0]:8.2f},{pos[1]:8.2f}  on road: {on}")
