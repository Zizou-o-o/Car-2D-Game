"""Quick read-only inspector for Unity .unity scene files (YAML).

Usage:
    python Tools/scene_inspect.py Assets/Scenes/SampleScene.unity            # tree
    python Tools/scene_inspect.py Assets/Scenes/SampleScene.unity --colliders
    python Tools/scene_inspect.py Assets/Scenes/SampleScene.unity --scripts
"""
import re
import sys
from collections import defaultdict

CLASS_NAMES = {
    1: "GameObject", 4: "Transform", 50: "Rigidbody2D",
    58: "CircleCollider2D", 60: "PolygonCollider2D", 61: "BoxCollider2D",
    68: "EdgeCollider2D", 212: "SpriteRenderer", 114: "MonoBehaviour",
    19719996: "TilemapCollider2D", 1839735485: "Tilemap", 20: "Camera",
    81: "AudioListener", 196: "Canvas", 157: "Light2D",
}

BLOCK_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)", re.M)


def blocks(text):
    """Yield (class_id, file_id, body_lines)."""
    marks = list(BLOCK_RE.finditer(text))
    for i, m in enumerate(marks):
        start = m.end()
        end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
        yield int(m.group(1)), m.group(2), text[start:end]


def field(body, name, default=None):
    m = re.search(r"^  %s: (.+)$" % re.escape(name), body, re.M)
    return m.group(1).strip() if m else default


def main():
    path = sys.argv[1]
    mode = sys.argv[2] if len(sys.argv) > 2 else ""
    text = open(path, encoding="utf-8-sig").read()

    gos, transforms, comp_to_go, colliders, scripts = {}, {}, {}, [], []
    for cid, fid, body in blocks(text):
        if cid == 1:
            comps = re.findall(r"- component: \{fileID: (-?\d+)\}", body)
            gos[fid] = {
                "name": field(body, "m_Name", "?"),
                "layer": int(field(body, "m_Layer", 0)),
                "tag": field(body, "m_TagString", "Untagged"),
                "active": field(body, "m_IsActive", "1") == "1",
                "comps": comps,
            }
            for c in comps:
                comp_to_go[c] = fid
        elif cid == 4:
            transforms[fid] = {
                "go": field(body, "m_GameObject", "{fileID: 0}"),
                "father": field(body, "m_Father", "{fileID: 0}"),
                "children": re.findall(r"- \{fileID: (-?\d+)\}", body),
            }
        elif cid in (58, 60, 61, 68, 19719996):
            colliders.append((cid, fid, body))
        elif cid == 114:
            scripts.append((fid, body))

    def go_of(ref):
        m = re.search(r"fileID: (-?\d+)", ref or "")
        return m.group(1) if m else "0"

    # map transform -> go, go -> transform
    tr_go = {t: go_of(v["go"]) for t, v in transforms.items()}
    go_tr = {v: k for k, v in tr_go.items()}

    def comp_name(cid, body):
        if cid == 114:
            ident = field(body, "m_EditorClassIdentifier")
            return ident.split("::")[-1] if ident else "MonoBehaviour"
        return CLASS_NAMES.get(cid, "Class%s" % cid)

    if mode == "--scripts":
        for cid, fid, body in sorted(scripts, key=lambda s: comp_name(114, s[1])):
            go = gos.get(comp_to_go.get(fid, ""), {})
            print("%-32s %-30s layer=%s" % (comp_name(114, body), go.get("name", "?"), go.get("layer", "?")))
        return

    if mode == "--colliders":
        for cid, fid, body in colliders:
            go = gos.get(comp_to_go.get(fid, ""), {})
            extra = ""
            if cid == 61:
                extra = "size=%s offset=%s trigger=%s" % (
                    field(body, "m_Size"), field(body, "m_Offset"), field(body, "m_IsTrigger"))
            elif cid == 58:
                extra = "radius=%s offset=%s trigger=%s" % (
                    field(body, "m_Radius"), field(body, "m_Offset"), field(body, "m_IsTrigger"))
            elif cid == 60:
                extra = "paths=%d trigger=%s" % (body.count("- - {x:"), field(body, "m_IsTrigger"))
            else:
                extra = "trigger=%s" % field(body, "m_IsTrigger")
            print("%-18s %-34s layer=%s tag=%-10s %s" % (
                CLASS_NAMES.get(cid, cid), go.get("name", "?"), go.get("layer"), go.get("tag"), extra))
        return

    # tree
    roots = [t for t, v in transforms.items() if go_of(v["father"]) == "0"]

    def label(t):
        go = gos.get(tr_go.get(t, ""), {})
        comps = [comp_name(cid, body) for cid, fid, body in all_comps if comp_to_go.get(fid) == tr_go.get(t)]
        return "%s  [layer %s%s]  {%s}" % (
            go.get("name", "?"), go.get("layer"),
            ", tag=" + go["tag"] if go.get("tag") not in (None, "Untagged") else "",
            ", ".join(comps))

    all_comps = [(cid, fid, body) for cid, fid, body in blocks(text) if cid in CLASS_NAMES and cid != 4]

    def walk(t, depth=0):
        print("  " * depth + label(t))
        for c in transforms[t]["children"]:
            if c in transforms:
                walk(c, depth + 1)

    for r in roots:
        walk(r)


if __name__ == "__main__":
    main()
