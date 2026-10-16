"""
しゃくとりの森 — 山小屋の中のアセット生成スクリプト

山の山小屋の中（5 つ目のエリア）を、しゃくとりむしから見た大きさで作る。
丸太のかべ（とびら・窓の穴）・屋根うらの板とたる木・はり・とびら・窓の外の景色・まきストーブとやかん・まきの山・
テーブルとベンチ・二段ベッド・棚・ラグ・登山ぐつ・リュック・マグカップ・パン・ランプ・かけ時計・ツバメの巣・
ネズミの穴・バケツ・ほうき・ほした薬草・地図・本と、小さな物（パンくず・ひまわりのたね・マッチ・ボタン）。
FBX を書き出します。1 単位 = しゃくとりむしの体長（2.5cm）。部屋の中は、人の大きさの山小屋（3.6m × 2.6m）。

座標は Unity の向きで書く（U() で Blender にかえる）：x = 東、y = 上、z = 北。部屋のまん中の床が原点。
部屋の物（かべ・屋根・はり・とびら・窓の外）は部屋の座標のまま。家具は、それぞれの床のまん中が原点（向きは +z が前）。

使い方:
  blender -b --factory-startup --python blender/scripts/build_hut_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import MB, TAU, build, hexc, lathe, mixc, scalec, sstep, tube, uv_sphere  # noqa: E402
from build_park_kit import box  # noqa: E402


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "hut_kit.blend")
    return fbx, blend


# ---------------------------------------------------------------------------
# 部屋の大きさ（HutLayout.cs と同じ数）
# ---------------------------------------------------------------------------
HALF_X, HALF_Z = 72.0, 52.0           # かべの内がわ
WALL_H, RIDGE_H = 84.0, 120.0         # かべの高さ・屋根のむね
LOG_R = 3.4                           # かべの丸太の太さ
DOOR = (-56.0, -24.0, 76.0)           # 南のかべのとびら（x の範囲と高さ）
WIN_S = (14.0, 54.0, 36.0, 68.0)      # 南のかべの窓（x の範囲、下と上）
WIN_E = (-28.0, 16.0, 36.0, 68.0)     # 東のかべの窓（z の範囲、下と上）
BEAMS_X = (-36.0, 0.0, 36.0)          # はり（南北にわたす）
BEAM_Y, BEAM_W = 78.0, 6.0            # はりの下の高さと太さ
SILL_DEPTH, SILL_T = 7.0, 2.5         # 窓台の出と厚さ
SILL_STEP = 0.5                       # とびらのしきいの高さ

WOOD = hexc("#b07a48")
WOOD_DARK = hexc("#7a4e2c")
WOOD_LIGHT = hexc("#d6a66c")
LOG = hexc("#a86e3e")
LOG_DARK = hexc("#6e4424")
IRON = hexc("#2f2c2b")
IRON_HI = hexc("#5c5654")
BRASS = hexc("#c9a050")
PAPER = hexc("#efe3c4")
WOOL = hexc("#ece2cc")
LEATHER = hexc("#6a4026")
SOOT = hexc("#1b1716")


def U(x, y, z):
    """Unity の座標（x 東・y 上・z 北）を Blender の座標にする（FBX で Blender (x, y, z) は Unity (-x, z, -y) になる）"""
    return Vector((-x, -z, y))


def ubox(mb, c, s, col):
    """Unity の座標で、まん中 c・大きさ s（x, y, z）の直方体"""
    box(mb, U(*c), (s[0], s[2], s[1]), col)


def ucyl(mb, c, r, h, col, seg=16, top=True):
    """Unity の座標で、床の点 c から上へ高さ h の円柱（上をふさぐ）"""
    base = U(*c)
    prof = [(0.0, 0.0), (r, 0.0), (r, h)] + ([(0.0, h)] if top else [])
    lathe(mb, prof, seg, lambda t, a, p: col(t, a, p) if callable(col) else col, center=base)


def grain(base, p, k=1.0):
    """木目：細いすじと、ゆるいむら"""
    g = 0.5 + 0.5 * math.sin(p.x * 1.7 * k + 3.0 * noise.noise(p * 0.08))
    return mixc(base, scalec(base, 0.82), 0.35 * g + 0.15 * noise.noise(p * 0.3))


def wood(base, k=1.0):
    return lambda n: base


# ---------------------------------------------------------------------------
# かべ：横に積んだ丸太（とびらと窓の所は切る）。当たり判定は、平らな板（Hut_Walls_Col）
# ---------------------------------------------------------------------------
def _spans(lo, hi, holes, y):
    """[lo, hi] から、高さ y で穴（a, b, y0, y1）にかかる所をのぞいた区間"""
    cuts = sorted((a, b) for a, b, y0, y1 in holes if y0 - LOG_R * 0.6 < y < y1 + LOG_R * 0.6)
    out, x = [], lo
    for a, b in cuts:
        if a > x:
            out.append((x, a))
        x = max(x, b)
    if x < hi:
        out.append((x, hi))
    return out


def make_walls():
    mb = MB()
    logs = 13
    step = WALL_H / logs
    holes_s = [(DOOR[0], DOOR[1], 0.0, DOOR[2]), (WIN_S[0], WIN_S[1], WIN_S[2], WIN_S[3])]
    holes_e = [(WIN_E[0], WIN_E[1], WIN_E[2], WIN_E[3])]
    over = 7.0   # かどで交わる丸太のはし

    def logcol(t, a, p, d):
        c = mixc(LOG, LOG_DARK, 0.25 + 0.25 * math.sin(p.x * 0.9 + p.y * 0.7) + 0.2 * noise.noise(p * 0.15))
        return scalec(c, 0.82 + 0.18 * max(0.0, d.z))
    for k in range(logs):
        y = step * (k + 0.5)
        off = LOG_R * 0.75
        # 南と北のかべ（x の向きに長い）
        for zs, holes in ((-1, holes_s), (1, [])):
            z = zs * (HALF_Z + off)
            for a, b in _spans(-HALF_X - over, HALF_X + over, holes, y):
                tube(mb, [U(a, y, z), U(b, y, z)], [LOG_R, LOG_R], 12, logcol)
        # 西と東のかべ（z の向きに長い）。かどで、半分ずらして組む
        for xs, holes in ((-1, []), (1, holes_e)):
            x = xs * (HALF_X + off)
            yy = y + step * 0.5 if k < logs - 1 else y
            for a, b in _spans(-HALF_Z - over, HALF_Z + over, holes, yy):
                tube(mb, [U(x, yy, a), U(x, yy, b)], [LOG_R, LOG_R], 12, logcol)
    # 妻かべ（三角の所）：たての板
    for xs in (-1, 1):
        x = xs * (HALF_X + 1.0)
        for k in range(-12, 13):
            z = k * 4.3
            top = RIDGE_H - (RIDGE_H - WALL_H) * abs(z) / HALF_Z
            if top - WALL_H < 1.0:
                continue
            ubox(mb, (x, (WALL_H + top) / 2, z), (1.6, top - WALL_H, 4.0),
                 lambda n, k=k: scalec(mixc(WOOD_DARK, WOOD, 0.4 + 0.2 * math.sin(k * 1.7)), 0.85 + 0.15 * abs(n.x)))
    # とびらのわく・窓のわくと窓台
    frame = WOOD_DARK
    a, b, h = DOOR
    for x in (a - 1.5, b + 1.5):
        ubox(mb, (x, h / 2, -HALF_Z + 0.5), (3.0, h, 7.0), frame)
    ubox(mb, ((a + b) / 2, h + 1.5, -HALF_Z + 0.5), (b - a + 6.0, 3.0, 7.0), frame)
    ubox(mb, ((a + b) / 2, SILL_STEP / 2, -HALF_Z - 1.0), (b - a, SILL_STEP, 8.0), WOOD)   # しきい
    a, b, y0, y1 = WIN_S
    for x in (a - 1.2, b + 1.2):
        ubox(mb, (x, (y0 + y1) / 2, -HALF_Z + 0.5), (2.4, y1 - y0, 7.0), frame)
    ubox(mb, ((a + b) / 2, y1 + 1.2, -HALF_Z + 0.5), (b - a + 4.8, 2.4, 7.0), frame)
    ubox(mb, ((a + b) / 2, y0 - SILL_T / 2, -HALF_Z + SILL_DEPTH / 2 - 3.0), (b - a + 6.0, SILL_T, SILL_DEPTH + 6.0), WOOD_LIGHT)
    a, b, y0, y1 = WIN_E
    for z in (a - 1.2, b + 1.2):
        ubox(mb, (HALF_X - 0.5, (y0 + y1) / 2, z), (7.0, y1 - y0, 2.4), frame)
    ubox(mb, (HALF_X - 0.5, y1 + 1.2, (a + b) / 2), (7.0, 2.4, b - a + 4.8), frame)
    ubox(mb, (HALF_X - SILL_DEPTH / 2 + 3.0, y0 - SILL_T / 2, (a + b) / 2), (SILL_DEPTH + 6.0, SILL_T, b - a + 6.0), WOOD_LIGHT)
    # 窓の十字の桟
    a, b, y0, y1 = WIN_S
    ubox(mb, ((a + b) / 2, (y0 + y1) / 2, -HALF_Z - 1.5), (b - a, 1.2, 1.2), frame)
    ubox(mb, ((a + b) / 2, (y0 + y1) / 2, -HALF_Z - 1.5), (1.2, y1 - y0, 1.2), frame)
    a, b, y0, y1 = WIN_E
    ubox(mb, (HALF_X + 1.5, (y0 + y1) / 2, (a + b) / 2), (1.2, 1.2, b - a), frame)
    ubox(mb, (HALF_X + 1.5, (y0 + y1) / 2, (a + b) / 2), (1.2, y1 - y0, 1.2), frame)
    # 床ぎわの幅木
    for zs in (-1, 1):
        for x0, x1 in ((-HALF_X, DOOR[0] - 3.0), (DOOR[1] + 3.0, HALF_X)) if zs < 0 else ((-HALF_X, HALF_X),):
            ubox(mb, ((x0 + x1) / 2, 1.5, zs * (HALF_Z - 0.4)), (x1 - x0, 3.0, 1.0), WOOD_DARK)
    for xs in (-1, 1):
        ubox(mb, (xs * (HALF_X - 0.4), 1.5, 0), (1.0, 3.0, HALF_Z * 2), WOOD_DARK)
    return build(mb, "Hut_Walls", smooth=True, sharp_deg=40)


def make_walls_col():
    """かべの当たり判定：内がわの面が平らな板（丸太のでこぼこに、はさまらない）。とびらと窓の穴、窓台もある"""
    mb = MB()
    t = 6.0
    face = 0.6   # 丸太の出っぱりの分、内がわへ
    white = hexc("#ffffff")

    def slab_x(z, x0, x1, y0, y1):
        if x1 - x0 > 0.01 and y1 - y0 > 0.01:
            ubox(mb, ((x0 + x1) / 2, (y0 + y1) / 2, z), (x1 - x0, y1 - y0, t), white)

    def slab_z(x, z0, z1, y0, y1):
        if z1 - z0 > 0.01 and y1 - y0 > 0.01:
            ubox(mb, (x, (y0 + y1) / 2, (z0 + z1) / 2), (t, y1 - y0, z1 - z0), white)
    zs = -(HALF_Z - face) - t / 2
    a, b, h = DOOR
    wa, wb, wy0, wy1 = WIN_S
    slab_x(zs, -HALF_X - t, a, 0, WALL_H)
    slab_x(zs, a, b, h, WALL_H)
    slab_x(zs, b, wa, 0, WALL_H)
    slab_x(zs, wa, wb, 0, wy0)
    slab_x(zs, wa, wb, wy1, WALL_H)
    slab_x(zs, wb, HALF_X + t, 0, WALL_H)
    slab_x(HALF_Z - face + t / 2, -HALF_X - t, HALF_X + t, 0, WALL_H)
    slab_z(-(HALF_X - face) - t / 2, -HALF_Z, HALF_Z, 0, WALL_H)
    xe = HALF_X - face + t / 2
    za, zb, ey0, ey1 = WIN_E
    slab_z(xe, -HALF_Z, za, 0, WALL_H)
    slab_z(xe, zb, HALF_Z, 0, WALL_H)
    slab_z(xe, za, zb, 0, ey0)
    slab_z(xe, za, zb, ey1, WALL_H)
    # とびらのしきい（のりこえて外へ）
    ubox(mb, ((a + b) / 2, SILL_STEP / 2, -HALF_Z - 1.0), (b - a, SILL_STEP, 8.0), white)
    # 窓台（内がわへ出っぱる板。上に乗れる）
    ubox(mb, ((wa + wb) / 2, wy0 - SILL_T / 2, -HALF_Z + SILL_DEPTH / 2 - 3.0), (wb - wa + 6.0, SILL_T, SILL_DEPTH + 6.0), white)
    ubox(mb, (HALF_X - SILL_DEPTH / 2 + 3.0, ey0 - SILL_T / 2, (za + zb) / 2), (SILL_DEPTH + 6.0, SILL_T, zb - za + 6.0), white)
    return build(mb, "Hut_Walls_Col", smooth=False)


# ---------------------------------------------------------------------------
# 屋根うら：板ばりの屋根の内がわと、たる木・むね木（当たり判定なし）
# ---------------------------------------------------------------------------
def make_roof():
    mb = MB()
    over = 6.0
    run = HALF_Z + 6.0
    rise = (RIDGE_H - WALL_H) * run / HALF_Z
    slope = math.atan2(RIDGE_H - WALL_H, HALF_Z)
    length = math.hypot(run, rise)
    for zs in (-1, 1):
        # 屋根板（内がわから見える面）
        for k in range(int(length / 3.0)):
            u = (k + 0.5) / int(length / 3.0)
            z = zs * u * run
            y = RIDGE_H - (RIDGE_H - WALL_H) * abs(z) / HALF_Z + 1.4
            ubox(mb, (0, y, z), (HALF_X * 2 + over * 2, 1.2, 3.0 / math.cos(slope) - 0.2),
                 lambda n, k=k: scalec(mixc(WOOD, WOOD_DARK, 0.35 + 0.3 * math.sin(k * 2.3)), 0.8 + 0.2 * max(0.0, -n.z)))
        # たる木
        for i in range(-6, 7):
            x = i * 11.5
            a = U(x, RIDGE_H - 1.5, 0)
            b = U(x, WALL_H - 1.5 - (RIDGE_H - WALL_H) * 6.0 / HALF_Z, zs * run)
            tube(mb, [a, b], [1.6, 1.6], 6, lambda t, ang, p, d: scalec(WOOD_DARK, 0.85 + 0.15 * max(0.0, d.z)), oval=(1.0, 1.4))
    # むね木
    tube(mb, [U(-HALF_X - over, RIDGE_H - 2.0, 0), U(HALF_X + over, RIDGE_H - 2.0, 0)], [2.6, 2.6], 10, lambda t, a, p, d: LOG_DARK)
    return build(mb, "Hut_Roof", smooth=True, sharp_deg=40)


def face_in(mb, pts_u, inside_u, col):
    """Unity の座標の点で、片面だけの多角形を作る（表は inside_u の方を向く）"""
    ps = [U(*p) for p in pts_u]
    n = (ps[1] - ps[0]).cross(ps[2] - ps[0])
    if n.dot(U(*inside_u) - ps[0]) < 0:
        ps.reverse()
    mb.f(*[mb.v(p, col) for p in ps])


def make_roof_col():
    """屋根うらの当たり判定：部屋の内がわだけを向いた片面の板（カメラが屋根の外へ出ない。上からのレイは、通りぬける）"""
    mb = MB()
    white = hexc("#ffffff")
    inside = (0.0, WALL_H * 0.5, 0.0)
    x0, x1 = -HALF_X - 2.0, HALF_X + 2.0
    zr = HALF_Z + 2.0
    yr = RIDGE_H + 0.4
    yw = RIDGE_H - (RIDGE_H - WALL_H) * zr / HALF_Z + 0.4
    for zs in (-1, 1):
        face_in(mb, [(x0, yr, 0.0), (x1, yr, 0.0), (x1, yw, zs * zr), (x0, yw, zs * zr)], inside, white)
    # 妻かべ（かべの上の三角の所）
    for xs in (-1, 1):
        x = xs * (HALF_X - 0.6)
        face_in(mb, [(x, WALL_H - 2.0, -zr), (x, WALL_H - 2.0, zr), (x, yr + 1.0, 0.0)], inside, white)
    return build(mb, "Hut_Roof_Col", smooth=False)


def make_beams():
    """はり：南北のかべにわたした太い角材（上に乗れる）。はしは、かべの中へ"""
    mb = MB()
    for x in BEAMS_X:
        ubox(mb, (x, BEAM_Y + BEAM_W / 2, 0), (BEAM_W, BEAM_W, HALF_Z * 2 + 4.0),
             lambda n, x=x: scalec(mixc(LOG, LOG_DARK, 0.45), 0.8 + 0.2 * max(0.0, n.z)))
        # 屋根を支える束（つか）と、斜めのほおづえ
        ubox(mb, (x, (BEAM_Y + BEAM_W + RIDGE_H) / 2, 0), (3.0, RIDGE_H - BEAM_Y - BEAM_W, 3.0), LOG_DARK)
    return build(mb, "Hut_Beams", smooth=False)


# ---------------------------------------------------------------------------
# とびら（内がわへ開いて、かべによせてある）と、窓・とびらの外の景色
# ---------------------------------------------------------------------------
def make_door():
    mb = MB()
    a, b, h = DOOR
    w = b - a
    hinge = U(a, 0, -HALF_Z + 2.0)
    tmp = MB()
    for k in range(6):
        x = (k + 0.5) * w / 6
        box(tmp, Vector((-x, -1.2, h / 2)), (w / 6 - 0.3, 2.4, h - 1.0),
            lambda n, k=k: mixc(hexc("#7a4a28"), hexc("#94603a"), 0.4 + 0.3 * math.sin(k * 2.1)))
    for y in (12.0, h - 14.0):
        box(tmp, Vector((-w / 2, -2.6, y)), (w - 2.0, 1.0, 3.0), hexc("#5e3a20"))
    uv_sphere(tmp, Vector((-w + 3.0, -3.4, h * 0.48)), 1.3, lambda n: BRASS, seg=10, rings=6)
    # 内がわ（北）へ 100 度ひらく（ちょうつがいは西のはし）
    mb.add(tmp, Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(100), 4, "Z"))
    return build(mb, "Hut_Door", smooth=False)


def make_view():
    """窓ととびらの外の景色：空・雲・遠くの山なみ・高山の草原（かべの外に立てた板。当たり判定なし）"""
    mb = MB()

    def sky(p_u):
        y = p_u[1]
        k = sstep(20.0, 120.0, y)
        c = mixc(hexc("#cfe6f5"), hexc("#7fb3e6"), k)
        return c

    def panel(corners_u, rows=24, cols=24, ground_fn=None):
        (p0, p1, p2, p3) = [Vector(c) for c in corners_u]   # 左下・右下・右上・左上（Unity）
        ids = []
        for j in range(rows + 1):
            row = []
            for i in range(cols + 1):
                u, v = i / cols, j / rows
                p = p0.lerp(p1, u).lerp(p3.lerp(p2, u), v)
                c = sky(p)
                # 雲
                cl = noise.noise(Vector((p.x * 0.03 + p.z * 0.03, p.y * 0.06, 2.0)))
                if cl > 0.25 and p.y > 70:
                    c = mixc(c, hexc("#ffffff"), min(1.0, (cl - 0.25) * 3.0))
                if ground_fn:
                    c = ground_fn(p, c)
                row.append(mb.v(U(p.x, p.y, p.z), c))
            ids.append(row)
        # 表（部屋から見える面）が、部屋の方を向くように
        for j in range(rows):
            for i in range(cols):
                mb.f(ids[j][i], ids[j + 1][i], ids[j + 1][i + 1], ids[j][i + 1])

    def mountains(p, c):
        s = p.x + p.z
        ridge = 52 + 14 * math.sin(s * 0.06) + 8 * math.sin(s * 0.17 + 1.0) + 4 * noise.noise(Vector((s * 0.05, 0, 0)))
        if p.y < ridge:
            far = mixc(hexc("#8aa0b8"), hexc("#a9bccd"), sstep(30, 60, p.y))
            snow = p.y > ridge - 6 and math.sin(s * 0.06) > 0.2
            c = mixc(far, hexc("#f2f6fa"), 0.7) if snow else far
        meadow = 30 + 3 * math.sin(s * 0.11)
        if p.y < meadow:
            c = mixc(hexc("#6f9a4a"), hexc("#8db35a"), 0.5 + 0.5 * noise.noise(p * 0.2))
            if noise.noise(p * 0.5) > 0.45:
                c = hexc("#f2d24a")   # 黄色い花
        return c
    d = 26.0
    # 南（とびらと窓）：かべの外 d の所に、西から東へ
    panel([(-110, -10, -HALF_Z - d), (110, -10, -HALF_Z - d), (110, 130, -HALF_Z - d), (-110, 130, -HALF_Z - d)], 30, 60, mountains)
    # 東の窓
    panel([(HALF_X + d, -10, -100), (HALF_X + d, -10, 100), (HALF_X + d, 130, 100), (HALF_X + d, 130, -100)], 30, 50, mountains)
    # 手前の外の地面は、地形（草の色）
    return build(mb, "Hut_View", smooth=True)


# ---------------------------------------------------------------------------
# まきストーブ（前は +z）・火・やかん・まきの山
# ---------------------------------------------------------------------------
STOVE_W, STOVE_D, STOVE_TOP = 24.0, 20.0, 29.5
PIPE_R = 3.6


def make_stove():
    mb = MB()
    iron = lambda n: scalec(IRON, 0.85 + 0.35 * max(0.0, n.z) - 0.1 * n.y)
    for sx in (-1, 1):
        for sz in (-1, 1):
            ubox(mb, (sx * (STOVE_W / 2 - 2.5), 3.0, sz * (STOVE_D / 2 - 2.5)), (3.0, 6.0, 3.0), IRON)
    ubox(mb, (0, 17.0, 0), (STOVE_W, 22.0, STOVE_D), iron)
    ubox(mb, (0, STOVE_TOP - 0.75, 0), (STOVE_W + 2.0, 1.5, STOVE_D + 2.0), lambda n: IRON_HI if n.z > 0.5 else IRON)
    # とびら（前）と取っ手、空気の口
    ubox(mb, (0, 16.0, STOVE_D / 2 + 0.4), (14.0, 12.0, 0.8), lambda n: scalec(IRON_HI, 0.8))
    tube(mb, [U(5.0, 19.0, STOVE_D / 2 + 1.0), U(5.0, 19.0, STOVE_D / 2 + 3.0), U(7.5, 19.0, STOVE_D / 2 + 3.0)], [0.5, 0.5, 0.5], 6,
         lambda t, a, p, d: BRASS)
    for k in range(4):
        ubox(mb, (-4.5 + k * 3.0, 9.0, STOVE_D / 2 + 0.5), (1.6, 1.6, 0.6), SOOT)
    # えんとつ（屋根をつきぬける）
    ucyl(mb, (0, STOVE_TOP, -STOVE_D / 2 + 5.0), PIPE_R, RIDGE_H + 8.0 - STOVE_TOP, lambda t, a, p: scalec(IRON, 0.9 + 0.2 * math.cos(a)), seg=14)
    for y in (STOVE_TOP + 20.0, STOVE_TOP + 46.0):
        ucyl(mb, (0, y, -STOVE_D / 2 + 5.0), PIPE_R + 0.5, 1.2, IRON_HI, seg=14)
    # 下の灰受けの板（床をこがさないように）
    ubox(mb, (0, 0.25, 4.0), (STOVE_W + 10.0, 0.5, STOVE_D + 16.0), hexc("#8a7f74"))
    return build(mb, "Hut_Stove", smooth=True, sharp_deg=35)


def make_stove_fire():
    """ストーブのとびらの窓から見える火（光る部品）"""
    mb = MB()
    ubox(mb, (0, 16.5, STOVE_D / 2 + 0.95), (9.0, 6.0, 0.3), lambda n: hexc("#ffb040"))
    for k in range(5):
        ubox(mb, (-3.6 + k * 1.8, 14.3, STOVE_D / 2 + 1.05), (1.2, 1.6 + (k % 2) * 1.4, 0.2), lambda n: hexc("#ffe28a"))
    return build(mb, "Hut_StoveFire", smooth=False)


def make_kettle():
    mb = MB()
    enamel = hexc("#c84a3a")
    prof = [(0.0, 0.0), (4.6, 0.0), (5.4, 1.4), (5.4, 4.2), (4.2, 6.4), (2.0, 7.0), (2.0, 7.6), (0.0, 7.6)]
    lathe(mb, prof, 20, lambda t, a, p: mixc(enamel, hexc("#f08a6a"), 0.3 * max(0.0, math.cos(a - 0.7))) if t < 0.85 else IRON)
    uv_sphere(mb, U(0, 8.2, 0), 0.9, lambda n: IRON, seg=8, rings=5)
    tube(mb, [U(0, 3.0, 4.6), U(0, 4.6, 7.2), U(0, 6.4, 8.6)], [1.1, 0.8, 0.55], 8, lambda t, a, p, d: enamel)
    tube(mb, [U(-4.2, 6.0, 0), U(-3.0, 10.5, 0), U(0, 11.8, 0), U(3.0, 10.5, 0), U(4.2, 6.0, 0)], [0.45] * 5, 6, lambda t, a, p, d: IRON)
    return build(mb, "Hut_Kettle", smooth=True)


FIREWOOD_Y0 = 1.95   # いちばん下の段の丸太の軸の高さ（丸太は床にのる）
FIREWOOD_CHIPS = 1.9  # 木くずの山の、丸太の横腹からのはば


def firewood_chips(mb, s, xo, r, L):
    """まきの山の横（s = -1 / 1）に、木くずの山を盛る：床から、丸太の横腹のいちばん出た所の少し上まで"""
    y0 = FIREWOOD_Y0
    nz = 12
    rings = []
    for i in range(nz + 1):
        z = -L / 2 + 0.6 + (L - 1.2) * i / nz
        k = sstep(0.0, 1.0, min(1.0, (z + L / 2 - 0.6) / 2.0, (L / 2 - 0.6 - z) / 2.0))
        yt = 0.15 + (y0 + 0.45 - 0.15) * (0.4 + 0.6 * k)                 # てっぺん（丸太の面の上）
        xt = xo + s * (math.sqrt(max(r * r - (yt - y0) ** 2, 0.0)) - 0.06)
        xf = xo + s * (r + FIREWOOD_CHIPS * (0.3 + 0.7 * k))              # すそ（床）
        wob = [0.12 * noise.noise(Vector((z * 0.4, jj * 2.3, s * 1.7))) for jj in range(3)]
        prof = [(xf + s * 0.2, -0.4), (xf + s * wob[0], 0.08), (xt + (xf - xt) * 0.5 + s * wob[1], 0.08 + (yt - 0.08) * 0.45),
                (xt + (xf - xt) * 0.16 + s * wob[2], 0.08 + (yt - 0.08) * 0.8), (xt, yt), (xo, yt - 0.3), (xo, -0.4)]
        ring = []
        for (px, py) in prof:
            q = U(px, py, z)
            c = mixc(hexc("#9a7348"), hexc("#d1a46e"), 0.5 + 0.5 * noise.noise(q * 1.7))   # 木くず
            ring.append(mb.v(q, c))
        rings.append(ring)
    n = 7

    def face(*ids):
        mb.f(*(ids if s < 0 else ids[::-1]))   # 右と左で、面の向きをそろえる（外向き）
    for i in range(nz):
        A, B = rings[i], rings[i + 1]
        for j in range(n):
            j2 = (j + 1) % n
            face(A[j], A[j2], B[j2], B[j])
    for ring in (rings[0], rings[-1]):
        for j in range(1, n - 1):
            face(ring[0], ring[j], ring[j + 1])


def make_firewood():
    """まきの山：割った丸太を、3 段に積む（前は +z。木口が前とうしろ）。
    いちばん下の段は床にのせ、丸太のあいだのすき間は奥でふさぐ（前は床とのすき間にもぐりこんで、出られなくなった）。
    両がわには木くずの山を盛って、横からも登れるように（丸太の下半分はせり出していて、床からは登れなかった）"""
    mb = MB()
    rnd = random.Random(7)
    L = 18.0
    sides = {}
    for row in range(3):
        n = 5 - row
        for i in range(n):
            x = (i - (n - 1) / 2) * 4.6 + rnd.uniform(-0.3, 0.3)
            y = FIREWOOD_Y0 + row * 4.0
            r = rnd.uniform(1.9, 2.4)
            if row == 0 and i in (0, n - 1):
                sides[-1 if i == 0 else 1] = (x, r)
            bark_c = mixc(hexc("#5e4632"), hexc("#7d6044"), rnd.random())
            cut = mixc(hexc("#e2c08c"), hexc("#c99a62"), rnd.random())

            def col(t, a, p, d, bark_c=bark_c, cut=cut, half=rnd.random() < 0.5):
                if t < 0.02 or t > 0.98:
                    return cut
                if half and math.sin(a) < -0.6:
                    return cut   # 割った面
                return scalec(bark_c, 0.85 + 0.25 * noise.noise(p * 0.6))
            zz = rnd.uniform(-0.8, 0.8)
            tube(mb, [U(x, y, -L / 2 + zz), U(x, y, -L / 2 + zz + 0.01), U(x, y, L / 2 + zz - 0.01), U(x, y, L / 2 + zz)],
                 [0.0, r, r, 0.0], 10, col)
    # 下の段の丸太のあいだ（奥の暗いところ）：すき間にもぐりこまない
    ubox(mb, (0.0, (FIREWOOD_Y0 + 0.6) / 2, 0.0), (18.4, FIREWOOD_Y0 + 0.6, L - 2.4), hexc("#3a2a1e"))
    # 両がわの木くずの山
    for s, (xo, r) in sides.items():
        firewood_chips(mb, s, xo, r, L)
    return build(mb, "Hut_Firewood", smooth=True, sharp_deg=50)


# ---------------------------------------------------------------------------
# テーブル・ベンチ（前は +z）
# ---------------------------------------------------------------------------
TABLE_W, TABLE_D, TABLE_H = 36.0, 60.0, 29.0     # x・z の大きさと、天板の上の高さ
BENCH_W, BENCH_D, BENCH_H = 10.0, 48.0, 17.0


def make_table():
    mb = MB()
    # 天板は、たての板 6 枚
    for k in range(6):
        x = -TABLE_W / 2 + (k + 0.5) * TABLE_W / 6
        ubox(mb, (x, TABLE_H - 1.25, 0), (TABLE_W / 6 - 0.25, 2.5, TABLE_D),
             lambda n, k=k: scalec(mixc(WOOD_LIGHT, WOOD, 0.25 + 0.2 * math.sin(k * 2.6)), 1.0 if n.z > 0.5 else 0.8))
    for sx in (-1, 1):
        for sz in (-1, 1):
            ubox(mb, (sx * (TABLE_W / 2 - 3.5), (TABLE_H - 2.5) / 2, sz * (TABLE_D / 2 - 4.0)), (3.4, TABLE_H - 2.5, 3.4),
                 lambda n: scalec(WOOD, 0.85 + 0.15 * abs(n.x)))
        ubox(mb, (sx * (TABLE_W / 2 - 3.5), TABLE_H - 5.5, 0), (1.6, 5.0, TABLE_D - 10.0), WOOD_DARK)   # 幕板
    for sz in (-1, 1):
        ubox(mb, (0, TABLE_H - 5.5, sz * (TABLE_D / 2 - 4.0)), (TABLE_W - 9.0, 5.0, 1.6), WOOD_DARK)
    return build(mb, "Hut_Table", smooth=False)


def make_bench():
    mb = MB()
    ubox(mb, (0, BENCH_H - 1.0, 0), (BENCH_W, 2.0, BENCH_D), lambda n: scalec(WOOD, 1.0 if n.z > 0.5 else 0.8))
    for sz in (-1, 1):
        ubox(mb, (0, (BENCH_H - 2.0) / 2, sz * (BENCH_D / 2 - 5.0)), (BENCH_W - 2.0, BENCH_H - 2.0, 2.0), WOOD_DARK)
    ubox(mb, (0, 4.0, 0), (1.6, 1.6, BENCH_D - 10.0), WOOD_DARK)
    return build(mb, "Hut_Bench", smooth=False)


# ---------------------------------------------------------------------------
# 二段ベッド（長い向きが x。前は +z。はしごは東のはし）
# ---------------------------------------------------------------------------
BUNK_L, BUNK_D = 80.0, 34.0
BUNK_LOW, BUNK_UP = 19.0, 57.0      # マットレスの上の高さ


def plaid(a, b, p, k=0.6):
    """チェックの毛布"""
    s = (math.sin(p.x * k) > 0.3) + (math.sin(p.y * k) > 0.3)
    return mixc(a, b, 0.0 if s == 0 else 0.35 if s == 1 else 0.7)


def make_bunk():
    mb = MB()
    post = lambda n: scalec(WOOD, 0.85 + 0.15 * abs(n.x))
    for sx in (-1, 1):
        for sz in (-1, 1):
            ubox(mb, (sx * (BUNK_L / 2 - 2.0), 33.0, sz * (BUNK_D / 2 - 2.0)), (4.0, 66.0, 4.0), post)
    for y0 in (BUNK_LOW - 7.0, BUNK_UP - 7.0):
        # 床板と、まわりの横木
        ubox(mb, (0, y0 + 1.0, 0), (BUNK_L - 4.0, 2.0, BUNK_D - 4.0), WOOD_DARK)
        for sz in (-1, 1):
            ubox(mb, (0, y0 + 2.5, sz * (BUNK_D / 2 - 2.0)), (BUNK_L - 4.0, 5.0, 2.0), WOOD)
        for sx in (-1, 1):
            ubox(mb, (sx * (BUNK_L / 2 - 2.0), y0 + 2.5, 0), (2.0, 5.0, BUNK_D - 4.0), WOOD)
    # マットレス・毛布・まくら（下は赤いチェック、上は緑のチェック）
    for y0, a, b in ((BUNK_LOW - 5.0, hexc("#b8433a"), hexc("#e8c8a0")), (BUNK_UP - 5.0, hexc("#3f7a4a"), hexc("#e8dcb0"))):
        ubox(mb, (0, y0 + 2.5, 0), (BUNK_L - 6.0, 5.0, BUNK_D - 6.0), WOOL)
        bl = MB()
        ubox(bl, (6.0, y0 + 5.3, 0), (BUNK_L - 18.0, 0.8, BUNK_D - 4.0), a)
        bl.recolor(lambda p, c, a=a, b=b: plaid(a, b, p))
        mb.add(bl)
        uv_sphere(mb, U(-BUNK_L / 2 + 9.0, y0 + 6.8, 0), 1.0, lambda n: hexc("#f6f2ea"), seg=14, rings=8,
                  scale=Vector((6.0, 9.0, 2.4)))
    # 上の段の、落ちないための手すり（前がわ）
    ubox(mb, (-8.0, BUNK_UP + 4.0, BUNK_D / 2 - 2.0), (BUNK_L - 28.0, 2.0, 2.0), WOOD)
    for x in (-34.0, 18.0):
        ubox(mb, (x, BUNK_UP + 1.0, BUNK_D / 2 - 2.0), (2.0, 6.0, 2.0), WOOD)
    # はしご（東のはし、前がわ）
    lx = BUNK_L / 2 - 9.0
    for dx in (-5.0, 5.0):
        ubox(mb, (lx + dx, BUNK_UP / 2 + 3.0, BUNK_D / 2 + 1.0), (1.8, BUNK_UP + 6.0, 1.8), WOOD_LIGHT)
    for k in range(6):
        y = 8.0 + k * 9.0
        ubox(mb, (lx, y, BUNK_D / 2 + 1.0), (10.0, 1.4, 1.6), WOOD_LIGHT)
    return build(mb, "Hut_Bunk", smooth=False)


# ---------------------------------------------------------------------------
# 棚（かべにつけた 3 段の板。前は +z）と、のせる物
# ---------------------------------------------------------------------------
SHELF_W, SHELF_D = 26.0, 10.0
SHELF_YS = (34.0, 52.0, 70.0)


def make_shelf():
    mb = MB()
    rnd = random.Random(3)
    for y in SHELF_YS:
        ubox(mb, (0, y - 0.8, 0), (SHELF_W, 1.6, SHELF_D), lambda n: scalec(WOOD_LIGHT, 1.0 if n.z > 0.5 else 0.8))
        for sx in (-1, 1):
            # 三角のうで木
            tube(mb, [U(sx * (SHELF_W / 2 - 3.0), y - 1.6, -SHELF_D / 2 + 0.8), U(sx * (SHELF_W / 2 - 3.0), y - 7.0, -SHELF_D / 2 + 0.8)],
                 [0.9, 0.9], 6, lambda t, a, p, d: WOOD_DARK)
            tube(mb, [U(sx * (SHELF_W / 2 - 3.0), y - 6.5, -SHELF_D / 2 + 1.0), U(sx * (SHELF_W / 2 - 3.0), y - 1.8, SHELF_D / 2 - 1.5)],
                 [0.8, 0.8], 6, lambda t, a, p, d: WOOD_DARK)
    # 下の段：びん（青・こはく・みどり）と、かん
    for x, col, h in ((-9.0, hexc("#6fa6c8"), 7.0), (-4.0, hexc("#c8923a"), 6.0), (1.5, hexc("#8ab070"), 8.0)):
        prof = [(0.0, 0.0), (2.0, 0.0), (2.2, 0.5), (2.2, h * 0.75), (1.2, h * 0.9), (1.2, h), (0.0, h)]
        lathe(mb, prof, 14, lambda t, a, p, col=col: mixc(col, hexc("#ffffff"), 0.35 * max(0.0, math.cos(a - 1.0))) if t < 0.8 else hexc("#d8b070"),
              center=U(x, SHELF_YS[0], 0.5))
    ucyl(mb, (8.0, SHELF_YS[0], 0.0), 2.4, 4.5, lambda t, a, p: mixc(hexc("#c0c4c8"), hexc("#e05040"), 1.0 if 0.3 < t < 0.7 else 0.0), seg=14)
    # まん中の段：本（立てて並べる）
    x = -11.0
    for k in range(7):
        w = rnd.uniform(1.4, 2.4)
        h = rnd.uniform(9.0, 12.0)
        c = [hexc("#8a3a32"), hexc("#3a5a8a"), hexc("#4a7a4a"), hexc("#c8a050"), hexc("#6a4a7a")][k % 5]
        ubox(mb, (x + w / 2, SHELF_YS[1] + h / 2, 0), (w, h, 7.0), lambda n, c=c: c if abs(n.y) < 0.5 else hexc("#f2ead6"))
        x += w + 0.2
    # 上の段：ロープの輪と、小さなランタン
    for k in range(4):
        r = 4.0 - k * 0.15
        pts = [U(-5.0 + math.cos(a) * r, SHELF_YS[2] + 0.8 + k * 0.9, math.sin(a) * r * 0.9) for a in [TAU * i / 24 for i in range(25)]]
        tube(mb, pts, [0.7] * 25, 6, lambda t, a, p, d: hexc("#c8a874"))
    ucyl(mb, (6.0, SHELF_YS[2], 0.0), 2.2, 1.0, IRON, seg=12)
    ucyl(mb, (6.0, SHELF_YS[2] + 1.0, 0.0), 1.8, 4.0, hexc("#f6d898"), seg=12)
    ucyl(mb, (6.0, SHELF_YS[2] + 5.0, 0.0), 2.2, 1.2, IRON, seg=12)
    return build(mb, "Hut_Shelf", smooth=True, sharp_deg=40)


# ---------------------------------------------------------------------------
# ラグ・玄関マット・登山ぐつ・リュック
# ---------------------------------------------------------------------------
def make_rug():
    """編んだ だ円のラグ（色の輪が重なる）"""
    mb = MB()
    cols = [hexc("#c45a4a"), hexc("#e8c070"), hexc("#5a7aa8"), hexc("#e0d0b0"), hexc("#7a5a3a")]
    n = 14
    prof = [(0.0, 0.55)] + [(i / n, 0.55 + 0.06 * math.sin(i * 1.7)) for i in range(1, n + 1)] + [(1.0, 0.0)]
    rx, rz = 28.0, 20.0
    ids = []
    for i, (r, y) in enumerate(prof):
        if r < 1e-6:
            ids.append([mb.v(U(0, y, 0), cols[0])])
            continue
        ring = []
        for j in range(48):
            a = TAU * j / 48
            c = cols[min(len(cols) - 1, int(r * n) // 3)]
            c = scalec(c, 0.88 + 0.12 * math.sin(a * 24 + i * 2.0))
            ring.append(mb.v(U(math.cos(a) * rx * r, y, math.sin(a) * rz * r), c))
        ids.append(ring)
    # 外のふちから、まん中へ（上の面が上を向く）
    kit.connect_rings(mb, ids[::-1])
    return build(mb, "Hut_Rug", smooth=True)


def make_doormat():
    mb = MB()
    ubox(mb, (0, 0.25, 0), (30.0, 0.5, 18.0), hexc("#7a5a3a"))
    for k in range(9):
        ubox(mb, (-12.0 + k * 3.0, 0.55, 0), (1.2, 0.2, 16.0), hexc("#a8865a"))
    return build(mb, "Hut_DoorMat", smooth=False)


def boot(mb, x, rot):
    tmp = MB()
    sole = hexc("#3a3230")
    upper = hexc("#7a4a2a")
    box(tmp, Vector((0, 0, 0.6)), (4.8, 12.0, 1.2), sole)
    uv_sphere(tmp, Vector((0, -2.6, 2.6)), 1.0, lambda n: upper, seg=14, rings=8, scale=Vector((2.5, 3.6, 2.0)))   # つま先（Unity の +z）
    tube(tmp, [Vector((0, 2.0, 1.2)), Vector((0, 2.6, 6.0)), Vector((0, 2.8, 10.0))], [3.2, 2.7, 2.6], 14,
         lambda t, a, p, d: upper if t < 0.9 else hexc("#b84a3a"))
    uv_sphere(tmp, Vector((0, 2.8, 10.0)), 1.0, lambda n: SOOT, seg=12, rings=4, scale=Vector((2.2, 2.2, 0.3)))   # はき口
    for k in range(4):
        tube(tmp, [Vector((-1.8, 0.4, 3.0 + k * 1.6)), Vector((1.8, 0.4, 3.0 + k * 1.6))], [0.25, 0.25], 4, lambda t, a, p, d: hexc("#e8c040"))
    mb.add(tmp, Matrix.Translation(U(x, 0, 0)) @ Matrix.Rotation(rot, 4, "Z"))


def make_boots():
    mb = MB()
    boot(mb, -3.2, math.radians(8))
    boot(mb, 3.4, math.radians(-14))
    return build(mb, "Hut_Boots", smooth=True, sharp_deg=50)


def make_backpack():
    """かべにもたれたリュック（前は +z。せなかは -z のかべがわ）"""
    mb = MB()
    canvas = hexc("#3f7a8c")
    dark = hexc("#2e5866")
    tmp = MB()
    uv_sphere(tmp, Vector((0, 0, 14.0)), 1.0, lambda n: scalec(canvas, 0.85 + 0.15 * n.z), seg=18, rings=12, scale=Vector((10.0, 6.5, 14.0)))
    uv_sphere(tmp, Vector((0, -5.2, 20.0)), 1.0, lambda n: dark, seg=14, rings=8, scale=Vector((9.0, 3.2, 6.0)))   # 上のふた
    uv_sphere(tmp, Vector((0, -6.2, 8.0)), 1.0, lambda n: dark, seg=14, rings=8, scale=Vector((6.5, 2.4, 5.0)))    # 前のポケット
    # かたひもとバンドは、体の面にそわせ、両はしは体の中へ（前は体からはなれて、うかんで見えた）
    for sx in (-1, 1):
        tube(tmp, [Vector((sx * 5.0, 2.6, 24.0)), Vector((sx * 5.8, 5.6, 19.0)), Vector((sx * 6.0, 6.0, 14.0)), Vector((sx * 5.6, 5.4, 8.0)),
                   Vector((sx * 5.0, 1.8, 3.0))], [0.8] * 5, 6, lambda t, a, p, d: hexc("#2a2a2a"))
    tube(tmp, [Vector((-5.5, -4.8, 15.0)), Vector((-3.0, -6.4, 15.0)), Vector((0.0, -6.8, 15.0)), Vector((3.0, -6.4, 15.0)), Vector((5.5, -4.8, 15.0))],
         [0.5] * 5, 5, lambda t, a, p, d: hexc("#e8c040"))
    # かべ（せなか）の方へ、少しかたむける
    mb.add(tmp, Matrix.Rotation(math.radians(-10), 4, "X"))
    return build(mb, "Hut_Backpack", smooth=True)


# ---------------------------------------------------------------------------
# テーブルの上：マグ・パンとまな板・地図・本・ランプ。かけ時計・バケツ・ほうき・ほした薬草
# ---------------------------------------------------------------------------
def make_mug():
    mb = MB()
    blue = hexc("#3f6ea8")
    prof = [(0.0, 0.0), (3.0, 0.0), (3.2, 0.3), (3.2, 4.6), (2.8, 4.6), (2.8, 0.8), (0.0, 0.8)]
    lathe(mb, prof, 18, lambda t, a, p: hexc("#f2f0ea") if t > 0.5 else blue)
    tube(mb, [U(3.0, 3.8, 0), U(4.8, 3.6, 0), U(5.0, 2.0, 0), U(3.0, 1.4, 0)], [0.45] * 4, 6, lambda t, a, p, d: blue)
    return build(mb, "Hut_Mug", smooth=True)


def make_bread():
    mb = MB()
    ubox(mb, (0, 0.6, 0), (16.0, 1.2, 10.0), lambda n: scalec(WOOD_LIGHT, 1.0 if n.z > 0.5 else 0.8))   # まな板
    uv_sphere(mb, U(-1.5, 3.4, 0), 1.0, lambda n: mixc(hexc("#c07a3a"), hexc("#8a4e22"), sstep(0.2, 0.9, n.z)), seg=18, rings=10,
              scale=Vector((6.0, 3.0, 3.6)))
    # 切った面
    uv_sphere(mb, U(4.4, 3.3, 0), 1.0, lambda n: hexc("#f2dca8") if n.x < -0.3 else hexc("#c07a3a"), seg=14, rings=8,
              scale=Vector((0.8, 2.7, 3.2)))
    return build(mb, "Hut_Bread", smooth=True)


def make_map():
    mb = MB()
    ubox(mb, (0, 0.1, 0), (18.0, 0.2, 13.0), PAPER)
    rnd = random.Random(5)
    for k in range(6):
        pts = []
        y0 = rnd.uniform(-5, 5)
        for i in range(8):
            x = -8 + i * 2.3
            pts.append(U(x, 0.25, y0 + 1.5 * math.sin(i * 0.9 + k)))
        tube(mb, pts, [0.12] * len(pts), 4, lambda t, a, p, d, k=k: hexc("#8a6a4a") if k % 2 else hexc("#4a7a9a"))
    uv_sphere(mb, U(3.0, 0.3, 1.0), 1.0, lambda n: hexc("#d84a3a"), seg=8, rings=4, scale=Vector((0.6, 0.6, 0.15)))
    return build(mb, "Hut_Map", smooth=False)


def make_book():
    mb = MB()
    ubox(mb, (0, 1.2, 0), (9.0, 2.4, 12.0), lambda n: hexc("#f2ead6") if abs(n.x) > 0.5 else hexc("#8a3a32"))
    return build(mb, "Hut_Book", smooth=False)


def make_lamp():
    """はりからつるしたランプ（ひもの上のはしが原点）"""
    mb = MB()
    tube(mb, [U(0, 0, 0), U(0, -18.0, 0)], [0.25, 0.25], 4, lambda t, a, p, d: hexc("#3a3a3a"))
    ucyl(mb, (0, -20.0, 0), 2.6, 2.0, IRON, seg=12)
    ucyl(mb, (0, -28.0, 0), 3.2, 1.6, IRON, seg=12)
    for k in range(4):
        a = TAU * k / 4 + 0.4
        tube(mb, [U(math.cos(a) * 3.0, -26.5, math.sin(a) * 3.0), U(math.cos(a) * 2.4, -20.0, math.sin(a) * 2.4)], [0.25, 0.25], 4,
             lambda t, aa, p, d: IRON)
    return build(mb, "Hut_Lamp", smooth=True)


def make_lamp_glow():
    mb = MB()
    uv_sphere(mb, U(0, -23.0, 0), 1.0, lambda n: hexc("#ffd080"), seg=12, rings=8, scale=Vector((2.2, 2.2, 3.4)))
    return build(mb, "Hut_LampGlow", smooth=True)


def make_clock():
    """かけ時計（かべに当てる面が原点。前は +z）"""
    mb = MB()
    face_c = Vector(U(0, 0, 0))
    # 文字盤：Unity の +z（前）を向く円盤
    tmp = MB()
    lathe(tmp, [(0.0, 0.0), (7.0, 0.0), (7.0, 1.6), (6.2, 2.0), (0.0, 2.0)], 28,
          lambda t, a, p: WOOD_DARK if t < 0.7 else hexc("#f6f0e0"))
    for k in range(12):
        a = TAU * k / 12
        box(tmp, Vector((math.cos(a) * 5.2, math.sin(a) * 5.2, 2.1)), (0.5, 0.5, 0.2), SOOT)
    box(tmp, Vector((1.2, 0.6, 2.2)), (3.0, 0.5, 0.2), SOOT)
    box(tmp, Vector((0.2, 2.0, 2.2)), (0.4, 4.4, 0.2), SOOT)
    # Blender の +z（円盤の前）を、Unity の +z（= Blender の -y）へ
    mb.add(tmp, Matrix.Rotation(math.radians(90), 4, "X"))
    # ふりこの箱
    ubox(mb, (0, -12.0, 1.2), (8.0, 14.0, 2.4), WOOD_DARK)
    tube(mb, [U(0, -6.0, 2.6), U(1.0, -15.0, 2.6)], [0.3, 0.3], 4, lambda t, a, p, d: BRASS)
    uv_sphere(mb, U(1.1, -16.0, 2.7), 1.5, lambda n: BRASS, seg=10, rings=6, scale=Vector((1, 0.4, 1)))
    return build(mb, "Hut_Clock", smooth=True)


def make_bucket():
    mb = MB()
    tin = hexc("#a8acb0")
    prof = [(0.0, 0.0), (6.0, 0.0), (7.2, 10.0), (6.8, 10.0), (5.6, 0.8), (0.0, 0.8)]
    lathe(mb, prof, 22, lambda t, a, p: scalec(tin, 0.8 + 0.25 * max(0.0, math.cos(a - 0.6))) if t < 0.55 else hexc("#5a5e62"))
    tube(mb, [U(-7.0, 9.5, 0), U(-5.0, 15.0, 0), U(0, 17.0, 0), U(5.0, 15.0, 0), U(7.0, 9.5, 0)], [0.35] * 5, 5, lambda t, a, p, d: IRON)
    return build(mb, "Hut_Bucket", smooth=True)


def make_broom():
    """かべにもたれたほうき（床の点が原点。かべは -z のがわ）"""
    mb = MB()
    tmp = MB()
    tube(tmp, [Vector((0, 0, 12.0)), Vector((0, 0, 58.0))], [0.8, 0.8], 8, lambda t, a, p, d: WOOD_LIGHT)
    rnd = random.Random(11)
    for k in range(60):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0.2, 1.0)
        top = Vector((math.cos(a) * r * 1.2, math.sin(a) * r * 0.5, 13.0))
        bot = Vector((math.cos(a) * r * 4.5, math.sin(a) * r * 1.6, 0.2))
        c = mixc(hexc("#d8b860"), hexc("#b89040"), rnd.random())
        tube(tmp, [top, bot], [0.18, 0.12], 3, lambda t, aa, p, d, c=c: c)
    tube(tmp, [Vector((-1.3, 0, 11.5)), Vector((1.3, 0, 11.5))], [0.6, 0.6], 6, lambda t, a, p, d: hexc("#b84a3a"))
    mb.add(tmp, Matrix.Rotation(math.radians(-14), 4, "X"))
    return build(mb, "Hut_Broom", smooth=True)


def make_herbs():
    """かべの上の方に、ひもでつるした薬草の束（ひもの左はしが原点。かべにそって東へのびる）"""
    mb = MB()
    tube(mb, [U(0, 0, 0), U(16.0, -1.5, 0), U(32.0, 0, 0)], [0.25, 0.25, 0.25], 4, lambda t, a, p, d: hexc("#c8a874"))
    rnd = random.Random(13)
    for k in range(5):
        x = 4.0 + k * 6.0
        y0 = -1.0 - 1.5 * math.sin(math.pi * x / 32.0)
        g = mixc(hexc("#6a8a4a"), hexc("#8a9a5a"), rnd.random())
        flower = hexc("#a87ab8") if k == 2 else scalec(g, 1.1)
        for s in range(9):
            a = rnd.uniform(-0.5, 0.5)
            end = U(x + math.sin(a) * 2.0, y0 - rnd.uniform(6.0, 9.0), rnd.uniform(-1.0, 1.0))
            tube(mb, [U(x, y0, 0), end], [0.3, 0.15], 3, lambda t, aa, p, d, g=g: g)
            uv_sphere(mb, end, 0.8, lambda n, f=flower: f, seg=5, rings=3)
        tube(mb, [U(x - 0.6, y0 - 1.0, 0), U(x + 0.6, y0 - 1.0, 0)], [0.5, 0.5], 4, lambda t, a, p, d: hexc("#b84a3a"))
    return build(mb, "Hut_Herbs", smooth=True)


# ---------------------------------------------------------------------------
# ツバメの巣（はりの上。どろのおわん）・ネズミの穴（かべのすそ）
# ---------------------------------------------------------------------------
def make_nest():
    mb = MB()
    mud = hexc("#8a6a4a")
    prof = [(0.0, 0.0), (3.0, 0.0), (5.2, 1.6), (6.0, 4.0), (5.4, 4.4), (4.6, 2.4), (0.0, 1.8)]
    lathe(mb, prof, 24, lambda t, a, p: scalec(mixc(mud, hexc("#a8865c"), 0.5 + 0.5 * noise.noise(p * 0.8)), 0.9 + 0.1 * math.sin(p.z * 6.0)),
          rfn=lambda t, a: 1.0 + 0.06 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, t * 4))))
    rnd = random.Random(17)
    for k in range(14):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(4.6, 6.2)
        p0 = U(math.cos(a) * r, rnd.uniform(2.0, 4.4), math.sin(a) * r)
        p1 = p0 + Vector((rnd.uniform(-1.5, 1.5), rnd.uniform(-1.5, 1.5), rnd.uniform(-0.6, 0.4)))
        tube(mb, [p0, p1], [0.12, 0.08], 3, lambda t, aa, p, d: hexc("#d8c080"))
    return build(mb, "Hut_Nest", smooth=True)


def make_mouse_hole():
    """かべのすそのネズミの穴（かべに当てる面が原点。前は +z）と、まわりの木くず"""
    mb = MB()
    n = 20
    w, h = 5.0, 6.5
    c0 = mb.v(U(0, 0.05, 0.12), SOOT)
    ring = [mb.v(U(math.cos(math.pi * i / n) * w, math.sin(math.pi * i / n) * h + 0.05, 0.12), SOOT) for i in range(n + 1)]
    for i in range(n):
        mb.f(c0, ring[i + 1], ring[i])   # 部屋の方（+z）を向く
    # 穴のふち（かじった木）
    arc = [U(math.cos(math.pi * i / n) * (w + 0.5), math.sin(math.pi * i / n) * (h + 0.5), 0.3) for i in range(n + 1)]
    tube(mb, arc, [0.7] * (n + 1), 6, lambda t, a, p, d: hexc("#d6a66c"))
    rnd = random.Random(19)
    for k in range(10):
        p = U(rnd.uniform(-8, 8), 0.3, rnd.uniform(1.5, 7.0))
        uv_sphere(mb, p, 1.0, lambda nn: hexc("#e2c08c"), seg=5, rings=3, scale=Vector((rnd.uniform(0.6, 1.2), rnd.uniform(0.3, 0.6), 0.2)))
    return build(mb, "Hut_MouseHole", smooth=True)


# ---------------------------------------------------------------------------
# 小さな物（押すと動く）：パンくず・ひまわりのたね・マッチ・ボタン
# ---------------------------------------------------------------------------
def make_crumb():
    mb = MB()
    uv_sphere(mb, U(0, 0.35, 0), 1.0, lambda n: hexc("#f0d8a0") if n.z > 0.2 else hexc("#c08a4a"), seg=8, rings=5,
              scale=Vector((0.6, 0.45, 0.38)))
    mb.xform(Matrix.Scale(0.55, 4))   # 本物の大きさ（しゃくとりむしの体長くらい）
    return build(mb, "Hut_Crumb", smooth=True)


def make_seed():
    mb = MB()
    uv_sphere(mb, U(0, 0.3, 0), 1.0, lambda n: hexc("#2e2a28") if abs(n.x) < 0.5 else hexc("#e8e2d4"), seg=10, rings=6,
              scale=Vector((0.4, 0.9, 0.3)))
    mb.xform(Matrix.Scale(0.5, 4))   # 本物の大きさ（しゃくとりむしの体長くらい）
    return build(mb, "Hut_Seed", smooth=True)


def make_match():
    mb = MB()
    ubox(mb, (0, 0.22, 0), (0.45, 0.45, 4.4), hexc("#e2c48a"))
    uv_sphere(mb, U(0, 0.42, 2.3), 0.42, lambda n: hexc("#c03a2e"), seg=8, rings=5)
    mb.xform(Matrix.Scale(0.45, 4))   # 本物の大きさ（しゃくとりむしの体長くらい）
    return build(mb, "Hut_Match", smooth=True)


def make_button():
    mb = MB()
    lathe(mb, [(0.0, 0.0), (1.1, 0.0), (1.2, 0.3), (1.0, 0.45), (0.0, 0.35)], 16, lambda t, a, p: hexc("#c84a5a"))
    for k in range(4):
        a = TAU * k / 4 + 0.78
        uv_sphere(mb, U(math.cos(a) * 0.35, 0.42, math.sin(a) * 0.35), 0.13, lambda n: hexc("#6a1e2a"), seg=5, rings=3)
    mb.xform(Matrix.Scale(0.5, 4))   # 本物の大きさ（しゃくとりむしの体長くらい）
    return build(mb, "Hut_Button", smooth=True)


MAKERS = [
    make_walls, make_walls_col, make_roof, make_roof_col, make_beams, make_door, make_view,
    make_stove, make_stove_fire, make_kettle, make_firewood, make_table, make_bench, make_bunk, make_shelf,
    make_rug, make_doormat, make_boots, make_backpack, make_mug, make_bread, make_map, make_book,
    make_lamp, make_lamp_glow, make_clock, make_bucket, make_broom, make_herbs, make_nest, make_mouse_hole,
    make_crumb, make_seed, make_match, make_button,
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
        if ob.name in ("Hut_Walls", "Hut_Walls_Col", "Hut_Roof", "Hut_Roof_Col", "Hut_Beams", "Hut_Door", "Hut_View"):
            continue
        w = max(ob.dimensions.x, 1.0)
        ob.location = (x + w / 2, 200.0, 0)
        x += w + 4.0
    bpy.ops.wm.save_as_mainfile(filepath=blend_out, compress=True)
    print("saved", blend_out)


if __name__ == "__main__":
    main()
