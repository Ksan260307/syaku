"""
しゃくとりの森 — 夜の森のアセット生成スクリプト

神社の奥の宮のうらの、秋の夜の雑木林（8 つ目のエリア）を、しゃくとりむしから見た大きさで作る。
竹（節のある幹と、てっぺんの葉）・タケノコ・枯れ沢の谷にわたす丸木橋・ツキヨタケの生える古い倒木・
シマエナガのねぐらの木・ミノムシのぶらさがる木・丘の上の星見の岩・月見の池のススキ。FBX を書き出します。
1 単位 = しゃくとりむしの体長（2.5cm）。

座標は Blender の向き（z が上）で書く。Unity では (x, y, z) が (-x, z, -y) になる。物の床のまん中が原点。

使い方:
  blender -b --factory-startup --python blender/scripts/build_night_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import (MB, TAU, bark_color, build, connect_rings, hexc, lathe, lerp, mixc, scalec, sstep,  # noqa: E402
                              trunk_mesh, tube, uv_sphere, with_alpha)


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "night_kit.blend")
    return fbx, blend


BAMBOO = hexc("#7c9e42")
BAMBOO_LIGHT = hexc("#a9c45e")
BAMBOO_NODE = hexc("#56742c")
BAMBOO_POWDER = hexc("#cfd8b8")
BAMBOO_LEAF = hexc("#6f9a3c")
BAMBOO_LEAF_LIGHT = hexc("#9cbf5a")
SHEATH = hexc("#8a6a44")
SHEATH_DARK = hexc("#4e3826")
MOSS = hexc("#6d9446")
MOSS_DEEP = hexc("#4c7034")
WOOD = hexc("#c8a77a")          # 樹皮のはがれた、白っぽい木
WOOD_DARK = hexc("#8a6a48")
STONE = hexc("#8e9096")
STONE_LIGHT = hexc("#b4b6ba")
LICHEN = hexc("#b8bf8e")
SUSUKI_STEM = hexc("#8e9a5a")
SUSUKI_LEAF = hexc("#7d8f4c")
SUSUKI_PLUME = hexc("#a89e86")   # 月あかりの下で、白くかがやきすぎない、くすんだ銀色
SUSUKI_PINK = hexc("#9c8478")
LEAF_RED = hexc("#8e4a44")       # 夜の木の葉：月あかりの下の、しずんだ赤（明るいだいだいは、夜の光にうく）
LEAF_ORANGE = hexc("#9a6a46")
LEAF_YELLOW = hexc("#8e8650")


def on_poly(pts, t):
    """折れ線 pts（点が同じ間かくでなくてもよい。点の番号でわる）の、t（0〜1）の所"""
    f = t * (len(pts) - 1)
    i = min(int(f), len(pts) - 2)
    return pts[i].lerp(pts[i + 1], f - i)


def leaf_blob(mb, c, length, width, d, col, seg=6, rings=3):
    """細長い葉 1 まい（うすい、だ円のかたまり。どちらから見ても見える）。d は葉の先の向き"""
    d = d.normalized()
    rot = Vector((0, 1, 0)).rotation_difference(d)
    # 葉のつけ根は、枝の中まで（はなれて見えない）
    uv_sphere(mb, c + d * (length * 0.3), 1.0, lambda n: mixc(col, scalec(col, 1.15), 0.5 + 0.5 * n.y), seg=seg, rings=rings,
              scale=Vector((width * 0.5, length * 0.5, 0.03)), rot=rot)


# ---------------------------------------------------------------------------
# 竹とタケノコ
# ---------------------------------------------------------------------------
BAMBOO_H = 46.0
BAMBOO_R = 1.0
BAMBOO_GAP = 4.3


def bamboo_radius(z, H=BAMBOO_H, R=BAMBOO_R):
    base = R * lerp(1.0, 0.72, max(0.0, z) / H)
    node = z / BAMBOO_GAP
    k = node - round(node)
    return base * (1.0 + 0.09 * math.exp(-(k * BAMBOO_GAP / 0.16) ** 2))


def make_bamboo():
    """竹（モウソウチクの若い竹・高さ 1.2m ほど）：まっすぐな緑の幹に、ふくらんだ節と、節の下の白い粉。
    上のほうの節から細い枝が出て、細長い葉がしげる。根もとは地面の中へ"""
    rnd = random.Random(61)
    mb = MB()
    zs = [-2.5]
    k = 0
    while True:
        node = BAMBOO_GAP * (k + 1)
        if node > BAMBOO_H - 2.5:
            break
        prev = zs[-1]
        for t in (0.35, 0.7):
            zs.append(lerp(prev, node - 0.25, t))
        zs += [node - 0.25, node, node + 0.25]
        k += 1
    zs += [BAMBOO_H - 1.2, BAMBOO_H]

    def col(t, a, p, d):
        node = p.z / BAMBOO_GAP
        kk = (node - round(node)) * BAMBOO_GAP
        if abs(kk) < 0.12:
            return BAMBOO_NODE                                   # 節
        if -0.75 < kk < -0.12:
            return mixc(BAMBOO, BAMBOO_POWDER, 0.55)             # 節の下の白い粉
        c = mixc(BAMBOO, BAMBOO_LIGHT, 0.5 + 0.5 * math.sin(a * 3.0 + p.z * 0.05))
        return mixc(c, scalec(BAMBOO, 0.85), 0.3 * sstep(0.6, 1.0, noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, p.z * 0.4)))))
    radii = [bamboo_radius(z) for z in zs]
    radii[-1] = 0.0
    tube(mb, [Vector((0, 0, z)) for z in zs], radii, 14, col)
    # 上の節から、細い枝と葉
    for k in range(5, int(BAMBOO_H / BAMBOO_GAP)):
        z = BAMBOO_GAP * k
        if z > BAMBOO_H - 3.0:
            break
        a = rnd.uniform(0, TAU) + k * 2.4
        d = Vector((math.cos(a), math.sin(a), 0.55)).normalized()
        r = bamboo_radius(z)
        base = Vector((math.cos(a) * r * 0.5, math.sin(a) * r * 0.5, z + 0.1))
        L = rnd.uniform(4.0, 6.5)
        tip = base + d * L + Vector((0, 0, -0.4))
        bp = [base, base.lerp(tip, 0.5) + Vector((0, 0, 0.3)), tip]
        tube(mb, bp, [0.14, 0.1, 0.0], 5, lambda t, aa, p, dd: BAMBOO_NODE)
        for j in range(6):
            q = on_poly(bp, 0.35 + 0.1 * j)
            la = a + rnd.uniform(-1.2, 1.2)
            ld = Vector((math.cos(la), math.sin(la), rnd.uniform(-0.7, -0.2)))
            leaf_blob(mb, q, rnd.uniform(2.0, 2.8), rnd.uniform(0.45, 0.6), ld,
                      mixc(BAMBOO_LEAF, BAMBOO_LEAF_LIGHT, rnd.uniform(0.0, 0.6)))
    return build(mb, "Night_Bamboo")


def make_bamboo_shoot():
    """タケノコ（高さ 12cm ほど）：茶色に黒いはん点のある皮が、何まいも重なった、とんがり。先は黄緑"""
    mb = MB()
    H = 5.2
    prof = [(0.0, -0.6)]
    n = 22
    for i in range(n + 1):
        t = i / n
        z = -0.4 + (H + 0.4) * t
        r = 1.45 * (1.0 - t) ** 0.85
        ridge = 1.0 + 0.07 * max(0.0, math.sin(t * 7 * math.pi))   # 皮のふち
        prof.append((r * ridge if i < n else 0.0, z))

    def col(t, a, p):
        if p.z > H * 0.8:
            return mixc(SHEATH, hexc("#b8c25a"), sstep(0.8, 0.95, p.z / H))
        c = mixc(SHEATH, hexc("#a88454"), 0.5 + 0.5 * math.sin(a * 5 + p.z * 3))
        if noise.noise(Vector((math.cos(a) * 4, math.sin(a) * 4, p.z * 2.5))) > 0.35:
            c = SHEATH_DARK                                       # 黒いはん点
        return c
    lathe(mb, prof, 16, col)
    return build(mb, "Night_BambooShoot")


# ---------------------------------------------------------------------------
# 丸木橋と、ツキヨタケの倒木
# ---------------------------------------------------------------------------
BRIDGE_LEN = 23.0
BRIDGE_R = 1.25
ROTLOG_LEN = 20.0
ROTLOG_R = 2.3


def log_mesh(mb, length, R, seed, moss, peel, stubs, rough=0.08):
    """横にした丸太（Blender の y の向き。原点は、まん中の軸）。上は苔、ところどころ樹皮がはがれて白っぽい木。
    両はしは、年輪の見える切り口（とじる）"""
    rnd = random.Random(seed)
    n = int(length * 1.6) + 2
    half = length / 2
    pts, radii = [], []
    for i in range(n + 1):
        t = i / n
        y = -half + length * t
        pts.append(Vector((0.15 * math.sin(t * 3 + seed), y, 0.1 * math.sin(t * 5 + seed))))
        radii.append(R * (1.0 - 0.12 * t) * (1.0 + 0.04 * math.sin(t * 9 + seed)))
    # 両はしは、平らな切り口（年輪の色の輪を、樹皮の輪のすぐ外に足して、まん中の極でとじる）
    pts = [pts[0] + Vector((0, -0.06, 0)), pts[0] + Vector((0, -0.03, 0))] + pts + [pts[-1] + Vector((0, 0.03, 0)), pts[-1] + Vector((0, 0.06, 0))]
    radii = [0.0, radii[0] * 0.995] + radii + [radii[-1] * 0.995, 0.0]

    def col(t, a, p, d):
        if abs(p.y) > half + 0.01:
            rr = math.hypot(p.x, p.z)
            return mixc(WOOD, WOOD_DARK, 0.5 + 0.5 * math.sin(rr * 9.0))    # 切り口の年輪
        c = bark_color(p * 0.6, a, 0.25 * sstep(0.6, 1.0, abs(math.sin(a * 6 + p.y * 0.3))), seed)
        if noise.noise(p * 0.35 + Vector((seed, 0, 0))) > 1.0 - peel:
            c = mixc(WOOD, WOOD_DARK, 0.3 + 0.3 * math.sin(p.y * 4))         # 樹皮がはがれた所
        m = sstep(0.25, 0.75, d.z + 0.25 * noise.noise(p * 0.5 + Vector((0, seed, 0))))
        return mixc(c, mixc(MOSS_DEEP, MOSS, 0.5 + 0.5 * noise.noise(p * 1.5)), m * moss)
    tube(mb, pts, radii, 18, col,
         rfn=lambda t, a: 1.0 + rough * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, t * length * 0.4 + seed))))
    # 折れた枝のあと（横から、少し上へ）
    for k in range(stubs):
        y = rnd.uniform(-length * 0.35, length * 0.35)
        side = rnd.choice((-1, 1))
        a = rnd.uniform(-0.3, 0.6)
        d = Vector((side * math.cos(a), rnd.uniform(-0.2, 0.2), math.sin(a))).normalized()
        b = Vector((0, y, 0)) + d * R * 0.6
        tip = b + d * rnd.uniform(1.2, 2.2) * (R / 1.25)
        tube(mb, [b, b.lerp(tip, 0.6), tip, tip + d * 0.05], [R * 0.32, R * 0.26, R * 0.2, 0.0], 8,
             lambda t, aa, p, dd: bark_color(p * 0.6, aa, 0.2, seed + 3) if t < 0.9 else WOOD)


def make_log_bridge():
    """丸木橋（長さ 58cm）：枯れ沢の谷にわたす、1 本の丸太。上は苔でふかふか"""
    mb = MB()
    log_mesh(mb, BRIDGE_LEN, BRIDGE_R, 4.0, 0.9, 0.25, 3)
    return build(mb, "Night_LogBridge")


def make_rot_log():
    """ツキヨタケの生える古い倒木（長さ 50cm）：太く、樹皮がところどころはがれ、苔むしている。サルノコシカケのような、かたいキノコも"""
    rnd = random.Random(23)
    mb = MB()
    log_mesh(mb, ROTLOG_LEN, ROTLOG_R, 11.0, 0.75, 0.4, 2, rough=0.12)
    for k in range(5):
        y = rnd.uniform(-ROTLOG_LEN * 0.4, ROTLOG_LEN * 0.4)
        side = rnd.choice((-1, 1))
        a = rnd.uniform(-0.2, 0.4)
        c = Vector((side * math.cos(a) * ROTLOG_R * 0.98, y, math.sin(a) * ROTLOG_R * 0.98))
        uv_sphere(mb, c + Vector((side * 0.25, 0, 0)), 1.0, lambda n: mixc(hexc("#8e6a46"), hexc("#d8c4a0"), sstep(0.2, 0.9, n.z)), seg=12, rings=6,
                  scale=Vector((0.9, 1.3, 0.28)))
    return build(mb, "Night_RotLog")


# ---------------------------------------------------------------------------
# 木：シマエナガのねぐらの木・ミノムシの木
# ---------------------------------------------------------------------------
def tree_trunk(mb, R, H, seed, flare=0.7):
    """先細りの幹（根もとは広がって、地面の中から）。てっぺんは細くとじる。点の列を返す（枝の付け根をさがす）"""
    n = 18
    pts, radii = [], []
    for i in range(n + 1):
        t = i / n
        z = -2.5 + (H + 2.5) * t
        lean = Vector((0.6 * math.sin(t * 2.2 + seed), 0.5 * math.sin(t * 1.7 + seed * 2.0), 0.0)) * t
        pts.append(Vector((0, 0, z)) + lean)
        radii.append(R * lerp(1.0, 0.3, t ** 0.9) + flare * math.exp(-max(0.0, z) / (R * 0.8)))
    radii[-1] = 0.0
    tube(mb, pts, radii, 20, lambda t, a, p, d: bark_color(p * 0.5, a, 0.35 * sstep(0.6, 1.0, abs(math.sin(a * 7 + p.z * 0.1))), seed, 0.08, 0.05),
         rfn=lambda t, a: 1.0 + 0.06 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, t * 6 + seed))))
    return pts, H + 2.5


def trunk_at(pts, total, z):
    """幹の、高さ z の所の軸の点"""
    return on_poly(pts, min(1.0, max(0.0, (z + 2.5) / total)))


def crown(mb, c, r, cols, seed, leaf_mb=None):
    """葉のかたまり：大きなかたまりに、小さなかたまりを 2 つ重ねた房。leaf_mb があれば、その表面の葉 1 まいずつを、そちらに"""
    rnd = random.Random(int(seed * 100))
    blob(mb, c, r, cols, seed, leaf_mb)
    for k in range(2):
        a = rnd.uniform(0, TAU)
        d = Vector((math.cos(a), math.sin(a), rnd.uniform(-0.3, 0.5))).normalized()
        blob(mb, c + d * (r * 0.62), r * rnd.uniform(0.55, 0.7), cols, seed + 7.3 * (k + 1), leaf_mb)


def leaf_card(mb, base, d, nrm, L, W, col, sway):
    """葉 1 まい（たまご形で、先がとがる。花・シダの材質で、両面から見える）。base は付け根、d は先の向き、nrm は表の向き"""
    side = d.cross(nrm).normalized()
    nrm = side.cross(d).normalized()
    rows = []
    for t, w, lift in ((0.0, 0.06, 0.0), (0.3, 0.48, 0.05), (0.65, 0.4, 0.06), (1.0, 0.0, -0.02)):
        p = base + d * (L * t) + nrm * (L * lift)
        k = 0.9 + 0.16 * t
        rows.append([mb.v(p + side * (W * w * s), with_alpha(scalec(col, k * (1.08 if s == 0 else 1.0)), sway * t)) for s in (-1, 0, 1)])
    for i in range(3):
        for j in range(2):
            mb.f(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])


def blob(mb, c, r, cols, seed, leaf_mb=None):
    """葉のかたまり（でこぼこの、まるいかたまり。秋の色のまだらで、下がわは少し暗い）"""
    n0 = len(mb.V)

    def col(n):
        k = 0.5 + 0.5 * noise.noise(n * 2.5 + Vector((seed, 0, 0)))
        cc = mixc(cols[0], cols[1], k)
        if noise.noise(n * 5.0 + Vector((0, seed, 0))) > 0.3:
            cc = mixc(cc, cols[2], 0.6)
        return mixc(cc, scalec(cc, 0.65), sstep(0.0, -0.8, n.z))
    uv_sphere(mb, c, r, col, seg=16, rings=10, scale=Vector((1.0, 1.0, 0.8)))
    for i in range(n0, len(mb.V)):
        v = mb.V[i] - c
        k = 1.0 + 0.16 * noise.noise(v / max(r, 1e-3) * 2.2 + Vector((seed, seed, 0))) + 0.06 * noise.noise(v / max(r, 1e-3) * 6.0)
        mb.V[i] = c + v * k
    if leaf_mb is None:
        return
    # かたまりの表面に、外へ向いた葉を 1 まいずつ（まるい玉に見せない。森の木と同じ、葉の木に）
    lr = random.Random(int(seed * 1000) + 17)
    n = int(100 * (r / 3.8) ** 2) + 24
    for k in range(n):
        z = lr.uniform(-0.6, 1.0)
        a = lr.uniform(0, TAU)
        dirv = Vector((math.cos(a) * math.sqrt(1 - z * z), math.sin(a) * math.sqrt(1 - z * z), z))
        kk = 1.0 + 0.16 * noise.noise(dirv * 2.2 + Vector((seed, seed, 0)))
        base = c + Vector((dirv.x, dirv.y, dirv.z * 0.8)) * (r * kk * lr.uniform(0.8, 0.98))
        tang = dirv.cross(Vector((0, 0, 1)))
        tang = tang.normalized() if tang.length > 1e-3 else Vector((1, 0, 0))
        tw = lr.uniform(0, TAU)
        tang = (tang * math.cos(tw) + dirv.cross(tang) * math.sin(tw)).normalized()
        d = (dirv * 0.7 + tang * 0.7 + Vector((0, 0, -0.2))).normalized()
        L = r * lr.uniform(0.36, 0.5)
        cc = mixc(cols[0], cols[1], lr.random())
        if lr.random() < 0.22:
            cc = mixc(cc, cols[2], 0.65)
        cc = mixc(cc, scalec(cc, 0.68), sstep(0.1, -0.6, dirv.z))
        leaf_card(leaf_mb, base, d, dirv, L, L * 0.58, cc, 0.3)


def branch(mb, base, d, L, r0, rise, seed, twigs, rnd):
    """太い枝 1 本：幹の中から外へ、少しそりあがる。先は細くとじる。小枝を何本か。枝の先の点を返す"""
    d = Vector((d.x, d.y, 0)).normalized()
    n = 10
    pts = [base - d * r0 * 0.8]
    radii = [r0 * 0.9]
    for i in range(n):
        t = (i + 1) / n
        pts.append(base + d * (L * t) + Vector((0, 0, rise * t * t + 0.35 * math.sin(t * 5 + seed))))
        radii.append(lerp(r0, r0 * 0.22, t ** 0.8))
    radii[-1] = 0.0
    tube(mb, pts, radii, 12, lambda t, a, p, dd: bark_color(p * 0.5, a, 0.3, seed, 0.06, 0.05))
    side = Vector((-d.y, d.x, 0))
    for k in range(twigs):
        t = rnd.uniform(0.45, 0.85)
        i = min(n - 1, int(t * n))
        b = pts[i + 1]
        tw = (d * 0.5 + side * rnd.choice((-1, 1)) * rnd.uniform(0.5, 1.0) + Vector((0, 0, rnd.uniform(0.4, 1.0)))).normalized()
        tip = b + tw * rnd.uniform(2.0, 3.5)
        tube(mb, [b, b.lerp(tip, 0.5), tip], [radii[i + 1] * 0.45 + 0.08, 0.12, 0.0], 5, lambda t, a, p, dd: bark_color(p * 0.5, a, 0.3, seed + 1))
    return pts[-1]


# 太い枝：（向き（ラジアン）、付け根の高さ、長さ、そりあがり）
ROOST_BRANCHES = ((0.3, 13.0, 14.0, 3.0), (1.6, 15.5, 12.5, 3.5), (2.9, 12.5, 13.5, 2.6), (4.2, 17.5, 11.5, 4.0), (5.3, 20.5, 10.0, 4.0))
MINO_BRANCHES = ((0.5, 8.5, 10.0, 1.2), (2.1, 9.5, 9.0, 1.6), (3.6, 8.0, 10.5, 1.0), (5.0, 10.5, 8.5, 1.8))
ROOST_H = 30.0
MINO_H = 19.0


def make_tree(name, R, H, branches, r0, crown_r, top_r, cols, seed, leaves=False):
    """leaves=True なら、木の形はそのまま作って捨て、葉のかたまりの表面の葉だけを、べつのメッシュにする（花・シダの材質・当たり判定なし）"""
    rnd = random.Random(int(seed * 10))
    mb = MB()
    leaf_mb = MB() if leaves else None
    pts, total = tree_trunk(mb, R, H, seed)
    for k, (a, y, L, rise) in enumerate(branches):
        d = Vector((math.cos(a), math.sin(a), 0))
        base = trunk_at(pts, total, y)
        tip = branch(mb, base + d * (R * 0.4), d, L, r0, rise, seed + k, 2, rnd)
        crown(mb, tip + Vector((0, 0, crown_r * 0.35)) - d * (crown_r * 0.25), crown_r * rnd.uniform(0.9, 1.1), cols, seed + k * 3.1, leaf_mb)
    # てっぺんの、細い枝先と、いちばん大きな葉のかたまり
    top = trunk_at(pts, total, H - 2.0)
    for k in range(3):
        a = TAU * k / 3 + 0.4
        d = Vector((math.cos(a), math.sin(a), 1.2)).normalized()
        tip = top + d * (top_r * 0.9)
        tube(mb, [top - d * 0.3, top.lerp(tip, 0.5), tip], [R * 0.3, R * 0.18, 0.0], 6, lambda t, aa, p, dd: bark_color(p * 0.5, aa, 0.3, seed + 9))
    crown(mb, top + Vector((0, 0, top_r * 0.45)), top_r, cols, seed + 20.0, leaf_mb)
    return build(leaf_mb if leaves else mb, name)


def make_roost_tree():
    """シマエナガのねぐらの木（高さ 90cm ほどの、秋のコナラ）：先細りの太い幹から、横へ太い枝が 5 本。
    枝の上は、ゆるい坂で歩ける。枝の先とてっぺんに、黄色とだいだいの葉のかたまり"""
    return make_tree("Night_RoostTree", 2.2, ROOST_H, ROOST_BRANCHES, 1.2, 3.8, 6.0,
                     (LEAF_YELLOW, LEAF_ORANGE, hexc("#8a9a3a")), 3.3)


def make_roost_leaves():
    """ねぐらの木の、葉のかたまりの表面の葉（木と同じ所に、花・シダの材質で重ねる）"""
    return make_tree("Night_LeavesRoost", 2.2, ROOST_H, ROOST_BRANCHES, 1.2, 3.8, 6.0,
                     (LEAF_YELLOW, LEAF_ORANGE, hexc("#8a9a3a")), 3.3, leaves=True)


def make_mino_tree():
    """ミノムシの木（高さ 50cm ほどの、サクラの若い木）：低い所から、横へ長い枝が 4 本（ミノムシは、この枝の下にぶらさがる）。
    枝の先とてっぺんに、赤くなりはじめた葉のかたまり"""
    return make_tree("Night_MinoTree", 1.5, MINO_H, MINO_BRANCHES, 0.8, 2.6, 4.2,
                     (LEAF_RED, LEAF_ORANGE, hexc("#7e8a3a")), 6.1)


def make_mino_leaves():
    """ミノムシの木の、葉のかたまりの表面の葉"""
    return make_tree("Night_LeavesMino", 1.5, MINO_H, MINO_BRANCHES, 0.8, 2.6, 4.2,
                     (LEAF_RED, LEAF_ORANGE, hexc("#7e8a3a")), 6.1, leaves=True)


# ---------------------------------------------------------------------------
# 星見の岩
# ---------------------------------------------------------------------------
STAR_ROCK_TOP = 4.4


def make_star_rock():
    """星見の岩（はば 35cm ほど）：丘の上の、平たい大きな岩。てっぺんは、ねころべるほど平ら。苔と地衣類のまだら"""
    mb = MB()
    seg, rings = 40, 20
    sx, sy, sz = 7.2, 6.0, 5.6
    prof = []
    for i in range(rings + 1):
        th = math.pi * i / rings
        prof.append((math.sin(th), -math.cos(th)))
    rows = []
    for i, (rr, zz) in enumerate(prof):
        if rr < 1e-6:
            z = -2.2 if zz < 0 else STAR_ROCK_TOP
            rows.append([mb.v(Vector((0, 0, z)), STONE)])
            continue
        row = []
        for j in range(seg):
            a = TAU * j / seg
            dvec = Vector((math.cos(a) * rr, math.sin(a) * rr, zz))
            k = 1.0 + 0.1 * noise.noise(dvec * 1.7 + Vector((3.1, 0, 0))) + 0.05 * noise.noise(dvec * 5.0)
            p = Vector((dvec.x * sx * k, dvec.y * sy * k, dvec.z * sz * k + 0.6))
            if p.z > STAR_ROCK_TOP - 0.6:
                p.z = STAR_ROCK_TOP - 0.6 + (p.z - (STAR_ROCK_TOP - 0.6)) * 0.12   # 平らなてっぺん
            p.z = max(p.z, -2.2)
            c = mixc(STONE, STONE_LIGHT, 0.5 + 0.5 * noise.noise(p * 0.6))
            if noise.noise(p * 1.1 + Vector((0, 7, 0))) > 0.3:
                c = mixc(c, LICHEN, 0.6)                                # 地衣類のまだら
            if p.z > STAR_ROCK_TOP - 1.2 and noise.noise(p * 0.5) > 0.0:
                c = mixc(c, MOSS, 0.45)
            row.append(mb.v(p, c))
        rows.append(row)
    start = len(mb.F)
    connect_rings(mb, rows)
    return build(mb, "Night_StarRock", sharp_deg=60)


# ---------------------------------------------------------------------------
# ススキ
# ---------------------------------------------------------------------------
def make_susuki():
    """ススキのかぶ（高さ 20cm ほど）：細いくきの先に、銀色の穂がたれさがる。根もとから、細長い葉が弓なりに"""
    rnd = random.Random(91)
    mb = MB()
    for s in range(8):
        a = TAU * s / 8 + rnd.uniform(-0.3, 0.3)
        base = Vector((math.cos(a) * rnd.uniform(0.1, 0.5), math.sin(a) * rnd.uniform(0.1, 0.5), -0.3))
        h = rnd.uniform(6.0, 8.5)
        lean = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.6, 1.6)
        pts = [base + Vector((0, 0, h * t)) + lean * t * t for t in (0.0, 0.35, 0.7, 1.0)]
        tube(mb, pts, [0.07, 0.06, 0.05, 0.0], 5, lambda t, aa, p, d: SUSUKI_STEM)
        top = pts[-1]
        # 穂：くきの先から、外へたれる細い穂が 5 本
        for k in range(5):
            pa = a + rnd.uniform(-0.9, 0.9)
            d = Vector((math.cos(pa), math.sin(pa), 0))
            L = rnd.uniform(1.3, 2.0)
            q0 = on_poly(pts, 1.0 - 0.035 * k - 0.01)
            q1 = q0 + d * (L * 0.5) + Vector((0, 0, 0.1))
            q2 = q0 + d * L + Vector((0, 0, -0.8))
            tube(mb, [q0 - d * 0.02, q0, q1, q2], [0.0, 0.08, 0.13, 0.0], 5,
                 lambda t, aa, p, dd: mixc(SUSUKI_PLUME, SUSUKI_PINK, 0.3 * t + 0.2 * math.sin(aa * 3)))
    for k in range(10):
        a = TAU * k / 10 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(3.5, 5.5)
        b = Vector((0, 0, -0.2))
        pts = [b, b + d * (L * 0.3) + Vector((0, 0, L * 0.45)), b + d * (L * 0.7) + Vector((0, 0, L * 0.5)), b + d * L + Vector((0, 0, L * 0.15))]
        tube(mb, pts, [0.12, 0.14, 0.1, 0.0], 5, lambda t, aa, p, dd: mixc(SUSUKI_LEAF, hexc("#a8a868"), 0.4 * t), oval=(0.25, 1.0))
    return build(mb, "Night_Susuki")


MAKERS = [
    make_bamboo, make_bamboo_shoot, make_log_bridge, make_rot_log, make_roost_tree, make_mino_tree, make_star_rock, make_susuki,
    make_roost_leaves, make_mino_leaves,
]


def main():
    fbx_dir, blend_out = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    objs = []
    for mk in MAKERS:
        ob = mk()
        p = kit.export_fbx(ob, fbx_dir)
        print("exported", ob.name, len(ob.data.vertices), "verts ->", os.path.basename(p))
        objs.append(ob)
    x = 0.0
    for ob in objs:
        w = max(ob.dimensions.x, 1.0)
        ob.location = (x + w / 2, 0, 0)
        x += w + 2.0
    bpy.ops.wm.save_as_mainfile(filepath=blend_out, compress=True)
    print("saved", blend_out)


if __name__ == "__main__":
    main()
