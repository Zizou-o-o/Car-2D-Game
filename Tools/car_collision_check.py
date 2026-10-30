"""Car root layer + collider settings, so collision events are guaranteed."""
import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}


def name(gid):
    m = re.search(r"m_Name: (.*)", docs[gid]["body"])
    return m.group(1).strip() if m else "?"


print("=== car GameObjects: layer / tag ===")
for gid, d in docs.items():
    if d["class"] != "1":
        continue
    n = name(gid)
    if not (n == "Care'" or n.startswith("AI Car")):
        continue
    layer = re.search(r"m_Layer: (\d+)", d["body"])
    tag = re.search(r"m_TagString: (.*)", d["body"])
    print(f"  {n:<12} layer={layer.group(1) if layer else '?'} tag={tag.group(1) if tag else '?'}")

    for fid, c in docs.items():
        if f"m_GameObject: {{fileID: {gid}}}" not in c["body"]:
            continue
        if c["class"] == "61":
            trig = re.search(r"m_IsTrigger: (\d)", c["body"])
            size = re.search(r"m_Size: \{x: ([\d.eE+-]+), y: ([\d.eE+-]+)", c["body"])
            print(f"    BoxCollider2D trigger={trig.group(1) if trig else '?'} size={size.groups() if size else '?'}")
        if c["class"] == "50":
            btype = re.search(r"m_BodyType: (\d)", c["body"])
            sim = re.search(r"m_Simulated: (\d)", c["body"])
            print(f"    Rigidbody2D bodyType={btype.group(1) if btype else '?'} simulated={sim.group(1) if sim else '?'}")
