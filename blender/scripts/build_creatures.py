"""
しゃくとりの森 — いきもの・川辺アセット生成スクリプト

いきもの図鑑の 16 種（てんとう虫・かたつむり・あり・だんごむし・ちょうちょ・カブトムシ・
あめんぼ・とんぼ・アマガエル・サワガニ・カワニナ・ゲンジボタル・バッタ・オトシブミ・スズメ・カラス）と、
オトシブミのゆりかご・アリの巣、
川辺のステージ用モデル（川石・スギナ／つくし・カキツバタ・木の根のゲート）を作り、
FBX と図鑑用の絵（PNG）を書き出します。

使い方:
  blender -b --factory-startup --python blender/scripts/build_creatures.py -- [fbx_out_dir] [portrait_out_dir] [blend_out]
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
from build_forest_kit import (MB, TAU, build, connect_rings, hexc, lathe, lerp, mixc, polar_sheet,  # noqa: E402
                              quad_bezier, ribbon, scalec, sstep, tube, uv_sphere, with_alpha)


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    png = a[1] if len(a) > 1 else os.path.join(repo, "unity", "Assets", "UI", "Creatures")
    blend = a[2] if len(a) > 2 else os.path.join(repo, "blender", "creatures_kit.blend")
    return fbx, png, blend


BLACK = hexc("#1c1b22")
WHITE = hexc("#ffffff")


def seg_body(mb, y0, y1, half_w, half_h, colfn, rings=40, seg=24, prof=None, ridge=0.0, ridge_n=0, z0=0.0, flat=0.35):
    """Y 方向に長い、上が丸くて下が平たい体。prof(t) -> (幅倍率, 高さ倍率)"""
    rr_list = []
    for i in range(rings + 1):
        t = i / rings
        y = lerp(y0, y1, t)
        if i == 0 or i == rings:
            rr_list.append([mb.v((0, y, z0), colfn(t, -math.pi / 2, Vector((0, y, z0))))])
            continue
        ws, hs = prof(t) if prof else (math.sin(math.pi * t) ** 0.5, math.sin(math.pi * t) ** 0.5)
        groove = 1.0 - ridge * abs(math.cos(math.pi * t * ridge_n)) ** 10 if ridge_n else 1.0
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            ca, sa = math.cos(a), math.sin(a)
            z = sa * half_h * hs * groove if sa > 0 else sa * half_h * hs * flat
            p = Vector((ca * half_w * ws * groove, y, z0 + z))
            ring.append(mb.v(p, colfn(t, a, p)))
        rr_list.append(ring)
    start = len(mb.F)
    connect_rings(mb, rr_list)
    for k in range(start, len(mb.F)):
        mb.F[k] = tuple(reversed(mb.F[k]))


# 脚を別メッシュにして Unity で動かすため、legs() の呼び出しを記録できるようにする
_LEG_CAPTURE = None
RIG = {}        # 体の名前 -> [(脚メッシュ名, 付け根, 先), ...]（Blender 座標・右側だけ）


def legs(mb, roots, color, tip_color=None, radius=0.012, tip_disc=0.0):
    """roots: [(付け根, 膝, 先), ...]"""
    tip_color = tip_color or color
    if _LEG_CAPTURE is not None:
        _LEG_CAPTURE.append((roots, color, tip_color, radius, tip_disc))
        return
    if tip_disc > 0.0:
        for base, knee, tip in roots:
            water_dimple(mb, Vector(tip), tip_disc)
    for base, knee, tip in roots:
        pts = [Vector(base), Vector(knee), Vector(tip)]
        tube(mb, pts, [radius, radius * 0.8, radius * 0.4], 6,
             lambda t, a, p, d: mixc(color, tip_color, sstep(0.5, 1.0, t)))


def water_dimple(mb, c, r):
    """アメンボの足先の水面のくぼみ"""
    c = Vector((c.x, c.y, 0.003 if abs(c.z) < 0.02 else c.z))
    ring = [mb.v(c + Vector((math.cos(a) * r, math.sin(a) * r, 0)), hexc("#dff4ff")) for a in [TAU * j / 12 for j in range(12)]]
    cen = mb.v(c, hexc("#ffffff"))
    for j in range(12):
        mb.f(cen, ring[j], ring[(j + 1) % 12])


def sneaker(mb, at, direction, size, upper=None):
    """小さなスニーカー（ゴム底・つま先・ひも）。at は足の先、direction はつま先の向き"""
    upper = upper or hexc("#e8433a")
    f = Vector((direction.x, direction.y, 0.0))
    if f.length < 1e-6:
        f = Vector((0, 1, 0))
    f.normalize()
    side = Vector((-f.y, f.x, 0.0))
    up = Vector((0, 0, 1))
    m = Matrix((
        (side.x, f.x, up.x, at.x),
        (side.y, f.y, up.y, at.y),
        (side.z, f.z, up.z, at.z),
        (0, 0, 0, 1)))
    tmp = MB()
    L = size
    # 白いゴム底
    uv_sphere(tmp, Vector((0, L * 0.15, L * 0.08)), 1.0, lambda n: hexc("#fbfbf6"), seg=14, rings=7,
              scale=Vector((L * 0.32, L * 0.62, L * 0.1)))
    # 甲（色つき）とつま先（白）
    uv_sphere(tmp, Vector((0, L * 0.05, L * 0.22)), 1.0, lambda n: upper if n.y < 0.55 else hexc("#ffffff"), seg=14, rings=9,
              scale=Vector((L * 0.28, L * 0.5, L * 0.2)))
    # ひも
    for k in range(3):
        y = L * (-0.02 + 0.12 * k)
        tube(tmp, [Vector((-L * 0.14, y, L * 0.38)), Vector((L * 0.14, y, L * 0.38))], [L * 0.025, L * 0.025], 5, lambda t, a, p, d: hexc("#ffffff"))
    # はき口
    uv_sphere(tmp, Vector((0, -L * 0.2, L * 0.34)), 1.0, lambda n: hexc("#2a2a2a"), seg=10, rings=6, scale=Vector((L * 0.15, L * 0.12, L * 0.05)))
    mb.add(tmp, m)


def cute_eye(mb, center, r, look, white=True):
    """白目＋黒目＋ハイライトのアニメ調の目"""
    look = Vector(look).normalized()
    if white:
        uv_sphere(mb, Vector(center), r, lambda n: WHITE, seg=12, rings=8)
        uv_sphere(mb, Vector(center) + look * r * 0.55, r * 0.62, lambda n: mixc(BLACK, hexc("#2c3a6a"), sstep(0.2, -0.8, n.z)), seg=10, rings=7)
        uv_sphere(mb, Vector(center) + look * r * 1.05 + Vector((0, 0, r * 0.3)), r * 0.2, lambda n: WHITE, seg=6, rings=4)
    else:
        uv_sphere(mb, Vector(center), r, lambda n: mixc(BLACK, hexc("#2c3a6a"), sstep(0.2, -0.8, n.z)), seg=12, rings=8)
        uv_sphere(mb, Vector(center) + look * r * 0.8 + Vector((0, 0, r * 0.35)), r * 0.3, lambda n: WHITE, seg=6, rings=4)


def split_legs(maker, name, leg_variant=None):
    """maker() を脚なしで作り、右側の脚を 1 本ずつ付け根が原点の別メッシュにする。
    leg_variant(i, mb, base, knee, tip) で足先に飾り（スニーカーなど）をつけた別の脚も作れる"""
    global _LEG_CAPTURE
    _LEG_CAPTURE = []
    body = maker()
    captured = _LEG_CAPTURE
    _LEG_CAPTURE = None
    right = []
    for roots, color, tipc, radius, disc in captured:
        for base, knee, tip in roots:
            if base[0] > 1e-6:
                right.append((Vector(base), Vector(knee), Vector(tip), color, tipc, radius, disc))
    out = [body]
    rig = []
    for i, (b, k, t, color, tipc, radius, disc) in enumerate(right):
        mb = MB()
        tube(mb, [Vector((0, 0, 0)), k - b, t - b], [radius, radius * 0.8, radius * 0.4], 6,
             lambda tt, a, p, d, c=color, tc=tipc: mixc(c, tc, sstep(0.5, 1.0, tt)))
        if disc > 0.0:
            water_dimple(mb, t - b, disc)
        leg = build(mb, f"{name}_Leg{i + 1}")
        out.append(leg)
        rig.append((leg.name, b, t))
        if leg_variant:
            mb2 = MB()
            tube(mb2, [Vector((0, 0, 0)), k - b, t - b], [radius, radius * 0.8, radius * 0.4], 6,
                 lambda tt, a, p, d, c=color, tc=tipc: mixc(c, tc, sstep(0.5, 1.0, tt)))
            leg_variant(i, mb2, Vector((0, 0, 0)), k - b, t - b)
            out.append(build(mb2, f"{name}_Leg{i + 1}_Sneaker"))
    RIG[name] = rig
    return out


# ---------------------------------------------------------------------------
# いきもの
# ---------------------------------------------------------------------------
def make_ladybug():
    mb = MB()
    red = hexc("#e8352b")
    spots = [Vector(v).normalized() for v in ((0.0, 0.62, 0.78), (0.55, 0.25, 0.8), (-0.55, 0.25, 0.8),
                                              (0.62, -0.35, 0.7), (-0.62, -0.35, 0.7), (0.3, -0.75, 0.6), (-0.3, -0.75, 0.6))]

    def shell(n):
        if n.z < 0.12:
            return BLACK
        if abs(n.x) < 0.035 and n.y < 0.7:
            return BLACK
        for s in spots:
            if (n - s).length < 0.24:
                return BLACK
        return mixc(red, hexc("#ff6a4a"), sstep(0.5, 1.0, n.z) * 0.35)
    uv_sphere(mb, Vector((0, -0.02, 0.09)), 1.0, shell, seg=40, rings=24, scale=Vector((0.19, 0.24, 0.15)))
    uv_sphere(mb, Vector((0, 0.21, 0.075)), 1.0, lambda n: BLACK if n.y < 0.5 else hexc("#f6f2e6"), seg=18, rings=10, scale=Vector((0.1, 0.07, 0.07)))
    for sx in (-1, 1):
        cute_eye(mb, (0.042 * sx, 0.265, 0.1), 0.026, (0.3 * sx, 1, 0.1))
        tube(mb, [Vector((0.03 * sx, 0.27, 0.12)), Vector((0.06 * sx, 0.32, 0.16)), Vector((0.08 * sx, 0.36, 0.16))], [0.006, 0.005, 0.0], 5,
             lambda t, a, p, d: BLACK)
    roots = []
    for k, y in enumerate((0.12, 0.0, -0.12)):
        for sx in (-1, 1):
            roots.append(((0.09 * sx, y, 0.04), (0.17 * sx, y + 0.02, 0.05), (0.21 * sx, y + 0.03 * (1 - k), 0.0)))
    legs(mb, roots, BLACK, radius=0.012)
    return build(mb, "Ladybug")


def make_snail():
    mb = MB()
    skin = hexc("#d2c2a6")
    skin_top = hexc("#b8a283")
    # 体（腹足）：殻の前から首と頭が、うしろから尾が、しっかり出ている
    pts = [Vector((0, -0.66, 0.025)), Vector((0, -0.5, 0.045)), Vector((0, -0.25, 0.07)), Vector((0, 0.05, 0.08)),
           Vector((0, 0.32, 0.085)), Vector((0, 0.5, 0.13)), Vector((0, 0.6, 0.21)), Vector((0, 0.64, 0.27))]
    radii = [0.0, 0.06, 0.095, 0.105, 0.1, 0.09, 0.075, 0.0]
    tube(mb, pts, radii, 18, lambda t, a, p, d: mixc(skin_top, skin, sstep(0.3, -0.6, d.z)), oval=(1.0, 0.8))
    # 目の触角
    for sx in (-1, 1):
        base = Vector((0.03 * sx, 0.6, 0.26))
        tip = Vector((0.1 * sx, 0.72, 0.54))
        tube(mb, [base, base.lerp(tip, 0.5) + Vector((0, 0, 0.02)), tip], [0.022, 0.016, 0.0], 8, lambda t, a, p, d: mixc(skin, skin_top, t))
        cute_eye(mb, tip + Vector((0, 0.01, 0.02)), 0.035, (0.2 * sx, 1, 0.2), white=False)
        tube(mb, [Vector((0.04 * sx, 0.63, 0.21)), Vector((0.08 * sx, 0.72, 0.19))], [0.015, 0.0], 6, lambda t, a, p, d: skin_top)
    # ほっぺ
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.07 * sx, 0.6, 0.21)), 0.022, lambda n: hexc("#ffb0b8"), seg=8, rings=5, scale=Vector((0.6, 1, 0.6)))
    # 殻（対数らせん）：体の背中の上にのせる（体の前とうしろが見えるように）
    center = Vector((0.0, -0.15, 0.31))
    spts = []
    srad = []
    n = 70
    turns = 3.4 * math.pi
    for i in range(n):
        t = i / (n - 1)
        th = turns * t
        r = 0.24 * math.exp(-0.21 * th)
        # いちばん太い口（殻の入り口）は、前の下で、下を向けて体にかぶせる。
        # そこから上へ立ち上がり、うしろへ小さくなりながらまく（まいている側が、うしろ）
        a = th - math.radians(30)
        spts.append(center + Vector((0.07 * t, math.cos(a) * r, math.sin(a) * r)))
        srad.append(0.56 * r)
    srad[-1] = 0.0

    def shell_col(t, a, p, d):
        band = 0.5 + 0.5 * math.sin(t * 70)
        c = mixc(hexc("#9a5a2a"), hexc("#d79a54"), band)
        c = mixc(c, hexc("#5a3018"), sstep(0.8, 1.0, abs(math.sin(t * 9))) * 0.5)
        return mixc(c, hexc("#f0d6a8"), sstep(0.85, 1.0, t))
    tube(mb, spts, srad, 18, shell_col, up_hint=Vector((1, 0, 0)))
    return build(mb, "Snail")


def make_ant():
    mb = MB()
    body = hexc("#2c1e16")
    top = hexc("#6a4632")

    def col(n):
        return mixc(body, top, sstep(0.2, 0.9, n.z) * 0.6)
    uv_sphere(mb, Vector((0, -0.13, 0.08)), 1.0, col, seg=18, rings=12, scale=Vector((0.065, 0.095, 0.065)))
    uv_sphere(mb, Vector((0, -0.03, 0.07)), 0.022, col, seg=8, rings=6)
    uv_sphere(mb, Vector((0, 0.03, 0.075)), 1.0, col, seg=12, rings=8, scale=Vector((0.035, 0.065, 0.038)))
    uv_sphere(mb, Vector((0, 0.12, 0.085)), 1.0, col, seg=16, rings=10, scale=Vector((0.055, 0.05, 0.05)))
    for sx in (-1, 1):
        cute_eye(mb, (0.04 * sx, 0.15, 0.1), 0.02, (0.4 * sx, 1, 0.1))
        tube(mb, [Vector((0.02 * sx, 0.16, 0.12)), Vector((0.05 * sx, 0.2, 0.17)), Vector((0.08 * sx, 0.28, 0.14))], [0.007, 0.006, 0.0], 5,
             lambda t, a, p, d: body)
    roots = []
    for y, fwd in ((0.06, 0.05), (0.03, 0.0), (0.0, -0.06)):
        for sx in (-1, 1):
            roots.append(((0.025 * sx, y, 0.06), (0.1 * sx, y + fwd * 0.5, 0.1), (0.15 * sx, y + fwd, 0.0)))
    legs(mb, roots, body, radius=0.009)
    return build(mb, "Ant")


def make_pillbug():
    mb = MB()
    plate = hexc("#5f6676")
    rim = hexc("#9aa1b0")

    def col(t, a, p):
        f = (t * 10) % 1.0
        c = mixc(plate, rim, sstep(0.7, 0.95, f) * 0.8)
        return mixc(c, hexc("#3e4350"), sstep(-0.2, -0.6, math.sin(a)))
    seg_body(mb, -0.3, 0.3, 0.17, 0.13, col, rings=60, seg=26,
             prof=lambda t: (math.sin(math.pi * t) ** 0.35, math.sin(math.pi * t) ** 0.55), ridge=0.06, ridge_n=10, flat=0.15)
    for sx in (-1, 1):
        cute_eye(mb, (0.06 * sx, 0.275, 0.06), 0.02, (0.3 * sx, 1, 0.0), white=False)
        tube(mb, [Vector((0.05 * sx, 0.29, 0.04)), Vector((0.12 * sx, 0.36, 0.06)), Vector((0.16 * sx, 0.4, 0.03))], [0.01, 0.008, 0.0], 5,
             lambda t, a, p, d: hexc("#4a505e"))
    return build(mb, "PillBug")


def make_butterfly_body():
    mb = MB()
    tube(mb, [Vector((0, -0.28, 0.0)), Vector((0, -0.1, 0.0)), Vector((0, 0.08, 0.0)), Vector((0, 0.12, 0.0))],
         [0.0, 0.035, 0.04, 0.03], 10, lambda t, a, p, d: mixc(hexc("#2a2a3a"), hexc("#5a5a70"), t))
    uv_sphere(mb, Vector((0, 0.15, 0.0)), 0.045, lambda n: hexc("#2a2a3a"), seg=12, rings=8)
    for sx in (-1, 1):
        cute_eye(mb, (0.03 * sx, 0.18, 0.015), 0.018, (0.4 * sx, 1, 0), white=False)
        a0 = Vector((0.015 * sx, 0.18, 0.03))
        a1 = Vector((0.12 * sx, 0.42, 0.1))
        tube(mb, [a0, a0.lerp(a1, 0.5), a1], [0.006, 0.005, 0.004], 5, lambda t, a, p, d: hexc("#2a2a3a"))
        uv_sphere(mb, a1, 0.018, lambda n: hexc("#2a2a3a"), seg=8, rings=5)
    return build(mb, "Butterfly_Body")


def make_butterfly_wing():
    mb = MB()

    def rfn(th):
        fore = 0.88 * math.exp(-((th - 0.95) / 0.55) ** 2)
        hind = 0.62 * math.exp(-((th - 2.25) / 0.5) ** 2)
        return max(0.12, fore, hind) * (1.0 + 0.02 * math.sin(th * 30))

    def cf(rho, th, p):
        c = mixc(hexc("#eef8ff"), hexc("#6fb8ff"), sstep(0.15, 0.8, rho))
        c = mixc(c, hexc("#26304e"), sstep(0.86, 0.93, rho))
        if th > 1.8 and 0.7 < rho < 0.84 and abs(math.sin(th * 9)) > 0.6:
            c = hexc("#ff9a3a")
        vein = sstep(0.04, 0.0, abs(math.sin(th * 7)) - 0.0) * 0.25
        return scalec(c, 1.0 - vein)
    polar_sheet(mb, rfn, cf, n_ang=60, n_rad=8, a0=0.05, a1=3.05, zfn=lambda rho, th: 0.03 * rho * rho, full=False)
    return build(mb, "Butterfly_Wing")


def make_beetle():
    mb = MB()
    shell = hexc("#3a1e10")
    shine = hexc("#8a5532")

    def elytra(t, a, p):
        c = mixc(shell, shine, sstep(0.6, 1.0, math.sin(a)) * 0.6 * (1 - t))
        if abs(p.x) < 0.012 and math.sin(a) > 0:
            c = hexc("#1e0e06")
        return c
    seg_body(mb, -0.75, 0.25, 0.42, 0.36, elytra, rings=36, seg=28,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.05 + 0.08)) ** 0.4, math.sin(math.pi * t) ** 0.6), z0=0.18)
    # 前胸
    uv_sphere(mb, Vector((0, 0.38, 0.32)), 1.0, lambda n: mixc(shell, shine, sstep(0.3, 0.9, n.z) * 0.7), seg=22, rings=14,
              scale=Vector((0.36, 0.22, 0.2)))
    tube(mb, [Vector((0, 0.42, 0.48)), Vector((0, 0.55, 0.56)), Vector((0, 0.62, 0.58))], [0.05, 0.03, 0.0], 8, lambda t, a, p, d: shell)
    # 頭と角
    uv_sphere(mb, Vector((0, 0.62, 0.24)), 1.0, lambda n: shell, seg=16, rings=10, scale=Vector((0.17, 0.14, 0.12)))
    horn = [Vector((0, 0.7, 0.24)), Vector((0, 0.95, 0.3)), Vector((0, 1.15, 0.48)), Vector((0, 1.25, 0.72))]
    tube(mb, horn, [0.07, 0.06, 0.045, 0.035], 10, lambda t, a, p, d: mixc(shell, shine, t * 0.6))
    for sx in (-1, 1):
        tip = horn[-1]
        tube(mb, [tip, tip + Vector((0.08 * sx, 0.06, 0.06)), tip + Vector((0.14 * sx, 0.06, 0.16))], [0.035, 0.025, 0.0], 7,
             lambda t, a, p, d: shine)
        cute_eye(mb, (0.11 * sx, 0.7, 0.28), 0.035, (0.5 * sx, 1, 0.1), white=False)
    roots = []
    for y in (0.4, 0.15, -0.2):
        for sx in (-1, 1):
            roots.append(((0.25 * sx, y, 0.18), (0.48 * sx, y + 0.05, 0.22), (0.62 * sx, y + 0.08, 0.0)))
    legs(mb, roots, shell, shine, radius=0.035)
    return build(mb, "Beetle")


def make_water_strider():
    mb = MB()
    body = hexc("#33303a")
    tube(mb, [Vector((0, -0.3, 0.12)), Vector((0, -0.1, 0.13)), Vector((0, 0.15, 0.14)), Vector((0, 0.25, 0.15))],
         [0.0, 0.045, 0.04, 0.0], 10, lambda t, a, p, d: mixc(body, hexc("#a9b4c6"), sstep(-0.2, -0.8, d.z)))
    uv_sphere(mb, Vector((0, 0.27, 0.15)), 0.04, lambda n: body, seg=10, rings=7)
    for sx in (-1, 1):
        cute_eye(mb, (0.035 * sx, 0.29, 0.17), 0.02, (0.4 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.02 * sx, 0.31, 0.17)), Vector((0.07 * sx, 0.42, 0.2))], [0.006, 0.0], 5, lambda t, a, p, d: body)
    # 前足（短い）・中足（こぐ）・後ろ足（かじ）
    for sx in (-1, 1):
        legs(mb, [((0.03 * sx, 0.22, 0.13), (0.1 * sx, 0.32, 0.12), (0.12 * sx, 0.38, 0.05))], body, radius=0.01)
    long_legs = [
        ((0.03, 0.1, 0.12), (0.45, 0.35, 0.22), (0.85, 0.75, 0.0)),
        ((0.03, -0.05, 0.12), (0.4, -0.25, 0.2), (0.7, -0.75, 0.0)),
    ]
    for (b, k, t) in long_legs:
        for sx in (-1, 1):
            legs(mb, [((b[0] * sx, b[1], b[2]), (k[0] * sx, k[1], k[2]), (t[0] * sx, t[1], t[2]))], body, radius=0.012, tip_disc=0.07)
    return build(mb, "WaterStrider")


def make_dragonfly_body():
    mb = MB()
    red = hexc("#e2482a")
    pts = [Vector((0, -1.05, 0.0)), Vector((0, -0.7, 0.0)), Vector((0, -0.35, 0.0)), Vector((0, -0.1, 0.0)), Vector((0, 0.0, 0.0))]

    def ab(t, a, p, d):
        ring = abs(math.sin(p.y * 28))
        c = mixc(red, hexc("#a8261a"), sstep(0.85, 1.0, ring))
        return mixc(c, hexc("#ffb08a"), sstep(-0.3, -0.8, d.z) * 0.6)
    tube(mb, pts, [0.0, 0.035, 0.04, 0.045, 0.05], 10, ab)
    uv_sphere(mb, Vector((0, 0.08, 0.0)), 1.0, lambda n: mixc(hexc("#b8582a"), hexc("#e8a060"), sstep(0.0, 0.8, n.z)), seg=14, rings=10,
              scale=Vector((0.09, 0.12, 0.09)))
    uv_sphere(mb, Vector((0, 0.22, 0.02)), 0.05, lambda n: hexc("#c05a30"), seg=10, rings=7)
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.055 * sx, 0.25, 0.04)), 0.06, lambda n: mixc(hexc("#8a2a1a"), hexc("#ff8a5a"), sstep(0.3, 1.0, n.z)), seg=14, rings=9)
        uv_sphere(mb, Vector((0.08 * sx, 0.28, 0.08)), 0.015, lambda n: WHITE, seg=6, rings=4)
    roots = []
    for y in (0.13, 0.08, 0.03):
        for sx in (-1, 1):
            roots.append(((0.04 * sx, y, -0.05), (0.09 * sx, y + 0.03, -0.1), (0.1 * sx, y + 0.06, -0.15)))
    legs(mb, roots, hexc("#2a2020"), radius=0.008)
    return build(mb, "Dragonfly_Body")


def make_dragonfly_wing():
    mb = MB()
    L = 0.85

    def rfn(th):
        # +X 方向に細長い羽（付け根が原点）
        d = th - math.pi / 2
        return L * math.exp(-(d / 0.17) ** 2) + 0.02

    def cf(rho, th, p):
        c = mixc(hexc("#f4f9ff"), hexc("#dfeefc"), rho)
        grid = abs(math.sin(p.x * 45)) < 0.12 or abs(math.sin(p.y * 70)) < 0.1
        if grid:
            c = scalec(c, 0.78)
        if 0.86 < rho < 0.94:
            c = hexc("#3a2a20")
        if rho < 0.12:
            c = hexc("#f2b46a")
        return c
    polar_sheet(mb, rfn, cf, n_ang=40, n_rad=8, a0=math.pi / 2 - 0.45, a1=math.pi / 2 + 0.45, full=False)
    return build(mb, "Dragonfly_Wing")


def make_frog():
    mb = MB()
    green = hexc("#79c84e")
    dark = hexc("#5a9e3a")
    belly = hexc("#f4f1d8")
    stripe = hexc("#4b3a2c")

    def body(n):
        c = mixc(green, dark, sstep(0.3, 1.0, n.z) * 0.25)
        c = mixc(c, belly, sstep(-0.1, -0.5, n.z))
        return c
    uv_sphere(mb, Vector((0, -0.08, 0.3)), 1.0, body, seg=28, rings=18, scale=Vector((0.42, 0.45, 0.3)))

    def head(n):
        c = body(n)
        if abs(abs(n.x) - 0.75) < 0.12 and -0.1 < n.z < 0.35 and n.y > -0.2:
            c = mixc(c, stripe, 0.9)
        if n.y > 0.7 and abs(n.z + 0.05) < 0.05:
            c = hexc("#3a5a2a")
        return c
    uv_sphere(mb, Vector((0, 0.28, 0.38)), 1.0, head, seg=28, rings=18, scale=Vector((0.4, 0.32, 0.25)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.2 * sx, 0.33, 0.56)), 0.11, lambda n: mixc(green, dark, 0.2), seg=14, rings=9)
        cute_eye(mb, (0.21 * sx, 0.39, 0.6), 0.075, (0.3 * sx, 1, 0.15))
        uv_sphere(mb, Vector((0.27 * sx, 0.45, 0.4)), 0.04, lambda n: hexc("#ffb3b3"), seg=8, rings=5, scale=Vector((0.6, 0.3, 0.5)))
        # 後ろ足（折りたたみ）
        uv_sphere(mb, Vector((0.33 * sx, -0.25, 0.16)), 1.0, body, seg=16, rings=10, scale=Vector((0.15, 0.25, 0.13)))
        for k in range(3):
            uv_sphere(mb, Vector((0.36 * sx + 0.05 * (k - 1) * sx, -0.02 + 0.03 * k, 0.03)), 0.04, lambda n: hexc("#9ad86a"), seg=8, rings=5)
        # 前足
        tube(mb, [Vector((0.25 * sx, 0.3, 0.22)), Vector((0.3 * sx, 0.42, 0.1)), Vector((0.3 * sx, 0.5, 0.02))], [0.045, 0.035, 0.03], 8,
             lambda t, a, p, d: green)
        uv_sphere(mb, Vector((0.3 * sx, 0.53, 0.02)), 0.04, lambda n: hexc("#9ad86a"), seg=8, rings=5)
    return build(mb, "Frog")


def make_crab():
    mb = MB()
    shell = hexc("#d8542a")
    light = hexc("#f4a66a")

    def cara(n):
        c = mixc(light, shell, sstep(-0.3, 0.4, n.z))
        return mixc(c, hexc("#b83e1e"), sstep(0.6, 1.0, n.z) * 0.3)
    uv_sphere(mb, Vector((0, 0, 0.3)), 1.0, cara, seg=26, rings=16, scale=Vector((0.42, 0.34, 0.15)))
    for sx in (-1, 1):
        base = Vector((0.1 * sx, 0.28, 0.36))
        tip = Vector((0.13 * sx, 0.34, 0.5))
        tube(mb, [base, tip], [0.025, 0.02], 6, lambda t, a, p, d: shell)
        cute_eye(mb, tip + Vector((0, 0.0, 0.03)), 0.045, (0.2 * sx, 1, 0.2), white=False)
        # はさみ
        big = 1.35 if sx > 0 else 1.0
        arm = [Vector((0.3 * sx, 0.2, 0.28)), Vector((0.45 * sx, 0.38, 0.3)), Vector((0.42 * sx, 0.55, 0.32))]
        tube(mb, arm, [0.05 * big, 0.045 * big, 0.04 * big], 8, lambda t, a, p, d: shell)
        claw = arm[-1]
        uv_sphere(mb, claw + Vector((0, 0.05, 0)), 1.0, cara, seg=12, rings=8, scale=Vector((0.08 * big, 0.11 * big, 0.07 * big)))
        for dz, ln in ((0.035, 0.16), (-0.035, 0.12)):
            p0 = claw + Vector((0, 0.12 * big, dz * big))
            tube(mb, [p0, p0 + Vector((-0.03 * sx, ln * big, 0))], [0.035 * big, 0.0], 7, lambda t, a, p, d: mixc(shell, light, t))
        roots = []
        for k, y in enumerate((0.1, -0.02, -0.14, -0.24)):
            roots.append(((0.35 * sx, y, 0.26), (0.58 * sx, y - 0.02 * k, 0.32), (0.72 * sx, y - 0.06 * k, 0.0)))
        legs(mb, roots, shell, light, radius=0.03)
    return build(mb, "Crab")


def make_river_snail():
    mb = MB()
    skin = hexc("#6b5f4e")
    tube(mb, [Vector((0, -0.25, 0.03)), Vector((0, 0.0, 0.05)), Vector((0, 0.25, 0.06)), Vector((0, 0.36, 0.08))],
         [0.0, 0.08, 0.07, 0.0], 12, lambda t, a, p, d: mixc(skin, hexc("#9a8c74"), sstep(0.2, -0.7, d.z)))
    for sx in (-1, 1):
        tube(mb, [Vector((0.03 * sx, 0.33, 0.08)), Vector((0.09 * sx, 0.48, 0.12))], [0.012, 0.0], 5, lambda t, a, p, d: skin)
        cute_eye(mb, (0.045 * sx, 0.32, 0.1), 0.018, (0.3 * sx, 1, 0.2), white=False)
    pts, rad = [], []
    n = 110
    turns = 9.0 * math.pi
    k = 0.085
    r0 = 0.17
    axis_base = Vector((0, -0.02, 0.15))
    axis_dir = Vector((0, -0.72, 0.69)).normalized()
    side = Vector((1, 0, 0))
    up = axis_dir.cross(side).normalized()
    for i in range(n):
        t = i / (n - 1)
        th = turns * t
        r = r0 * math.exp(-k * th)
        s_ax = 1.25 * r0 / (TAU * k) * (1 - math.exp(-k * th))   # 1巻きごとに太さ分だけ進む（すき間のない円すい形）
        c = axis_base + axis_dir * s_ax
        pts.append(c + side * math.cos(th) * r * 0.55 + up * math.sin(th) * r * 0.55)
        rad.append(r * 0.95)
    rad[-1] = 0.0

    def sc(t, a, p, d):
        band = 0.5 + 0.5 * math.sin(t * 90)
        c = mixc(hexc("#2f2d1e"), hexc("#6d6a45"), band * 0.6)
        return mixc(c, hexc("#c8bfa0"), sstep(0.9, 1.0, t))
    tube(mb, pts, rad, 14, sc, up_hint=Vector((1, 0, 0)))
    return build(mb, "RiverSnail")


def make_firefly_body():
    mb = MB()

    def el(t, a, p):
        return mixc(BLACK, hexc("#3a3a48"), sstep(0.5, 1.0, math.sin(a)) * 0.5)
    seg_body(mb, -0.28, 0.12, 0.1, 0.07, el, rings=24, seg=18, z0=0.06)

    def pro(n):
        if abs(n.x) < 0.14 or abs(n.y) < 0.12:
            return BLACK
        return hexc("#ef7a63")
    uv_sphere(mb, Vector((0, 0.16, 0.08)), 1.0, pro, seg=16, rings=10, scale=Vector((0.1, 0.07, 0.05)))
    uv_sphere(mb, Vector((0, 0.23, 0.06)), 0.04, lambda n: BLACK, seg=10, rings=7)
    for sx in (-1, 1):
        cute_eye(mb, (0.03 * sx, 0.26, 0.075), 0.018, (0.3 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.02 * sx, 0.26, 0.08)), Vector((0.07 * sx, 0.36, 0.12))], [0.006, 0.0], 5, lambda t, a, p, d: BLACK)
    roots = []
    for y in (0.14, 0.08, 0.02):
        for sx in (-1, 1):
            roots.append(((0.05 * sx, y, 0.04), (0.1 * sx, y + 0.02, 0.05), (0.13 * sx, y + 0.03, 0.0)))
    legs(mb, roots, BLACK, radius=0.007)
    return build(mb, "Firefly_Body")


def make_firefly_glow():
    mb = MB()
    seg_body(mb, -0.36, -0.2, 0.075, 0.05, lambda t, a, p: hexc("#eaff9a"), rings=10, seg=16, z0=0.05, flat=0.8)
    return build(mb, "Firefly_Glow")



# ---------------------------------------------------------------------------
# モブ（追加のいきもの）
# ---------------------------------------------------------------------------
def make_grasshopper():
    mb = MB()
    green = hexc("#7ab648")
    dark = hexc("#4f8a30")
    tan = hexc("#b89a5a")

    def body(t, a, p):
        c = mixc(green, dark, sstep(0.5, 1.0, math.sin(a)) * 0.3)
        if math.sin(a) > 0.55 and t < 0.6:
            c = mixc(c, tan, 0.75)
        return mixc(c, hexc("#c9e68a"), sstep(-0.2, -0.7, math.sin(a)))
    seg_body(mb, -0.95, 0.42, 0.12, 0.15, body, rings=40, seg=20, z0=0.22,
             prof=lambda t: (math.sin(math.pi * t) ** 0.35, math.sin(math.pi * min(1.0, t * 1.1)) ** 0.45), flat=0.6)
    uv_sphere(mb, Vector((0, 0.52, 0.3)), 1.0, lambda n: mixc(green, hexc("#9ad064"), sstep(-0.5, 0.5, n.y)), seg=16, rings=10,
              scale=Vector((0.11, 0.16, 0.13)))
    for sx in (-1, 1):
        cute_eye(mb, (0.09 * sx, 0.58, 0.36), 0.045, (0.5 * sx, 1, 0.1), white=False)
        a0 = Vector((0.04 * sx, 0.64, 0.42))
        tube(mb, [a0, a0 + Vector((0.08 * sx, 0.25, 0.18)), a0 + Vector((0.16 * sx, 0.55, 0.22))], [0.012, 0.009, 0.0], 5,
             lambda t, a, p, d: tan)
        # 後ろ足（大きなもも）。脚を分けるときは Grasshopper_Hind として別に作る
        if _LEG_CAPTURE is None:
            grasshopper_hind(mb, sx)
    roots = []
    for y in (0.35, 0.18):
        for sx in (-1, 1):
            roots.append(((0.08 * sx, y, 0.15), (0.18 * sx, y + 0.06, 0.15), (0.24 * sx, y + 0.1, 0.0)))
    legs(mb, roots, dark, radius=0.02)
    return build(mb, "Grasshopper")


GRASSHOPPER_HIP = Vector((0.1, -0.05, 0.25))


def grasshopper_hind(mb, sx, origin=None):
    green = hexc("#7ab648")
    dark = hexc("#4f8a30")
    o = origin if origin is not None else Vector((0, 0, 0))
    hip = Vector((GRASSHOPPER_HIP.x * sx, GRASSHOPPER_HIP.y, GRASSHOPPER_HIP.z)) - o
    knee = Vector((0.2 * sx, -0.55, 0.62)) - o
    foot = Vector((0.22 * sx, -0.9, 0.0)) - o
    tube(mb, [hip, hip.lerp(knee, 0.5) + Vector((0.03 * sx, 0, 0.04)), knee], [0.06, 0.07, 0.035], 10,
         lambda t, a, p, d: mixc(green, dark, t * 0.4))
    tube(mb, [knee, foot], [0.025, 0.015], 6, lambda t, a, p, d: mixc(dark, hexc("#7a5a3a"), t))


def make_grasshopper_hind():
    """右の後ろ足（付け根が原点）。跳ぶときにのばす"""
    mb = MB()
    grasshopper_hind(mb, 1, origin=GRASSHOPPER_HIP)
    return build(mb, "Grasshopper_Hind")


def make_otoshibumi():
    mb = MB()
    red = hexc("#d8452a")

    def el(t, a, p):
        dots = (int(p.x * 90) + int(p.y * 90)) % 3 == 0
        c = mixc(red, hexc("#ff7a52"), sstep(0.6, 1.0, math.sin(a)) * 0.4)
        if dots:
            c = scalec(c, 0.85)
        if abs(p.x) < 0.006 and math.sin(a) > 0:
            c = hexc("#8a2010")
        return c
    seg_body(mb, -0.2, 0.06, 0.1, 0.1, el, rings=24, seg=20, z0=0.08,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.1 + 0.05)) ** 0.4, math.sin(math.pi * t) ** 0.5))
    uv_sphere(mb, Vector((0, 0.09, 0.1)), 1.0, lambda n: BLACK, seg=12, rings=8, scale=Vector((0.06, 0.05, 0.05)))
    neck = [Vector((0, 0.12, 0.1)), Vector((0, 0.2, 0.13)), Vector((0, 0.27, 0.15))]
    tube(mb, neck, [0.025, 0.02, 0.018], 8, lambda t, a, p, d: BLACK)
    uv_sphere(mb, Vector((0, 0.3, 0.15)), 0.035, lambda n: BLACK, seg=10, rings=7)
    for sx in (-1, 1):
        cute_eye(mb, (0.028 * sx, 0.31, 0.165), 0.014, (0.5 * sx, 1, 0.2), white=False)
        tube(mb, [Vector((0.02 * sx, 0.32, 0.16)), Vector((0.04 * sx, 0.36, 0.2)), Vector((0.08 * sx, 0.39, 0.22))], [0.005, 0.004, 0.0], 5,
             lambda t, a, p, d: BLACK)
    roots = []
    for y in (0.05, -0.02, -0.09):
        for sx in (-1, 1):
            roots.append(((0.05 * sx, y, 0.06), (0.11 * sx, y + 0.02, 0.07), (0.14 * sx, y + 0.03, 0.0)))
    legs(mb, roots, BLACK, radius=0.008)
    return build(mb, "Otoshibumi")


def spider_legs():
    """ハエトリグモの 8 本の脚（右側の 4 本、左は鏡うつし）"""
    roots = []
    spec = [  # 付け根 y, 先の y, 太さ
        (0.11, 0.3, 0.026),
        (0.07, 0.13, 0.02),
        (0.03, -0.06, 0.02),
        (-0.01, -0.25, 0.022),
    ]
    for y0, y1, r in spec:
        roots.append(((0.06, y0, 0.12), (0.2, (y0 + y1) * 0.5, 0.22), (0.3, y1, 0.0), r))
    return roots


def make_spider():
    """ハエトリグモ：大きな前の目が特ちょう。ぴょんと跳ねる"""
    mb = MB()
    dark = hexc("#2b221c")
    fur = hexc("#5a4632")
    white = hexc("#f2ece0")

    def abdomen(n):
        c = mixc(dark, fur, sstep(-0.3, 0.6, n.z) * 0.7)
        if n.z > 0.2 and abs(n.x) < 0.12:
            c = white
        if n.z > 0.0 and abs(abs(n.x) - 0.55) < 0.08 and n.y < 0.3:
            c = mixc(c, white, 0.8)
        return c
    uv_sphere(mb, Vector((0, -0.17, 0.14)), 1.0, abdomen, seg=22, rings=14, scale=Vector((0.12, 0.15, 0.11)))
    uv_sphere(mb, Vector((0, -0.04, 0.13)), 0.03, lambda n: dark, seg=8, rings=5)

    def ceph(n):
        c = mixc(dark, fur, sstep(0.3, 0.9, n.z) * 0.5)
        if n.z > 0.1 and abs(n.y + 0.2) < 0.15:
            c = mixc(c, hexc("#d8c9a8"), 0.7)
        return c
    uv_sphere(mb, Vector((0, 0.06, 0.13)), 1.0, ceph, seg=22, rings=14, scale=Vector((0.11, 0.12, 0.085)))
    for sx in (-1, 1):
        cute_eye(mb, (0.036 * sx, 0.165, 0.15), 0.04, (0.15 * sx, 1, 0.0), white=False)   # 大きな前の目
        cute_eye(mb, (0.082 * sx, 0.14, 0.17), 0.018, (0.8 * sx, 0.5, 0.2), white=False)
        uv_sphere(mb, Vector((0.075 * sx, 0.04, 0.2)), 0.013, lambda n: BLACK, seg=6, rings=4)
        # 触肢
        tube(mb, [Vector((0.025 * sx, 0.17, 0.08)), Vector((0.05 * sx, 0.22, 0.07)), Vector((0.05 * sx, 0.25, 0.03))], [0.014, 0.012, 0.011], 6,
             lambda t, a, p, d: mixc(dark, hexc("#d8c9a8"), sstep(0.6, 1.0, t)))
    roots = spider_legs()
    for b, k, t, r in roots:
        for sx in (-1, 1):
            legs(mb, [((b[0] * sx, b[1], b[2]), (k[0] * sx, k[1], k[2]), (t[0] * sx, t[1], t[2]))], dark, fur, radius=r)
    return build(mb, "Spider")


def spider_sneaker_variant(i, mb, base, knee, tip):
    d = Vector((tip.x - knee.x, tip.y - knee.y, 0.0))
    colors = [hexc("#e8433a"), hexc("#3a7be8"), hexc("#f2c53a"), hexc("#3ab86a")]
    sneaker(mb, tip, d, 0.075, colors[i % len(colors)])


def make_mantis():
    """オオカマキリ：長い首と、かまの前足"""
    mb = MB()
    green = hexc("#7fbf4a")
    dark = hexc("#4f8a2e")
    pale = hexc("#c9e68a")

    def wings(t, a, p):
        c = mixc(green, dark, sstep(0.6, 1.0, math.sin(a)) * 0.25)
        if abs(p.x - 0.06) < 0.008 and math.sin(a) > 0.3:
            c = mixc(c, hexc("#3f7a22"), 0.8)
        if abs(p.x + 0.06) < 0.008 and math.sin(a) > 0.3:
            c = mixc(c, hexc("#3f7a22"), 0.8)
        return mixc(c, pale, sstep(-0.2, -0.7, math.sin(a)))
    seg_body(mb, -1.25, -0.05, 0.17, 0.14, wings, rings=40, seg=22, z0=0.42,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 0.9 + 0.12)) ** 0.5, math.sin(math.pi * t) ** 0.6), flat=0.7)
    neck = [Vector((0, -0.08, 0.46)), Vector((0, 0.15, 0.55)), Vector((0, 0.4, 0.68)), Vector((0, 0.56, 0.76))]
    tube(mb, neck, [0.06, 0.05, 0.045, 0.04], 10, lambda t, a, p, d: mixc(green, pale, sstep(-0.3, -0.8, d.z)))
    uv_sphere(mb, Vector((0, 0.63, 0.8)), 1.0, lambda n: mixc(green, pale, sstep(-0.2, -0.8, n.z)), seg=18, rings=10,
              scale=Vector((0.12, 0.07, 0.085)))
    uv_sphere(mb, Vector((0, 0.7, 0.74)), 1.0, lambda n: pale, seg=10, rings=6, scale=Vector((0.05, 0.04, 0.05)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.12 * sx, 0.63, 0.84)), 0.055, lambda n: mixc(hexc("#b8e06a"), hexc("#e8f6b0"), sstep(0.0, 0.9, n.z)), seg=14, rings=9)
        uv_sphere(mb, Vector((0.15 * sx, 0.66, 0.85)), 0.016, lambda n: BLACK, seg=6, rings=4)
        a0 = Vector((0.03 * sx, 0.67, 0.87))
        tube(mb, [a0, a0 + Vector((0.1 * sx, 0.25, 0.25)), a0 + Vector((0.22 * sx, 0.5, 0.3))], [0.008, 0.006, 0.0], 5,
             lambda t, a, p, d: dark)
    roots = []
    for (b, k, t) in (((0.05, -0.02, 0.44), (0.36, 0.08, 0.52), (0.5, 0.26, 0.0)),
                      ((0.05, -0.18, 0.44), (0.42, -0.34, 0.54), (0.56, -0.7, 0.0))):
        for sx in (-1, 1):
            roots.append(((b[0] * sx, b[1], b[2]), (k[0] * sx, k[1], k[2]), (t[0] * sx, t[1], t[2])))
    legs(mb, roots, green, dark, radius=0.024)
    return build(mb, "Mantis")


MANTIS_SHOULDER = Vector((0.05, 0.42, 0.66))


def mantis_arm(mb, sx, origin):
    green = hexc("#7fbf4a")
    pale = hexc("#c9e68a")
    o = origin
    s0 = Vector((MANTIS_SHOULDER.x * sx, MANTIS_SHOULDER.y, MANTIS_SHOULDER.z)) - o
    coxa = s0 + Vector((0.03 * sx, 0.12, -0.2))
    femur = coxa + Vector((0.01 * sx, 0.3, 0.12))
    tibia = femur + Vector((0.0, -0.16, -0.1))
    tube(mb, [s0, coxa], [0.045, 0.038], 8, lambda t, a, p, d: green)
    tube(mb, [coxa, coxa.lerp(femur, 0.5) + Vector((0, 0, 0.02)), femur], [0.04, 0.045, 0.03], 8, lambda t, a, p, d: mixc(green, pale, t * 0.3))
    tube(mb, [femur, tibia, tibia + Vector((0, -0.04, -0.06))], [0.028, 0.022, 0.0], 7, lambda t, a, p, d: pale)
    for k in range(4):   # とげ
        q = coxa.lerp(femur, 0.25 + 0.18 * k)
        tube(mb, [q, q + Vector((0, 0.01, -0.05))], [0.01, 0.0], 4, lambda t, a, p, d: hexc("#3f6a22"))


def make_mantis_arm():
    """右のかま（肩が原点）。いきものが近づくと持ち上げる"""
    mb = MB()
    mantis_arm(mb, 1, MANTIS_SHOULDER)
    return build(mb, "Mantis_Arm")


def make_ant_helmet():
    """工事現場のヘルメット（アリの頭にのせる。アリと同じ座標）"""
    mb = MB()
    yellow = hexc("#f6c21c")
    head = Vector((0, 0.125, 0.12))
    r = 0.052

    def dome(n):
        if n.z < 0.12:
            return hexc("#e0a810")
        if abs(n.x) < 0.09 and n.z > 0.2:
            return hexc("#ffd84a")   # まんなかのすじ
        return yellow
    uv_sphere(mb, head, r, dome, seg=20, rings=12, scale=Vector((1.0, 1.08, 0.82)))
    # つば（前が長い）
    ring = []
    for j in range(24):
        a = TAU * j / 24
        f = 1.0 + 0.45 * max(0.0, math.sin(a))
        ring.append(Vector((math.cos(a) * r * 1.22, math.sin(a) * r * 1.22 * f, 0.0)))
    top = [mb.v(head + v + Vector((0, 0, -0.012)), yellow) for v in ring]
    inner = [mb.v(head + v * 0.78 + Vector((0, 0, -0.008)), yellow) for v in ring]
    bot = [mb.v(head + v + Vector((0, 0, -0.02)), hexc("#d89a10")) for v in ring]
    n = len(ring)
    for j in range(n):
        k = (j + 1) % n
        mb.f(inner[j], inner[k], top[k], top[j])
        mb.f(top[j], top[k], bot[k], bot[j])
        mb.f(bot[j], bot[k], inner[k], inner[j])
    # 緑十字（安全）
    c = head + Vector((0, r * 0.98, r * 0.2))
    for dx, dz, w, h in ((0, 0, 0.012, 0.03), (0, 0, 0.03, 0.012)):
        uv_sphere(mb, c, 1.0, lambda nn: hexc("#2fa04a"), seg=6, rings=4, scale=Vector((w * 0.5, 0.006, h * 0.5)))
    return build(mb, "Ant_Helmet")


def make_crumb():
    """アリが運ぶ食べもののかけら"""
    mb = MB()
    rnd = random.Random(7)
    uv_sphere(mb, Vector((0, 0, 0)), 1.0, lambda n: mixc(hexc("#e8d2a0"), hexc("#c9a46a"), 0.5 + 0.5 * n.z), seg=10, rings=7,
              scale=Vector((0.045, 0.05, 0.035)))
    return build(mb, "Crumb")


def make_pillbug_ball():
    """まるくなっただんごむし"""
    mb = MB()
    plate = hexc("#5f6676")
    rim = hexc("#9aa1b0")

    def col(n):
        f = (math.atan2(n.z, n.y) / TAU * 10) % 1.0
        return mixc(plate, rim, sstep(0.75, 0.95, f) * 0.8)
    uv_sphere(mb, Vector((0, 0, 0.13)), 0.13, col, seg=22, rings=16)
    return build(mb, "PillBug_Ball")


# ---------------------------------------------------------------------------
# 当たり判定用のメッシュ（見た目のメッシュは部品が重なっているので、登るときに中へ入りこんでしまう）
# 柄からかさの裏へなめらかにつながる、すき間のないひとつの回転体にする
# ---------------------------------------------------------------------------
COLLIDER_PROFILES = {
    "Mushroom_Red_Col": [(0.0, -0.3), (0.9, -0.3), (0.9, -0.05), (0.84, 0.3), (0.75, 0.6), (0.56, 1.0), (0.46, 2.0), (0.43, 3.4), (0.46, 4.2),
                         (0.62, 4.55), (1.2, 4.72), (2.2, 4.66), (2.85, 4.62), (3.1, 4.72), (3.12, 4.88), (2.95, 5.2), (2.55, 5.65),
                         (1.9, 6.08), (1.0, 6.38), (0.0, 6.48)],
    "Mushroom_Brown_Col": [(0.0, -0.3), (1.18, -0.3), (1.18, -0.05), (1.12, 0.35), (1.05, 1.0), (0.82, 1.9), (0.74, 2.25), (0.95, 2.46),
                           (1.7, 2.45), (2.2, 2.5), (2.35, 2.7), (2.25, 3.05), (1.85, 3.45), (1.1, 3.75), (0.0, 3.85)],
    "Mushroom_Glow_Col": [(0.0, -0.2), (0.48, -0.2), (0.48, 0.0), (0.45, 0.3), (0.32, 1.0), (0.27, 1.55), (0.4, 1.73), (0.6, 1.75),
                          (1.15, 1.72), (1.25, 1.85), (1.05, 2.15), (0.6, 2.42), (0.0, 2.52)],
}


def make_collider(name):
    mb = MB()
    lathe(mb, COLLIDER_PROFILES[name], 20, lambda t, a, p: hexc("#ffffff"))
    return build(mb, name)


def make_cradle():
    """オトシブミのゆりかご（葉っぱをくるくる巻いたもの）"""
    mb = MB()
    pts = [Vector((0, -0.5, 0.17)), Vector((0, -0.48, 0.17)), Vector((0, -0.25, 0.17)), Vector((0, 0.0, 0.17)),
           Vector((0, 0.25, 0.17)), Vector((0, 0.48, 0.17)), Vector((0, 0.5, 0.17))]

    def col(t, a, p, d):
        spiral = abs(math.sin(a * 1.0 + t * 14.0))
        c = mixc(hexc("#6f9a3a"), hexc("#a8b85a"), 0.5 + 0.5 * math.sin(t * 9))
        return mixc(c, hexc("#3e5a22"), sstep(0.92, 1.0, spiral))
    tube(mb, pts, [0.0, 0.15, 0.18, 0.18, 0.17, 0.14, 0.0], 16, col, up_hint=Vector((0, 0, 1)))
    tube(mb, [pts[-1], pts[-1] + Vector((0, 0.18, 0.05))], [0.02, 0.0], 5, lambda t, a, p, d: hexc("#6a5a2a"))
    return build(mb, "Cradle")


def make_anthill():
    mb = MB()
    prof = [(1.35, 0.0), (1.0, 0.14), (0.6, 0.3), (0.32, 0.34), (0.18, 0.2), (0.0, 0.18)]

    def col(t, a, p):
        rr = math.hypot(p.x, p.y)
        c = mixc(hexc("#8a6a48"), hexc("#b08a60"), 0.5 + 0.5 * noise.noise(p * 6.0))
        return mixc(c, hexc("#2a1c12"), sstep(0.3, 0.18, rr))
    lathe(mb, prof, 28, col, rfn=lambda t, a: 1.0 + 0.08 * math.sin(a * 5 + 1))
    rnd = random.Random(4)
    for i in range(30):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0.4, 1.4)
        uv_sphere(mb, Vector((math.cos(a) * r, math.sin(a) * r, 0.02 + 0.25 * max(0.0, 1.0 - r / 1.35))), rnd.uniform(0.03, 0.06),
                  lambda n: hexc("#9c7a52"), seg=6, rings=4)
    return build(mb, "AntHill")


def make_bird_body(name, L, crow):
    mb = MB()
    if crow:
        back, belly, cap = hexc("#18181f"), hexc("#22222c"), hexc("#18181f")
    else:
        back, belly, cap = hexc("#8b5a36"), hexc("#e6dccb"), hexc("#7a3a22")
    sheen = hexc("#3b4470")

    def body_col(n):
        if crow:
            return mixc(back, sheen, sstep(0.3, 0.9, n.z) * 0.6)
        c = mixc(belly, back, sstep(-0.1, 0.35, n.z))
        stripe = abs(math.sin(n.x * 18 + n.y * 6))
        if n.z > 0.3 and stripe > 0.85:
            c = hexc("#4a2e1c")
        return c
    bw, bl = (0.13, 0.3) if crow else (0.17, 0.27)
    rot = Matrix.Rotation(math.radians(-14), 4, "X")
    tmp = MB()
    uv_sphere(tmp, Vector((0, 0, 0)), 1.0, body_col, seg=28, rings=18, scale=Vector((bw, bl, bw)))
    mb.add(tmp, Matrix.Translation(Vector((0, -0.02, 0.32))) @ rot)
    hc = Vector((0, 0.27 if crow else 0.24, 0.47))
    hr = 0.11 if crow else 0.13

    def head_col(n):
        if crow:
            return mixc(back, sheen, sstep(0.3, 0.9, n.z) * 0.6)
        if n.z > 0.45:
            return cap
        if n.y > 0.45 and n.z < -0.05:
            return hexc("#1e1a18")
        if abs(n.x) > 0.55 and -0.35 < n.z < 0.45:
            if abs(n.x) > 0.8 and -0.1 < n.z < 0.2 and n.y < 0.2:
                return hexc("#1e1a18")
            return hexc("#f6f4ee")
        return mixc(cap, belly, 0.4)
    uv_sphere(mb, hc, hr, head_col, seg=22, rings=14)
    # くちばし
    blen = 0.22 if crow else 0.09
    brad = 0.05 if crow else 0.035
    base = hc + Vector((0, hr * 0.85, -0.01))
    tube(mb, [base, base + Vector((0, blen * 0.6, -0.005)), base + Vector((0, blen, -0.03 if crow else -0.02))], [brad, brad * 0.6, 0.0], 10,
         lambda t, a, p, d: hexc("#2a2a2e") if crow else hexc("#3a3a3a"))
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.075 * sx, 0.06, 0.03)), 0.028 if crow else 0.032, (0.6 * sx, 1, 0.1), white=False)
    # 尾
    tl = 0.32 if crow else 0.26
    tail = [Vector((0, -0.22, 0.38)), Vector((0, -0.22 - tl * 0.5, 0.36)), Vector((0, -0.22 - tl, 0.32))]
    ribbon(mb, tail, [0.1, 0.13, 0.11 if crow else 0.09], [Vector((1, 0, 0))] * 3,
           lambda tt, v, p: back if crow else mixc(back, hexc("#4a2e1c"), tt), fold=0.05, normal_vecs=[Vector((0, 0, 1))] * 3)
    # 足
    leg = hexc("#2a2a2e") if crow else hexc("#b88a6a")
    for sx in (-1, 1):
        hip = Vector((0.05 * sx, 0.02, 0.2))
        ft = Vector((0.06 * sx, 0.04, 0.0))
        tube(mb, [hip, ft], [0.015, 0.012], 6, lambda t, a, p, d: leg)
        for ang in (-0.5, 0.0, 0.5):
            tube(mb, [ft, ft + Vector((math.sin(ang) * 0.06, math.cos(ang) * 0.07, 0))], [0.01, 0.006], 5, lambda t, a, p, d: leg)
        tube(mb, [ft, ft + Vector((0, -0.05, 0))], [0.01, 0.006], 5, lambda t, a, p, d: leg)
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    return build(mb, name)


def make_bird_wing(name, L, crow):
    mb = MB()
    length = 0.48 if crow else 0.4

    def rfn(th):
        d = th - 1.75
        r = length * math.exp(-(d / 0.55) ** 2) + 0.06
        return r * (1.0 + 0.07 * abs(math.sin(th * 22)) * sstep(1.6, 2.2, th))

    def cf(rho, th, p):
        if crow:
            c = mixc(hexc("#18181f"), hexc("#3b4470"), 0.35 + 0.3 * math.sin(th * 20))
            return mixc(c, hexc("#101015"), sstep(0.8, 1.0, rho))
        c = mixc(hexc("#9a6a44"), hexc("#6a442a"), sstep(0.5, 1.0, rho))
        if 0.42 < rho < 0.5:
            c = hexc("#f2ece0")
        return mixc(c, hexc("#2e2018"), sstep(0.85, 1.0, rho) * 0.8)
    polar_sheet(mb, rfn, cf, n_ang=40, n_rad=6, a0=math.pi / 2 - 0.5, a1=math.pi / 2 + 1.0, full=False)
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    return build(mb, name)

# ---------------------------------------------------------------------------
# 川辺のステージ
# ---------------------------------------------------------------------------
def make_river_stone(name, seed, size, palette):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    off = Vector((rnd.uniform(-50, 50), rnd.uniform(-50, 50), rnd.uniform(-50, 50)))
    for v in bm.verts:
        p = v.co.copy()
        p *= 1.0 + 0.06 * noise.noise(p * 1.4 + off)
        p = Vector((p.x * size[0], p.y * size[1], p.z * size[2]))
        if p.z < -0.15 * size[2]:
            p.z = -0.15 * size[2] + (p.z + 0.15 * size[2]) * 0.2
        v.co = p
    bm.normal_update()
    mb = MB()
    idx = {}
    for v in bm.verts:
        p = v.co
        t = 0.5 + 0.5 * noise.noise(p * 0.8 + off)
        c = mixc(palette[0], palette[1], t)
        speck = noise.noise(p * 12.0 + off)
        if speck > 0.55:
            c = scalec(c, 1.18)
        elif speck < -0.6:
            c = scalec(c, 0.8)
        c = mixc(c, scalec(c, 0.6), sstep(0.15 * size[2], -0.1 * size[2], p.z))   # ぬれた下側
        c = mixc(c, hexc("#8bb85a"), sstep(0.75, 0.95, v.normal.z) * sstep(0.3, 0.6, noise.noise(p * 2.0 + off)) * 0.6)
        idx[v.index] = mb.v(p, c)
    for fc in bm.faces:
        mb.f(*[idx[v.index] for v in fc.verts])
    bm.free()
    return build(mb, name, smooth=True)


def make_horsetail():
    rnd = random.Random(8)
    mb = MB()
    stem = hexc("#6fa043")
    node = hexc("#3e5a26")
    for i in range(4):
        a = rnd.uniform(0, TAU)
        base = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.0, 0.6)
        h = rnd.uniform(3.8, 5.6)
        top = base + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), h))
        pts = quad_bezier(base + Vector((0, 0, -0.1)), base + Vector((0, 0, h * 0.6)), top, 12)
        tube(mb, pts, [0.075] * 11 + [0.0], 7,
             lambda t, a2, p, d: with_alpha(mixc(stem, node, sstep(0.85, 1.0, abs(math.sin(t * 22)))), t))
        for k in range(1, 9):
            t = k / 9
            c = pts[int(t * 11)]
            ln = 0.7 * (1 - t) + 0.2
            for j in range(8):
                ang = TAU * j / 8 + k
                d = Vector((math.cos(ang), math.sin(ang), 0.8)).normalized()
                sv = Vector((-math.sin(ang), math.cos(ang), 0))
                ppts = [c + d * ln * s / 2 for s in range(3)]
                ribbon(mb, ppts, [0.04, 0.03, 0.0], [sv] * 3,
                       lambda tt, v, p, t=t: with_alpha(mixc(stem, hexc("#a8d06a"), tt), t), normal_vecs=[Vector((0, 0, 1))] * 3)
    # つくし
    for i in range(3):
        a = rnd.uniform(0, TAU)
        base = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0.6, 1.0)
        h = rnd.uniform(1.8, 2.6)
        top = base + Vector((0, 0, h))
        tube(mb, [base + Vector((0, 0, -0.1)), base + Vector((0, 0, h * 0.5)), top], [0.07, 0.065, 0.06], 7,
             lambda t, a2, p, d: with_alpha(mixc(hexc("#e2d2b2"), hexc("#c9b48a"), sstep(0.8, 1.0, abs(math.sin(t * 9)))), t))
        tmp = MB()
        lathe(tmp, [(0.0, 0.0), (0.1, 0.02), (0.13, 0.25), (0.11, 0.5), (0.06, 0.62), (0.0, 0.65)], 12,
              lambda t, a2, p: with_alpha(hexc("#8a5a34") if (int(p.z * 30) + int(a2 * 3)) % 2 else hexc("#b07a48"), 1.0))
        mb.add(tmp, Matrix.Translation(top))
    return build(mb, "Horsetail")


def make_iris():
    rnd = random.Random(9)
    mb = MB()
    leaf = hexc("#3f7f3a")
    for i in range(7):
        a = TAU * i / 7 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        h = rnd.uniform(5.5, 8.0)
        pts = [d * 0.15 * s + Vector((0, 0, h * s / 6)) + d * (0.6 * (s / 6) ** 2) for s in range(7)]
        widths = [0.45, 0.5, 0.48, 0.42, 0.34, 0.2, 0.0]
        ribbon(mb, pts, widths, [sv] * 7, lambda tt, v, p: with_alpha(mixc(leaf, hexc("#7cb85a"), tt), tt), fold=0.1,
               normal_vecs=[d] * 7)
    h = 7.2
    stem = quad_bezier(Vector((0, 0, -0.1)), Vector((0.2, 0, h * 0.6)), Vector((0.1, 0.1, h)), 8)
    tube(mb, stem, [0.07] * 7 + [0.05], 6, lambda t, a, p, d: with_alpha(leaf, t))
    top = stem[-1]
    purple = hexc("#5b4ad2")
    for k in range(3):
        a = TAU * k / 3
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        # 外花被（垂れる）
        fall = [top + d * (0.9 * s / 4) + Vector((0, 0, 0.25 * (s / 4) - 0.75 * (s / 4) ** 2)) for s in range(5)]
        ribbon(mb, fall, [0.15, 0.45, 0.6, 0.55, 0.0], [sv] * 5,
               lambda tt, v, p: with_alpha(mixc(purple, hexc("#f6e66a"), sstep(0.35, 0.0, abs(v)) * sstep(0.2, 0.5, tt) * sstep(0.8, 0.5, tt)), 1.0),
               fold=0.15, normal_vecs=[Vector((0, 0, 1))] * 5)
        # 内花被（立つ）
        a2 = a + math.pi / 3
        d2 = Vector((math.cos(a2), math.sin(a2), 0))
        sv2 = Vector((-d2.y, d2.x, 0))
        std = [top + d2 * (0.2 * s / 3) + Vector((0, 0, 0.7 * s / 3)) for s in range(4)]
        ribbon(mb, std, [0.1, 0.28, 0.24, 0.0], [sv2] * 4,
               lambda tt, v, p: with_alpha(mixc(hexc("#7a6ae8"), purple, tt), 1.0), fold=0.1, normal_vecs=[d2] * 4)
    return build(mb, "Iris")


def make_root_arch():
    rnd = random.Random(10)
    mb = MB()
    for k, (yoff, w, h, r0) in enumerate(((-0.7, 2.2, 3.4, 0.5), (0.0, 2.5, 3.9, 0.6), (0.7, 2.1, 3.2, 0.45))):
        pts = []
        n = 26
        for i in range(n):
            t = i / (n - 1)
            th = math.pi * t
            x = -math.cos(th) * w * (1.0 + 0.15 * (t - 0.5) ** 2)
            z = math.sin(th) * h - 0.6 + 0.6 * (1 - math.sin(th))
            y = yoff + 0.25 * math.sin(t * 5 + k)
            pts.append(Vector((x, y, z)))
        radii = [r0 * (1.0 + 0.6 * (1 - math.sin(math.pi * t)) ** 2) * (0.9 + 0.1 * math.sin(t * 13 + k)) for t in (i / (n - 1) for i in range(n))]

        def col(t, a, p, d, k=k):
            c = kit.bark_color(p * 0.6, a, 0.3 * sstep(0.7, 1.0, abs(math.sin(a * 3 + t * 9))), 7.0 + k, moss_amt=0.0)
            m = sstep(0.2, 0.6, d.z + 0.3 * noise.noise(p * 1.5))
            return mixc(c, mixc(kit.MOSS_A, kit.MOSS_B, 0.5 + 0.5 * noise.noise(p * 3)), m)
        tube(mb, pts, radii, 14, col, up_hint=Vector((0, 1, 0)))
    # 小さな光るキノコの飾り
    for i in range(7):
        t = rnd.uniform(0.15, 0.85)
        th = math.pi * t
        p = Vector((-math.cos(th) * 2.5, rnd.uniform(-0.5, 0.5), math.sin(th) * 3.9 - 0.6 + 0.6 * (1 - math.sin(th)) + 0.55))
        lathe(mb, [(0.0, 0.0), (0.04, 0.0), (0.03, 0.18), (0.0, 0.2)], 8, lambda tt, a, q: hexc("#e8fbff"), center=p)
        lathe(mb, [(0.0, 0.16), (0.14, 0.14), (0.1, 0.24), (0.0, 0.27)], 12, lambda tt, a, q: hexc("#8fe8ff"), center=p)
    return build(mb, "RootArch")


# ---------------------------------------------------------------------------
# 図鑑の絵
# ---------------------------------------------------------------------------
CREATURES = [
    ("ant", ["Ant", "Crumb_Portrait"]),
    ("snail", ["Snail"]),
    ("butterfly", ["Butterfly_Body", "Butterfly_Wing", "Butterfly_Wing_L"]),
    ("otoshibumi", ["Otoshibumi", "Cradle_Portrait"]),
    ("grasshopper", ["Grasshopper"]),
    ("frog", ["Frog"]),
    ("sparrow", ["Sparrow_Body", "Sparrow_Wing_R", "Sparrow_Wing_L"]),
    ("crow", ["Crow_Body", "Crow_Wing_R", "Crow_Wing_L"]),
    ("ladybug", ["Ladybug"]),
    ("pillbug", ["PillBug"]),
    ("beetle", ["Beetle"]),
    ("waterstrider", ["WaterStrider"]),
    ("dragonfly", ["Dragonfly_Body", "Dragonfly_Wing_FR", "Dragonfly_Wing_FL", "Dragonfly_Wing_BR", "Dragonfly_Wing_BL"]),
    ("crab", ["Crab"]),
    ("riversnail", ["RiverSnail"]),
    ("firefly", ["Firefly_Body", "Firefly_Glow"]),
    ("spider", ["Spider"]),
    ("mantis", ["Mantis", "Mantis_Arm_R", "Mantis_Arm_L"]),
    ("ant_helmet", ["Ant", "Ant_Helmet"]),
    ("spider_sneaker", ["Spider"]),
]

# 図鑑の絵で脚をつける体（脚メッシュのコピーを付け根に置く）。スニーカーグモだけ脚の種類がちがう
PORTRAIT_LEGS = {
    "ant": "Ant", "ladybug": "Ladybug", "beetle": "Beetle", "waterstrider": "WaterStrider", "crab": "Crab",
    "grasshopper": "Grasshopper", "otoshibumi": "Otoshibumi", "spider": "Spider", "mantis": "Mantis",
    "ant_helmet": "Ant", "spider_sneaker": "Spider",
}


def pose_portrait_copies(objs):
    """図鑑の絵のために、羽を広げた姿勢のコピーを作る"""
    wing = objs.get("Butterfly_Wing")
    if wing:
        l = wing.copy()
        l.data = wing.data
        l.name = "Butterfly_Wing_L"
        bpy.context.scene.collection.objects.link(l)
        l.scale = (-1, 1, 1)
        wing.rotation_euler = (0, -0.35, 0)
        l.rotation_euler = (0, 0.35, 0)
        objs["Butterfly_Wing_L"] = l
    dw = objs.get("Dragonfly_Wing")
    if dw:
        for nm, (sx, y) in {"Dragonfly_Wing_FR": (1, 0.1), "Dragonfly_Wing_FL": (-1, 0.1), "Dragonfly_Wing_BR": (1, -0.02), "Dragonfly_Wing_BL": (-1, -0.02)}.items():
            c = dw.copy()
            c.data = dw.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (0.03 * sx, y, 0.05)
            c.rotation_euler = (0, 0, (0.15 if y > 0 else -0.25) * sx)
            objs[nm] = c
        dw.hide_render = True
    for bird, L in (("Sparrow", 5.5), ("Crow", 18.0)):
        w = objs.get(bird + "_Wing")
        if not w:
            continue
        for nm, sx in ((bird + "_Wing_R", 1), (bird + "_Wing_L", -1)):
            c = w.copy()
            c.data = w.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (0.12 * L * sx, 0.02 * L, 0.38 * L)
            # たたんだ羽：体の横に沿わせる
            c.rotation_euler = (0.0, -0.25 * sx, -1.35 * sx)
            objs[nm] = c
        w.hide_render = True
    arm = objs.get("Mantis_Arm")
    if arm:
        for nm, sx in (("Mantis_Arm_R", 1), ("Mantis_Arm_L", -1)):
            c = arm.copy()
            c.data = arm.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (MANTIS_SHOULDER.x * sx, MANTIS_SHOULDER.y, MANTIS_SHOULDER.z)
            objs[nm] = c
        arm.hide_render = True
    crumb = objs.get("Crumb")
    if crumb:
        c = crumb.copy()
        c.data = crumb.data
        c.name = "Crumb_Portrait"
        bpy.context.scene.collection.objects.link(c)
        c.location = (0, 0.2, 0.08)
        objs["Crumb_Portrait"] = c
    hind = objs.get("Grasshopper_Hind")
    if hind:
        for nm, sx in (("Grasshopper_Hind_R", 1), ("Grasshopper_Hind_L", -1)):
            c = hind.copy()
            c.data = hind.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (GRASSHOPPER_HIP.x * sx, GRASSHOPPER_HIP.y, GRASSHOPPER_HIP.z)
            objs[nm] = c
        hind.hide_render = True
    cr = objs.get("Cradle")
    if cr:
        c = cr.copy()
        c.data = cr.data
        c.name = "Cradle_Portrait"
        bpy.context.scene.collection.objects.link(c)
        c.location = (0.45, -0.2, 0.0)
        c.scale = (0.6, 0.6, 0.6)
        objs["Cradle_Portrait"] = c


def portrait_leg_names(objs, cid):
    """図鑑の絵のために、脚メッシュのコピーを左右の付け根に置く（名前の一覧を返す）"""
    body = PORTRAIT_LEGS.get(cid)
    if not body or body not in RIG:
        return []
    sneaker = cid == "spider_sneaker"
    names = []
    for i, (leg_name, base, tip) in enumerate(RIG[body]):
        src = objs.get(leg_name + ("_Sneaker" if sneaker else ""))
        if src is None:
            continue
        for sx in (1, -1):
            nm = f"{cid}_{leg_name}_{'R' if sx > 0 else 'L'}"
            if nm not in objs:
                c = src.copy()
                c.data = src.data
                c.name = nm
                bpy.context.scene.collection.objects.link(c)
                c.scale = (sx, 1, 1)
                c.location = (base.x * sx, base.y, base.z)
                objs[nm] = c
            names.append(nm)
    return names


def write_rig(path):
    """Unity 用の脚の付け根の一覧（C#）を書き出す。Blender (x, y, z) → Unity (-x, z, -y)"""
    def u(v):
        return f"new Vector3({-v.x:.4f}f, {v.z:.4f}f, {-v.y:.4f}f)"
    lines = [
        "// 自動生成ファイル（blender/scripts/build_creatures.py）。手で編集しないこと。",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace Shakutori",
        "{",
        "    public static partial class CreatureRig",
        "    {",
        "        static readonly Dictionary<string, LegMount[]> Generated = new Dictionary<string, LegMount[]>",
        "        {",
    ]
    for body, legs_ in RIG.items():
        items = ", ".join(f'new LegMount("{n}", {u(b)}, {u(t)})' for n, b, t in legs_)
        lines.append(f'            {{ "{body}", new[] {{ {items} }} }},')
    lines += [
        "        };",
        "",
        f"        public static readonly Vector3 GrasshopperHip = {u(GRASSHOPPER_HIP)};",
        f"        public static readonly Vector3 MantisShoulder = {u(MANTIS_SHOULDER)};",
        "    }",
        "}",
        "",
    ]
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("wrote", path)


def render_portraits(objs, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_object_outline = True
    scene.display.shading.object_outline_color = (0.18, 0.12, 0.1)
    scene.display.shading.show_specular_highlight = True
    scene.render.film_transparent = True
    scene.render.resolution_x = 256
    scene.render.resolution_y = 256
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    cam_data = bpy.data.cameras.new("PortraitCam")
    cam = bpy.data.objects.new("PortraitCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.lens = 70
    for cid, names in CREATURES:
        names = list(names) + portrait_leg_names(objs, cid)
        if cid == "grasshopper":
            names += ["Grasshopper_Hind_R", "Grasshopper_Hind_L"]
        for o in scene.objects:
            if o.type == "MESH":
                o.hide_render = o.name not in names
        parts = [objs[n] for n in names if n in objs]
        pts = []
        for o in parts:
            for c in o.bound_box:
                pts.append(o.matrix_world @ Vector(c))
        mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        center = (mn + mx) / 2
        size = (mx - mn).length
        d = Vector((0.9, 1.25, 0.75)).normalized()
        cam.location = center + d * size * 1.55
        cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam_data.clip_start = size * 0.01
        cam_data.clip_end = size * 20
        scene.render.filepath = os.path.join(out_dir, cid + ".png")
        bpy.ops.render.render(write_still=True)
        print("portrait", cid)


def main():
    fbx_dir, png_dir, blend_out = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    walkers = [
        (make_ladybug, "Ladybug", None), (make_ant, "Ant", None), (make_beetle, "Beetle", None),
        (make_water_strider, "WaterStrider", None), (make_crab, "Crab", None), (make_grasshopper, "Grasshopper", None),
        (make_otoshibumi, "Otoshibumi", None), (make_spider, "Spider", spider_sneaker_variant), (make_mantis, "Mantis", None),
    ]
    makers = [
        make_snail, make_pillbug, make_butterfly_body, make_butterfly_wing,
        make_dragonfly_body, make_dragonfly_wing, make_frog, make_river_snail,
        make_firefly_body, make_firefly_glow,
        make_cradle, make_anthill, make_grasshopper_hind, make_mantis_arm, make_ant_helmet, make_crumb, make_pillbug_ball,
        lambda: make_bird_body("Sparrow_Body", 5.5, False), lambda: make_bird_wing("Sparrow_Wing", 5.5, False),
        lambda: make_bird_body("Crow_Body", 18.0, True), lambda: make_bird_wing("Crow_Wing", 18.0, True),
        lambda: make_river_stone("RiverStone_A", 1, (1.6, 1.2, 0.55), (hexc("#8d96a3"), hexc("#b7b2a5"))),
        lambda: make_river_stone("RiverStone_B", 2, (1.3, 1.1, 0.7), (hexc("#a99a84"), hexc("#d2c4a8"))),
        lambda: make_river_stone("RiverStone_C", 3, (1.9, 1.5, 0.45), (hexc("#7d8a86"), hexc("#a6b0a4"))),
        make_horsetail, make_iris, make_root_arch,
    ]
    objs = {}
    for mk, name, variant in walkers:
        for ob in split_legs(mk, name, variant):
            kit.export_fbx(ob, fbx_dir)
            objs[ob.name] = ob
            print("exported", ob.name, len(ob.data.vertices), "verts")
    for mk in makers:
        ob = mk()
        kit.export_fbx(ob, fbx_dir)
        objs[ob.name] = ob
        print("exported", ob.name, len(ob.data.vertices), "verts")
    for cname in COLLIDER_PROFILES:
        ob = make_collider(cname)
        kit.export_fbx(ob, fbx_dir)
        ob.hide_render = True
        ob.hide_set(True)
        print("exported", ob.name, len(ob.data.vertices), "verts")
    here = os.path.dirname(os.path.abspath(__file__))
    write_rig(os.path.join(here, "..", "..", "unity", "Assets", "Scripts", "Gameplay", "CreatureRig.g.cs"))
    pose_portrait_copies(objs)
    render_portraits(objs, png_dir)
    x = 0.0
    for name, ob in objs.items():
        if name.startswith(("Butterfly_Wing_", "Dragonfly_Wing_", "Sparrow_Wing_", "Crow_Wing_", "Cradle_Portrait", "Mantis_Arm_",
                            "Grasshopper_Hind_", "Crumb_Portrait")) or "_Leg" in name:
            continue
        w = max(ob.dimensions.x, 0.5)
        ob.location.x += x
        x += w + 1.0
    for o in bpy.context.scene.objects:
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=blend_out, compress=True)
    print("saved", blend_out)


if __name__ == "__main__":
    main()
