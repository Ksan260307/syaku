"""
しゃくとりの森 — 山のアセット生成スクリプト

しゃくとりむしから見た山の物（ねじれた大きな松・山小屋・岩のアーチ・平たい岩の段・道しるべ・山頂の標柱・三角点・
雪のかたまり・はい松・雲）と、高山の花（コマクサ・チングルマ・チングルマの綿毛・クルマユリ）、湧き水のかけいを作り、
FBX を書き出します。1 単位 = しゃくとりむしの体長くらい（公園と同じく、おもちゃのような大きさ）。

使い方:
  blender -b --factory-startup --python blender/scripts/build_mountain_kit.py -- [fbx_out_dir] [blend_out]
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
from build_forest_kit import (MB, TAU, bark_color, build, connect_rings, hexc, lathe, lerp, mixc, quad_bezier,  # noqa: E402
                              ribbon, scalec, sstep, tube, uv_sphere, with_alpha)
from build_park_kit import box  # noqa: E402


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "mountain_kit.blend")
    return fbx, blend


GRANITE_A = hexc("#9a9a9e")
GRANITE_B = hexc("#c4c0b8")
GRANITE_DARK = hexc("#6f6e72")
LICHEN = hexc("#c9c78a")
NEEDLE_DARK = hexc("#284a33")
NEEDLE = hexc("#3f6b45")
NEEDLE_TIP = hexc("#6f9a5a")
LOG = hexc("#a0703f")
LOG_DARK = hexc("#6e4826")
ROOF = hexc("#7a3e2e")
ROOF_DARK = hexc("#552a20")
SNOW = hexc("#f3f6fa")
SNOW_SHADE = hexc("#d9e3ef")


# ---------------------------------------------------------------------------
# 岩（花こう岩：灰色に、白い点と地衣類）
# ---------------------------------------------------------------------------
def rock_blob(mb, center, size, seed, cuts=7, flat_top=None, lichen=0.35, floor=-0.3, m=None):
    """けずった岩のかたまり（make_rock と同じ作り方）。flat_top を指定すると、上を平らに切る（段の岩）"""
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
        p *= 1.0 + 0.14 * noise.fractal(p * 1.2 + off, 0.6, 2.0, 4) + 0.03 * noise.noise(p * 5.0 + off)
        for nrm, d in planes:
            k = p.dot(nrm)
            if k > d:
                p -= nrm * (k - d) * (1.0 if nrm.z > 0.99 else 0.92)
        if p.z < floor:
            p.z = floor + (p.z - floor) * 0.3
        v.co = p
    bm.normal_update()
    idx = {}
    for v in bm.verts:
        p = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        nz = v.normal.z
        t = 0.5 + 0.5 * noise.noise(p * 0.5 + off * 1.3)
        c = mixc(GRANITE_A, GRANITE_B, t)
        c = scalec(c, 0.82 + 0.22 * sstep(-0.3, 0.8, v.co.z))
        if noise.noise(p * 6.0 + off) > 0.45:
            c = mixc(c, hexc("#eeeae2"), 0.6)            # 白い長石の点
        lk = sstep(lichen - 0.15, lichen + 0.15, nz + 0.3 * noise.noise(p * 1.1 + off * 0.7))
        c = mixc(c, mixc(LICHEN, hexc("#9fb07a"), 0.5 + 0.5 * noise.noise(p * 3 + off)), 0.6 * lk)
        q = Vector(center) + p
        if m is not None:
            q = m @ q
        idx[v.index] = mb.v(q, c)
    for fc in bm.faces:
        mb.f(*[idx[v.index] for v in fc.verts])
    bm.free()


def make_slab(name, seed, size):
    """平たい岩（岩の階段の段）：上は平らで少しかたむき、ふちは丸い"""
    mb = MB()
    rock_blob(mb, (0, 0, size[2] * 0.25), size, seed, cuts=6, flat_top=0.55, lichen=0.6, floor=-0.45)
    return build(mb, name, smooth=True, sharp_deg=40)


def make_rock_arch():
    """岩のアーチ（岩のトンネル）：2 本の岩の柱に、大きな岩がのる。下をくぐれる（すきまは、はば 5・高さ 5）"""
    mb = MB()
    rock_blob(mb, (-5.6, 0, 2.6), (3.0, 3.2, 3.6), 11, cuts=6, lichen=0.45, floor=-0.75)
    rock_blob(mb, (5.6, 0.3, 2.4), (3.1, 3.0, 3.4), 12, cuts=6, lichen=0.45, floor=-0.75)
    rock_blob(mb, (0.0, 0.1, 7.6), (8.6, 3.4, 2.4), 13, cuts=7, lichen=0.3, floor=-0.95)
    # 上にのった小さな岩（登ると、ながめがいい）
    rock_blob(mb, (-2.2, -0.4, 10.0), (2.0, 1.7, 1.3), 14, cuts=5, lichen=0.5)
    return build(mb, "Mtn_RockArch", smooth=True, sharp_deg=40)


# ---------------------------------------------------------------------------
# ねじれた大きな松
# ---------------------------------------------------------------------------
PINE_PADS = []   # (中心, 半径) — テスト用（枝先の葉のかたまり）


def needle_pad(mb, center, rx, ry, rz, seed):
    """松の葉のかたまり：上が平たく、ふちがもこもこ。上は明るく、下は暗い"""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(-9, 9), rnd.uniform(-9, 9), rnd.uniform(-9, 9)))
    prof = []
    rings = 9
    for i in range(rings + 1):
        th = math.pi * i / rings
        prof.append((math.sin(th), -math.cos(th)))
    tmp = MB()

    def cf(t, a, p):
        n = p.normalized() if p.length > 1e-6 else Vector((0, 0, 1))
        c = mixc(NEEDLE_DARK, NEEDLE, sstep(-0.6, 0.5, n.z))
        c = mixc(c, NEEDLE_TIP, 0.45 * sstep(0.3, 0.9, n.z) * (0.5 + 0.5 * noise.noise(p * 3 + off)))
        return with_alpha(c, 1.0)
    lathe(tmp, prof, 22, cf, rfn=lambda t, a: 1.0 + 0.12 * noise.noise(Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, t * 3)) + off))
    for i, p in enumerate(tmp.V):
        z = p.z
        # 上はほとんど平ら（乗れる）、下はふっくら
        z = z * (0.55 if z > 0 else 1.0)
        bump = 0.08 * noise.noise(p * 4.0 + off)
        tmp.V[i] = Vector((p.x * rx, p.y * ry, z * rz + bump))
    mb.add(tmp, Matrix.Translation(Vector(center)))
    PINE_PADS.append((Vector(center), (rx + ry) * 0.5, rz))


def make_pine():
    """ねじれながら育った松：太い幹が風下へかたむき、横へのびた枝の先に、平たい葉のかたまり（乗って登れる）"""
    PINE_PADS.clear()
    rnd = random.Random(31)
    mb = MB()
    # 幹：根もとは太く、上へ行くほど、ねじれながら東へかたむく
    n = 26
    pts = []
    for i in range(n):
        t = i / (n - 1)
        z = -1.5 + 31.0 * t
        x = 2.6 * math.sin(t * 2.4) * t + 3.2 * t * t
        y = 1.6 * math.sin(t * 3.7 + 0.8) * t
        pts.append(Vector((x, y, z)))
    radii = [lerp(2.6, 0.55, t ** 0.8) for t in (i / (n - 1) for i in range(n))]
    radii[0] = 3.2
    radii[-1] = 0.0

    def trunk_col(t, a, p, d):
        g = sstep(0.7, 1.0, abs(math.sin(a * 7 + p.z * 0.35 + 1.4 * noise.noise(p * 0.3))))
        c = mixc(hexc("#7a4a32"), hexc("#a0684a"), 0.5 + 0.5 * noise.noise(p * 0.8))
        c = mixc(c, hexc("#4a2c1e"), 0.6 * g)                     # 亀の甲のようなわれめ
        return mixc(c, hexc("#c08060"), 0.3 * sstep(0.6, 1.0, t))   # 上の方は赤っぽい
    tube(mb, pts, radii, 22, trunk_col, rfn=lambda t, a: 1.0 + 0.06 * math.sin(a * 5 + t * 9))
    # 根：岩をつかむように、地面をはう
    for k in range(6):
        a = TAU * k / 6 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(4.0, 6.0)
        rp = [d * (1.8 + L * s / 6) + Vector((0, 0, lerp(1.2, -0.5, (s / 6) ** 0.7))) for s in range(7)]
        tube(mb, rp, [1.4, 1.2, 1.0, 0.8, 0.6, 0.4, 0.0], 10, trunk_col)
    # 枝と葉のかたまり：下の枝ほど長く、横へのびる
    branches = [
        (7.0, 0.3, 10.5), (10.5, 2.4, 9.5), (13.5, 4.6, 9.0), (16.5, 1.2, 8.0), (19.0, 3.4, 7.5),
        (22.0, 5.6, 6.5), (24.5, 0.6, 5.5), (27.0, 2.8, 4.5),
    ]
    for k, (z, ang, L) in enumerate(branches):
        i = min(n - 1, int((z + 1.5) / 31.0 * (n - 1)))
        base = pts[i]
        d = Vector((math.cos(ang), math.sin(ang), 0))
        side = Vector((-d.y, d.x, 0))
        bp = [base + d * (L * s / 6) + side * (0.8 * math.sin(s * 0.9 + k)) + Vector((0, 0, 0.9 * math.sin(math.pi * s / 6 * 0.7) - 0.15 * s))
              for s in range(7)]
        r0 = lerp(0.9, 0.45, k / (len(branches) - 1))
        tube(mb, bp, [r0, r0 * 0.85, r0 * 0.7, r0 * 0.6, r0 * 0.5, r0 * 0.4, r0 * 0.3], 10, trunk_col)
        # 先と、まん中の葉のかたまり
        tip = bp[-1]
        pr = lerp(5.2, 3.4, k / (len(branches) - 1))
        needle_pad(mb, tip + Vector((0, 0, 0.5)), pr, pr * 0.82, 1.9, 100 + k)
        # 先の葉のかたまりのまわりに、小さなかたまりをいくつか（もこもこの雲のように）
        for j in range(3):
            a2 = ang + (j - 1) * 0.9
            q = tip + Vector((math.cos(a2), math.sin(a2), 0)) * pr * 0.75 + Vector((0, 0, 0.2 + 0.3 * j))
            needle_pad(mb, q, pr * 0.5, pr * 0.42, 1.4, 150 + k * 3 + j)
        if L > 7.0:
            needle_pad(mb, bp[3] + Vector((0, 0, 0.7)), pr * 0.62, pr * 0.52, 1.3, 200 + k)
    # てっぺん
    needle_pad(mb, pts[-2] + Vector((0, 0, 0.8)), 3.6, 3.0, 2.2, 300)
    return build(mb, "Mtn_Pine", smooth=True)


# ---------------------------------------------------------------------------
# 山小屋：丸太のかべ・赤い三角屋根・木のとびら・窓・えんとつ・入り口の石段
# 正面（とびら）は -Y。床は z = 0、屋根のむねは z = HUT_RIDGE
# ---------------------------------------------------------------------------
HUT_W, HUT_D, HUT_WALL, HUT_RIDGE = 15.0, 10.0, 6.5, 11.5


def make_hut():
    mb = MB()
    hw, hd = HUT_W / 2, HUT_D / 2
    # 土台の石
    box(mb, (0, 0, 0.25), (HUT_W + 0.8, HUT_D + 0.8, 1.1), lambda n: scalec(GRANITE_A, 0.95 + 0.1 * n.z))
    # 丸太のかべ：横に積んだ丸太（はしは少しつき出る）
    logs = 9
    for k in range(logs):
        z = 0.8 + (HUT_WALL - 0.8) * (k + 0.5) / logs
        r = (HUT_WALL - 0.8) / logs * 0.55
        sh = 0.92 + 0.08 * ((k * 7) % 3) / 2
        for (a, b) in (((-hw - 0.6, -hd, z), (hw + 0.6, -hd, z)), ((-hw - 0.6, hd, z), (hw + 0.6, hd, z)),
                       ((-hw, -hd - 0.6, z), (-hw, hd + 0.6, z)), ((hw, -hd - 0.6, z), (hw, hd + 0.6, z))):
            tube(mb, [Vector(a), Vector(b)], [r, r], 10,
                 lambda t, ang, p, d, sh=sh: scalec(mixc(LOG, LOG_DARK, 0.25 + 0.25 * math.sin(p.x * 3 + p.y * 3)), sh * (0.85 + 0.15 * d.z)))
    # かべの中身（丸太のすきまをうめる板）
    box(mb, (0, 0, (HUT_WALL + 0.8) / 2), (HUT_W - 0.4, HUT_D - 0.4, HUT_WALL - 0.8), lambda n: scalec(LOG_DARK, 0.9))
    # 妻かべ（むねのはしの、三角の所）：たての板
    for sx in (-1, 1):
        x = sx * (hw - 0.1)
        a = mb.v((x, -hd, HUT_WALL - 0.2), LOG_DARK)
        b = mb.v((x, hd, HUT_WALL - 0.2), LOG_DARK)
        c = mb.v((x, 0, HUT_RIDGE - 0.4), LOG_DARK)
        if sx > 0:
            mb.f(a, b, c)
        else:
            mb.f(b, a, c)
        for k in range(-3, 4):
            y = k * 1.25
            top = HUT_RIDGE - 0.4 - (HUT_RIDGE - HUT_WALL) * abs(y) / hd
            if top - HUT_WALL < 0.4:
                continue
            box(mb, (x + sx * 0.12, y, (HUT_WALL + top) / 2), (0.12, 0.14, top - HUT_WALL), lambda n: scalec(LOG_DARK, 0.75))
    # 屋根：むねは X 方向。のき先はかべより外へ出る（板の厚み 0.5）
    over = 1.4
    slope = math.atan2(HUT_RIDGE - HUT_WALL, hd)
    length = math.hypot(hd + over, (HUT_RIDGE - HUT_WALL) * (hd + over) / hd)
    for sy in (-1, 1):
        rot = Matrix.Rotation(-sy * slope, 4, "X")
        cy = sy * (hd + over) / 2
        cz = HUT_RIDGE - (HUT_RIDGE - HUT_WALL) * ((hd + over) / 2) / hd + 0.25
        m = Matrix.Translation(Vector((0, cy, cz))) @ rot
        box(mb, (0, 0, 0), (HUT_W + over * 2, length, 0.5),
            lambda nn: mixc(ROOF, ROOF_DARK, 0.3 if nn.z < 0.5 else 0.0), m=m)
        # トタンのすじ
        for k in range(12):
            x = -hw - over + (HUT_W + over * 2) * (k + 0.5) / 12
            box(mb, (x, 0, 0.3), (0.12, length * 0.98, 0.12), lambda nn: ROOF_DARK, m=m)
    # むねの木
    tube(mb, [Vector((-hw - over, 0, HUT_RIDGE + 0.35)), Vector((hw + over, 0, HUT_RIDGE + 0.35))], [0.35, 0.35], 8,
         lambda t, a, p, d: ROOF_DARK)
    # とびら（正面 -Y）と窓
    box(mb, (-3.0, -hd - 0.45, 2.8), (2.6, 0.3, 4.0), lambda n: mixc(hexc("#6a3e22"), hexc("#8a5a34"), 0.5 + 0.5 * n.z))
    uv_sphere(mb, Vector((-2.0, -hd - 0.65, 2.8)), 0.18, lambda n: hexc("#d8b85a"), seg=8, rings=5)
    for wx in (3.2,):
        box(mb, (wx, -hd - 0.4, 3.9), (3.0, 0.25, 2.4), lambda n: hexc("#5a3a22"))
        box(mb, (wx, -hd - 0.52, 3.9), (2.4, 0.1, 1.8), lambda n: hexc("#a8d4ec"))
        box(mb, (wx, -hd - 0.6, 3.9), (0.15, 0.1, 1.8), lambda n: hexc("#5a3a22"))
        box(mb, (wx, -hd - 0.6, 3.9), (2.4, 0.1, 0.15), lambda n: hexc("#5a3a22"))
    for wy in (-2.0, 2.0):
        box(mb, (hw + 0.4, wy, 3.9), (0.25, 2.2, 2.0), lambda n: hexc("#5a3a22"))
        box(mb, (hw + 0.52, wy, 3.9), (0.1, 1.7, 1.5), lambda n: hexc("#a8d4ec"))
    # えんとつ
    box(mb, (4.5, 2.0, HUT_RIDGE - 0.6), (1.4, 1.4, 3.6), lambda n: scalec(GRANITE_A, 0.9 + 0.1 * n.z))
    box(mb, (4.5, 2.0, HUT_RIDGE + 1.25), (1.7, 1.7, 0.3), lambda n: GRANITE_DARK)
    # 入り口の石段（2 段）
    box(mb, (-3.0, -hd - 1.6, 0.35), (3.4, 1.6, 0.7), lambda n: scalec(GRANITE_B, 0.9 + 0.1 * n.z))
    box(mb, (-3.0, -hd - 2.7, 0.0), (3.6, 1.4, 0.6), lambda n: scalec(GRANITE_A, 0.9 + 0.1 * n.z))
    return build(mb, "Mtn_Hut", smooth=False)


# ---------------------------------------------------------------------------
# 道しるべ・山頂の標柱・三角点
# ---------------------------------------------------------------------------
def make_sign():
    """木の道しるべ：柱に、矢印の板が 2 まい（右と左を指す）"""
    mb = MB()
    wood = hexc("#b08a5a")
    dark = hexc("#7a5a36")
    box(mb, (0, 0, 3.0), (0.7, 0.7, 6.4), lambda n: scalec(wood, 0.9 + 0.1 * n.x))
    box(mb, (0, 0, 6.3), (0.9, 0.9, 0.3), lambda n: dark)
    for z, sx in ((5.0, 1), (3.9, -1)):
        x0 = sx * 0.35
        x1 = sx * 4.0
        box(mb, ((x0 + x1) / 2 - sx * 0.3, -0.45, z), (abs(x1 - x0) - 0.6, 0.18, 0.85), lambda n: mixc(hexc("#efe4c8"), wood, 0.2))
        # 矢の先（三角）
        a = mb.v((x1 - sx * 0.6, -0.55, z + 0.55), hexc("#efe4c8"))
        b = mb.v((x1 - sx * 0.6, -0.55, z - 0.55), hexc("#efe4c8"))
        c = mb.v((x1 + sx * 0.3, -0.55, z), hexc("#efe4c8"))
        if sx > 0:
            mb.f(a, b, c)
        else:
            mb.f(b, a, c)
        # 文字のかわりの、黒い線
        for k in range(3):
            xs = x0 + sx * (0.7 + k * 0.9)
            box(mb, (xs, -0.56, z), (0.5, 0.04, 0.12), lambda n: hexc("#3a2a1e"))
    return build(mb, "Mtn_Sign", smooth=False)


def make_summit_post():
    """山頂の標柱：太い角柱（てっぺんは、とがった屋根）"""
    mb = MB()
    wood = hexc("#c8a476")
    box(mb, (0, 0, 4.5), (1.4, 1.4, 9.4), lambda n: scalec(wood, 0.88 + 0.12 * (n.x + n.y + 1) / 2))
    # とがった頭
    tip = mb.v((0, 0, 10.4), hexc("#8a6a44"))
    cs = [mb.v((sx * 0.75, sy * 0.75, 9.2), hexc("#9a7a52")) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for i in range(4):
        mb.f(cs[i], cs[(i + 1) % 4], tip)
    # 白くぬった面に、たて書きの文字のような、細いすじ
    box(mb, (0, -0.72, 5.6), (1.0, 0.04, 6.4), lambda n: hexc("#f2ece0"))
    rnd = random.Random(5)
    for k in range(4):
        zc = 8.0 - k * 1.45
        for j in range(3):
            if rnd.random() < 0.8:
                box(mb, (0, -0.76, zc + 0.35 - j * 0.35), (rnd.uniform(0.35, 0.7), 0.04, 0.09), lambda n: hexc("#2e241c"))
        box(mb, (rnd.uniform(-0.15, 0.15), -0.76, zc), (0.09, 0.04, 0.9), lambda n: hexc("#2e241c"))
    return build(mb, "Mtn_SummitPost", smooth=False)


def make_benchmark():
    """三角点：四角い石の柱（上に十字のしるし）"""
    mb = MB()
    stone = hexc("#d8d4cc")
    box(mb, (0, 0, 0.7), (1.6, 1.6, 2.0), lambda n: scalec(stone, 0.88 + 0.12 * n.z))
    box(mb, (0, 0, 1.72), (0.9, 0.12, 0.06), lambda n: hexc("#5a5650"))
    box(mb, (0, 0, 1.72), (0.12, 0.9, 0.06), lambda n: hexc("#5a5650"))
    return build(mb, "Mtn_Benchmark", smooth=False)


# ---------------------------------------------------------------------------
# 雪のかたまり・はい松・雲
# ---------------------------------------------------------------------------
def make_snow_mound(name, seed, size):
    """とけ残った雪：まるいかたまり。上は白、すそは青いかげ、よごれのすじ"""
    rnd = random.Random(seed)
    off = Vector((rnd.uniform(-9, 9), rnd.uniform(-9, 9), rnd.uniform(-9, 9)))
    mb = MB()
    prof = [(0.0, -0.3), (1.0, -0.3), (1.0, 0.05), (0.92, 0.35), (0.72, 0.68), (0.42, 0.9), (0.0, 1.0)]

    def col(t, a, p):
        c = mixc(SNOW_SHADE, SNOW, sstep(0.0, 0.6, p.z))
        if noise.noise(p * 0.6 + off) > 0.42:
            c = mixc(c, hexc("#c9c0b0"), 0.35)   # 少しよごれた所
        return c
    lathe(mb, prof, 32, col, rfn=lambda t, a: 1.0 + 0.18 * noise.noise(Vector((math.cos(a) * 1.6, math.sin(a) * 1.6, t * 2)) + off))
    mb.V = [Vector((p.x * size[0], p.y * size[1], p.z * size[2] + 0.12 * noise.noise(p * 2.0 + off))) for p in mb.V]
    return build(mb, name, smooth=True)


def make_haimatsu():
    """はい松：地面をはうように広がる、低い松のしげみ（風でゆれる）"""
    rnd = random.Random(7)
    mb = MB()
    for k in range(9):
        a = TAU * k / 9 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(2.5, 4.2)
        bp = [d * (L * s / 4) + Vector((0, 0, 0.35 + 0.5 * math.sin(math.pi * s / 4 * 0.8))) for s in range(5)]
        tube(mb, bp, [0.22, 0.18, 0.14, 0.1, 0.0], 6, lambda t, ang, p, dd: with_alpha(hexc("#5a3a26"), 0.1))
        for s in (2, 3, 4):
            c = bp[s] + Vector((0, 0, 0.3))
            r = 1.3 - 0.15 * s
            uv_sphere(mb, c, 1.0,
                      lambda n, s=s: with_alpha(mixc(NEEDLE_DARK, NEEDLE_TIP, sstep(-0.4, 0.9, n.z) * 0.8), 0.4 + 0.15 * s),
                      seg=12, rings=7, scale=Vector((r, r * 0.85, r * 0.55)))
    return build(mb, "Mtn_Haimatsu", smooth=True)


def make_cloud_puff():
    """雲海のひとかたまり：まるいもこもこを重ねた、平たい雲（山頂から見下ろす）"""
    rnd = random.Random(3)
    mb = MB()
    for k in range(9):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0, 9)
        c = Vector((math.cos(a) * r * 1.4, math.sin(a) * r, rnd.uniform(0, 2.5)))
        s = rnd.uniform(4.5, 7.5)
        uv_sphere(mb, c, s, lambda n: mixc(hexc("#dfe7f2"), hexc("#ffffff"), sstep(-0.4, 0.8, n.z)), seg=14, rings=8,
                  scale=Vector((1.2, 1.0, 0.55)))
    return build(mb, "Mtn_CloudPuff", smooth=True)


# ---------------------------------------------------------------------------
# 湧き水のかけい：岩のすきまからのびる、竹のとい（水がちょろちょろ流れ出る）
# ---------------------------------------------------------------------------
SPOUT_TIP = Vector((0.0, -5.6, 2.1))   # といの先（水が落ちはじめる所）


def make_spout():
    mb = MB()
    rock_blob(mb, (0, 1.6, 1.6), (3.0, 2.6, 2.6), 21, cuts=6, lichen=0.4, floor=-0.6)
    green = hexc("#7fa64a")
    # 竹のとい（半分に切った竹）：岩から泉の方（-Y）へ、少し下りながら
    a, b = Vector((0, 0.6, 2.9)), SPOUT_TIP
    tube(mb, [a, a.lerp(b, 0.5), b], [0.42, 0.42, 0.42], 12,
         lambda t, ang, p, d: mixc(green, hexc("#c8d890"), 0.4 if d.z > 0.3 else 0.0) if math.sin(ang) < 0.5 else hexc("#4a6a2a"))
    # ふし
    for t in (0.35, 0.7):
        q = a.lerp(b, t)
        tube(mb, [q + Vector((0, 0.06, 0)), q - Vector((0, 0.06, 0))], [0.48, 0.48], 12, lambda t, ang, p, d: hexc("#5a7a32"))
    # ささえの、交差した 2 本の枝
    q = a.lerp(b, 0.75)
    for sx in (-1, 1):
        tube(mb, [q + Vector((sx * 1.2, 0, -2.4)), q + Vector((-sx * 0.3, 0, 0.3))], [0.16, 0.12], 6, lambda t, ang, p, d: hexc("#7a5a3a"))
    return build(mb, "Mtn_Spout", smooth=True)


# ---------------------------------------------------------------------------
# 高山の花
# ---------------------------------------------------------------------------
def make_komakusa(name, seed):
    """コマクサ：白っぽい青緑の、こまかく切れた葉の株から、うすいピンクのハート形の花がうつむいてさく"""
    rnd = random.Random(seed)
    mb = MB()
    leaf = hexc("#9fb8b0")
    # こまかい葉（細いリボンを放射状に）
    for k in range(14):
        a = TAU * k / 14 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        L = rnd.uniform(1.0, 1.6)
        pts = [d * (L * s / 4) + Vector((0, 0, 0.45 * math.sin(math.pi * s / 4 * 0.85))) for s in range(5)]
        ribbon(mb, pts, [0.05, 0.16, 0.2, 0.14, 0.0], [sv] * 5,
               lambda tt, v, p: with_alpha(mixc(hexc("#7f9a92"), leaf, tt), tt * 0.3), fold=0.25, normal_vecs=[Vector((0, 0, 1))] * 5)
    # 花茎と、うつむいた花（2〜3 つ）
    for k in range(rnd.randint(2, 3)):
        a = rnd.uniform(0, TAU)
        d = Vector((math.cos(a), math.sin(a), 0))
        h = rnd.uniform(1.8, 2.5)
        stem = quad_bezier(Vector((0, 0, 0)), d * 0.3 + Vector((0, 0, h)), d * 0.9 + Vector((0, 0, h * 0.85)), 8)
        tube(mb, stem, [0.05] * 7 + [0.04], 6, lambda t, ang, p, dd: with_alpha(mixc(hexc("#8a7a8a"), hexc("#b49aaa"), t), t))
        top = stem[-1]
        for s in (-1, 1):
            # ハート形：ふくらんだ花びら 2 枚が、外へそり返る
            c = top + Vector((0, 0, -0.35)) + Vector((-d.y, d.x, 0)) * 0.14 * s
            uv_sphere(mb, c, 1.0, lambda n: with_alpha(mixc(hexc("#f2a6c0"), hexc("#fbe0ea"), sstep(-0.6, 0.6, n.z)), 1.0),
                      seg=10, rings=7, scale=Vector((0.2, 0.2, 0.36)))
            tip = c + Vector((-d.y, d.x, 0)) * 0.18 * s + Vector((0, 0, -0.38))
            tube(mb, [c + Vector((0, 0, -0.2)), tip], [0.1, 0.0], 6, lambda t, ang, p, dd: with_alpha(hexc("#e88aac"), 1.0))
    return build(mb, name)


def make_chinguruma(name, seed):
    """チングルマ：低くはう木。つやのある葉の上に、白い 5 まいの花びらと、黄色いまん中"""
    rnd = random.Random(seed)
    mb = MB()
    for k in range(9):
        a = TAU * k / 9 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        L = rnd.uniform(0.9, 1.3)
        pts = [d * (0.2 + L * s / 4) + Vector((0, 0, 0.18 + 0.12 * math.sin(math.pi * s / 4))) for s in range(5)]
        ribbon(mb, pts, [0.1, 0.36, 0.42, 0.3, 0.0], [sv] * 5,
               lambda tt, v, p: with_alpha(mixc(hexc("#3f7a3a"), hexc("#6fae52"), tt), tt * 0.2), fold=0.15,
               normal_vecs=[Vector((0, 0, 1))] * 5)
    for k in range(rnd.randint(2, 4)):
        a = rnd.uniform(0, TAU)
        c = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.1, 0.8) + Vector((0, 0, rnd.uniform(0.9, 1.3)))
        tube(mb, [Vector((c.x * 0.6, c.y * 0.6, 0.1)), c], [0.04, 0.03], 5, lambda t, ang, p, d: with_alpha(hexc("#7a5a3a"), t))
        for j in range(5):
            pa = TAU * j / 5 + k
            pd = Vector((math.cos(pa), math.sin(pa), 0))
            psv = Vector((-pd.y, pd.x, 0))
            pts = [c + pd * (0.06 + 0.42 * s / 3) + Vector((0, 0, 0.05 * s)) for s in range(4)]
            ribbon(mb, pts, [0.16, 0.36, 0.34, 0.0], [psv] * 4,
                   lambda tt, v, p: with_alpha(mixc(hexc("#fffef6"), hexc("#f2f0e2"), tt), 1.0), fold=0.06,
                   normal_vecs=[Vector((0, 0, 1))] * 4)
        uv_sphere(mb, c + Vector((0, 0, 0.06)), 0.13, lambda n: with_alpha(hexc("#f2c234"), 1.0), seg=8, rings=5,
                  scale=Vector((1, 1, 0.6)))
    return build(mb, name)


def make_chinguruma_seed(name, seed):
    """チングルマの綿毛：花のあと、羽のような毛が風車のように広がる（名前の「稚児車」の由来）"""
    rnd = random.Random(seed)
    mb = MB()
    tube(mb, [Vector((0, 0, 0)), Vector((0.1, 0, 1.2)), Vector((0.15, 0.05, 2.2))], [0.05, 0.045, 0.04], 5,
         lambda t, ang, p, d: with_alpha(hexc("#8a5a4a"), t))
    top = Vector((0.15, 0.05, 2.2))
    for j in range(18):
        a = TAU * j / 18 + rnd.uniform(-0.1, 0.1)
        d = Vector((math.cos(a), math.sin(a), 0))
        tip = top + d * rnd.uniform(0.7, 0.95) + Vector((0, 0, rnd.uniform(0.25, 0.55)))
        mid = top.lerp(tip, 0.5) + Vector((0, 0, 0.15))
        sv = Vector((-d.y, d.x, 0))
        ribbon(mb, [top, mid, tip], [0.02, 0.12, 0.04], [sv] * 3,
               lambda tt, v, p: with_alpha(mixc(hexc("#d8a8a0"), hexc("#f6eae2"), tt), 1.0), fold=0.0,
               normal_vecs=[Vector((0, 0, 1))] * 3)
    return build(mb, name)


def make_kurumayuri(name, seed):
    """クルマユリ：茎の中ほどに葉が車輪のようにつき、てっぺんに、そり返ったオレンジの花（黒い点）"""
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(4.6, 5.6)
    stem = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 0.6)), Vector((0.3, 0.1, h)), 10)
    tube(mb, stem, [0.09] * 9 + [0.07], 7, lambda t, a, p, d: with_alpha(mixc(hexc("#3f6e2e"), hexc("#6a9a44"), t), t))
    # 車輪のような葉
    z = h * 0.42
    for k in range(8):
        a = TAU * k / 8
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        pts = [Vector((0, 0, z)) + d * (1.3 * s / 4) + Vector((0, 0, 0.15 * math.sin(math.pi * s / 4))) for s in range(5)]
        ribbon(mb, pts, [0.06, 0.28, 0.3, 0.2, 0.0], [sv] * 5,
               lambda tt, v, p: with_alpha(mixc(hexc("#3c6e30"), hexc("#68a84a"), tt), 0.4 + 0.4 * tt), fold=0.2,
               normal_vecs=[Vector((0, 0, 1))] * 5)
    # 花：下を向いて、6 まいの花びらが上へそり返る
    top = stem[-1]
    for k in range(rnd.randint(1, 2)):
        c = top + Vector((0.35 * k, 0.2 * k, -0.25 - 0.3 * k))
        for j in range(6):
            a = TAU * j / 6
            d = Vector((math.cos(a), math.sin(a), 0))
            sv = Vector((-d.y, d.x, 0))
            pts = [c + d * (0.12 + 0.4 * s / 4) + Vector((0, 0, -0.4 + 0.65 * (s / 4) ** 1.6)) for s in range(5)]

            def pc(tt, v, p):
                cc = mixc(hexc("#f07a28"), hexc("#ffa848"), tt)
                if 0.2 < tt < 0.6 and abs(math.sin(p.x * 23 + p.y * 19)) > 0.92:
                    cc = hexc("#5a2a14")   # 黒っぽい点
                return with_alpha(cc, 1.0)
            ribbon(mb, pts, [0.08, 0.22, 0.24, 0.18, 0.0], [sv] * 5, pc, fold=0.12, normal_vecs=[d] * 5)
        for j in range(5):
            a = TAU * j / 5
            tube(mb, [c, c + Vector((math.cos(a) * 0.15, math.sin(a) * 0.15, -0.55))], [0.015, 0.012], 4,
                 lambda t, ang, p, d: with_alpha(hexc("#8a3a1a") if t > 0.85 else hexc("#f0a060"), 1.0))
    return build(mb, name)


def main():
    fbx_dir, blend_out = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    makers = [
        make_pine, make_hut, make_rock_arch, make_sign, make_summit_post, make_benchmark, make_spout,
        lambda: make_slab("Mtn_Slab_A", 41, (3.4, 2.8, 1.5)),
        lambda: make_slab("Mtn_Slab_B", 42, (2.8, 2.3, 1.3)),
        lambda: make_slab("Mtn_Slab_C", 43, (4.2, 3.2, 1.8)),
        lambda: make_snow_mound("Mtn_SnowMound_A", 51, (3.2, 2.6, 1.3)),
        lambda: make_snow_mound("Mtn_SnowMound_B", 52, (2.2, 1.9, 0.9)),
        make_haimatsu, make_cloud_puff,
        lambda: make_komakusa("Mtn_Komakusa", 61),
        lambda: make_chinguruma("Mtn_Chinguruma", 62),
        lambda: make_chinguruma_seed("Mtn_ChingurumaSeed", 63),
        lambda: make_kurumayuri("Mtn_Kurumayuri", 64),
    ]
    objs = []
    for mk in makers:
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
