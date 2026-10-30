import re
from collections import Counter

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()

docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}

classes = Counter(d["class"] for d in docs.values())
print("documents by class:")
for cls, count in sorted(classes.items(), key=lambda x: -x[1]):
    print(f"  class {cls:>4}: {count}")

offroad_guid = "77597dbba7d728d40bad6302841ba069"   # OffRoadSlowdown
controller_guid = "382fa2e7e9cf156419c9747ba3f2b2a2"  # TopDown2DCarController

print("\nOffRoadSlowdown components:",
      sum(1 for d in docs.values() if offroad_guid in d["body"]))
print("TopDown2DCarController components:",
      sum(1 for d in docs.values() if controller_guid in d["body"]))

# layer 6 = Road
road_objects = 0
for gid, d in docs.items():
    if d["class"] == "1":  # GameObject
        m = re.search(r"m_Layer: (\d+)", d["body"])
        if m and int(m.group(1)) == 6:
            road_objects += 1
print("GameObjects on layer 6 (Road):", road_objects)

# collider components
poly = sum(1 for d in docs.values() if d["class"] == "60")
box = sum(1 for d in docs.values() if d["class"] == "61")
print("PolygonCollider2D:", poly, " BoxCollider2D:", box)

# cars: show name, layer, and speedLimit if the controller is new enough
print("\ncars:")
for gid, d in docs.items():
    if d["class"] != "1":
        continue
    body = d["body"]
    name = re.search(r"m_Name: (.*)", body).group(1).strip()
    if not name.lower().startswith(("care", "ai car")):
        continue
    layer = re.search(r"m_Layer: (\d+)", body).group(1)
    tags = []
    if controller_guid in scene:
        pass
    print(f"  {name:<12} layer={layer}")
