"""Car facing direction vs the direction of the racing line at the start."""
import re
import math

exec(open("Tools/path_on_road_check.py").read().split("# path points")[0])

cars = []
for gid, d in docs.items():
    if d["class"] != "1":
        continue
    name = re.search(r"m_Name: (.*)", d["body"]).group(1).strip()
    if not (name.startswith("Care") or name.startswith("AI Car")):
        continue
    tf = transform_of(gid)
    pos, rot, _ = world_transform(tf)
    x, y, z, w = rot
    angle = 2.0 * math.degrees(math.atan2(z, w))
    up = (-math.sin(math.radians(angle)), math.cos(math.radians(angle)))
    cars.append((name, pos, angle, up))

# first path direction
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

dx = points[1][0] - points[0][0]
dy = points[1][1] - points[0][1]
length = math.hypot(dx, dy)
direction = (dx / length, dy / length)
print(f"racing line leaves the start line heading ({direction[0]:.2f}, {direction[1]:.2f})")
print(f"start line at {points[0]}, next path point at {points[1]}")
print()

for name, pos, angle, up in sorted(cars):
    dot = up[0] * direction[0] + up[1] * direction[1]
    diff = math.degrees(math.acos(max(-1.0, min(1.0, dot))))
    print(f"  {name:<10} at {pos[0]:7.2f},{pos[1]:7.2f}  angle {angle:7.1f}  "
          f"facing ({up[0]:5.2f},{up[1]:5.2f})  {diff:5.1f} deg from the line")
