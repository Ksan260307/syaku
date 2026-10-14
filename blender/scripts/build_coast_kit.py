"""
しゃくとりの森 — 海岸のアセット生成スクリプト

しゃくとりむしから見た海辺の物（大きな巻き貝・白くさらされた流木・フジツボと海藻のついた磯の岩・岬の岩・
なくしたビーチサンダル・ガラスの浮き玉・シオマネキの巣穴・遠くの灯台）と、押せる小物（ハマグリ・ホタテ・小さな巻き貝・シーグラス）、
浜の草花（コウボウムギ・ハマヒルガオ・潮だまりの海藻・打ち上げられた海藻）を作り、FBX を書き出します。
1 単位 = しゃくとりむしの体長くらい（2.5 cm）。

使い方:
  blender -b --factory-startup --python blender/scripts/build_coast_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import (MB, TAU, build, connect_rings, hexc, lathe, lerp, mixc, polar_sheet, quad_bezier,  # noqa: E402
                              ribbon, scalec, sstep, tube, uv_sphere, with_alpha)


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "coast_kit.blend")
    return fbx, blend


SAND = hexc("#e6d3a3")
SAND_DARK = hexc("#c4ad7c")
DRIFT = hexc("#c6bfae")
DRIFT_LIGHT = hexc("#e6e0d2")
DRIFT_DARK = hexc("#8c8475")
BASALT = hexc("#5c5854")
BASALT_LIGHT = hexc("#8a847c")
BASALT_WET = hexc("#3c3936")
ALGAE = hexc("#5f7d3b")
ALGAE_DARK = hexc("#3b5226")
BARNACLE = hexc("#e9e3d3")
BARNACLE_IN = hexc("#7b7166")
SHELL_CREAM = hexc("#f1e2c4")
SHELL_BROWN = hexc("#a46536")
SHELL_PINK = hexc("#f5b9a2")
PEARL = hexc("#fbe9e1")
MUD = hexc("#6f6252")
MUD_DARK = hexc("#4c4237")


# ---------------------------------------------------------------------------
# 磯の岩（黒っぽい岩に、フジツボと海藻。下の方ほどぬれて、海藻がつく）
# ---------------------------------------------------------------------------
def reef_blob(mb, center, size, seed, cuts=7, flat_top=None, floor=-0.3, wet=0.35, barnacles=40, m=None):
    """けずった岩のかたまり（山の岩と同じ作り方）に、磯の色・フジツボ・海藻をつける"""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    off = Vector((rnd.uniform(-50, 50), rnd.uniform(-50, 50), rnd.uniform(-50, 50)))
    planes = []
    for _ in range(cuts):
        nrm = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 1.0))).normalized()
        planes.append((nrm, rnd.uniform(0.65, 0.88)))
    if flat_top is not None:
        planes.append((Vector((0, 0, 1)), flat_top))
    for v in bm.verts:
        p = v.co.copy()
        p *= 1.0 + 0.16 * noise.fractal(p * 1.3 + off, 0.6, 2.0, 4) + 0.04 * noise.noise(p * 6.0 + off)
        for nrm, d in planes:
            k = p.dot(nrm)
            if k > d:
                p -= nrm * (k - d) * (1.0 if nrm.z > 0.99 else 0.92)
        if p.z < floor:
            p.z = floor + (p.z - floor) * 0.3
        v.co = p
    bm.normal_update()
    idx = {}
    spots = []
    zmin = min(v.co.z for v in bm.verts)
    zmax = max(v.co.z for v in bm.verts)
    for v in bm.verts:
        p = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        h01 = (v.co.z - zmin) / max(1e-4, zmax - zmin)
        t = 0.5 + 0.5 * noise.noise(p * 0.45 + off * 1.3)
        c = mixc(BASALT, BASALT_LIGHT, t * 0.7)
        # 下の方はぬれて暗い
        c = mixc(c, BASALT_WET, sstep(wet + 0.1, wet - 0.15, h01) * 0.8)
        # ひびや、くぼみは暗く
        c = scalec(c, 0.8 + 0.2 * sstep(-0.4, 0.2, noise.noise(p * 1.1 + off * 0.7)))
        # ぬれた所に海藻（あおさ・ひじき）
        ak = sstep(wet + 0.08, wet - 0.12, h01) * sstep(-0.2, 0.3, noise.noise(p * 1.4 + off * 0.3))
        c = mixc(c, mixc(ALGAE_DARK, ALGAE, 0.5 + 0.5 * noise.noise(p * 3 + off)), 0.85 * ak)
        # 白い塩のあと（てっぺん）
        c = mixc(c, hexc("#d8d4cc"), 0.25 * sstep(0.75, 1.0, h01) * sstep(0.2, 0.8, v.normal.z))
        q = Vector(center) + p
        if m is not None:
            q = m @ q
        idx[v.index] = mb.v(q, c)
        if barnacles and 0.15 < h01 < wet + 0.35 and v.normal.z > -0.2:
            spots.append((q, (m.to_3x3() @ v.normal).normalized() if m is not None else v.normal.copy()))
    for fc in bm.faces:
        mb.f(*[idx[v.index] for v in fc.verts])
    bm.free()
    rnd.shuffle(spots)
    for q, n in spots[:barnacles]:
        barnacle(mb, q, n, rnd.uniform(0.12, 0.22) * min(1.4, max(0.7, size[2] / 1.6)))


def barnacle(mb, at, n, r):
    """フジツボ：岩にはりついた、白い小さな火山の形"""
    prof = [(r, -0.02), (r * 0.95, r * 0.35), (r * 0.62, r * 0.8), (r * 0.42, r * 0.85), (r * 0.3, r * 0.55), (0.0, r * 0.5)]

    def cf(t, a, p):
        if t > 0.75:
            return BARNACLE_IN
        return scalec(BARNACLE, 0.86 + 0.14 * math.sin(a * 6.0))
    tmp = MB()
    lathe(tmp, prof, 8, cf)
    rot = Vector((0, 0, 1)).rotation_difference(n)
    mb.add(tmp, Matrix.Translation(at - n * 0.03) @ rot.to_matrix().to_4x4())


def make_reef(name, seed, size, flat_top=0.62, barnacles=36):
    mb = MB()
    reef_blob(mb, (0, 0, size[2] * 0.3), size, seed, cuts=10, flat_top=flat_top, floor=-0.45, wet=0.5, barnacles=barnacles)
    return build(mb, name, sharp_deg=38)


def make_cape():
    """岬の先の大岩：上が平らで、海を見わたせる。根もとはぬれて海藻"""
    mb = MB()
    reef_blob(mb, (0, 0, 2.2), (6.2, 5.0, 4.6), 731, cuts=10, flat_top=0.58, floor=-0.5, wet=0.42, barnacles=70)
    # 手前の、足がかりの低い岩
    reef_blob(mb, (-4.2, -3.4, 0.7), (2.6, 2.2, 1.6), 732, cuts=6, flat_top=0.55, floor=-0.4, wet=0.5, barnacles=14)
    reef_blob(mb, (-6.4, -0.6, 0.4), (2.0, 1.8, 1.0), 733, cuts=6, flat_top=0.5, floor=-0.4, wet=0.6, barnacles=8)
    return build(mb, "Coast_Cape", sharp_deg=38)


def make_islet():
    """遠くの小島（灯台の土台）：大きな岩"""
    mb = MB()
    reef_blob(mb, (0, 0, 2.0), (22.0, 16.0, 7.0), 777, cuts=8, flat_top=0.55, floor=-0.5, wet=0.3, barnacles=0)
    return build(mb, "Coast_Islet")


# ---------------------------------------------------------------------------
# 巻き貝（らせんに巻きながら太くなる管）
# ---------------------------------------------------------------------------
def helico_shell(mb, A=1.0, R=1.1, H=2.4, W=2.2, turns=4.5, per_turn=36, seg=20, oval=1.3,
                 knobs=0, knob_h=0.0, colfn=None, inner_turns=0.0, inner_col=None, lip=1.15, wall=0.86):
    """巻き貝の殻。とがった先が上（z≈0）、口は下の方で、+y を向いて開く（口の中心は +x 側）。
    A=口の太さ R=軸からのきょり H=1 巻きで下がる量 W=1 巻きで太くなる倍率。
    colfn(u, ph, th) -> 色（u=0 先、1 口）。inner_turns>0 なら口から奥へ内がわの面をつける"""
    k = math.log(W) / TAU
    tmax = turns * TAU
    n = max(2, int(turns * per_turn))

    def point(th, ph, shrink=1.0):
        s = math.exp(k * (th - tmax))
        ang = th - tmax
        er = Vector((math.cos(ang), math.sin(ang), 0))
        ez = Vector((0, 0, 1))
        c = er * (R * s) - ez * (H * s)
        fl = 1.0 + (lip - 1.0) * sstep(tmax - 0.3 * math.pi, tmax, th)
        bump = 0.0
        if knobs and shrink >= 1.0:
            sh = math.exp(-((ph - 0.8) ** 2) / 0.07)
            bump = knob_h * sh * max(0.0, math.cos(th * knobs)) ** 2 * sstep(0.15, 0.5, th / tmax)
        r = A * s * fl * shrink
        return c + (er * math.cos(ph) + ez * (math.sin(ph) * oval)) * r * (1.0 + bump), c

    def phase(j):
        return -TAU * j / seg

    rings = []
    for i in range(n + 1):
        th = tmax * i / n
        if i == 0:
            p, _ = point(0.0, 0.0, 0.0)
            rings.append([mb.v(p, colfn(0.0, 0.0, 0.0))])
            continue
        ring = []
        for j in range(seg):
            p, _ = point(th, phase(j))
            ring.append(mb.v(p, colfn(i / n, phase(j) % TAU, th)))
        rings.append(ring)
    connect_rings(mb, rings)
    if inner_turns <= 0:
        # 口をふさぐ（小さな貝は中を作らない）
        _, c = point(tmax, 0.0)
        cc = mb.v(c, inner_col(1.0) if inner_col else hexc("#5a3e2c"))
        last = rings[-1]
        for j in range(seg):
            mb.f(last[(j + 1) % seg], last[j], cc)
        return
    m = max(2, int(inner_turns * per_turn))
    inner = []
    for i in range(m + 1):
        th = tmax - inner_turns * TAU * i / m
        depth = i / m
        if i == m:
            _, c = point(th, 0.0)
            inner.append([mb.v(c, inner_col(1.0))])
            continue
        ring = []
        for j in range(seg):
            p, _ = point(th, phase(j), wall)
            ring.append(mb.v(p, inner_col(depth)))
        inner.append(ring)
    connect_rings(mb, inner)
    connect_rings(mb, [rings[-1], inner[0]])


def shell_colors(base, band, dark, knob=None):
    def cf(u, ph, th):
        stripe = sstep(0.5, 0.8, 0.5 + 0.5 * math.sin(ph * 5.0 + 0.6))
        c = mixc(base, band, 0.7 * stripe)
        c = mixc(c, dark, 0.35 * sstep(0.85, 0.97, 0.5 + 0.5 * math.sin(ph * 17.0)))
        growth = 0.5 + 0.5 * math.sin(th * 9.0)
        c = scalec(c, 0.9 + 0.1 * growth)
        c = mixc(c, dark, 0.5 * sstep(0.12, 0.0, u))
        if knob is not None:
            c = mixc(c, knob, 0.5 * math.exp(-((ph - 0.8) ** 2) / 0.07) * max(0.0, math.cos(th * 9.0)) ** 2)
        return c
    return cf


def lay_shell(ob, tilt=30.0, sink=0.0):
    """立てて作った貝を横にたおし、口をななめ上へ向けて、いちばん下を z=-sink に"""
    me = ob.data
    me.transform(Matrix.Rotation(math.radians(tilt), 4, "X") @ Matrix.Rotation(math.radians(90), 4, "Y"))
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zmin = min(v.co.z for v in me.vertices)
    me.transform(Matrix.Translation((-(min(xs) + max(xs)) / 2, -(min(ys) + max(ys)) / 2, -zmin - sink)))
    me.update()
    return ob


def make_conch():
    """大きな巻き貝：口から中へ入っていける。肩にこぶのある、白と茶色のしまの殻"""
    mb = MB()
    helico_shell(mb, A=2.3, R=2.3, H=6.0, W=1.95, turns=5.2, per_turn=40, seg=28, oval=1.3,
                 knobs=9, knob_h=0.22,
                 colfn=shell_colors(SHELL_CREAM, SHELL_BROWN, hexc("#6b4a36"), knob=hexc("#fff5e2")),
                 inner_turns=0.9,
                 inner_col=lambda d: scalec(mixc(PEARL, SHELL_PINK, sstep(0.0, 0.6, d)), 1.0 - 0.45 * sstep(0.35, 1.0, d)),
                 lip=1.2)
    ob = build(mb, "Coast_Conch")
    return lay_shell(ob, tilt=28.0, sink=0.35)


def make_spiral_shell(name="Coast_Spiral", scale=1.0, cols=(hexc("#e9d2b4"), hexc("#8f5a3a"))):
    """小さな巻き貝（ヤドカリの家にもなる形）"""
    mb = MB()
    helico_shell(mb, A=0.24, R=0.24, H=0.62, W=2.0, turns=4.6, per_turn=16, seg=12, oval=1.25,
                 colfn=shell_colors(cols[0], cols[1], hexc("#5a3e2c")), lip=1.1, inner_turns=0.4, wall=0.82,
                 inner_col=lambda d: scalec(mixc(hexc("#f6e6dc"), hexc("#c99a86"), d), 1.0 - 0.6 * d))
    ob = build(mb, name)
    lay_shell(ob, tilt=20.0, sink=0.03)
    if scale != 1.0:
        ob.data.transform(Matrix.Scale(scale, 4))
        ob.data.update()
    return ob


# ---------------------------------------------------------------------------
# 流木（白くさらされた、大きな木。登れる）
# ---------------------------------------------------------------------------
def make_driftwood():
    rnd = random.Random(9)
    mb = MB()
    L = 18.0
    n = 26
    pts, rad = [], []
    for i in range(n):
        t = i / (n - 1)
        pts.append(Vector(((t - 0.5) * L, 0.7 * math.sin(t * 2.4 + 0.4), 1.25 + 0.25 * math.sin(t * math.pi) - 0.35 * t)))
        rad.append(lerp(1.45, 0.8, t) * (1.0 + 0.07 * math.sin(t * 19.0)) * (1.0 + 0.25 * sstep(0.12, 0.0, t)))

    def bc(t, a, p, d):
        grain = 0.5 + 0.5 * math.sin(a * 11.0 + p.x * 1.1 + 2.0 * noise.noise(p * 0.4))
        c = mixc(DRIFT, DRIFT_LIGHT, grain * 0.6 + 0.25 * noise.noise(p * 0.9))
        c = mixc(c, DRIFT_DARK, sstep(0.82, 1.0, grain) * 0.45)
        return c
    rings = tube(mb, pts, rad, 18, bc)
    for end in (0, len(rings) - 1):
        ring = rings[end]
        dirv = (pts[end] - pts[1 if end == 0 else end - 1]).normalized()
        cc = mb.v(pts[end] + dirv * 0.06, hexc("#efe6cf"))
        cnt = len(ring)
        for j in range(cnt):
            if end == 0:
                mb.f(cc, ring[(j + 1) % cnt], ring[j])
            else:
                mb.f(cc, ring[j], ring[(j + 1) % cnt])
    # 根もとの、折れた根（太い方のはし）
    root = pts[0]
    for k in range(5):
        a = TAU * k / 5 + rnd.uniform(-0.3, 0.3)
        d = Vector((-0.4, math.cos(a), math.sin(a) * 0.8)).normalized()
        rp = [root + Vector((0.4, 0, 0)), root + d * 1.5, root + d * 2.6 + Vector((0, 0, -0.4))]
        tube(mb, rp, [0.42, 0.26, 0.0], 8, bc)
    # 折れた枝
    for t, side, ln in ((0.38, 1, 3.4), (0.62, -1, 2.6), (0.8, 1, 1.8)):
        b = pts[int(t * (n - 1))]
        bp = [b, b + Vector((0.8, 1.6 * side, 0.9)), b + Vector((1.6, ln * side, 1.4))]
        tube(mb, bp, [0.45, 0.3, 0.0], 9, bc)
    return build(mb, "Coast_Driftwood")


# ---------------------------------------------------------------------------
# なくしたビーチサンダル・ガラスの浮き玉
# ---------------------------------------------------------------------------
def make_sandal():
    mb = MB()
    L, W, H = 10.5, 4.0, 0.55

    def outline(a):
        # 足の形：かかとは丸く、つま先は広い
        x = math.cos(a) * L * 0.5
        w = W * 0.5 * (0.82 + 0.18 * sstep(-L * 0.5, L * 0.4, x))
        return Vector((x, math.sin(a) * w, 0))
    seg = 48
    top, bot = [], []
    for j in range(seg):
        a = TAU * j / seg
        p = outline(a)
        top.append(mb.v(p + Vector((0, 0, H)), hexc("#f2b6c4")))
        bot.append(mb.v(p, hexc("#4fa3b0")))
    ct = mb.v(Vector((0, 0, H + 0.02)), hexc("#f6c5d0"))
    cb = mb.v(Vector((0, 0, 0)), hexc("#4fa3b0"))
    for j in range(seg):
        mb.f(ct, top[j], top[(j + 1) % seg])
        mb.f(cb, bot[(j + 1) % seg], bot[j])
        mb.f(bot[j], bot[(j + 1) % seg], top[(j + 1) % seg], top[j])
    # 鼻緒（つま先の柱から、左右のふちへ）
    post = Vector((L * 0.24, 0, H))
    for sy in (-1, 1):
        sp = [post + Vector((0, 0, 0.9)), post + Vector((-1.6, 0.9 * sy, 1.05)), Vector((-0.6, (W * 0.5 - 0.1) * sy, H + 0.05))]
        tube(mb, quad_bezier(sp[0], sp[1], sp[2], 9), [0.28] * 9, 8, lambda t, a, p, d: hexc("#3c8fa0"))
    tube(mb, [post, post + Vector((0, 0, 0.95))], [0.22, 0.24], 8, lambda t, a, p, d: hexc("#3c8fa0"))
    return build(mb, "Coast_Sandal")


def make_float():
    mb = MB()
    R = 1.55

    def gc(n):
        k = 0.5 + 0.5 * n.z
        return mixc(hexc("#3e958f"), hexc("#a9e3d4"), k * 0.7)
    uv_sphere(mb, Vector((0, 0, R * 0.85)), R, gc, seg=22, rings=14)
    # 網（なわ）
    rope = lambda t, a, p, d: hexc("#8a6a42")
    for k in range(6):
        a = TAU * k / 6
        pts = []
        for s in range(13):
            th = math.pi * (0.08 + 0.84 * s / 12)
            pts.append(Vector((math.cos(a) * math.sin(th), math.sin(a) * math.sin(th), -math.cos(th))) * (R + 0.04) + Vector((0, 0, R * 0.85)))
        tube(mb, pts, [0.06] * 13, 5, rope)
    for z in (-0.45, 0.0, 0.45):
        rr = math.sqrt(max(0.0, 1 - z * z)) * (R + 0.04)
        pts = [Vector((math.cos(TAU * s / 24) * rr, math.sin(TAU * s / 24) * rr, z * R + R * 0.85)) for s in range(25)]
        tube(mb, pts, [0.06] * 25, 5, rope)
    return build(mb, "Coast_Float")


# ---------------------------------------------------------------------------
# 押せる小物（貝がら・シーグラス）
# ---------------------------------------------------------------------------
def make_clam():
    """ハマグリの片方の殻（ふくらんだ方を上に）"""
    mb = MB()

    def rfn(th):
        return 0.55 * (1.0 - 0.18 * math.cos(th) - 0.08 * math.cos(2 * th))

    def zfn(rho, th):
        return 0.32 * (1 - rho * rho) + 0.02

    def cf(rho, th, p):
        ring = 0.5 + 0.5 * math.sin(rho * 26.0)
        c = mixc(hexc("#efe2c8"), hexc("#b98a62"), 0.35 * ring + 0.35 * sstep(0.4, 1.0, 0.5 + 0.5 * math.sin(th * 3 + 1)))
        return c
    polar_sheet(mb, rfn, cf, n_ang=40, n_rad=8, zfn=zfn)
    ob = build(mb, "Coast_Clam", solidify=0.05)
    ob.scale = (1.0, 1.0, 1.0)
    return ob


def make_scallop():
    """ホタテ：すじのある扇の形"""
    mb = MB()
    a0, a1 = -math.pi * 0.42, math.pi * 0.42

    def rfn(th):
        return 0.62 * (1.0 + 0.03 * math.cos(th * 18))

    def zfn(rho, th):
        return 0.18 * (1 - rho * rho) + 0.035 * abs(math.sin(th * 9)) * rho

    def cf(rho, th, p):
        rib = 0.5 + 0.5 * math.cos(th * 18)
        c = mixc(hexc("#f0b07a"), hexc("#fbe2c8"), rib * 0.6 + 0.3 * rho)
        return c
    polar_sheet(mb, rfn, cf, n_ang=36, n_rad=7, a0=a0, a1=a1, zfn=zfn, full=False)
    # 耳（付け根の、左右に出た平たい三角）
    ec = hexc("#e8a874")
    e = [mb.v(Vector((-0.19, -0.03, 0.02)), ec), mb.v(Vector((0.19, -0.03, 0.02)), ec),
         mb.v(Vector((0.1, 0.12, 0.03)), ec), mb.v(Vector((-0.1, 0.12, 0.03)), ec)]
    mb.f(e[0], e[1], e[2], e[3])
    ob = build(mb, "Coast_Scallop", solidify=0.04)
    me = ob.data
    me.transform(Matrix.Rotation(math.radians(90), 4, "Z") @ Matrix.Translation((-0.3, 0, 0)))
    me.update()
    return ob


def make_sea_glass():
    """波にもまれて、角のとれたガラスのかけら"""
    mb = MB()

    def gc(n):
        return mixc(hexc("#8fd3bd"), hexc("#d6f2e6"), 0.5 + 0.5 * n.z)
    uv_sphere(mb, Vector((0, 0, 0.1)), 1.0, gc, seg=10, rings=6, scale=Vector((0.32, 0.22, 0.11)))
    return build(mb, "Coast_SeaGlass")


# ---------------------------------------------------------------------------
# シオマネキの巣穴・遠くの灯台
# ---------------------------------------------------------------------------
def make_burrow():
    """どろの小さな山に、まるい穴（シオマネキの巣穴）"""
    mb = MB()
    prof = [(0.0, -0.4), (0.22, -0.4), (0.28, 0.05), (0.42, 0.18), (0.7, 0.16), (1.0, 0.04), (1.25, -0.05)]

    def cf(t, a, p):
        rr = math.hypot(p.x, p.y)
        if rr < 0.3:
            return mixc(hexc("#1f1a15"), MUD_DARK, sstep(0.0, 0.3, rr))
        c = mixc(MUD_DARK, MUD, 0.5 + 0.5 * noise.noise(p * 4.0))
        return c
    # 外から見える面になるよう、内がわ（穴）から外へ
    rings = []
    for i, (r, z) in enumerate(prof):
        if r <= 1e-7:
            rings.append([mb.v(Vector((0, 0, z)), hexc("#16120e"))])
            continue
        ring = []
        for j in range(18):
            a = TAU * j / 18
            rr = r * (1.0 + 0.08 * math.sin(a * 5 + i))
            p = Vector((math.cos(a) * rr, math.sin(a) * rr, z))
            ring.append(mb.v(p, cf(i / 6, a, p)))
        rings.append(ring)
    # 穴の底から、穴のかべ（内向き）、ふちから外のすそ（外向き）
    for a, b in zip(rings[:-1], rings[1:]):
        if len(a) == 1:
            for j in range(18):
                mb.f(a[0], b[j], b[(j + 1) % 18])
        else:
            for j in range(18):
                mb.f(a[j], b[j], b[(j + 1) % 18], a[(j + 1) % 18])
    # まわりのどろだんご
    rnd = random.Random(13)
    for k in range(9):
        a = TAU * k / 9 + rnd.uniform(-0.2, 0.2)
        r = rnd.uniform(0.75, 1.15)
        uv_sphere(mb, Vector((math.cos(a) * r, math.sin(a) * r, 0.1)), 0.13, lambda n: mixc(MUD, MUD_DARK, 0.3), seg=7, rings=4)
    return build(mb, "Coast_Burrow")


def make_lighthouse():
    """遠くの灯台（白い塔、上に赤い帯と明かりの部屋）"""
    mb = MB()
    prof = [(0.0, 0.0), (3.0, 0.0), (2.9, 4.0), (2.5, 14.0), (2.42, 17.9), (2.4, 18.1), (2.36, 20.0), (2.32, 21.9), (2.3, 22.1),
            (2.2, 22.2), (2.9, 22.4), (2.9, 23.0), (1.9, 23.2), (0.0, 23.2)]

    def cf(t, a, p):
        z = p.z
        if 18.0 < z < 22.0:
            return hexc("#d2463e")
        if z > 22.15:
            return hexc("#3c4a52")
        return scalec(hexc("#f4f2ec"), 0.92 + 0.08 * math.cos(a))
    lathe(mb, prof, 20, cf)
    # 明かりの部屋（ガラス）と屋根
    lathe(mb, [(0.0, 23.2), (1.7, 23.2), (1.7, 25.6), (0.0, 25.6)], 14, lambda t, a, p: hexc("#fff3b0"))
    lathe(mb, [(0.0, 25.6), (2.1, 25.6), (0.25, 27.6), (0.0, 28.2)], 14, lambda t, a, p: hexc("#c43b33"))
    return build(mb, "Coast_Lighthouse")


# ---------------------------------------------------------------------------
# 浜の草花
# ---------------------------------------------------------------------------
GRASS_SAND = (hexc("#6f8a4a"), hexc("#93ab62"), hexc("#c8cf8e"))


def make_beach_grass(name, seed, count=9):
    """コウボウムギ：かたい、細い葉の株。まん中から、茶色い穂がのびる"""
    rnd = random.Random(seed)
    mb = MB()
    for i in range(count):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0, 0.4)
        base = Vector((math.cos(a) * r, math.sin(a) * r, -0.05))
        la = rnd.uniform(0, TAU)
        kit.blade(mb, rnd, base, rnd.uniform(2.6, 4.6), rnd.uniform(0.13, 0.2), Vector((math.cos(la), math.sin(la), 0)),
                  rnd.uniform(0.25, 0.7), cols=GRASS_SAND, twist=rnd.uniform(-0.4, 0.4))
    # 穂（小さな粒がかたまった、ずんぐりした茶色い穂）
    for k in range(rnd.randint(1, 2)):
        h = rnd.uniform(2.4, 3.4)
        lean = Vector((rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), 0))
        pts = quad_bezier(Vector((0, 0, 0)), Vector((0, 0, h * 0.6)), Vector((0, 0, h)) + lean, 8)
        tube(mb, pts, [0.05] * 7 + [0.04], 5, lambda t, a, p, d: with_alpha(mixc(hexc("#7d8a4c"), hexc("#a39a62"), t), t))
        top = pts[-1]
        axis = (pts[-1] - pts[-2]).normalized()
        for q in range(14):
            t = q / 13
            a = q * 2.4
            side = Vector((math.cos(a), math.sin(a), 0))
            c = top + axis * (0.1 + 0.85 * t) + side * 0.13 * (1.0 - 0.5 * abs(t - 0.4))
            rot = Vector((0, 0, 1)).rotation_difference((axis * 0.8 + side * 0.6).normalized())
            uv_sphere(mb, c, 1.0, lambda n: with_alpha(mixc(hexc("#6b4a2a"), hexc("#b08a58"), 0.5 + 0.5 * n.z), 1.0),
                      seg=6, rings=4, scale=Vector((0.1, 0.1, 0.2)), rot=rot)
    return build(mb, name)


def make_hirugao(name, seed):
    """ハマヒルガオ：砂の上をはうつると、つやのある丸い葉、うすいピンクのらっぱの花"""
    rnd = random.Random(seed)
    mb = MB()
    leafc = lambda t, v, p: with_alpha(mixc(hexc("#3f7a3a"), hexc("#6aa957"), 0.4 + 0.4 * t - 0.1 * abs(v)), 0.15)
    vine = lambda t, a, p, d: with_alpha(hexc("#5b7f3c"), 0.05)
    flowers = []
    for r in range(3):
        a0 = TAU * r / 3 + rnd.uniform(-0.4, 0.4)
        d = Vector((math.cos(a0), math.sin(a0), 0))
        side = Vector((-d.y, d.x, 0))
        L = rnd.uniform(2.2, 3.4)
        pts = [d * (L * s / 8) + side * 0.3 * math.sin(s * 0.9) + Vector((0, 0, 0.08)) for s in range(9)]
        tube(mb, pts, [0.05] * 9, 5, vine)
        for s in range(2, 9, 2):
            p = pts[s]
            lv = (side * (1 if s % 4 == 0 else -1) + Vector((0, 0, 0.5))).normalized()
            sv = d
            lp = [p, p + lv * 0.18, p + lv * 0.4, p + lv * 0.62, p + lv * 0.78]
            ribbon(mb, lp, [0.12, 0.62, 0.74, 0.55, 0.0], [sv] * 5, leafc, fold=0.15, normal_vecs=[Vector((0, 0, 1))] * 5)
        if r < 2:
            flowers.append(pts[rnd.randint(5, 8)])
    for f in flowers:
        # らっぱの形の花（上を向く）
        prof = [(0.06, 0.0), (0.1, 0.25), (0.22, 0.45), (0.5, 0.62), (0.62, 0.68)]

        def fc(t, a, p):
            c = mixc(hexc("#fff4f2"), hexc("#f3a7c4"), sstep(0.3, 1.0, t))
            star = 0.5 + 0.5 * math.cos(a * 5)
            return with_alpha(mixc(c, hexc("#fde3ec"), 0.3 * star * t), 0.2)
        tmp = MB()
        lathe(tmp, prof, 20, fc)
        # 内がわ（花の中も見える）
        lathe(tmp, [(r * 0.97, z + 0.005) for (r, z) in reversed(prof)], 20, lambda t, a, p: with_alpha(mixc(hexc("#f6b3cb"), hexc("#fffaf3"), t), 0.2))
        mb.add(tmp, Matrix.Translation(f + Vector((0, 0, 0.05))))
    return build(mb, name)


def make_kelp(name, seed):
    """潮だまりの海藻（ワカメのような、波打つ帯）"""
    rnd = random.Random(seed)
    mb = MB()
    for k in range(5):
        a = rnd.uniform(0, TAU)
        base = Vector((math.cos(a) * 0.15, math.sin(a) * 0.15, -0.05))
        h = rnd.uniform(1.6, 2.8)
        la = rnd.uniform(0, TAU)
        lean = Vector((math.cos(la), math.sin(la), 0))
        n = 9
        pts, ws, sides = [], [], []
        for s in range(n):
            t = s / (n - 1)
            pts.append(base + Vector((0, 0, h * t)) + lean * (0.5 * t * t) + Vector((math.sin(t * 7 + k) * 0.12, 0, 0)))
            ws.append(0.42 * math.sin(math.pi * min(1.0, t * 1.1 + 0.05)) * (1.0 + 0.2 * math.sin(t * 13 + k)))
            sides.append(Vector((-lean.y, lean.x, 0)))
        ws[-1] = 0.0
        ribbon(mb, pts, ws, sides, lambda t, v, p: with_alpha(mixc(hexc("#4a5e22"), hexc("#8a8a3a"), t * 0.8), t), fold=0.1)
    return build(mb, name)


def make_wrack(name, seed):
    """打ち上げられた海藻のかたまり（砂の上に、ぺたりと）"""
    rnd = random.Random(seed)
    mb = MB()
    for k in range(8):
        a = rnd.uniform(0, TAU)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        L = rnd.uniform(1.2, 2.4)
        pts = [d * (L * s / 6) + side * 0.25 * math.sin(s * 1.3 + k) + Vector((0, 0, 0.05 + 0.04 * math.sin(s + k))) for s in range(7)]
        ws = [0.25, 0.3, 0.32, 0.28, 0.24, 0.16, 0.0]
        ribbon(mb, pts, ws, [side] * 7,
               lambda t, v, p: with_alpha(mixc(hexc("#3d3a1e"), hexc("#6b6a2c"), 0.3 + 0.5 * t), 0.0), fold=0.05,
               normal_vecs=[Vector((0, 0, 1))] * 7)
    for k in range(5):
        uv_sphere(mb, Vector((rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), 0.08)), 0.09,
                  lambda n: hexc("#5a4e22"), seg=6, rings=4)
    return build(mb, name)


# ---------------------------------------------------------------------------
MAKERS = [
    make_conch, make_driftwood, make_cape, make_islet, make_sandal, make_float,
    lambda: make_reef("Coast_Reef_A", 701, (3.0, 2.4, 1.6)),
    lambda: make_reef("Coast_Reef_B", 702, (2.2, 1.8, 1.2), barnacles=24),
    lambda: make_reef("Coast_Reef_C", 703, (4.2, 3.0, 2.0), barnacles=48),
    make_clam, make_scallop, make_spiral_shell, make_sea_glass,
    make_burrow, make_lighthouse,
    lambda: make_beach_grass("Coast_Grass_A", 801),
    lambda: make_beach_grass("Coast_Grass_B", 802, count=6),
    lambda: make_hirugao("Coast_Hirugao", 811),
    lambda: make_kelp("Coast_Kelp", 821),
    lambda: make_wrack("Coast_Wrack", 831),
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
