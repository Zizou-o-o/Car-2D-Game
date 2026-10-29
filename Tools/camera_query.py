import re
t = open(r'Assets/Scenes/SampleScene.unity', encoding='utf-8-sig').read()
m = re.search(r'^--- !u!20 &(-?\d+)\n', t, re.M)
start = m.end()
end = re.search(r'^--- ', t[start:], re.M).start() + start
body = t[start:end]
for line in body.splitlines():
    s = line.strip()
    if s.startswith(('m_ClearFlags', 'm_BackGroundColor', 'm_Orthographic', 'm_OrthographicSize', 'm_CullingMask', 'm_Bits', 'm_NormalizedViewPortRect')):
        print(s)
