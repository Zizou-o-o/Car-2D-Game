import re, sys
t = open(r'Assets/Scenes/SampleScene.unity', encoding='utf-8-sig').read()

def blk(fid):
    m = re.search(r'^--- !u!\d+ &%s\n' % fid, t, re.M)
    if not m:
        return 'NOT FOUND'
    nxt = re.search(r'^--- ', t[m.end():], re.M)
    return t[m.end(): m.end() + (nxt.start() if nxt else 2000)]

for fid in sys.argv[1:]:
    b = blk(fid)
    for line in b.splitlines():
        s = line.strip()
        if s.startswith(('m_Name:', 'm_Sprite:', 'm_Size:', 'm_Color:', 'm_SortingOrder:', 'm_SortingLayerID:', 'm_FlipX:', 'm_FlipY:', 'm_LocalPosition:', 'm_LocalScale:', 'm_LocalRotation:', 'm_IsTrigger:', 'm_Offset:')):
            print(fid, s)
    print('---')
