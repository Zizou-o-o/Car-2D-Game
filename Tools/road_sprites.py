import re
t = open(r'Assets/Scenes/SampleScene.unity', encoding='utf-8-sig').read()
blocks = re.split(r'^--- ', t, flags=re.M)

def f(b, name):
    m = re.search(r'^  %s: (.+)$' % name, b, re.M)
    return m.group(1).strip() if m else None

gos = {}
for b in blocks:
    if b.startswith('!u!1 '):
        fid = b.split('&')[1].split('\n')[0]
        gos[fid] = (f(b, 'm_Name'), f(b, 'm_Layer'), f(b, 'm_TagString'))

# map component fileID -> gameobject
comp2go = {}
for b in blocks:
    if b.startswith('!u!1 '):
        fid = b.split('&')[1].split('\n')[0]
        for c in re.findall(r'- component: \{fileID: (-?\d+)\}', b):
            comp2go[c] = fid

print('unique sprites among road sprites:')
seen = {}
for b in blocks:
    if b.startswith('!u!212 '):
        fid = b.split('&')[1].split('\n')[0]
        go = comp2go.get(fid)
        name, layer, tag = gos.get(go, ('?', '?', '?'))
        if name.startswith('Roads Final'):
            sp = f(b, 'm_Sprite')
            seen.setdefault(sp, []).append(name)
for sp, names in seen.items():
    print(' %-60s %d objects e.g. %s' % (sp, len(names), names[:3]))
