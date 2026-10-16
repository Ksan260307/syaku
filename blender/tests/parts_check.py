"""
モデルの部品のつながりを調べる（Blender のテストから使う）。
  ・見える筒の口：筒（チューブ）のはしの口があいたまま、ほかの部品にもかくれず、外から見える所
    （キツネの像の首が、胴の筒の口のまま頭の下に出て、胴と首がはなれて見えた）
  ・浮いた部品：ほかのどの部品にも、ふれも交わりもしない部品（地面の上にある物はのぞく）
"""
import random

import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

_rnd = random.Random(7)
DIRS = []
while len(DIRS) < 26:
    _d = Vector((_rnd.uniform(-1, 1), _rnd.uniform(-1, 1), _rnd.uniform(-1, 1)))
    if 0.2 < _d.length <= 1.0:
        DIRS.append(_d.normalized())


def _inside(tree, p, d=Vector((0.0123, 0.0213, 0.9997)).normalized()):
    n = 0
    q = p.copy()
    for _ in range(64):
        h = tree.ray_cast(q, d, 1e4)
        if h[0] is None:
            break
        n += 1
        q = h[0] + d * 1e-5
    return n % 2 == 1


def _parts(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bm.faces.ensure_lookup_table()
    bm.verts.ensure_lookup_table()
    comp = {}
    n = 0
    for f in bm.faces:
        if f.index in comp:
            continue
        comp[f.index] = n
        stack = [f]
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in comp:
                        comp[h.index] = n
                        stack.append(h)
        n += 1
    faces = [[] for _ in range(n)]
    for f in bm.faces:
        faces[comp[f.index]].append(f)
    closed, trees = [], []
    for fs in faces:
        closed.append(all(len(e.link_faces) == 2 for f in fs for e in f.edges))
        vs = sorted({v.index for f in fs for v in f.verts})
        remap = {vi: k for k, vi in enumerate(vs)}
        trees.append((BVHTree.FromPolygons([bm.verts[i].co.copy() for i in vs], [[remap[v.index] for v in f.verts] for f in fs]), vs))
    return bm, faces, closed, trees


def open_ends(ob, min_r):
    """外から見える筒の口の、まん中の点のリスト（半径 min_r より小さい口は見ない）"""
    bm, faces, closed, trees = _parts(ob)
    whole = BVHTree.FromBMesh(bm)
    found = []
    for c, fs in enumerate(faces):
        if closed[c]:
            continue
        border = {e.index: e for f in fs for e in f.edges if len(e.link_faces) == 1}
        seen = set()
        for e0 in border.values():
            if e0.index in seen:
                continue
            loop, stack = set(), [e0]
            seen.add(e0.index)
            while stack:
                e = stack.pop()
                for v in e.verts:
                    loop.add(v.index)
                    for e2 in v.link_edges:
                        if e2.index in border and e2.index not in seen:
                            seen.add(e2.index)
                            stack.append(e2)
            pts = [bm.verts[i].co.copy() for i in loop]
            cen = sum(pts, Vector()) / len(pts)
            if sum((p - cen).length for p in pts) / len(pts) < min_r:
                continue
            samples = [cen] + [cen + (p - cen) * k for p in pts for k in (0.5, 0.85)]
            hidden = 0
            for p in samples:
                if p.z < 0.03 or any(k != c and closed[k] and _inside(trees[k][0], p) for k in range(len(faces))):
                    hidden += 1
                elif sum(1 for d in DIRS if whole.ray_cast(p + d * 2e-3, d, 1e4)[0] is None) < 0.08 * len(DIRS):
                    hidden += 1
            if hidden < 0.6 * len(samples):
                found.append(tuple(round(x, 2) for x in cen))
    bm.free()
    return found


def floating_parts(ob, tol):
    """どの部品にもふれず交わらない、宙に浮いた部品の、まん中の点のリスト"""
    bm, faces, closed, trees = _parts(ob)
    found = []
    for c in range(len(faces)):
        vs = trees[c][1]
        if min(bm.verts[i].co.z for i in vs) < 0.03:
            continue
        if any(trees[c][0].overlap(trees[k][0]) for k in range(len(faces)) if k != c):
            continue
        near = False
        for k in range(len(faces)):
            if k == c:
                continue
            for i in vs[:: max(1, len(vs) // 200)]:
                h = trees[k][0].find_nearest(bm.verts[i].co)
                if h[0] is not None and h[3] < tol:
                    near = True
                    break
            if near:
                break
        if not near:
            cen = sum((bm.verts[i].co for i in vs), Vector()) / len(vs)
            found.append(tuple(round(x, 2) for x in cen))
    bm.free()
    return found
