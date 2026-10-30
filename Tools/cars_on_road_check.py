"""Are the cars themselves on the road, and how far from the racing line do they start?"""
import re

exec(open("Tools/path_on_road_check.py").read().split("# path points")[0])

# cars: GameObjects whose name starts with "Care" or "AI Car"
cars = []
for gid, d in docs.items():
    if d["class"] != "1":
        continue
    name = re.search(r"m_Name: (.*)", d["body"]).group(1).strip()
    if not (name.startswith("Care") or name.startswith("AI Car")):
        continue
    tf = transform_of(gid)
    pos = world_transform(tf)[0] if tf else (0.0, 0.0)
    cars.append((name, pos))

print(f"cars found: {len(cars)}")
for name, pos in sorted(cars):
    on = any(point_in_polygon(pos, poly) for poly in road_polys)
    print(f"  {name:<12} {pos[0]:8.2f},{pos[1]:8.2f}  on road: {on}")
