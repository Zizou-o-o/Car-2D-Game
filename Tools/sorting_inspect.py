"""Sorting layers of road, cars and the existing trails."""
import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}


def go_name(gid):
    m = re.search(r"m_Name: (.*)", docs[gid]["body"])
    return m.group(1).strip() if m else "?"


def transform_of(go_id):
    for fid, d in docs.items():
        if d["class"] == "4" and f"m_GameObject: {{fileID: {go_id}}}" in d["body"]:
            return fid
    return None


def parent_name(tf_id):
    m = re.search(r"m_Father: \{fileID: (\d+)\}", docs[tf_id]["body"])
    if not m or m.group(1) == "0":
        return None
    for fid, d in docs.items():
        if d["class"] == "4" and fid == m.group(1):
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"])
            return go_name(go.group(1)) if go else None
    return None


print("=== SpriteRenderers (name, layerID, order, layer) ===")
rows = []
for fid, d in docs.items():
    if d["class"] != "212":
        continue
    go = re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"])
    if not go:
        continue
    gid = go.group(1)
    tf = transform_of(gid)
    pname = parent_name(tf) if tf else None
    name = go_name(gid)
    layer = re.search(r"m_SortingLayer: (-?\d+)", d["body"])
    layer_id = re.search(r"m_SortingLayerID: (-?\d+)", d["body"])
    order = re.search(r"m_SortingOrder: (-?\d+)", d["body"])
    rows.append((int(order.group(1)) if order else 0,
                 name, pname,
                 layer_id.group(1) if layer_id else "?",
                 layer.group(1) if layer else "?"))

for order, name, pname, layer_id, layer in sorted(rows):
    tag = f" ({pname})" if pname else ""
    print(f"  order {order:4d}  layerID {layer_id:>12}  layer {layer}  {name}{tag}")

print("\n=== sorting layers (TagManager) ===")
text = open("ProjectSettings/TagManager.asset", encoding="utf-8", errors="ignore").read()
in_layers = False
for line in text.splitlines():
    if "m_SortingLayers:" in line:
        in_layers = True
    elif in_layers:
        print("  ", line.strip())
        if line.strip() == "[]":
            break
