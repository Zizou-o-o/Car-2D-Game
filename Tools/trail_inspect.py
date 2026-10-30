import re

scene = open("Assets/Scenes/SampleScene.unity", encoding="utf-8-sig").read()
docs = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)(.*?)$(.*?)(?=^--- |\Z)", scene, re.S | re.M):
    docs[m.group(2)] = {"class": m.group(1), "body": m.group(4)}

body = docs["436843394"]["body"]
print("\n".join(body.splitlines()[:90]))

print("\n=== all trail renderers: time / width / sorting ===")
for fid, d in docs.items():
    if d["class"] != "96":
        continue
    b = d["body"]

    def grab(pattern, default="?"):
        m = re.search(pattern, b)
        return m.group(1) if m else default

    print(fid,
          "time", grab(r"m_Time: ([\d.]+)"),
          "width", grab(r"m_WidthMultiplier: ([\d.]+)"),
          "minVertex", grab(r"m_MinVertexDistance: ([\d.]+)"),
          "sortOrder", grab(r"m_SortingOrder: (-?\d+)"),
          "sortLayer", grab(r"m_SortingLayer: (-?\d+)"),
          "emitting", grab(r"m_Emitting: (\d)"),
          "align", grab(r"m_Alignment: (\d)"))
