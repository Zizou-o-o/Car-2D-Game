"""How much of the map is actually road? Guards the "everything is on road" result."""
import re

exec(open("Tools/path_on_road_check.py").read().split("# path points")[0])

xs = [p[0] for poly in road_polys for p in poly]
ys = [p[1] for poly in road_polys for p in poly]

min_x, max_x = min(xs), max(xs)
min_y, max_y = min(ys), max(ys)

print(f"road bounds: x {min_x:.1f}..{max_x:.1f}   y {min_y:.1f}..{max_y:.1f}")

inside = 0
total = 0
step = 2.0
y = min_y
while y <= max_y:
    x = min_x
    while x <= max_x:
        total += 1
        if any(point_in_polygon((x, y), poly) for poly in road_polys):
            inside += 1
        x += step
    y += step

print(f"road coverage of its own bounding box: {100.0 * inside / total:.1f}%  ({step} unit grid)")
