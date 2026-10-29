import re
t = open(r'Assets/Scenes/SampleScene.unity', encoding='utf-8-sig').read()
blocks = re.split(r'^--- ', t, flags=re.M)

def f(b, name):
    m = re.search(r'^  %s: (.+)$' % name, b, re.M)
    return m.group(1).strip() if m else None

names = {}
for b in blocks:
    if b.startswith('!u!1 '):
        fid = b.split('&')[1].split('\n')[0]
        names[fid] = f(b, 'm_Name')

comp2go = {}
for b in blocks:
    if b.startswith('!u!1 '):
        fid = b.split('&')[1].split('\n')[0]
        for c in re.findall(r'- component: \{fileID: (-?\d+)\}', b):
            comp2go[c] = fid

scaled = rotated = 0
for b in blocks:
    if b.startswith('!u!4 '):
        fid = b.split('&')[1].split('\n')[0]
        name = names.get(comp2go.get(fid), '')
        if not name.startswith('Roads Final'):
            continue
        scale = f(b, 'm_LocalScale') or ''
        rot = f(b, 'm_LocalRotation') or ''
        pos = f(b, 'm_LocalPosition') or ''
        if 'x: 1, y: 1' not in scale:
            scaled += 1
            print('scale  ', name, pos, scale)
        m = re.match(r'\{x: ([\-\d.e]+), y: ([^,]+), z: ([^,]+)', rot)
        if m and (abs(float(m.group(3))) > 0.0001 or abs(float(m.group(1))) > 0.0001 or abs(float(m.group(2))) > 0.0001):
            rotated += 1
print('road transforms with non-1 scale:', scaled)
print('road transforms with z/x/y rotation:', rotated)
