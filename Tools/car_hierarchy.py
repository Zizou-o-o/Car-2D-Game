"""Dump one car's hierarchy: children, components, trail renderers, materials."""
import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}

CLASS = {
    "1": "GameObject", "4": "Transform", "50": "Rigidbody2D", "58": "CircleCollider2D",
    "60": "PolygonCollider2D", "61": "BoxCollider2D", "96": "TrailRenderer",
    "114": "MonoBehaviour", "120": "LineRenderer", "199": "ParticleSystem",
    "212": "SpriteRenderer", "198": "ParticleSystemRenderer",
}


def go_name(gid):
    m = re.search(r"m_Name: (.*)", docs[gid]["body"])
    return m.group(1).strip() if m else "?"


def transform_of(go_id):
    for fid, d in docs.items():
        if d["class"] == "4" and f"m_GameObject: {{fileID: {go_id}}}" in d["body"]:
            return fid
    return None


def transform_go(tf_id):
    m = re.search(r"m_GameObject: \{fileID: (\d+)\}", docs[tf_id]["body"])
    return m.group(1) if m else None


def children(tf_id):
    out = []
    for fid, d in docs.items():
        if d["class"] == "4" and f"m_Father: {{fileID: {tf_id}}}" in d["body"]:
            out.append(fid)
    return out


def components(go_id):
    out = []
    for fid, d in docs.items():
        if f"m_GameObject: {{fileID: {go_id}}}" in d["body"] and d["class"] != "4":
            out.append((fid, CLASS.get(d["class"], d["class"])))
    return out


def script_name(body):
    m = re.search(r"m_Script: \{fileID: \d+, guid: ([0-9a-f]+)", body)
    if not m:
        return "?"
    for meta in __import__("glob").glob("Assets/Zizou/_script/**/*.meta", recursive=True):
        text = open(meta, encoding="utf-8").read()
        if m.group(1) in text:
            return meta.replace("\\", "/").split("/")[-1].replace(".meta", "")
    return m.group(1)


def dump(go_id, depth=0):
    tf = transform_of(go_id)
    local = re.search(r"m_LocalPosition: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)", docs[tf]["body"])
    pos = (float(local.group(1)), float(local.group(2))) if local else (0, 0)
    print("  " * depth + f"- {go_name(go_id)}  at ({pos[0]:.2f}, {pos[1]:.2f})")
    for fid, kind in sorted(components(go_id), key=lambda c: c[1]):
        extra = ""
        if kind == "MonoBehaviour":
            extra = " (" + script_name(docs[fid]["body"]) + ")"
        if kind in ("TrailRenderer", "SpriteRenderer"):
            body = docs[fid]["body"]
            enabled = re.search(r"m_Enabled: (\d)", body)
            mat = re.search(r"- \{fileID: 2100000, guid: ([0-9a-f]+)", body)
            extra = f" enabled={enabled.group(1) if enabled else '?'} mat={mat.group(1) if mat else 'none'}"
        px = "  " * (depth + 1)
        print(px + f". {kind}{extra}")
    for child in children(tf):
        dump(transform_go(child), depth + 1)


for gid, d in docs.items():
    if d["class"] != "1":
        continue
    name = go_name(gid)
    if name in ("Care'", "AI Car 1"):
        dump(gid)
        print()
