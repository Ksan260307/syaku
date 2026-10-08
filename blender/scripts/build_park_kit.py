"""
しゃくとりの森 — 公園のアセット生成スクリプト

しゃくとりむしから見た公園の遊具（すべり台・ブランコ・シーソー・ジャングルジム・砂場・どかん・ベンチ・
水飲み場・花だん・タイヤ・ボール・さく・街灯・クヌギの木）と、花だんの草花（チューリップ・キャベツ）を作り、
FBX を書き出します。1 単位 = しゃくとりむしの体長くらい（実物の遊具よりずっと小さく、おもちゃのような大きさ）。

使い方:
  blender -b --factory-startup --python blender/scripts/build_park_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import (MB, TAU, bark_color, build, hexc, lathe, lerp, mixc, polar_sheet, scalec,  # noqa: E402
                              sstep, trunk_mesh, tube, uv_sphere, with_alpha)


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "park_kit.blend")
    return fbx, blend


# ---------------------------------------------------------------------------
# かたい物の部品
# ---------------------------------------------------------------------------
def box(mb, c, s, col, m=None):
    """直方体（面ごとに頂点を分けて、角をくっきり）。col は色、または col(法線) -> 色"""
    c = Vector(c)
    h = Vector(s) * 0.5
    faces = [
        (Vector((1, 0, 0)), [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)]),
        (Vector((-1, 0, 0)), [(-1, 1, -1), (-1, -1, -1), (-1, -1, 1), (-1, 1, 1)]),
        (Vector((0, 1, 0)), [(1, 1, -1), (-1, 1, -1), (-1, 1, 1), (1, 1, 1)]),
        (Vector((0, -1, 0)), [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)]),
        (Vector((0, 0, 1)), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
        (Vector((0, 0, -1)), [(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)]),
    ]
    for n, quad in faces:
        cc = col(n) if callable(col) else col
        ids = []
        for q in quad:
            p = c + Vector((q[0] * h.x, q[1] * h.y, q[2] * h.z))
            if m is not None:
                p = m @ p
            ids.append(mb.v(p, cc))
        mb.f(*ids)


def rod(mb, a, b, r, col, seg=10, caps=True):
    """丸い棒（はしはふた付き）"""
    a, b = Vector(a), Vector(b)
    cf = (lambda t, ang, p, d: col(t, p)) if callable(col) else (lambda t, ang, p, d: col)
    if caps:
        tube(mb, [a, a.lerp(b, 1e-4), b.lerp(a, 1e-4), b], [0.0, r, r, 0.0], seg, cf)
    else:
        tube(mb, [a, b], [r, r], seg, cf)


def torus(mb, R, r, colfn, seg=28, rseg=12, m=None):
    """Z 軸まわりのドーナツ。colfn(u, v, p) u=まわり, v=断面"""
    rings = []
    for i in range(seg):
        u = TAU * i / seg
        ring = []
        for j in range(rseg):
            v = TAU * j / rseg
            p = Vector(((R + r * math.cos(v)) * math.cos(u), (R + r * math.cos(v)) * math.sin(u), r * math.sin(v)))
            q = m @ p if m is not None else p
            ring.append(mb.v(q, colfn(u, v, p)))
        rings.append(ring)
    for i in range(seg):
        A, B = rings[i], rings[(i + 1) % seg]
        for j in range(rseg):
            mb.f(A[j], A[(j + 1) % rseg], B[(j + 1) % rseg], B[j])


PAINT_RED = hexc("#e2483a")
PAINT_BLUE = hexc("#3a7fd0")
PAINT_YELLOW = hexc("#f2c232")
PAINT_GREEN = hexc("#4fb05a")
PAINT_ORANGE = hexc("#f08a2e")
STEEL = hexc("#cfd6dc")
STEEL_DARK = hexc("#8a949c")
WOOD = hexc("#b98a55")
WOOD_DARK = hexc("#8a6038")
CONCRETE = hexc("#bdb8ae")
CONCRETE_DARK = hexc("#948f86")


def lit(col, k=0.12):
    """上の面を少し明るく、下の面を少し暗く（ぬったペンキの感じ）"""
    def f(n):
        return scalec(col, 1.0 + k * n.z)
    return f


# ---------------------------------------------------------------------------
# すべり台：台（高さ 10）・はしご・ステンレスのすべる面（うしろ = -Y、すべる先 = +Y）
# ---------------------------------------------------------------------------
SLIDE_TOP = 10.0
SLIDE_RAMP_TOP = Vector((0.0, 2.3, SLIDE_TOP))       # すべる面の上のはし（まん中）
SLIDE_RAMP_LOW = Vector((0.0, 17.9, 1.0))            # 坂の下のはし
SLIDE_RAMP_END = Vector((0.0, 20.9, 0.8))            # 平らな出口の先
SLIDE_WIDTH = 3.6


def make_slide_frame():
    mb = MB()
    # 柱（4 本）と台
    for sx in (-1, 1):
        for sy in (-1, 1):
            rod(mb, (2.1 * sx, 2.1 * sy, 0.0), (2.1 * sx, 2.1 * sy, SLIDE_TOP + 3.0), 0.32, PAINT_RED, seg=12)
            uv_sphere(mb, Vector((2.1 * sx, 2.1 * sy, SLIDE_TOP + 3.05)), 0.42, lambda n: PAINT_YELLOW, seg=12, rings=8)
    box(mb, (0, 0, SLIDE_TOP - 0.3), (4.8, 4.8, 0.6), lit(PAINT_BLUE))
    # 手すり（横と、はしごの上は開けておく）
    for z in (SLIDE_TOP + 1.4, SLIDE_TOP + 2.6):
        for sx in (-1, 1):
            rod(mb, (2.1 * sx, -2.1, z), (2.1 * sx, 2.1, z), 0.16, PAINT_YELLOW, seg=8)
    # はしご（-Y 側。ななめ）
    top_y, bot_y = -2.4, -6.4
    for sx in (-1, 1):
        rod(mb, (1.5 * sx, top_y, SLIDE_TOP + 1.6), (1.5 * sx, bot_y, 0.0), 0.2, PAINT_GREEN, seg=10)
    n = 9
    for i in range(1, n + 1):
        t = i / (n + 1)
        y = lerp(bot_y, top_y, t) + 0.0
        z = lerp(0.0, SLIDE_TOP + 1.6, t)
        rod(mb, (-1.5, y, z), (1.5, y, z), 0.17, PAINT_YELLOW, seg=8)
    return build(mb, "Park_SlideFrame", smooth=True, sharp_deg=40)


def make_slide_ramp():
    """すべる面：U 字の溝（底は幅 3.6、両がわに低い壁）。坂から平らな出口へ"""
    mb = MB()
    path = []
    n = 26
    for i in range(n):
        t = i / (n - 1)
        if t < 0.82:
            k = t / 0.82
            p = SLIDE_RAMP_TOP.lerp(SLIDE_RAMP_LOW, k)
            # 上と下はなめらかに曲がる
            p.z += -0.35 * math.sin(math.pi * k) ** 2 * 0.0
        else:
            k = (t - 0.82) / 0.18
            p = SLIDE_RAMP_LOW.lerp(SLIDE_RAMP_END, k)
        path.append(p)
    w = SLIDE_WIDTH * 0.5
    wall = 1.0
    th = 0.25
    # 断面（左の壁の外 → 左の壁の上 → 底 → 右の壁の上 → 右の壁の外 → 下）。底はステンレス、壁はペンキ
    prof = [(-w - th, -0.3, 0), (-w - th, wall, 0), (-w, wall, 0), (-w, 0.0, 0), (-w, 0.0, 1), (w, 0.0, 1), (w, 0.0, 0),
            (w, wall, 0), (w + th, wall, 0), (w + th, -0.3, 0)]
    rows = []
    for i, p in enumerate(path):
        d = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        side = Vector((1, 0, 0))
        up = side.cross(d).normalized()
        row = []
        for x, y, steel in prof:
            q = p + side * x + up * y
            shine = 0.5 + 0.5 * math.sin(i * 0.9 + x * 2.0)
            c = mixc(STEEL, hexc("#f4f8fb"), 0.5 * shine) if steel else mixc(PAINT_ORANGE, scalec(PAINT_ORANGE, 0.85), shine * 0.3)
            row.append(mb.v(q, c))
        rows.append(row)
    m = len(prof)
    for i in range(n - 1):
        A, B = rows[i], rows[i + 1]
        for j in range(m):
            k = (j + 1) % m
            mb.f(A[j], A[k], B[k], B[j])
    # 両はしのふた
    for row in (rows[0], rows[-1]):
        cen = mb.v(sum((mb.V[i] for i in row), Vector()) / m, PAINT_ORANGE)
        for j in range(m):
            mb.f(cen, row[(j + 1) % m], row[j]) if row is rows[0] else mb.f(cen, row[j], row[(j + 1) % m])
    # 坂を支える柱
    for t in (0.55,):
        p = SLIDE_RAMP_TOP.lerp(SLIDE_RAMP_LOW, t)
        for sx in (-1, 1):
            rod(mb, (1.4 * sx, p.y, 0.0), (1.4 * sx, p.y, p.z - 0.3), 0.22, PAINT_RED, seg=10)
    return build(mb, "Park_SlideRamp", smooth=True, sharp_deg=35)


# ---------------------------------------------------------------------------
# ブランコ：わく（幅 21・高さ 16）と、座板・くさり（くさりの上のはしが原点、下へ 13.4）
# ---------------------------------------------------------------------------
SWING_BAR = 16.0
SWING_CHAIN = 13.4


def make_swing_frame():
    mb = MB()
    for sx in (-1, 1):
        x = 10.0 * sx
        for sy in (-1, 1):
            rod(mb, (x, 4.6 * sy, 0.0), (x, 0.0, SWING_BAR + 0.2), 0.42, PAINT_BLUE, seg=12)
        rod(mb, (x, -2.4, 7.0), (x, 2.4, 7.0), 0.26, PAINT_BLUE, seg=10)
        uv_sphere(mb, Vector((x, 0.0, SWING_BAR + 0.25)), 0.75, lambda n: PAINT_RED, seg=14, rings=9)
    rod(mb, (-10.6, 0.0, SWING_BAR), (10.6, 0.0, SWING_BAR), 0.48, PAINT_YELLOW, seg=14)
    return build(mb, "Park_SwingFrame", smooth=True, sharp_deg=45)


def make_swing_seat():
    """座板（上の面が z = 0。くさりをつなぐ金具つき）"""
    mb = MB()
    box(mb, (0, 0, -0.18), (4.2, 1.7, 0.36), lit(hexc("#2f3236"), 0.2))
    box(mb, (0, 0, 0.0), (4.0, 1.5, 0.06), lit(PAINT_RED, 0.15))
    for sx in (-1, 1):
        rod(mb, (1.9 * sx, 0.0, -0.1), (1.9 * sx, 0.0, 0.5), 0.12, STEEL_DARK, seg=8)
    return build(mb, "Park_SwingSeat", smooth=True, sharp_deg=40)


def make_swing_chain():
    """くさり（上のはしが原点、まっすぐ下へ）"""
    mb = MB()
    L = SWING_CHAIN
    link = 0.7
    n = int(L / link)
    for i in range(n):
        z = -i * link - link * 0.5
        rot = Matrix.Rotation(math.pi / 2 if i % 2 else 0.0, 4, "Z") @ Matrix.Rotation(math.pi / 2, 4, "X")
        m = Matrix.Translation((0, 0, z)) @ rot @ Matrix.Diagonal((1.0, 1.6, 1.0, 1.0))
        torus(mb, 0.22, 0.07, lambda u, v, p: mixc(STEEL, STEEL_DARK, 0.5 + 0.5 * math.sin(v)), seg=10, rseg=6, m=m)
    return build(mb, "Park_SwingChain", smooth=True)


# ---------------------------------------------------------------------------
# シーソー：台と、板（板のまん中が原点、長さ 24 は X 方向）
# ---------------------------------------------------------------------------
SEESAW_PIVOT = 2.8


def make_seesaw_base():
    mb = MB()
    # 三角の台
    for sy in (-1, 1):
        y = 1.6 * sy
        rod(mb, (-1.8, y, 0.0), (0.0, y, SEESAW_PIVOT), 0.3, PAINT_GREEN, seg=10)
        rod(mb, (1.8, y, 0.0), (0.0, y, SEESAW_PIVOT), 0.3, PAINT_GREEN, seg=10)
    rod(mb, (0.0, -1.9, SEESAW_PIVOT), (0.0, 1.9, SEESAW_PIVOT), 0.32, STEEL_DARK, seg=12)
    box(mb, (0, 0, 0.1), (4.2, 4.0, 0.3), lit(CONCRETE))
    return build(mb, "Park_SeesawBase", smooth=True, sharp_deg=45)


def make_seesaw_plank():
    mb = MB()
    box(mb, (0, 0, 0.2), (24.0, 2.4, 0.42), lit(WOOD, 0.18))
    for sx in (-1, 1):
        # 座るところ（色つき）と取っ手
        box(mb, (10.0 * sx, 0, 0.44), (3.0, 2.3, 0.1), lit(PAINT_YELLOW if sx > 0 else PAINT_RED, 0.12))
        x = 7.6 * sx
        for sy in (-1, 1):
            rod(mb, (x, 0.9 * sy, 0.4), (x, 0.9 * sy, 2.4), 0.16, STEEL_DARK, seg=8)
        rod(mb, (x, -1.05, 2.4), (x, 1.05, 2.4), 0.2, PAINT_BLUE, seg=8)
        # 下のゴム（地面に当たるところ）
        box(mb, (11.4 * sx, 0, -0.15), (0.9, 1.6, 0.3), lit(hexc("#2f3236")))
    return build(mb, "Park_SeesawPlank", smooth=True, sharp_deg=40)


# ---------------------------------------------------------------------------
# ジャングルジム：3 × 3 × 3 のます（1 ます 4、高さ 12）。段ごとに色がちがう
# ---------------------------------------------------------------------------
def make_jungle_gym():
    mb = MB()
    cell = 4.0
    n = 3
    cols = [PAINT_RED, PAINT_YELLOW, PAINT_BLUE, PAINT_GREEN]
    h = n * cell
    off = -h * 0.5
    r = 0.24
    for k in range(n + 1):
        z = k * cell
        col = cols[k % len(cols)]
        for i in range(n + 1):
            x = off + i * cell
            rod(mb, (x, off, z), (x, off + h, z), r, col, seg=8, caps=False)
            rod(mb, (off, x, z), (off + h, x, z), r, col, seg=8, caps=False)
    for i in range(n + 1):
        for j in range(n + 1):
            rod(mb, (off + i * cell, off + j * cell, 0.0), (off + i * cell, off + j * cell, h), r * 1.05, STEEL, seg=8, caps=False)
            uv_sphere(mb, Vector((off + i * cell, off + j * cell, h)), r * 1.5, lambda nn: PAINT_ORANGE, seg=8, rings=5)
    return build(mb, "Park_JungleGym", smooth=True, sharp_deg=50)


# ---------------------------------------------------------------------------
# 砂場：木のわく（外 26 × 18、高さ 1.2）、砂山、バケツ、スコップ
# ---------------------------------------------------------------------------
SANDBOX = (26.0, 18.0)


def make_sandbox_frame():
    mb = MB()
    W, D = SANDBOX
    t = 1.0
    hgt = 1.2
    rnd = random.Random(5)

    def plank(n):
        return scalec(mixc(WOOD, WOOD_DARK, 0.3 + 0.2 * rnd.random()), 1.0 + 0.12 * n.z)
    box(mb, (0, D * 0.5 - t * 0.5, hgt * 0.5 - 0.3), (W, t, hgt + 0.6), plank)
    box(mb, (0, -D * 0.5 + t * 0.5, hgt * 0.5 - 0.3), (W, t, hgt + 0.6), plank)
    box(mb, (W * 0.5 - t * 0.5, 0, hgt * 0.5 - 0.3), (t, D - 2 * t, hgt + 0.6), plank)
    box(mb, (-W * 0.5 + t * 0.5, 0, hgt * 0.5 - 0.3), (t, D - 2 * t, hgt + 0.6), plank)
    return build(mb, "Park_SandboxFrame", smooth=False)


SAND = hexc("#e2cf9c")
SAND_DARK = hexc("#c9b47e")


def make_sand_mound():
    mb = MB()
    prof = []
    for i in range(14):
        t = i / 13
        r = 7.0 * (1 - t) + 0.001
        z = 3.2 * (1 - (1 - t) ** 2) - 0.3
        prof.append((r if i < 13 else 0.0, z))

    def col(t, a, p):
        return mixc(SAND, SAND_DARK, 0.3 + 0.3 * math.sin(a * 5 + p.z * 2) * (1 - t))
    lathe(mb, prof, 36, col, rfn=lambda t, a: 1.0 + 0.06 * math.sin(a * 3 + 1.0) * (1 - t))
    return build(mb, "Park_SandMound")


def make_bucket():
    """おもちゃのバケツ（中が空いている）"""
    mb = MB()
    red = hexc("#e8483c")
    prof = [(0.0, 0.0), (1.25, 0.0), (1.6, 2.6), (1.48, 2.6), (1.15, 0.18), (0.0, 0.18)]
    lathe(mb, prof, 28, lambda t, a, p: scalec(red, 1.0 - 0.15 * (p.z < 0.2)), closed=False)
    # とって
    pts = []
    for i in range(13):
        a = math.pi * i / 12
        pts.append(Vector((math.cos(a) * 1.6, 0.0, 2.5 + math.sin(a) * 1.4)))
    tube(mb, pts, [0.07] * 13, 6, lambda t, a, p, d: hexc("#f4f4f0"))
    return build(mb, "Park_Bucket")


def make_shovel():
    """おもちゃのスコップ（長さ 5。+Y が先）"""
    mb = MB()
    yel = hexc("#f6c43a")
    rod(mb, (0, -2.6, 0.25), (0, 0.6, 0.25), 0.22, yel, seg=10)
    uv_sphere(mb, Vector((0, -2.7, 0.25)), 0.38, lambda n: yel, seg=10, rings=6)

    def rfn(th):
        return 1.0

    blade = MB()

    def cf(rho, th, p):
        return scalec(yel, 0.95 + 0.1 * rho)
    polar_sheet(blade, lambda th: 1.1 + 0.25 * math.cos(th), cf, n_ang=24, n_rad=4,
                zfn=lambda rho, th: 0.25 * rho * rho * (0.5 + 0.5 * abs(math.sin(th))))
    mb.add(blade, Matrix.Translation((0, 1.6, 0.18)) @ Matrix.Diagonal((1.0, 1.4, 1.0, 1.0)))
    return build(mb, "Park_Shovel", smooth=True)


# ---------------------------------------------------------------------------
# どかん（コンクリートの土管。中をくぐれる。X 方向に長さ 10）
# ---------------------------------------------------------------------------
DOKAN_R = 3.0
DOKAN_LEN = 10.0


def make_dokan():
    mb = MB()
    ro, ri, L = DOKAN_R, DOKAN_R - 0.5, DOKAN_LEN
    prof = [(ri, 0.0), (ro, 0.0), (ro, L), (ri, L)]

    def col(t, a, p):
        stain = 0.5 + 0.5 * math.sin(p.z * 1.3 + a * 2)
        return mixc(CONCRETE, CONCRETE_DARK, 0.15 + 0.2 * stain)
    tmp = MB()
    lathe(tmp, prof, 40, col, closed=True)
    # Z に長い形を、X に長くねかせる（まん中を原点に）
    mb.add(tmp, Matrix.Rotation(math.pi / 2, 4, "Y") @ Matrix.Translation((0, 0, -L * 0.5)))
    return build(mb, "Park_Dokan", smooth=True, sharp_deg=60)


# ---------------------------------------------------------------------------
# ベンチ（長さ 18、すわる面の高さ 4.5。前 = +Y）
# ---------------------------------------------------------------------------
def make_bench():
    mb = MB()
    L = 18.0
    rnd = random.Random(3)
    for k in range(4):
        c = mixc(WOOD, WOOD_DARK, 0.2 + 0.25 * rnd.random())
        box(mb, (0, -1.4 + k * 0.95, 4.5), (L, 0.8, 0.32), lit(c, 0.2))
    for k in range(3):
        c = mixc(WOOD, WOOD_DARK, 0.2 + 0.25 * rnd.random())
        box(mb, (0, -2.1, 6.1 + k * 1.1), (L, 0.32, 0.8), lit(c, 0.2))
    for sx in (-1, 1):
        x = (L * 0.5 - 1.5) * sx
        rod(mb, (x, 1.3, 0.0), (x, 1.3, 4.3), 0.22, STEEL_DARK, seg=8)
        rod(mb, (x, -1.5, 0.0), (x, -1.9, 9.0), 0.22, STEEL_DARK, seg=8)
        rod(mb, (x, 1.3, 4.3), (x, -1.7, 4.3), 0.2, STEEL_DARK, seg=8)
    return build(mb, "Park_Bench", smooth=True, sharp_deg=40)


# ---------------------------------------------------------------------------
# 水飲み場（コンクリートの柱と、上の受け皿・じゃぐち）
# ---------------------------------------------------------------------------
def make_fountain():
    mb = MB()
    prof = [(0.0, 0.0), (1.8, 0.0), (1.5, 6.4), (2.6, 6.6), (2.6, 7.4), (2.2, 7.4), (2.1, 7.0), (0.0, 7.0)]
    lathe(mb, prof, 32, lambda t, a, p: mixc(CONCRETE, CONCRETE_DARK, 0.25 + 0.15 * math.sin(a * 3 + p.z)))
    rod(mb, (0, 0, 7.0), (0, 0, 8.2), 0.22, STEEL, seg=10)
    rod(mb, (0, 0, 8.2), (0, 1.0, 8.4), 0.18, STEEL, seg=10)
    uv_sphere(mb, Vector((0, 0, 8.3)), 0.32, lambda n: STEEL, seg=10, rings=6)
    return build(mb, "Park_Fountain", smooth=True, sharp_deg=50)


# ---------------------------------------------------------------------------
# 花だん（れんがのふち。外 22 × 9、高さ 1.5）
# ---------------------------------------------------------------------------
BED = (22.0, 9.0)


def make_flower_bed():
    mb = MB()
    W, D = BED
    brick = hexc("#b5523a")
    rnd = random.Random(9)
    bw, bh = 1.6, 0.5
    for row in range(3):
        z = row * bh + bh * 0.5
        shift = 0.8 if row % 2 else 0.0
        for side in range(4):
            horiz = side < 2
            length = W if horiz else D - 1.2
            n = int(length / bw)
            for i in range(n):
                u = -length * 0.5 + (i + 0.5) * (length / n) + (shift if i < n - 1 else 0.0) * 0.0
                c = scalec(mixc(brick, hexc("#d07a52"), rnd.random() * 0.4), 0.9 + 0.15 * rnd.random())
                if horiz:
                    y = (D * 0.5 - 0.3) * (1 if side == 0 else -1)
                    box(mb, (u, y, z), (length / n - 0.08, 0.6, bh - 0.06), lit(c, 0.1))
                else:
                    x = (W * 0.5 - 0.3) * (1 if side == 2 else -1)
                    box(mb, (x, u, z), (0.6, length / n - 0.08, bh - 0.06), lit(c, 0.1))
    return build(mb, "Park_FlowerBed", smooth=False)


# ---------------------------------------------------------------------------
# 花だんの草花：チューリップ（色ちがい 3 つ）とキャベツ
# ---------------------------------------------------------------------------
def make_tulip(name, petal, petal_tip):
    mb = MB()
    stem = hexc("#4f9a3c")
    h = 4.2
    tube(mb, [Vector((0, 0, -0.1)), Vector((0.05, 0.0, h * 0.5)), Vector((0, 0.05, h))], [0.1, 0.09, 0.08], 6,
         lambda t, a, p, d: with_alpha(stem, t))
    # 葉（2 枚）
    for k, a in enumerate((0.3, 2.9)):
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [Vector((0, 0, 0.1)), d * 0.5 + Vector((0, 0, 1.4)), d * 0.9 + Vector((0, 0, 2.6))]
        side = Vector((-d.y, d.x, 0))
        kit.ribbon(mb, pts, [0.55, 0.6, 0.0], [side] * 3, lambda t, v, p: with_alpha(mixc(stem, hexc("#7cc25a"), t), t * 0.6), fold=0.3)
    # 花：6 つにふくらんだカップ（上が少し開く）
    prof = [(0.0, -0.05), (0.42, 0.05), (0.72, 0.45), (0.78, 0.95), (0.68, 1.4), (0.6, 1.62)]

    def cup(t, a, p):
        return with_alpha(mixc(petal, petal_tip, sstep(0.35, 1.0, t)), 1.0)
    lathe(mb, prof, 24, cup, center=Vector((0, 0.05, h - 0.05)),
          rfn=lambda t, a: 1.0 + 0.16 * t * abs(math.cos(a * 3)) ** 0.6)
    return build(mb, name)


def make_cabbage():
    mb = MB()
    rnd = random.Random(21)
    green = hexc("#7fc25a")
    pale = hexc("#cdeaa0")
    uv_sphere(mb, Vector((0, 0, 1.2)), 1.3, lambda n: mixc(pale, green, 0.3 + 0.3 * n.z), seg=18, rings=12)
    for i in range(9):
        a = TAU * i / 9 + rnd.uniform(-0.2, 0.2)
        leaf = MB()

        def cf(rho, th, p):
            vein = sstep(0.06, 0.0, abs(math.sin(th * 3))) * 0.3
            return with_alpha(mixc(mixc(pale, green, rho), hexc("#e8f7c8"), vein), 0.3 + 0.5 * rho)
        polar_sheet(leaf, lambda th: 1.0 + 0.12 * math.sin(th * 7), cf, n_ang=18, n_rad=4, a0=-1.3, a1=1.3, full=False,
                    zfn=lambda rho, th: 0.35 * rho * rho)
        tilt = rnd.uniform(0.5, 0.9)
        m = (Matrix.Translation((0, 0, 0.4)) @ Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(-tilt, 4, "Y")
             @ Matrix.Diagonal((2.2, 1.6, 1.0, 1.0)))
        mb.add(leaf, m)
    return build(mb, "Park_Cabbage")


# ---------------------------------------------------------------------------
# タイヤ（半分うまっている。ペンキでぬった 3 色）
# ---------------------------------------------------------------------------
def make_tire(name, paint):
    mb = MB()
    rubber = hexc("#2b2c30")

    def col(u, v, p):
        # 外がわの面はペンキ、内がわはゴムの黒
        outer = math.cos(v) > -0.2
        c = paint if outer else rubber
        tread = 0.5 + 0.5 * math.sin(u * 40)
        return scalec(c, 0.9 + 0.12 * tread * (0 if outer else 1))
    torus(mb, 3.0, 1.1, col, seg=40, rseg=14, m=Matrix.Rotation(math.pi / 2, 4, "X"))
    return build(mb, name)


def make_ball():
    mb = MB()
    red = hexc("#ec4a4a")
    white = hexc("#fbf6ee")

    def col(n):
        band = abs(math.atan2(n.y, n.x)) % (math.pi / 3) < math.pi / 6
        return red if band else white
    uv_sphere(mb, Vector((0, 0, 0)), 2.4, col, seg=36, rings=20)
    return build(mb, "Park_Ball")


# ---------------------------------------------------------------------------
# さく（長さ 8 のひとくぎり。前 = +Y）と、街灯
# ---------------------------------------------------------------------------
def make_fence():
    mb = MB()
    white = hexc("#f2efe6")
    for k in range(6):
        x = -3.5 + k * 1.4
        box(mb, (x, 0, 2.2), (0.55, 0.3, 4.4), lit(white, 0.1))
        # とがった頭
        tip = MB()
        tip.f(tip.v((x - 0.275, -0.15, 4.4), white), tip.v((x + 0.275, -0.15, 4.4), white), tip.v((x, -0.15, 4.9), white))
        tip.f(tip.v((x + 0.275, 0.15, 4.4), white), tip.v((x - 0.275, 0.15, 4.4), white), tip.v((x, 0.15, 4.9), white))
        mb.add(tip)
    for z in (1.2, 3.4):
        box(mb, (0, -0.3, z), (8.2, 0.25, 0.45), lit(white, 0.1))
    return build(mb, "Park_Fence", smooth=False)


def make_lamp():
    mb = MB()
    green = hexc("#3d6a58")
    rod(mb, (0, 0, 0), (0, 0, 46), 0.7, green, seg=14)
    rod(mb, (0, 0, 0), (0, 0, 2.0), 1.2, green, seg=14)
    rod(mb, (0, 0, 44), (0, 3.5, 46), 0.4, green, seg=10)
    prof = [(0.0, 0.0), (1.6, 0.4), (2.2, 1.4), (0.0, 1.8)]
    lathe(mb, prof, 20, lambda t, a, p: mixc(hexc("#fff6c8"), green, sstep(0.6, 1.0, t)), center=Vector((0, 3.8, 44.4)))
    return build(mb, "Park_Lamp", smooth=True, sharp_deg=50)


# ---------------------------------------------------------------------------
# クヌギの木（ごつごつの幹と、甘い樹液のしみ）
# ---------------------------------------------------------------------------
def make_kunugi():
    rnd = random.Random(31)
    mb = MB()
    R = 4.2
    trunk_mesh(mb, R, 110.0, 40, 30, 4, 0.35, 2.5, 3.3, moss=0.2, z0=-1.5)
    # 樹液のしみ（茶色くつやつやした所）
    for k in range(3):
        a = 0.6 + k * 0.5
        z = 5.0 + k * 3.5
        c = Vector((math.cos(a) * (R + 0.15), math.sin(a) * (R + 0.15), z))
        uv_sphere(mb, c, 1.0, lambda n: hexc("#4a2a12"), seg=12, rings=8, scale=Vector((0.7, 0.7, 1.4)))
    # 根
    for k in range(5):
        a = TAU * k / 5 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = [d * (R * 0.8) + Vector((0, 0, 1.8)), d * (R + 2.5) + Vector((0, 0, 0.4)), d * (R + 5.5) + Vector((0, 0, -0.6))]
        tube(mb, pts, [1.3, 0.8, 0.0], 12, lambda t, a2, p, dd: bark_color(p * 0.5, a2, 0.3, 7.0 + k, 0.2, 0.1))
    return build(mb, "Park_Kunugi")


def main():
    fbx_dir, blend_out = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    makers = [
        make_slide_frame, make_slide_ramp, make_swing_frame, make_swing_seat, make_swing_chain,
        make_seesaw_base, make_seesaw_plank, make_jungle_gym, make_sandbox_frame, make_sand_mound,
        make_bucket, make_shovel, make_dokan, make_bench, make_fountain, make_flower_bed,
        lambda: make_tulip("Tulip_Red", hexc("#e83a3a"), hexc("#ff8a7a")),
        lambda: make_tulip("Tulip_Yellow", hexc("#f2c41e"), hexc("#fff08a")),
        lambda: make_tulip("Tulip_Pink", hexc("#f07aa8"), hexc("#ffd0e0")),
        make_cabbage,
        lambda: make_tire("Park_Tire_Red", hexc("#e2483a")),
        lambda: make_tire("Park_Tire_Blue", hexc("#3a7fd0")),
        lambda: make_tire("Park_Tire_Yellow", hexc("#f2c232")),
        make_ball, make_fence, make_lamp, make_kunugi,
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
