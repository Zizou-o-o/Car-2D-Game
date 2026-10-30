"""Car size + wheel scale, to place FX correctly."""
import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}


def name(gid):
    m = re.search(r"m_Name: (.*)", docs[gid]["body"])
    return m.group(1).strip() if m else "?"


def transform_of(go_id):
    for fid, d in docs.items():
        if d["class"] == "4" and f"m_GameObject: {{fileID: {go_id}}}" in d["body"]:
            return fid
    return None


def info(go_id, indent=0):
    tf = transform_of(go_id)
    b = docs[tf]["body"]
    pos = re.search(r"m_LocalPosition: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", b)
    scale = re.search(r"m_LocalScale: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", b)
    print("  " * indent + f"{name(go_id)}  pos=({pos.group(1)},{pos.group(2)})  scale=({scale.group(1)},{scale.group(2)})")

    for fid, d in docs.items():
        if f"m_GameObject: {{fileID: {go_id}}}" not in d["body"]:
            continue
        if d["class"] == "61":
            size = re.search(r"m_Size: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", d["body"])
            off = re.search(r"m_Offset: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", d["body"])
            print("  " * indent + f". BoxCollider2D size=({size.group(1)},{size.group(2)}) offset=({off.group(1)},{off.group(2)})")
        if d["class"] == "212":
            sprite = re.search(r"m_Sprite: \{fileID: (-?\d+)", d["body"])
            print("  " * indent + f". SpriteRenderer sprite={sprite.group(1) if sprite else '?'}")

    for fid, d in docs.items():
        if d["class"] == "4" and f"m_Father: {{fileID: {tf}}}" in d["body"]:
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", d["body"])
            if go:
                info(go.group(1), indent + 1)


for gid, d in docs.items():
    if d["class"] == "1" and name(gid) == "Care'":
        info(gid)
