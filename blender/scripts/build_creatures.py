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


# ヘルメットのふち（目のてっぺん z = 0.12 より上。目がヘルメットをつきぬけない）
HELMET_RIM = Vector((0.0, 0.112, 0.126))


def make_ant_helmet():
    """工事現場のヘルメット（アリの頭にのせる。アリと同じ座標）。
    頭のてっぺんにかぶせる半球で、ふちとつばは目より上にある"""
    mb = MB()
    yellow = hexc("#f6c21c")
    shade = hexc("#e0a810")
    stripe = hexc("#ffd84a")
    c = HELMET_RIM
    rx, ry, h = 0.058, 0.064, 0.044
    seg = 28

    def dome_col(zf, x):
        if abs(x) < 0.008 and zf > 0.35:
            return stripe   # まんなかのすじ
        return mixc(shade, yellow, sstep(0.0, 0.35, zf))
    # ドーム（上半分だけ）：ふちから上の極へ
    prof = [(1.0, 0.0), (0.985, 0.2), (0.93, 0.42), (0.82, 0.62), (0.64, 0.8), (0.38, 0.94), (0.0, 1.0)]
    rings = []
    for rf, zf in prof:
        if rf <= 1e-6:
            p = c + Vector((0, 0, h))
            rings.append([mb.v(p, dome_col(zf, 0.0))])
            continue
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            p = c + Vector((math.cos(a) * rx * rf, math.sin(a) * ry * rf, h * zf))
            ring.append(mb.v(p, dome_col(zf, p.x)))
        rings.append(ring)
    connect_rings(mb, rings)
    # まんなかのすじ（少しもり上がった帯）
    pts = []
    for k in range(9):
        ang = math.radians(-62 + 124 * k / 8)
        pts.append(c + Vector((0.0, ry * math.sin(ang) * 1.0, h * math.cos(ang) * 1.0 + 0.0015)))
    tube(mb, pts, [0.0035] * len(pts), 8, lambda t, a, p, d: stripe)
    # つば：前が長い。上の面・下の面・外のへり（下の面は頭のまわりまでふさぐ）
    rim = []
    for j in range(seg):
        a = TAU * j / seg
        f = 1.0 + 0.4 * max(0.0, math.sin(a)) ** 1.5
        rim.append((math.cos(a) * rx, math.sin(a) * ry, f))
    top = [mb.v(c + Vector((x * 1.18 * f, y * 1.18 * f, 0.0)), yellow) for x, y, f in rim]
    bot = [mb.v(c + Vector((x * 1.18 * f, y * 1.18 * f, -0.004)), shade) for x, y, f in rim]
    inner_top = [mb.v(c + Vector((x, y, 0.0005)), yellow) for x, y, f in rim]
    inner_bot = [mb.v(c + Vector((x * 0.5, y * 0.5, -0.004)), shade) for x, y, f in rim]
    n = len(rim)
    for j in range(n):
        k = (j + 1) % n
        mb.f(inner_top[j], inner_top[k], top[k], top[j])
        mb.f(top[j], top[k], bot[k], bot[j])
        mb.f(bot[j], bot[k], inner_bot[k], inner_bot[j])
    # 緑十字（安全）：ドームの前
    zf = 0.32
    cross = c + Vector((0.0, ry * 0.97 + 0.001, h * zf))
    for w, hh in ((0.012, 0.03), (0.03, 0.012)):
        uv_sphere(mb, cross, 1.0, lambda nn: hexc("#2fa04a"), seg=6, rings=4, scale=Vector((w * 0.5, 0.004, hh * 0.5)))
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


def loft(mb, pts, rx, rz, colfn, seg=24, cap0=True, cap1=True):
    """背骨 pts にそって、だ円の輪（横 rx・たて rz）をつないだ、なめらかな体。colfn(p, n, t) -> 色。
    はしは、とがった極でとじる"""
    n = len(pts)
    rings = []
    for i in range(n):
        p = pts[i]
        tng = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        # 背骨は左右対称の面（x = 0）にあるので、横の向きはいつも x（首が真上を向いても、輪がねじれない）
        side = Vector((1, 0, 0)) - tng * tng.x
        side.normalize()
        upv = side.cross(tng).normalized()
        if upv.z < 0 and abs(tng.z) < 0.99:
            upv = -upv
        t = i / (n - 1)
        ring = []
        for j in range(seg):
            ang = TAU * j / seg
            nrm = (side * math.cos(ang) * rz[i] + upv * math.sin(ang) * rx[i])
            q = p + side * math.cos(ang) * rx[i] + upv * math.sin(ang) * rz[i]
            nn = (side * math.cos(ang) / max(rx[i], 1e-4) + upv * math.sin(ang) / max(rz[i], 1e-4)).normalized()
            ring.append(mb.v(q, colfn(q, nn, t)))
        rings.append(ring)
    for i in range(n - 1):
        A, B = rings[i], rings[i + 1]
        for j in range(seg):
            k = (j + 1) % seg
            mb.f(A[j], A[k], B[k], B[j])
    for idx, cap in ((0, cap0), (n - 1, cap1)):
        if not cap:
            continue
        tng = (pts[min(idx + 1, n - 1)] - pts[max(idx - 1, 0)]).normalized() * (1 if idx else -1)
        tip = mb.v(pts[idx] + tng * min(rx[idx], rz[idx]) * 0.6, colfn(pts[idx], tng, idx / (n - 1)))
        ring = rings[idx]
        for j in range(seg):
            k = (j + 1) % seg
            if idx == 0:
                mb.f(ring[k], ring[j], tip)
            else:
                mb.f(ring[j], ring[k], tip)
    return rings


def bird_style(crow):
    """鳥の種類（True/False は、カラス/スズメ）"""
    if crow is True:
        return "crow"
    if crow is False:
        return "sparrow"
    return crow


# 鳥の体の背骨（尾の付け根 → 腰 → 胴 → 胸 → 首 → 頭 → 顔。y が前、z が上）と、そこでの幅・高さ（半分）
BIRD_SPINES = {
    "sparrow": ([(-0.24, 0.36), (-0.17, 0.33), (-0.08, 0.30), (0.02, 0.30), (0.11, 0.34), (0.17, 0.41), (0.205, 0.48),
                 (0.235, 0.535), (0.27, 0.555), (0.305, 0.55), (0.33, 0.535)],
                [0.07, 0.12, 0.165, 0.175, 0.16, 0.12, 0.105, 0.12, 0.125, 0.105, 0.06],
                [0.06, 0.11, 0.155, 0.165, 0.15, 0.11, 0.1, 0.115, 0.115, 0.095, 0.055]),
    "crow": ([(-0.25, 0.37), (-0.17, 0.335), (-0.07, 0.31), (0.03, 0.31), (0.12, 0.35), (0.18, 0.42), (0.215, 0.485),
              (0.245, 0.53), (0.28, 0.55), (0.315, 0.545), (0.34, 0.53)],
             [0.062, 0.106, 0.145, 0.154, 0.141, 0.106, 0.092, 0.106, 0.11, 0.092, 0.053],
             [0.054, 0.099, 0.14, 0.149, 0.135, 0.099, 0.09, 0.104, 0.104, 0.086, 0.05]),
    # ハト：胸が厚く、頭は小さい
    "pigeon": ([(-0.26, 0.36), (-0.18, 0.33), (-0.08, 0.30), (0.03, 0.31), (0.12, 0.37), (0.17, 0.44), (0.2, 0.5),
                (0.225, 0.545), (0.255, 0.565), (0.285, 0.56), (0.305, 0.55)],
               [0.06, 0.11, 0.15, 0.165, 0.16, 0.115, 0.085, 0.085, 0.09, 0.075, 0.045],
               [0.055, 0.1, 0.15, 0.17, 0.155, 0.105, 0.085, 0.085, 0.085, 0.07, 0.04]),
    # ライチョウ：まるまるとした体に、小さな頭と短い尾
    "raichou": ([(-0.24, 0.33), (-0.17, 0.31), (-0.07, 0.29), (0.03, 0.3), (0.12, 0.35), (0.17, 0.42), (0.2, 0.48),
                 (0.225, 0.52), (0.255, 0.54), (0.285, 0.535), (0.305, 0.525)],
                [0.07, 0.13, 0.175, 0.185, 0.17, 0.12, 0.095, 0.095, 0.1, 0.08, 0.05],
                [0.06, 0.12, 0.17, 0.18, 0.165, 0.11, 0.09, 0.09, 0.095, 0.075, 0.045]),
    # フラミンゴ：高い脚の上の体から、長い S 字の首がのびる
    "flamingo": ([(-0.28, 1.02), (-0.2, 0.99), (-0.08, 0.97), (0.06, 1.0), (0.14, 1.08), (0.15, 1.25), (0.1, 1.42),
                  (0.08, 1.58), (0.12, 1.72), (0.18, 1.8), (0.24, 1.83), (0.29, 1.82)],
                 [0.06, 0.12, 0.16, 0.16, 0.1, 0.045, 0.042, 0.042, 0.045, 0.06, 0.055, 0.035],
                 [0.06, 0.12, 0.15, 0.15, 0.09, 0.045, 0.042, 0.042, 0.045, 0.06, 0.05, 0.03]),
}
BIRD_HEAD = {"sparrow": 8, "crow": 8, "pigeon": 8, "flamingo": 9, "raichou": 8}   # 頭のまん中の背骨の番号


def bird_sections(crow):
    spine, w, h = BIRD_SPINES[bird_style(crow)]
    return spine, w, h


def bird_side_x(crow, y, z):
    """体の横の表面の x（高さ z・前後 y の所）。体の外なら、いちばん近い所"""
    spine, w, h = bird_sections(crow)
    # 胴の部分だけ（首から先は見ない）
    n = 6
    ys = [p[0] for p in spine[:n]]
    if y <= ys[0]:
        i, t = 0, 0.0
    elif y >= ys[-1]:
        i, t = n - 2, 1.0
    else:
        i = max(k for k in range(n - 1) if ys[k] <= y)
        t = (y - ys[i]) / max(1e-4, ys[i + 1] - ys[i])
    zc = lerp(spine[i][1], spine[i + 1][1], t)
    ww = lerp(w[i], w[i + 1], t)
    hh = lerp(h[i], h[i + 1], t)
    k = max(0.0, 1.0 - ((z - zc) / max(hh, 1e-4)) ** 2)
    return ww * math.sqrt(k)


BIRD_COLORS = {
    "sparrow": dict(back="#8b5a36", belly="#e8dfcf", cap="#7a3a22", leg="#c09070", beak="#3a3634"),
    "crow": dict(back="#17171d", belly="#20202a", cap="#17171d", leg="#2a2a2e", beak="#26262c"),
    "pigeon": dict(back="#aab0bd", belly="#c6cad3", cap="#8f95a3", leg="#d97a78", beak="#3a3a40"),
    "flamingo": dict(back="#f39aa6", belly="#f7b6bd", cap="#f39aa6", leg="#e98a95", beak="#f3d6d0"),
    "raichou": dict(back="#5a4a3c", belly="#f6f4ee", cap="#3e322a", leg="#f6f4ee", beak="#2a2622"),
}


def make_bird_body(name, L, crow):
    """スズメ・カラス・ハト・フラミンゴの体：尾の付け根から胴・胸・首・頭まで、ひとつながりのなめらかな体（首が胴とはなれない）。
    頭の模様（スズメは茶色のぼうし、白いほおに黒い点、黒いのど。ハトは首が緑と紫に光る）、くちばし・目・尾羽・足"""
    style = bird_style(crow)
    mb = MB()
    pal = BIRD_COLORS[style]
    back, belly, cap = hexc(pal["back"]), hexc(pal["belly"]), hexc(pal["cap"])
    sheen = hexc("#3b4470")
    spine, w, h = bird_sections(style)
    pts = [Vector((0, y, z)) for y, z in spine]
    hi = BIRD_HEAD[style]
    head_c = pts[hi]

    def col(p, n, t):
        if style == "crow":
            return mixc(back, sheen, sstep(0.3, 0.9, n.z) * 0.55 + 0.1 * math.sin(p.y * 40))
        if style == "flamingo":
            c = mixc(belly, back, sstep(-0.3, 0.6, n.z))
            return mixc(c, hexc("#ff7f8f"), 0.25 * sstep(0.4, 0.9, t))
        if style == "raichou":
            rel = p - head_c
            if t > 0.62:
                # 夏の頭は黒っぽく、目の上に赤い「とさか」
                if 0.22 < n.z < 0.8 and abs(n.x) > 0.45 and -0.005 < rel.y < 0.055:
                    return hexc("#e2453a")
                return mixc(cap, back, 0.3 + 0.3 * noise.noise(p * 40))
            if n.z < -0.35 or (t < 0.25 and n.z < 0.1):
                return belly                                      # 白いおなか
            c = mixc(back, hexc("#8a7660"), 0.5 + 0.5 * noise.noise(p * 30))
            if noise.noise(p * 55) > 0.35:
                c = mixc(c, hexc("#efe8dc"), 0.7)                 # 白い点のまじった、せなかのもよう
            return c
        if style == "pigeon":
            if t > 0.72:
                return cap                                        # 頭は少し濃い灰色
            if t > 0.48:
                # 首：見る向きで、緑と紫に光る
                return mixc(hexc("#7fbf92"), hexc("#ab84bd"), 0.5 + 0.5 * math.sin(n.x * 4 + n.z * 3))
            c = mixc(belly, back, sstep(-0.2, 0.4, n.z))
            return c
        if t > 0.62:   # スズメの頭
            rel = p - head_c
            if n.z > 0.5 and rel.y < 0.05:
                return cap                       # 茶色のぼうし
            if n.y > 0.3 and n.z < -0.2:
                return hexc("#1e1a18")           # 黒いのど
            if abs(n.x) > 0.5 and -0.45 < n.z < 0.5:
                if abs(n.x) > 0.75 and -0.15 < n.z < 0.2 and rel.y < 0.0:
                    return hexc("#1e1a18")       # ほおの黒い点
                return hexc("#f6f4ee")           # 白いほお
            return mixc(cap, belly, 0.35)
        if t > 0.5 and n.y > 0.2 and n.z < 0.0:
            return hexc("#1e1a18") if t > 0.55 else belly   # のどの黒が、胸の上まで
        c = mixc(belly, back, sstep(-0.15, 0.3, n.z))
        if n.z > 0.25 and abs(math.sin(p.x * 30 + p.y * 9)) > 0.8:
            c = hexc("#3e2618")   # 背中のしま
        return c
    loft(mb, pts, w, h, col, seg=28)
    # くちばし
    beak_col = hexc(pal["beak"])
    base = pts[-1] + Vector((0, -0.015, -0.005))
    if style == "flamingo":
        # 太く、途中で下へ曲がる。先は黒い
        tip = base + Vector((0, 0.06, -0.09))
        tube(mb, [base, base + Vector((0, 0.05, -0.005)), base + Vector((0, 0.07, -0.04)), tip],
             [0.03, 0.026, 0.018, 0.0], 12, lambda t, a, p, d: mixc(beak_col, hexc("#1b1b1f"), sstep(0.55, 0.7, t)))
    else:
        blen = {"crow": 0.2, "sparrow": 0.085, "pigeon": 0.07, "raichou": 0.05}[style]
        brad = {"crow": 0.045, "sparrow": 0.032, "pigeon": 0.022, "raichou": 0.026}[style]
        tube(mb, [base, base + Vector((0, blen * 0.55, -0.004)), base + Vector((0, blen, -0.028 if style == "crow" else -0.018))],
             [brad, brad * 0.62, 0.0], 12, lambda t, a, p, d: beak_col)
        if style == "pigeon":
            uv_sphere(mb, base + Vector((0, 0.012, 0.012)), 0.018, lambda n: hexc("#f0ece6"), seg=8, rings=5)   # 白い鼻こぶ
    # 目
    er = {"crow": 0.026, "sparrow": 0.03, "pigeon": 0.014, "flamingo": 0.018, "raichou": 0.019}[style]
    ex = {"crow": 0.095, "sparrow": 0.095, "pigeon": 0.07, "flamingo": 0.045, "raichou": 0.08}[style]
    for sx in (-1, 1):
        if style == "pigeon":
            uv_sphere(mb, head_c + Vector((ex * sx * 0.97, 0.034, 0.022)), er * 1.35, lambda n: hexc("#e8762e"), seg=10, rings=6)   # だいだい色の目
        cute_eye(mb, head_c + Vector((ex * sx, 0.035 if style != "flamingo" else 0.02, 0.022)), er, (0.75 * sx, 0.7, 0.1), white=False)
    # 尾羽：羽を 1 枚ずつ、扇に重ねる
    nfe = 7
    tl = {"crow": 0.3, "sparrow": 0.24, "pigeon": 0.26, "flamingo": 0.12, "raichou": 0.13}[style]
    root_z = spine[0][1]
    for k in range(nfe):
        u = (k / (nfe - 1)) * 2 - 1
        root = Vector((u * 0.035, spine[0][0] + 0.02, root_z))
        tip = root + Vector((u * 0.07, -tl, -0.045 + 0.01 * abs(u)))
        mid = root.lerp(tip, 0.5) + Vector((0, 0, 0.01))
        if style == "crow":
            fc = mixc(back, hexc("#101015"), 0.3 + 0.4 * abs(u))
        elif style == "pigeon":
            fc = hexc("#2a2a30") if False else back
        elif style == "raichou":
            fc = hexc("#1e1a18")   # 尾は黒
        else:
            fc = mixc(back, hexc("#4a2e1c"), 0.3 + 0.4 * abs(u))
        ribbon(mb, [root, mid, tip], [0.045, 0.06, 0.04], [Vector((1, 0, 0))] * 3,
               lambda tt, v, p, fc=fc: mixc(fc, hexc("#2a2a30"), sstep(0.7, 0.9, tt)) if style == "pigeon" else fc,
               fold=0.12, normal_vecs=[Vector((0, 0, 1))] * 3)
    # 足
    leg = hexc(pal["leg"])
    if style == "flamingo":
        # 長い脚（ひざのように見えるのは、かかと。うしろへ曲がる）
        for sx in (-1, 1):
            hip = Vector((0.05 * sx, 0.02, 0.92))
            heel = Vector((0.055 * sx, -0.04, 0.48))
            ft = Vector((0.055 * sx, 0.03, 0.01))
            tube(mb, [hip, heel, ft], [0.022, 0.018, 0.015], 8, lambda t, a, p, d: leg)
            uv_sphere(mb, heel, 0.026, lambda n: hexc("#d9707e"), seg=8, rings=5)
            for ang in (-0.45, 0.0, 0.45):
                tip = ft + Vector((math.sin(ang) * 0.06, math.cos(ang) * 0.075, -0.006))
                tube(mb, [ft, tip], [0.012, 0.006], 5, lambda t, a, p, d: leg)
    else:
        lr = 1.8 if style == "raichou" else 1.0   # ライチョウの足は、白い羽毛でふわふわ（雪の上を歩くため）
        for sx in (-1, 1):
            hip = Vector((0.055 * sx, 0.02, 0.2))
            knee = Vector((0.06 * sx, 0.0, 0.1))
            ft = Vector((0.06 * sx, 0.035, 0.008))
            tube(mb, [hip, knee, ft], [0.016 * lr, 0.013 * lr, 0.011 * lr], 7, lambda t, a, p, d: leg)
            for ang in (-0.45, 0.0, 0.45):
                tip = ft + Vector((math.sin(ang) * 0.065, math.cos(ang) * 0.075, -0.006))
                tube(mb, [ft, ft.lerp(tip, 0.6) + Vector((0, 0, 0.004)), tip], [0.009, 0.007, 0.004], 5, lambda t, a, p, d: leg)
            tube(mb, [ft, ft + Vector((0, -0.055, -0.004))], [0.009, 0.005], 5, lambda t, a, p, d: leg)
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    return build(mb, name, smooth=True, sharp_deg=75)


def bird_wing_colors(style):
    if style == "crow":
        return hexc("#1d1d26"), hexc("#141419"), hexc("#0b0b0f"), hexc("#3b4470")
    if style == "pigeon":
        return hexc("#b4bac6"), hexc("#7a808d"), hexc("#2a2a30"), hexc("#d0d4dc")
    if style == "flamingo":
        return hexc("#f6a3ae"), hexc("#1b1b20"), hexc("#0e0e12"), hexc("#ff8091")
    if style == "raichou":
        return hexc("#6a5646"), hexc("#f6f4ee"), hexc("#d8d2c8"), hexc("#8a7660")   # 風切り羽は白
    return hexc("#a2714a"), hexc("#5a3a24"), hexc("#2e2018"), hexc("#c8a07a")


def make_bird_wing_folded(name, L, crow):
    """地上で体の横にたたんだ翼（右がわ。体と同じ座標）。肩から尾のほうへ、体の表面にそってのび、
    風切り羽の先は尾の上で重なる。雨おおい羽（明るい）・帯（スズメの白・ハトの黒）・1 枚ずつの風切り羽のすじ"""
    style = bird_style(crow)
    mb = MB()
    cov, flight, edge, sheen = bird_wing_colors(style)
    spine, _, _ = bird_sections(style)
    body_z = spine[3][1]
    y0, y1 = spine[4][0] + 0.03, spine[0][0] - 0.1
    nu, nv = 26, 8
    rows = []
    for i in range(nu + 1):
        u = i / nu
        y = lerp(y0, y1, u)
        top = body_z + lerp(0.17, 0.08, u) + 0.02 * math.sin(math.pi * u)
        depth = 0.19 * math.sin(math.pi * min(1.0, 0.12 + 0.95 * u)) ** 0.7 + 0.015
        row = []
        for j in range(nv + 1):
            v = j / nv
            z = top - depth * v
            x = bird_side_x(style, y, z)
            x = max(x, 0.04 + 0.03 * (1 - u)) + 0.012 + 0.012 * (1 - v)
            c = mixc(cov, sheen, 0.25 * (1 - v) * (1 - u if style == "crow" else 1.0))
            fl = sstep(0.3, 0.5, u + v * 0.35)
            c = mixc(c, flight, fl)
            if fl > 0.5 and (i % 3) == 0:
                c = mixc(c, edge, 0.5)
            if style == "sparrow" and 0.12 < u < 0.42 and 0.45 < v < 0.6:
                c = hexc("#f2ece0")   # スズメの白い帯
            if style == "pigeon" and 0.25 < u < 0.5 and (0.35 < v < 0.45 or 0.6 < v < 0.7):
                c = hexc("#2a2a30")   # ハトの黒い 2 本の帯
            if v > 0.85:
                c = mixc(c, edge, 0.5)
            row.append(mb.v((x, y, z), c))
        rows.append(row)
    for i in range(nu):
        for j in range(nv):
            mb.f(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    return build(mb, name, solidify=0.01 * L)


def make_bird_wing(name, L, crow):
    """鳥の翼：付け根が広く、先がとがる。肩から外（+X）へのび、うしろ（-Y）が風切り羽。
    雨おおい羽（明るい帯）と、1 枚ずつの風切り羽。カラスは、翼の先の羽が指のように分かれる"""
    style = bird_style(crow)
    crowish = style in ("crow", "flamingo")
    mb = MB()
    span = 0.5 if crowish else 0.42
    cov, flight, edge, sheen = bird_wing_colors(style)
    rows = 6
    cols = 10
    grid = []
    for i in range(cols + 1):
        x = span * 0.62 * i / cols
        lead = 0.0 - 0.02 * (x / span) ** 2
        chord = (0.15 if crowish else 0.13) * (1.0 - 0.45 * (x / (span * 0.62)) ** 1.5)
        row = []
        for j in range(rows + 1):
            v = j / rows
            y = lead - chord * v
            z = 0.025 * math.sin(math.pi * v) * (1 - x / span)
            if style == "crow":
                c = mixc(cov, sheen, 0.3 * math.sin(i * 1.3) ** 2)
            else:
                c = mixc(cov, sheen, 0.25 * (1 - v))
            if style == "sparrow" and 0.55 < v < 0.75:
                c = hexc("#f2ece0")
            if style == "pigeon" and (0.45 < v < 0.55 or 0.7 < v < 0.8) and i < cols * 0.7:
                c = hexc("#2a2a30")
            row.append(mb.v((x, y, z), c))
        grid.append(row)
    for i in range(cols):
        for j in range(rows):
            mb.f(grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
    nf = 12 if crowish else 10
    for k in range(nf):
        u = k / (nf - 1)
        x0 = span * 0.6 * u
        chord0 = (0.15 if crowish else 0.13) * (1.0 - 0.45 * min(1.0, u) ** 1.5)
        root = Vector((x0, -chord0 * 0.55, 0.012))
        ang = math.radians(-20 + 75 * u ** 1.4)
        flen = (0.17 if crowish else 0.15) + (0.2 if crowish else 0.16) * u ** 1.2
        d = Vector((math.sin(ang), -math.cos(ang), 0.0))
        tip = root + d * flen
        mid = root.lerp(tip, 0.55)
        wid = 0.05 if crowish else 0.045
        if crowish and u > 0.75:
            wid *= 0.7
        fc = mixc(flight, edge, 0.4 * u)
        if style == "flamingo" and u < 0.35:
            fc = mixc(cov, hexc("#ff7f8f"), 0.4)   # 内がわの羽はピンク、外がわの羽は黒
        ribbon(mb, [root, mid, tip], [wid, wid * 1.05, wid * 0.45], [Vector((math.cos(ang), math.sin(ang), 0))] * 3,
               lambda tt, v, p, fc=fc: mixc(fc, edge, sstep(0.75, 1.0, tt) * 0.6), fold=0.08,
               normal_vecs=[Vector((0, 0, 1))] * 3)
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    return build(mb, name, solidify=0.012 * L)

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
# 公園のいきもの（カミキリムシ・クワガタ・カメムシ・トカゲ・モンシロチョウ）
# ---------------------------------------------------------------------------
def make_kamikiri():
    """ゴマダラカミキリ：つやのある黒に白い点。体より長い、白黒のしまの触角"""
    mb = MB()
    black = hexc("#16161c")
    sheen = hexc("#3a3f58")
    spots = [Vector(v) for v in ((0.08, -0.05), (-0.08, -0.12), (0.1, -0.3), (-0.06, -0.38), (0.05, 0.08), (-0.1, 0.05), (0.03, -0.48))]

    def elytra(t, a, p):
        c = mixc(black, sheen, sstep(0.5, 1.0, math.sin(a)) * 0.6)
        if math.sin(a) > 0.2:
            for sp in spots:
                if (Vector((p.x, p.y)) - sp).length < 0.032:
                    return hexc("#f4f4ee")
        if abs(p.x) < 0.008 and math.sin(a) > 0:
            return hexc("#060608")
        return c
    seg_body(mb, -0.55, 0.26, 0.15, 0.12, elytra, rings=34, seg=22,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.08 + 0.05)) ** 0.3, math.sin(math.pi * t) ** 0.5), z0=0.12)
    # 前胸（横にとげ）と頭
    uv_sphere(mb, Vector((0, 0.32, 0.15)), 1.0, lambda n: mixc(black, sheen, sstep(0.3, 0.9, n.z) * 0.6), seg=16, rings=10,
              scale=Vector((0.1, 0.08, 0.075)))
    for sx in (-1, 1):
        tube(mb, [Vector((0.08 * sx, 0.32, 0.15)), Vector((0.15 * sx, 0.33, 0.16))], [0.02, 0.0], 6, lambda t, a, p, d: black)
    uv_sphere(mb, Vector((0, 0.44, 0.13)), 1.0, lambda n: black, seg=14, rings=9, scale=Vector((0.085, 0.075, 0.07)))
    for sx in (-1, 1):
        cute_eye(mb, (0.06 * sx, 0.47, 0.15), 0.024, (0.5 * sx, 1, 0.1), white=False)
        # 触角：体の 1.5 倍。白と黒のしま
        pts = [Vector((0.04 * sx, 0.48, 0.19)), Vector((0.2 * sx, 0.7, 0.38)), Vector((0.5 * sx, 0.62, 0.52)),
               Vector((0.85 * sx, 0.2, 0.48)), Vector((1.02 * sx, -0.35, 0.34))]
        from build_forest_kit import bezier
        curve = bezier(pts[0], pts[1], pts[3], pts[4], 22)
        tube(mb, curve, [0.016 - 0.01 * (i / 21) for i in range(22)], 5,
             lambda t, a, p, d: hexc("#f2f2ea") if (t * 11) % 1.0 < 0.38 else black)
    roots = []
    for y, dy in ((0.3, 0.1), (0.14, 0.0), (-0.02, -0.1)):
        for sx in (-1, 1):
            roots.append(((0.08 * sx, y, 0.09), (0.24 * sx, y + dy * 0.5, 0.15), (0.34 * sx, y + dy, 0.0)))
    legs(mb, roots, black, sheen, radius=0.018)
    return build(mb, "Kamikiri")


def make_kuwagata():
    """ノコギリクワガタ：赤みのある黒いつやつやの体と、のこぎりのような大あご"""
    mb = MB()
    shell = hexc("#2a160c")
    shine = hexc("#7a4426")

    def elytra(t, a, p):
        c = mixc(shell, shine, sstep(0.6, 1.0, math.sin(a)) * 0.55)
        if abs(p.x) < 0.01 and math.sin(a) > 0:
            c = hexc("#140904")
        return c
    seg_body(mb, -0.6, 0.12, 0.3, 0.2, elytra, rings=32, seg=26,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.05 + 0.08)) ** 0.35, math.sin(math.pi * t) ** 0.55), z0=0.14, flat=0.4)
    uv_sphere(mb, Vector((0, 0.26, 0.18)), 1.0, lambda n: mixc(shell, shine, sstep(0.3, 0.9, n.z) * 0.6), seg=20, rings=12,
              scale=Vector((0.27, 0.15, 0.11)))
    uv_sphere(mb, Vector((0, 0.45, 0.16)), 1.0, lambda n: mixc(shell, shine, sstep(0.4, 0.9, n.z) * 0.5), seg=18, rings=10,
              scale=Vector((0.25, 0.12, 0.08)))
    for sx in (-1, 1):
        # 大あご：前へのびて、先が内へまがる。内がわに歯
        jaw = [Vector((0.13 * sx, 0.52, 0.17)), Vector((0.2 * sx, 0.7, 0.2)), Vector((0.19 * sx, 0.9, 0.22)), Vector((0.07 * sx, 1.04, 0.22))]
        tube(mb, jaw, [0.055, 0.045, 0.035, 0.0], 9, lambda t, a, p, d: mixc(shell, shine, 0.3 + 0.4 * t))
        for k, t in enumerate((0.35, 0.55, 0.75)):
            i = int(t * 3)
            p = jaw[i].lerp(jaw[i + 1], t * 3 - i)
            tip = p + Vector((-0.06 * sx, 0.02, 0.0))
            tube(mb, [p, tip], [0.022, 0.0], 5, lambda tt, a, pp, d: shine)
        cute_eye(mb, (0.21 * sx, 0.48, 0.18), 0.03, (0.6 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.16 * sx, 0.52, 0.2)), Vector((0.3 * sx, 0.62, 0.26)), Vector((0.36 * sx, 0.72, 0.24))], [0.012, 0.01, 0.0], 5,
             lambda t, a, p, d: shell)
    roots = []
    for y in (0.28, 0.06, -0.2):
        for sx in (-1, 1):
            roots.append(((0.2 * sx, y, 0.13), (0.4 * sx, y + 0.05, 0.17), (0.54 * sx, y + 0.07, 0.0)))
    legs(mb, roots, shell, shine, radius=0.03)
    return build(mb, "Kuwagata")


def make_kamemushi():
    """アオクサカメムシ：たての形の、平たい緑の体"""
    mb = MB()
    green = hexc("#6fbf3a")
    light = hexc("#a8dc6a")

    def shield(t):
        # 前（t=1）のかたがいちばん広く、うしろ（t=0）は丸くせまい
        w = 0.55 + 0.5 * sstep(0.0, 0.7, t) - 0.45 * sstep(0.82, 1.0, t)
        return (max(0.05, w * math.sin(math.pi * min(1.0, t * 1.02 + 0.02)) ** 0.25), math.sin(math.pi * t) ** 0.6)

    def col(t, a, p):
        c = mixc(green, light, sstep(0.6, 1.0, math.sin(a)) * 0.35)
        # 小さな三角（背中のまん中）
        if math.sin(a) > 0 and 0.35 < t < 0.75 and abs(p.x) < 0.09 * (t - 0.35) / 0.4:
            c = mixc(light, green, 0.3)
        return c
    seg_body(mb, -0.28, 0.2, 0.2, 0.07, col, rings=30, seg=24, prof=shield, z0=0.07, flat=0.2)
    uv_sphere(mb, Vector((0, 0.25, 0.07)), 1.0, lambda n: green, seg=12, rings=8, scale=Vector((0.08, 0.06, 0.04)))
    for sx in (-1, 1):
        cute_eye(mb, (0.06 * sx, 0.27, 0.085), 0.018, (0.5 * sx, 1, 0.1), white=False)
        pts = [Vector((0.04 * sx, 0.29, 0.08)), Vector((0.12 * sx, 0.4, 0.13)), Vector((0.17 * sx, 0.52, 0.12)), Vector((0.2 * sx, 0.62, 0.09))]
        tube(mb, pts, [0.01, 0.009, 0.008, 0.0], 5, lambda t, a, p, d: mixc(green, hexc("#c25a3a"), sstep(0.6, 1.0, t)))
    roots = []
    for y, dy in ((0.12, 0.06), (0.02, 0.0), (-0.08, -0.06)):
        for sx in (-1, 1):
            roots.append(((0.07 * sx, y, 0.04), (0.16 * sx, y + dy * 0.5, 0.07), (0.23 * sx, y + dy, 0.0)))
    legs(mb, roots, green, hexc("#4a8a2a"), radius=0.012)
    return build(mb, "Kamemushi")


def make_tokage():
    """ニホントカゲの子ども：こげ茶に 5 本のクリーム色の線、つやつやの青いしっぽ"""
    mb = MB()
    brown = hexc("#3a2c22")
    stripe = hexc("#e6d6a4")
    blue = hexc("#3f7fe0")
    belly = hexc("#d8cfb8")
    pts = [Vector((0, -2.5, 0.06)), Vector((0, -1.9, 0.08)), Vector((0, -1.2, 0.12)), Vector((0, -0.6, 0.17)), Vector((0, -0.2, 0.2)),
           Vector((0, 0.2, 0.21)), Vector((0, 0.5, 0.21)), Vector((0, 0.68, 0.22)), Vector((0, 0.85, 0.24)), Vector((0, 1.02, 0.22)), Vector((0, 1.12, 0.2))]
    radii = [0.0, 0.05, 0.09, 0.15, 0.2, 0.22, 0.19, 0.15, 0.17, 0.13, 0.0]

    def col(t, a, p, d):
        up = d.z
        if up < -0.35:
            return belly
        if t < 0.42:
            # しっぽ：つけ根から先へ、青く光る
            k = sstep(0.42, 0.3, t)
            return mixc(mixc(brown, blue, k), hexc("#7ab6ff"), sstep(0.5, 1.0, up) * 0.4 * k)
        # 5 本の線（背中のまん中と、両がわ 2 本ずつ）
        for c0 in (0.0, 0.42, -0.42, 0.78, -0.78):
            if abs(d.x - c0 * max(up, 0.2)) < 0.07 and up > 0.0:
                return stripe
        return mixc(brown, hexc("#5a4434"), sstep(0.0, 0.8, up) * 0.4)
    tube(mb, pts, radii, 18, col, oval=(1.25, 0.75))
    for sx in (-1, 1):
        cute_eye(mb, (0.11 * sx, 0.9, 0.29), 0.04, (0.7 * sx, 0.6, 0.15), white=False)
    # 口のまわり（にっこり）
    tube(mb, [Vector((-0.07, 1.05, 0.18)), Vector((0, 1.09, 0.17)), Vector((0.07, 1.05, 0.18))], [0.01, 0.01, 0.01], 4, lambda t, a, p, d: hexc("#2a1e16"))
    roots = []
    for (y, ky, ty) in ((0.42, 0.55, 0.7), (-0.42, -0.5, -0.32)):
        for sx in (-1, 1):
            roots.append(((0.16 * sx, y, 0.18), (0.38 * sx, ky, 0.2), (0.5 * sx, ty, 0.0)))
    legs(mb, roots, brown, hexc("#2a201a"), radius=0.05)
    return build(mb, "Tokage")


def make_monshiro_body():
    mb = MB()
    tube(mb, [Vector((0, -0.26, 0.0)), Vector((0, -0.1, 0.0)), Vector((0, 0.08, 0.0)), Vector((0, 0.12, 0.0))],
         [0.0, 0.032, 0.038, 0.03], 10, lambda t, a, p, d: mixc(hexc("#3a3a40"), hexc("#d8d8d0"), sstep(0.0, 1.0, d.z) * 0.6))
    uv_sphere(mb, Vector((0, 0.15, 0.0)), 0.042, lambda n: hexc("#4a4a50"), seg=12, rings=8)
    for sx in (-1, 1):
        cute_eye(mb, (0.03 * sx, 0.18, 0.015), 0.017, (0.4 * sx, 1, 0), white=False)
        a0 = Vector((0.015 * sx, 0.18, 0.03))
        a1 = Vector((0.11 * sx, 0.4, 0.1))
        tube(mb, [a0, a0.lerp(a1, 0.5), a1], [0.005, 0.005, 0.004], 5, lambda t, a, p, d: hexc("#2a2a30"))
        uv_sphere(mb, a1, 0.016, lambda n: hexc("#f0f0e8"), seg=8, rings=5)
    return build(mb, "Monshiro_Body")


def make_monshiro_wing():
    """モンシロチョウのはね：白に、前ばねの先が黒く、黒い点が 2 つ"""
    mb = MB()

    def rfn(th):
        fore = 0.86 * math.exp(-((th - 0.95) / 0.58) ** 2)
        hind = 0.64 * math.exp(-((th - 2.25) / 0.52) ** 2)
        return max(0.12, fore, hind)

    def cf(rho, th, p):
        c = mixc(hexc("#e9ecd8"), hexc("#fbfbf4"), sstep(0.1, 0.6, rho))
        if th > 1.75:
            c = mixc(c, hexc("#f6efc2"), 0.4 * sstep(0.2, 0.0, abs(rho - 0.3)))   # うしろばねは、少しクリーム色
        if th < 1.3 and rho > 0.78:
            c = mixc(c, hexc("#3a3a40"), 0.85)                                    # 前ばねの先は黒
        for (r0, t0) in ((0.55, 1.05), (0.42, 1.55)):
            if math.hypot(rho - r0, (th - t0) * 0.5) < 0.07:
                c = hexc("#3a3a40")
        return c
    polar_sheet(mb, rfn, cf, n_ang=60, n_rad=8, a0=0.05, a1=3.05, zfn=lambda rho, th: 0.03 * rho * rho, full=False)
    return build(mb, "Monshiro_Wing")


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
    ("kamikiri", ["Kamikiri"]),
    ("kuwagata", ["Kuwagata"]),
    ("kamemushi", ["Kamemushi"]),
    ("tokage", ["Tokage"]),
    ("monshiro", ["Monshiro_Body", "Monshiro_Wing", "Monshiro_Wing_L"]),
    ("koumori", ["Koumori_Body", "Koumori_Wing", "Koumori_Wing_L"]),
    ("mogura", ["Mogura"]),
    ("okera", ["Okera"]),
    ("nanafushi", ["Nanafushi"]),
    ("gengorou", ["Gengorou"]),
    ("hanakamakiri", ["Hanakamakiri", "Hanakamakiri_Arm_R", "Hanakamakiri_Arm_L"]),
    ("hato", ["Hato_Body", "Hato_Wing_R", "Hato_Wing_L"]),
    ("ant_helmet", ["Ant", "Ant_Helmet"]),
    ("spider_sneaker", ["Spider"]),
    ("kameleon", ["Kameleon"]),
    ("herakuresu", ["Herakuresu"]),
    ("flamingo", ["Flamingo_Body", "Flamingo_Wing_R", "Flamingo_Wing_L"]),
    ("harinezumi", ["Harinezumi"]),
    ("raichou", ["Raichou_Body", "Raichou_Wing_R", "Raichou_Wing_L"]),
    ("risu", ["Risu"]),
    ("okojo", ["Okojo", "Okojo_Rocks"]),
    ("nakiusagi", ["Nakiusagi"]),
    ("sanshouuo", ["Sanshouuo"]),
    ("asagimadara", ["Asagimadara_Body", "Asagimadara_Wing", "Asagimadara_Wing_L"]),
    ("maruhanabachi", ["Maruhanabachi", "Maruhanabachi_Wing_R", "Maruhanabachi_Wing_L"]),
    ("oniyanma", ["Oniyanma_Body", "Oniyanma_Wing_FR", "Oniyanma_Wing_FL", "Oniyanma_Wing_BR", "Oniyanma_Wing_BL"]),
    ("higurashi", ["Higurashi"]),
    ("maimaikaburi", ["Maimaikaburi"]),
]

# 図鑑の絵で脚をつける体（脚メッシュのコピーを付け根に置く）。スニーカーグモだけ脚の種類がちがう
PORTRAIT_LEGS = {
    "ant": "Ant", "ladybug": "Ladybug", "beetle": "Beetle", "waterstrider": "WaterStrider", "crab": "Crab",
    "grasshopper": "Grasshopper", "otoshibumi": "Otoshibumi", "spider": "Spider", "mantis": "Mantis",
    "ant_helmet": "Ant", "spider_sneaker": "Spider",
    "kamikiri": "Kamikiri", "kuwagata": "Kuwagata", "kamemushi": "Kamemushi", "tokage": "Tokage",
    "okera": "Okera", "nanafushi": "Nanafushi", "hanakamakiri": "Hanakamakiri", "kameleon": "Kameleon", "herakuresu": "Herakuresu",
    "risu": "Risu", "sanshouuo": "Sanshouuo", "higurashi": "Higurashi", "maimaikaburi": "Maimaikaburi",
}


def pose_portrait_copies(objs):
    """図鑑の絵のために、羽を広げた姿勢のコピーを作る"""
    kw = objs.get("Koumori_Wing")
    if kw:
        l = kw.copy()
        l.data = kw.data
        l.name = "Koumori_Wing_L"
        bpy.context.scene.collection.objects.link(l)
        l.scale = (-1, 1, 1)
        kw.location = (0.25, 0.3, 0.05)
        l.location = (-0.25, 0.3, 0.05)
        kw.rotation_euler = (0, -0.25, 0)
        l.rotation_euler = (0, 0.25, 0)
        objs["Koumori_Wing_L"] = l
    mw = objs.get("Monshiro_Wing")
    if mw:
        l = mw.copy()
        l.data = mw.data
        l.name = "Monshiro_Wing_L"
        bpy.context.scene.collection.objects.link(l)
        l.scale = (-1, 1, 1)
        mw.rotation_euler = (0, -0.35, 0)
        l.rotation_euler = (0, 0.35, 0)
        objs["Monshiro_Wing_L"] = l
    for nm in ("Asagimadara_Wing",):
        aw = objs.get(nm)
        if aw:
            l = aw.copy()
            l.data = aw.data
            l.name = nm + "_L"
            bpy.context.scene.collection.objects.link(l)
            l.scale = (-1, 1, 1)
            aw.rotation_euler = (0, -0.3, 0)
            l.rotation_euler = (0, 0.3, 0)
            objs[nm + "_L"] = l
    bw = objs.get("Maruhanabachi_Wing")
    if bw:
        for nm, sx in (("Maruhanabachi_Wing_R", 1), ("Maruhanabachi_Wing_L", -1)):
            c = bw.copy()
            c.data = bw.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (0.12 * sx, 0.15, 0.6)
            c.rotation_euler = (0, -0.25 * sx, -0.5 * sx)
            objs[nm] = c
        bw.hide_render = True
    ow = objs.get("Oniyanma_Wing")
    if ow:
        for nm, (sx, y) in {"Oniyanma_Wing_FR": (1, 0.4), "Oniyanma_Wing_FL": (-1, 0.4), "Oniyanma_Wing_BR": (1, 0.1), "Oniyanma_Wing_BL": (-1, 0.1)}.items():
            c = ow.copy()
            c.data = ow.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (0.1 * sx, y, 0.2)
            c.rotation_euler = (0, 0, (0.12 if y > 0.2 else -0.2) * sx)
            objs[nm] = c
        ow.hide_render = True
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
    for bird, L in (("Sparrow", 5.5), ("Crow", 18.0), ("Hato", 13.0), ("Flamingo", 20.0), ("Raichou", 15.0)):
        w = objs.get(bird + "_WingFolded")
        if not w:
            continue
        # たたんだ羽（体と同じ座標で、体にそわせて作ってある）：左は鏡にうつす
        for nm, sx in ((bird + "_Wing_R", 1), (bird + "_Wing_L", -1)):
            c = w.copy()
            c.data = w.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            objs[nm] = c
        w.hide_render = True
        if objs.get(bird + "_Wing"):
            objs[bird + "_Wing"].hide_render = True
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
    harm = objs.get("Hanakamakiri_Arm")
    if harm:
        for nm, sx in (("Hanakamakiri_Arm_R", 1), ("Hanakamakiri_Arm_L", -1)):
            c = harm.copy()
            c.data = harm.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (HANA_SHOULDER.x * sx, HANA_SHOULDER.y, HANA_SHOULDER.z)
            objs[nm] = c
        harm.hide_render = True
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
        f"        public static readonly Vector3 HanaShoulder = {u(HANA_SHOULDER)};",
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
        cam.location = center + d * size * (2.0 if cid == "flamingo" else 1.55)
        cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam_data.clip_start = size * 0.01
        cam_data.clip_end = size * 20
        scene.render.filepath = os.path.join(out_dir, cid + ".png")
        bpy.ops.render.render(write_still=True)
        print("portrait", cid)


# ---------------------------------------------------------------------------
# 新しいいきもの：コウモリ・モグラ・オケラ・ナナフシ・ゲンゴロウ・ハナカマキリ（ハトは鳥の作り方）
# レア：カメレオン・ヘラクレスオオカブト・ハリネズミ（フラミンゴは鳥の作り方）
# 大きさは本物と同じ（1 単位 = 2.5cm）
# ---------------------------------------------------------------------------
def make_koumori_body():
    """アブラコウモリ：茶色の毛の体（約 4.5cm）、大きな耳、小さな目、豚のような鼻"""
    mb = MB()
    fur = hexc("#5a4436")
    dark = hexc("#3a2c24")

    def col(n):
        return mixc(dark, fur, sstep(-0.4, 0.6, n.z))
    uv_sphere(mb, Vector((0, 0, 0)), 1.0, col, seg=22, rings=14, scale=Vector((0.34, 0.75, 0.32)))
    hc = Vector((0, 0.82, 0.08))
    uv_sphere(mb, hc, 0.3, col, seg=20, rings=12)
    uv_sphere(mb, hc + Vector((0, 0.26, -0.05)), 1.0, lambda n: hexc("#7a5a4a"), seg=12, rings=8, scale=Vector((0.12, 0.1, 0.09)))
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.13 * sx, 0.22, 0.08)), 0.055, (0.4 * sx, 1, 0.1), white=False)
        ear0 = hc + Vector((0.16 * sx, 0.02, 0.2))
        tube(mb, [ear0, ear0 + Vector((0.12 * sx, -0.02, 0.22)), ear0 + Vector((0.18 * sx, -0.06, 0.42))], [0.13, 0.1, 0.0], 10,
             lambda t, a, p, d: mixc(hexc("#4a362a"), hexc("#9a7262"), sstep(0.2, 0.9, t) * 0.5), oval=(1.0, 0.45))
        # うしろ足（小さなかぎづめ）
        tube(mb, [Vector((0.15 * sx, -0.65, -0.05)), Vector((0.22 * sx, -0.85, -0.08))], [0.04, 0.02], 6, lambda t, a, p, d: dark)
    return build(mb, "Koumori_Body")


def make_koumori_wing():
    """右の翼（肩が原点、+X へのびる）：指の骨のあいだに、うすい皮の膜。うしろのへりは、指のあいだでへこむ"""
    mb = MB()
    mem = hexc("#4a3a34")
    vein = hexc("#2a1e1a")
    P0 = Vector((0, 0, 0))
    W = Vector((1.6, 0.25, 0.04))
    tips = [Vector((4.0, -0.05, 0.0)), Vector((3.7, -1.1, -0.02)), Vector((2.7, -1.9, -0.03)), Vector((1.4, -1.8, -0.02))]
    B = Vector((0.15, -1.1, 0.0))

    def scallop(a, b):
        m = a.lerp(b, 0.5)
        return m.lerp(W, 0.22)
    ring = [tips[0], scallop(tips[0], tips[1]), tips[1], scallop(tips[1], tips[2]), tips[2], scallop(tips[2], tips[3]), tips[3]]
    wv = mb.v(W, mem)
    vs = [mb.v(p, mixc(mem, hexc("#6a5048"), 0.3)) for p in ring]
    for i in range(len(vs) - 1):
        mb.f(wv, vs[i], vs[i + 1])
    p0 = mb.v(P0, mem)
    s4 = mb.v(tips[3].lerp(B, 0.5).lerp(W, 0.15), mem)
    bv = mb.v(B, mem)
    mb.f(p0, wv, vs[-1])
    mb.f(p0, vs[-1], s4)
    mb.f(p0, s4, bv)
    # 骨（腕と指）
    tube(mb, [P0, W], [0.06, 0.045], 6, lambda t, a, p, d: vein)
    for t_ in tips:
        tube(mb, [W, W.lerp(t_, 0.5) + Vector((0, 0, 0.01)), t_], [0.035, 0.025, 0.008], 5, lambda t, a, p, d: vein)
    uv_sphere(mb, W + Vector((0, 0.08, 0.02)), 0.05, lambda n: vein, seg=6, rings=4)   # 親指のかぎづめ
    return build(mb, "Koumori_Wing", solidify=0.02)


def make_mogura():
    """アズマモグラ（約 13cm）：ビロードのような灰黒色の毛、ピンクの鼻先、大きなシャベルのような前足、小さな目"""
    mb = MB()
    fur = hexc("#2c2a2e")
    sheen = hexc("#5a5866")
    pink = hexc("#e7a3a0")
    pts = [Vector((0, -2.7, 0.75)), Vector((0, -2.3, 0.9)), Vector((0, -1.4, 1.0)), Vector((0, -0.2, 1.02)), Vector((0, 0.9, 0.98)),
           Vector((0, 1.7, 0.9)), Vector((0, 2.25, 0.8)), Vector((0, 2.6, 0.7)), Vector((0, 2.85, 0.62))]
    radii = [0.0, 0.6, 0.98, 1.05, 1.0, 0.85, 0.55, 0.3, 0.0]

    def col(t, a, p, d):
        if t > 0.86:
            return pink
        return mixc(fur, sheen, sstep(0.3, 1.0, d.z) * 0.5)
    tube(mb, pts, radii, 24, col, oval=(1.1, 0.95))
    uv_sphere(mb, Vector((0, 2.92, 0.62)), 0.13, lambda n: hexc("#d47f80"), seg=10, rings=7)   # 鼻の先
    tube(mb, [Vector((0, -2.6, 0.8)), Vector((0, -3.1, 0.95))], [0.12, 0.04], 6, lambda t, a, p, d: pink)   # しっぽ
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.36 * sx, 2.15, 1.12)), 0.045, lambda n: BLACK, seg=6, rings=4)   # 小さな目
        # 前足：外を向いた、大きなシャベル（ピンクの手のひらと、5 本のつめ）
        c = Vector((0.98 * sx, 1.55, 0.42))
        uv_sphere(mb, c, 1.0, lambda n: pink, seg=14, rings=9, scale=Vector((0.18, 0.5, 0.42)))
        for k in range(5):
            a0 = c + Vector((0.05 * sx, 0.25 - 0.12 * k, 0.35))
            tube(mb, [a0, a0 + Vector((0.05 * sx, 0.05, 0.22))], [0.05, 0.0], 5, lambda t, a, p, d: hexc("#f3e6d6"))
        # うしろ足
        uv_sphere(mb, Vector((0.7 * sx, -1.6, 0.18)), 1.0, lambda n: pink, seg=10, rings=6, scale=Vector((0.22, 0.32, 0.15)))
    return build(mb, "Mogura")


def make_mogura_hill():
    """モグラ塚：ほりだした土の山。まん中に、モグラが顔を出す穴"""
    mb = MB()
    rnd = random.Random(5)
    prof = [(2.6, -0.05), (2.3, 0.35), (1.8, 0.75), (1.25, 1.0), (1.0, 1.02), (0.85, 0.8), (0.7, 0.3), (0.0, 0.25)]
    soil = hexc("#6b4a33")
    dark = hexc("#3e2a1c")

    def col(t, a, p):
        c = mixc(soil, hexc("#8a6446"), 0.5 + 0.5 * noise.noise(p * 2.0))
        return mixc(c, dark, sstep(0.7, 0.95, t))
    lathe(mb, prof, 28, col, rfn=lambda t, a: 1.0 + 0.08 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, t * 3))))
    for k in range(10):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(1.4, 2.6)
        uv_sphere(mb, Vector((math.cos(a) * r, math.sin(a) * r, rnd.uniform(0.1, 0.5))), rnd.uniform(0.15, 0.3),
                  lambda n: mixc(soil, dark, 0.4), seg=6, rings=4)
    return build(mb, "Mogura_Hill")


def make_okera():
    """オケラ（約 3cm）：ビロードの茶色の体、土をほるシャベルの前足、短いはね、2 本の尾"""
    mb = MB()
    brown = hexc("#6a4a30")
    light = hexc("#a07a52")

    def col(t, a, p):
        return mixc(brown, light, sstep(-0.4, -0.9, math.sin(a)) * 0.6 + 0.1 * math.sin(p.y * 40))
    seg_body(mb, -0.62, 0.05, 0.2, 0.17, col, rings=30, seg=20, z0=0.18,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.0 + 0.1)) ** 0.5, math.sin(math.pi * t) ** 0.6))
    # 前胸（かたいよろい）と頭
    uv_sphere(mb, Vector((0, 0.18, 0.22)), 1.0, lambda n: mixc(hexc("#4a3220"), light, sstep(0.4, 0.95, n.z) * 0.4), seg=18, rings=12,
              scale=Vector((0.2, 0.22, 0.17)))
    uv_sphere(mb, Vector((0, 0.43, 0.2)), 1.0, lambda n: hexc("#3a2818"), seg=14, rings=9, scale=Vector((0.12, 0.1, 0.1)))
    for sx in (-1, 1):
        cute_eye(mb, (0.08 * sx, 0.48, 0.24), 0.028, (0.5 * sx, 1, 0.1), white=False)
        a0 = Vector((0.04 * sx, 0.52, 0.24))
        tube(mb, [a0, a0 + Vector((0.08 * sx, 0.15, 0.04)), a0 + Vector((0.18 * sx, 0.28, 0.02))], [0.008, 0.006, 0.0], 4,
             lambda t, a, p, d: hexc("#3a2818"))
        # 前足のシャベル（ぎざぎざのつめ）
        c = Vector((0.2 * sx, 0.42, 0.1))
        uv_sphere(mb, c, 1.0, lambda n: hexc("#5a3a22"), seg=10, rings=6, scale=Vector((0.06, 0.1, 0.08)))
        for k in range(4):
            q = c + Vector((0.04 * sx, 0.04 + 0.025 * k, -0.06))
            tube(mb, [q, q + Vector((0.06 * sx, 0.03, -0.04))], [0.018, 0.0], 4, lambda t, a, p, d: hexc("#2a1a10"))
        # 尾（2 本）
        tube(mb, [Vector((0.04 * sx, -0.6, 0.2)), Vector((0.1 * sx, -0.85, 0.24))], [0.012, 0.0], 4, lambda t, a, p, d: brown)
    roots = []
    for (y, k, t) in ((0.12, 0.18, 0.28), (-0.12, -0.2, -0.38)):
        for sx in (-1, 1):
            roots.append(((0.15 * sx, y, 0.14), (0.3 * sx, k, 0.16), (0.38 * sx, t, 0.0)))
    legs(mb, roots, brown, hexc("#3a2818"), radius=0.025)
    return build(mb, "Okera")


def make_nanafushi():
    """ナナフシ（約 8cm）：小枝そっくりの、細長い体と長い脚。ふしごとに少し太い"""
    mb = MB()
    green = hexc("#7c8a44")
    brown = hexc("#8a7448")
    pts = [Vector((0, -1.7, 0.36)), Vector((0, -1.1, 0.37)), Vector((0, -0.5, 0.38)), Vector((0, 0.1, 0.39)), Vector((0, 0.7, 0.4)),
           Vector((0, 1.3, 0.41)), Vector((0, 1.55, 0.42))]
    radii = [0.02, 0.05, 0.06, 0.065, 0.06, 0.05, 0.0]

    def col(t, a, p, d):
        node = abs(math.sin(p.y * 6.0))
        return mixc(mixc(green, brown, 0.5 + 0.5 * math.sin(p.y * 2.3)), hexc("#b8a878"), sstep(0.95, 1.0, node) * 0.6)
    tube(mb, pts, radii, 10, col)
    uv_sphere(mb, Vector((0, 1.5, 0.43)), 0.07, lambda n: green, seg=10, rings=6)
    for sx in (-1, 1):
        cute_eye(mb, (0.05 * sx, 1.56, 0.47), 0.022, (0.6 * sx, 1, 0.1), white=False)
        a0 = Vector((0.02 * sx, 1.58, 0.45))
        tube(mb, [a0, a0 + Vector((0.08 * sx, 0.35, 0.05)), a0 + Vector((0.12 * sx, 0.8, 0.0))], [0.01, 0.008, 0.0], 4, lambda t, a, p, d: brown)
    roots = []
    for (y, k, t) in ((0.95, 1.4, 1.75), (0.25, 0.35, 0.3), (-0.2, -0.45, -0.7)):
        for sx in (-1, 1):
            roots.append(((0.05 * sx, y, 0.36), (0.45 * sx, k, 0.52), (0.75 * sx, t, 0.0)))
    legs(mb, roots, green, brown, radius=0.022)
    return build(mb, "Nanafushi")


def make_gengorou():
    """ゲンゴロウ（約 4cm）：つやつやのだ円の体（こい緑の黒に、黄色のふち）、オールのようなうしろ足"""
    mb = MB()
    shell = hexc("#1f2a20")
    rim = hexc("#d9c34a")

    def col(n):
        if abs(n.z) < 0.28 and n.z > -0.4:
            return rim
        return mixc(shell, hexc("#3f5a3a"), sstep(0.5, 1.0, n.z) * 0.6)
    uv_sphere(mb, Vector((0, 0, 0)), 1.0, col, seg=24, rings=14, scale=Vector((0.48, 0.78, 0.26)))
    uv_sphere(mb, Vector((0, 0.68, 0.0)), 1.0, lambda n: mixc(shell, rim, 0.2 if abs(n.x) > 0.7 else 0.0), seg=14, rings=8,
              scale=Vector((0.28, 0.16, 0.14)))
    for sx in (-1, 1):
        cute_eye(mb, (0.16 * sx, 0.78, 0.06), 0.035, (0.6 * sx, 1, 0.2), white=False)
        # オールのうしろ足（毛のふさ）
        base = Vector((0.3 * sx, -0.25, -0.05))
        mid = base + Vector((0.35 * sx, -0.25, -0.02))
        tip = mid + Vector((0.25 * sx, -0.35, 0.0))
        tube(mb, [base, mid, tip], [0.04, 0.035, 0.02], 6, lambda t, a, p, d: hexc("#3a3a2a"))
        ribbon(mb, [mid, mid.lerp(tip, 0.5), tip], [0.08, 0.1, 0.06], [Vector((0, 1, 0))] * 3,
               lambda t, v, p: hexc("#b8a868"), fold=0.0, normal_vecs=[Vector((0, 0, 1))] * 3)
        # 前・なかの足
        for y in (0.45, 0.15):
            b = Vector((0.25 * sx, y, -0.08))
            tube(mb, [b, b + Vector((0.2 * sx, 0.12, -0.04)), b + Vector((0.28 * sx, 0.25, -0.08))], [0.025, 0.02, 0.0], 5,
                 lambda t, a, p, d: hexc("#3a3a2a"))
    return build(mb, "Gengorou")


HANA_SHOULDER = Vector((0.04, 0.28, 0.42))


def make_hanakamakiri():
    """ハナカマキリ（約 4cm）：ランの花そっくりの、うすいピンクと白。脚の花びらのようなふくらみ"""
    mb = MB()
    pink = hexc("#f3c4d6")
    white = hexc("#fbf2f5")
    deep = hexc("#e48aaa")

    def body(t, a, p):
        return mixc(mixc(white, pink, sstep(0.3, 1.0, t)), deep, sstep(0.75, 1.0, math.sin(a)) * 0.2)
    seg_body(mb, -0.85, 0.0, 0.2, 0.13, body, rings=30, seg=20, z0=0.28,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 0.95 + 0.1)) ** 0.45, math.sin(math.pi * t) ** 0.55), flat=0.6)
    neck = [Vector((0, -0.04, 0.32)), Vector((0, 0.12, 0.38)), Vector((0, 0.28, 0.44))]
    tube(mb, neck, [0.06, 0.05, 0.045], 10, lambda t, a, p, d: mixc(white, pink, 0.4))
    uv_sphere(mb, Vector((0, 0.36, 0.48)), 1.0, lambda n: white, seg=16, rings=10, scale=Vector((0.1, 0.06, 0.075)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.09 * sx, 0.37, 0.52)), 0.04, lambda n: mixc(pink, deep, 0.3), seg=12, rings=8)
        uv_sphere(mb, Vector((0.11 * sx, 0.39, 0.53)), 0.013, lambda n: BLACK, seg=6, rings=4)
        # 腹のわきの、花びらのようなひらひら
        for k in range(3):
            c = Vector((0.2 * sx, -0.2 - 0.18 * k, 0.3))
            uv_sphere(mb, c, 1.0, lambda n: mixc(white, pink, 0.5), seg=10, rings=6, scale=Vector((0.12, 0.09, 0.025)))
    roots = []
    for (b, k, t) in (((0.04, -0.02, 0.3), (0.22, 0.06, 0.36), (0.3, 0.18, 0.0)),
                      ((0.04, -0.14, 0.3), (0.25, -0.26, 0.38), (0.34, -0.48, 0.0))):
        for sx in (-1, 1):
            roots.append(((b[0] * sx, b[1], b[2]), (k[0] * sx, k[1], k[2]), (t[0] * sx, t[1], t[2])))
    legs(mb, roots, pink, white, radius=0.022)
    # 脚の花びら（ふともものふくらみ）：脚と同じ場所に、体の部品として
    for (b, k, t) in (((0.04, -0.02, 0.3), (0.22, 0.06, 0.36), (0.3, 0.18, 0.0)),
                      ((0.04, -0.14, 0.3), (0.25, -0.26, 0.38), (0.34, -0.48, 0.0))):
        for sx in (-1, 1):
            c = Vector((b[0] * sx, b[1], b[2])).lerp(Vector((k[0] * sx, k[1], k[2])), 0.6)
            uv_sphere(mb, c + Vector((0, 0, 0.03)), 1.0, lambda n: mixc(white, pink, 0.6), seg=10, rings=6,
                      scale=Vector((0.1, 0.07, 0.025)))
    return build(mb, "Hanakamakiri")


def make_hanakamakiri_arm():
    """右のかま（肩が原点）：ピンクの花びらのようなかま"""
    mb = MB()
    pink = hexc("#f3c4d6")
    white = hexc("#fbf2f5")
    o = HANA_SHOULDER
    s0 = Vector((HANA_SHOULDER.x, HANA_SHOULDER.y, HANA_SHOULDER.z)) - o
    coxa = s0 + Vector((0.025, 0.09, -0.16))
    femur = coxa + Vector((0.01, 0.22, 0.09))
    tibia = femur + Vector((0.0, -0.12, -0.08))
    tube(mb, [s0, coxa], [0.035, 0.03], 8, lambda t, a, p, d: pink)
    tube(mb, [coxa, femur], [0.032, 0.025], 8, lambda t, a, p, d: white)
    tube(mb, [femur, tibia], [0.022, 0.0], 7, lambda t, a, p, d: pink)
    uv_sphere(mb, coxa.lerp(femur, 0.5) + Vector((0.03, 0, 0)), 1.0, lambda n: mixc(white, pink, 0.5), seg=10, rings=6,
              scale=Vector((0.05, 0.1, 0.03)))
    return build(mb, "Hanakamakiri_Arm")


def make_kameleon():
    """エボシカメレオン（レア）：とさかのような頭、くるっと巻いたしっぽ、ぐるぐる動く目。緑に黄色と青の帯"""
    mb = MB()
    green = hexc("#4fb34a")
    yellow = hexc("#e8d84a")
    blue = hexc("#4a8fd8")
    pts = [Vector((0, -1.0, 0.75)), Vector((0, -0.6, 0.9)), Vector((0, -0.1, 0.98)), Vector((0, 0.4, 0.95)), Vector((0, 0.85, 0.9)),
           Vector((0, 1.15, 0.92)), Vector((0, 1.4, 0.9))]
    radii = [0.25, 0.42, 0.5, 0.48, 0.38, 0.3, 0.0]

    def col(t, a, p, d):
        band = math.sin(p.y * 7.0)
        c = green if band < 0.3 else (yellow if band < 0.8 else blue)
        return mixc(c, hexc("#d8f0a0"), sstep(-0.3, -0.8, d.z) * 0.6)
    tube(mb, pts, radii, 18, col, oval=(0.7, 1.15))
    # とさか（頭の上の高いかぶと）
    tube(mb, [Vector((0, 0.95, 1.1)), Vector((0, 0.85, 1.55)), Vector((0, 0.7, 1.75))], [0.2, 0.12, 0.0], 10,
         lambda t, a, p, d: mixc(green, yellow, t * 0.6), oval=(0.5, 1.0))
    # くるっと巻いたしっぽ
    tail = []
    for k in range(14):
        a = k * 0.55
        r = 0.6 * (1.0 - k / 16)
        tail.append(Vector((0, -1.0 - r * math.sin(a) - 0.05 * k, 0.75 - r * (1 - math.cos(a)) * 0.9)))
    tube(mb, tail, [0.25 * (1 - k / 14) + 0.03 for k in range(14)], 10, lambda t, a, p, d: mixc(green, blue, sstep(0.4, 1.0, t)))
    for sx in (-1, 1):
        # 大きくて丸い目（まん中に小さな黒目）
        uv_sphere(mb, Vector((0.24 * sx, 1.12, 1.02)), 0.15, lambda n: mixc(green, yellow, 0.3), seg=14, rings=10)
        uv_sphere(mb, Vector((0.36 * sx, 1.17, 1.04)), 0.045, lambda n: BLACK, seg=8, rings=5)
    roots = []
    for (y, k, t) in ((0.55, 0.7, 0.85), (-0.45, -0.55, -0.65)):
        for sx in (-1, 1):
            roots.append(((0.3 * sx, y, 0.8), (0.6 * sx, k, 0.55), (0.62 * sx, t, 0.0)))
    legs(mb, roots, green, hexc("#2f7a2a"), radius=0.09)
    return build(mb, "Kameleon")


def make_herakuresu():
    """ヘラクレスオオカブト（レア、約 15cm）：オリーブ色に黒い点の前ばね、黒くて長い胸の角と頭の角"""
    mb = MB()
    olive = hexc("#c9b25a")
    black = hexc("#16120e")

    def elytra(t, a, p):
        c = mixc(olive, hexc("#e2cf7a"), sstep(0.6, 1.0, math.sin(a)) * 0.5 * (1 - t))
        if noise.noise(Vector((p.x * 9, p.y * 9, 1.3))) > 0.35:
            c = mixc(c, black, 0.8)   # 黒い点
        if abs(p.x) < 0.03 and math.sin(a) > 0:
            c = mixc(c, black, 0.6)
        return c
    seg_body(mb, -2.2, 0.6, 1.15, 0.95, elytra, rings=36, seg=28,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.05 + 0.08)) ** 0.4, math.sin(math.pi * t) ** 0.6), z0=0.5)
    # 前胸（黒くつやつや）と、長くのびる胸の角（先は下へ曲がり、毛のふさ）
    uv_sphere(mb, Vector((0, 1.0, 0.9)), 1.0, lambda n: mixc(black, hexc("#3a3428"), sstep(0.4, 0.95, n.z)), seg=22, rings=14,
              scale=Vector((0.95, 0.6, 0.55)))
    horn = [Vector((0, 1.3, 1.35)), Vector((0, 2.2, 1.7)), Vector((0, 3.4, 1.85)), Vector((0, 4.2, 1.6)), Vector((0, 4.5, 1.2))]
    tube(mb, horn, [0.28, 0.24, 0.18, 0.12, 0.0], 12, lambda t, a, p, d: mixc(black, hexc("#3a2a1e"), t * 0.3))
    for k in range(3):
        q = Vector((0, 3.3 + 0.3 * k, 1.68))
        tube(mb, [q, q + Vector((0, 0.05, -0.25))], [0.08, 0.0], 6, lambda t, a, p, d: hexc("#b8763a"))   # 毛のふさ
    # 頭と、下から上へ曲がる頭の角
    uv_sphere(mb, Vector((0, 1.65, 0.55)), 1.0, lambda n: black, seg=16, rings=10, scale=Vector((0.4, 0.3, 0.3)))
    tube(mb, [Vector((0, 1.85, 0.55)), Vector((0, 2.8, 0.7)), Vector((0, 3.6, 1.05)), Vector((0, 3.9, 1.35))],
         [0.18, 0.14, 0.09, 0.0], 10, lambda t, a, p, d: black)
    for sx in (-1, 1):
        cute_eye(mb, (0.3 * sx, 1.8, 0.65), 0.08, (0.5 * sx, 1, 0.1), white=False)
    roots = []
    for y in (1.0, 0.3, -0.6):
        for sx in (-1, 1):
            roots.append(((0.7 * sx, y, 0.5), (1.4 * sx, y + 0.15, 0.6), (1.8 * sx, y + 0.25, 0.0)))
    legs(mb, roots, black, hexc("#3a2a1e"), radius=0.09)
    return build(mb, "Herakuresu")


def harinezumi_spines(mb, center, rx, ry, rz, n, rnd, zmin=-0.2, length=0.35, face=False):
    """ハリネズミのはり：だ円の上半分から、外向きに。根もとは白、先は茶色。face なら、顔のまわり（前）には生やさない"""
    for _ in range(n):
        th = rnd.uniform(0, TAU)
        ph = math.asin(rnd.uniform(zmin, 1.0))
        d = Vector((math.cos(th) * math.cos(ph), math.sin(th) * math.cos(ph), math.sin(ph)))
        if face and d.y > 0.55 and d.z < 0.75:
            continue
        base = center + Vector((d.x * rx, d.y * ry, d.z * rz)) * 0.92
        tipd = (Vector((d.x / rx, d.y / ry, d.z / rz))).normalized()
        tipd = (tipd + Vector((0, -0.6, 0.15))).normalized()   # うしろへ流れる
        tube(mb, [base, base + tipd * length * 0.5, base + tipd * length], [0.045, 0.03, 0.0], 4,
             lambda t, a, p, d: mixc(hexc("#efe6d6"), hexc("#5a4030"), sstep(0.3, 0.9, t)))


def make_harinezumi():
    """ハリネズミ（レア、子ども・約 10cm）：背中いっぱいのはり、とがった顔、黒い鼻"""
    mb = MB()
    rnd = random.Random(17)
    face = hexc("#c9b29a")
    uv_sphere(mb, Vector((0, -0.25, 0.75)), 1.0, lambda n: hexc("#8a6a52"), seg=22, rings=14, scale=Vector((1.0, 1.15, 0.8)))
    harinezumi_spines(mb, Vector((0, -0.25, 0.78)), 1.0, 1.15, 0.82, 240, rnd, face=True)
    # 顔：体の前へ、とがった鼻先まで
    snout = [Vector((0, 0.55, 0.62)), Vector((0, 1.05, 0.55)), Vector((0, 1.5, 0.44))]
    tube(mb, snout, [0.62, 0.4, 0.1], 16, lambda t, a, p, d: face)
    uv_sphere(mb, Vector((0, 1.56, 0.44)), 0.1, lambda n: hexc("#1a1414"), seg=8, rings=5)
    for sx in (-1, 1):
        # つぶらな瞳：まんまるの黒い目に、大きな光と小さな光
        ec = Vector((0.3 * sx, 0.95, 0.82))
        look = Vector((0.55 * sx, 1.0, 0.15)).normalized()
        uv_sphere(mb, ec, 0.15, lambda n: mixc(hexc("#0e0b0b"), hexc("#3a2a28"), sstep(0.2, -0.8, n.z)), seg=16, rings=12)
        uv_sphere(mb, ec + look * 0.125 + Vector((0, 0, 0.055)), 0.048, lambda n: WHITE, seg=8, rings=5)
        uv_sphere(mb, ec + look * 0.14 + Vector((0.025 * sx, 0, -0.045)), 0.02, lambda n: WHITE, seg=6, rings=4)
        uv_sphere(mb, Vector((0.4 * sx, 0.68, 1.02)), 1.0, lambda n: hexc("#d8b8a0"), seg=8, rings=5, scale=Vector((0.12, 0.05, 0.12)))
        for y in (0.5, -0.85):
            uv_sphere(mb, Vector((0.55 * sx, y, 0.12)), 1.0, lambda n: face, seg=8, rings=5, scale=Vector((0.14, 0.18, 0.12)))
    return build(mb, "Harinezumi")


def make_harinezumi_ball():
    """まるくなったハリネズミ：はりの玉"""
    mb = MB()
    rnd = random.Random(23)
    uv_sphere(mb, Vector((0, 0, 0.9)), 0.9, lambda n: hexc("#8a6a52"), seg=20, rings=14)
    harinezumi_spines(mb, Vector((0, 0, 0.9)), 0.9, 0.9, 0.9, 300, rnd, zmin=-0.85, length=0.35)
    return build(mb, "Harinezumi_Ball")


# ---------------------------------------------------------------------------
# 山のいきもの：ニホンリス・オコジョ（岩のすみか）・ナキウサギ・ハコネサンショウウオ・アサギマダラ・
# マルハナバチ・オニヤンマ・ヒグラシ・マイマイカブリ（ライチョウは鳥の作り方）
# 大きさは本物と同じ（1 単位 = 2.5cm）
# ---------------------------------------------------------------------------
def col_sphere(c1, c2, belly):
    def f(n):
        if n.z < -0.4:
            return belly
        return mixc(c1, c2, 0.5 + 0.4 * n.z)
    return f


def make_risu():
    """ニホンリス（約 20cm と、ふさふさのしっぽ）：夏毛の赤茶色、白いおなか、耳の先の毛、大きな黒い目"""
    mb = MB()
    fur = hexc("#9a5a34")
    fur2 = hexc("#c27c4a")
    belly = hexc("#f4eee2")
    dark = hexc("#5a3420")

    def col(t, a, p, d):
        if d.z < -0.35:
            return belly
        return mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 1.6)) if d.z < 0.6 else mixc(fur, dark, 0.25)
    pts = [Vector((0, -2.9, 1.8)), Vector((0, -2.4, 2.05)), Vector((0, -1.4, 2.2)), Vector((0, -0.3, 2.25)), Vector((0, 0.7, 2.3)),
           Vector((0, 1.5, 2.5)), Vector((0, 2.0, 2.75))]
    tube(mb, pts, [0.0, 1.05, 1.35, 1.38, 1.22, 0.95, 0.75], 22, col)
    hc = Vector((0, 2.55, 3.05))
    uv_sphere(mb, hc, 1.05, lambda n: belly if n.z < -0.45 and n.y > -0.2 else mixc(fur, fur2, 0.4 + 0.3 * n.z), seg=22, rings=14,
              scale=Vector((1.0, 1.05, 0.95)))
    uv_sphere(mb, hc + Vector((0, 0.95, -0.3)), 0.55, lambda n: belly if n.z < -0.2 else fur2, seg=16, rings=10, scale=Vector((0.9, 1.0, 0.8)))
    uv_sphere(mb, hc + Vector((0, 1.45, -0.22)), 0.16, lambda n: hexc("#2a1a14"), seg=8, rings=5)
    for sx in (-1, 1):
        uv_sphere(mb, hc + Vector((0.6 * sx, 0.4, 0.24)), 0.3, lambda n: belly, seg=10, rings=6, scale=Vector((0.5, 1.0, 1.0)))   # 目のまわりの白いふち
        cute_eye(mb, hc + Vector((0.66 * sx, 0.45, 0.25)), 0.25, (0.6 * sx, 0.8, 0.1), white=False)
        e0 = hc + Vector((0.45 * sx, -0.2, 0.75))
        tube(mb, [e0, e0 + Vector((0.12 * sx, -0.05, 0.5)), e0 + Vector((0.18 * sx, -0.1, 1.05))], [0.32, 0.22, 0.0], 8,
             lambda t, a, p, d: mixc(fur, dark, sstep(0.5, 1.0, t)), oval=(1.0, 0.5))
        # うしろ足のもも
        uv_sphere(mb, Vector((0.95 * sx, -1.7, 1.55)), 1.0, col_sphere(fur, fur2, belly), seg=14, rings=9, scale=Vector((0.55, 1.0, 0.95)))
        for k in range(3):
            tube(mb, [hc + Vector((0.25 * sx, 1.2, -0.55)), hc + Vector((1.1 * sx + 0.08 * (k - 1), 1.45, -0.6 + 0.12 * (k - 1)))], [0.03, 0.0], 4,
                 lambda t, a, p, d: hexc("#e8c8b0"))   # ひげ
    # しっぽ：ふさふさで、せなかの上へ大きくそり返る
    tail = [Vector((0, -2.8, 2.1)), Vector((0, -3.8, 2.5)), Vector((0, -4.5, 3.5)), Vector((0, -4.65, 4.9)), Vector((0, -4.1, 6.0)),
            Vector((0, -3.3, 6.45)), Vector((0, -2.7, 6.25))]

    def tail_col(t, a, p, d):
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 2.5))
        if abs(d.x) > 0.85:
            c = mixc(c, hexc("#e6c8a8"), 0.5)   # ふちの毛は、白っぽい
        return c
    tube(mb, tail, [0.55, 1.05, 1.4, 1.5, 1.35, 1.0, 0.0], 18, tail_col, oval=(1.0, 0.75),
         rfn=lambda t, a: 1.0 + 0.1 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, t * 9))))
    roots = []
    for (b, k, tp) in (((0.6, 1.4, 1.8), (0.78, 1.85, 0.9), (0.72, 2.15, 0.0)), ((0.95, -1.75, 1.4), (1.25, -1.0, 0.75), (1.1, -0.55, 0.0))):
        for sx in (-1, 1):
            roots.append(((b[0] * sx, b[1], b[2]), (k[0] * sx, k[1], k[2]), (tp[0] * sx, tp[1], tp[2])))
    legs(mb, roots, fur, dark, radius=0.3)
    return build(mb, "Risu")


def make_okojo():
    """オコジョ（夏毛、約 18cm）：岩のすきまから、うしろ足で立ち上がって、あたりを見まわす。
    せなかは茶色、おなかはクリーム色、しっぽの先は黒。体は上（+Z）へのびる（顔は +Y）。z = 0 が穴のふち"""
    mb = MB()
    brown = hexc("#8a5a34")
    cream = hexc("#f6eccf")

    def col(t, a, p, d):
        return cream if d.y > 0.15 else mixc(brown, hexc("#a8703f"), 0.5 + 0.5 * noise.noise(p * 2.0))
    # 立ち上がった体（下はすみかの穴の中へ）
    pts = [Vector((0, -0.3, -1.6)), Vector((0, -0.25, 0.0)), Vector((0, -0.05, 1.6)), Vector((0, 0.15, 3.0)), Vector((0, 0.3, 4.0)),
           Vector((0, 0.35, 4.6))]
    tube(mb, pts, [0.95, 1.0, 0.95, 0.8, 0.65, 0.6], 20, col, up_hint=Vector((0, 1, 0)))
    hc = Vector((0, 0.55, 5.2))
    uv_sphere(mb, hc, 0.85, lambda n: cream if n.z < -0.25 and n.y > 0.0 else mixc(brown, hexc("#a8703f"), 0.3), seg=20, rings=12,
              scale=Vector((1.0, 1.15, 0.85)))
    uv_sphere(mb, hc + Vector((0, 0.95, -0.15)), 0.12, lambda n: hexc("#2a1a18"), seg=8, rings=5)
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.4 * sx, 0.62, 0.2)), 0.15, (0.4 * sx, 1, 0.1), white=False)
        uv_sphere(mb, hc + Vector((0.6 * sx, -0.15, 0.55)), 1.0, lambda n: mixc(brown, cream, 0.5 if n.y > 0.2 else 0.0), seg=10, rings=6,
                  scale=Vector((0.35, 0.18, 0.32)))   # まるい耳
        # 前足（胸の前で、そろえている）
        tube(mb, [Vector((0.45 * sx, 0.5, 3.3)), Vector((0.5 * sx, 0.95, 2.8)), Vector((0.35 * sx, 1.05, 2.5))], [0.22, 0.18, 0.14], 8,
             lambda t, a, p, d: cream)
        for k in range(3):
            tube(mb, [hc + Vector((0.3 * sx, 0.85, -0.3)), hc + Vector((0.9 * sx, 1.0, -0.35 + 0.12 * (k - 1)))], [0.025, 0.0], 4,
                 lambda t, a, p, d: hexc("#3a2a20"))   # ひげ
    # しっぽ（先は黒）：穴のふちから、うしろへ
    tail = [Vector((0, -0.8, 0.2)), Vector((0, -1.6, 0.6)), Vector((0, -2.3, 1.2)), Vector((0, -2.6, 1.9))]
    tube(mb, tail, [0.35, 0.33, 0.3, 0.0], 10, lambda t, a, p, d: hexc("#141012") if t > 0.6 else brown)
    return build(mb, "Okojo")


def make_okojo_rocks():
    """オコジョのすみか：花こう岩の石が、まるく積み重なり、まん中に暗い穴"""
    mb = MB()
    rnd = random.Random(9)
    prof = [(0.0, -0.6), (1.2, -0.5), (1.45, -0.1), (1.5, 0.15), (0.0, 0.16)]

    def hole(t, a, p):
        return mixc(hexc("#151210"), hexc("#3a3028"), sstep(0.8, 1.45, math.hypot(p.x, p.y)))
    lathe(mb, prof, 24, hole)
    for k in range(8):
        a = TAU * k / 8 + rnd.uniform(-0.2, 0.2)
        r = rnd.uniform(2.6, 3.0)
        c = Vector((math.cos(a) * r, math.sin(a) * r, rnd.uniform(0.4, 0.9)))
        s = rnd.uniform(0.85, 1.2)

        def rc(n, k=k):
            c0 = mixc(hexc("#8e8e92"), hexc("#bdb8b0"), 0.5 + 0.5 * n.x * math.sin(k))
            return mixc(c0, hexc("#c9c78a"), 0.5 * sstep(0.6, 0.9, n.z))
        tilt = Vector((0, 0, 1)).rotation_difference(Vector((rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), 1)).normalized())
        uv_sphere(mb, c, 1.0, rc, seg=10, rings=6, scale=Vector((s * 1.1, s * 0.9, s * 0.8)), rot=tilt)
    for k in range(3):
        a = TAU * k / 3 + 0.5
        uv_sphere(mb, Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, 1.5)), 1.0,
                  lambda n: mixc(hexc("#9a9a9e"), hexc("#c4c0b8"), 0.5 + 0.5 * n.z), seg=10, rings=6, scale=Vector((0.9, 0.75, 0.6)))
    return build(mb, "Okojo_Rocks", smooth=True, sharp_deg=50)


def make_nakiusagi():
    """エゾナキウサギ（約 15cm）：まるい体に、まるい耳。しっぽは見えない。灰色がかった茶色の毛"""
    mb = MB()
    fur = hexc("#8a7462")
    fur2 = hexc("#a89078")
    belly = hexc("#d8ccb8")

    def body(n):
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(n * 3.0))
        return mixc(c, belly, sstep(-0.2, -0.6, n.z))
    uv_sphere(mb, Vector((0, -0.3, 1.45)), 1.0, body, seg=26, rings=16, scale=Vector((1.55, 1.9, 1.4)))
    hc = Vector((0, 1.55, 1.95))
    uv_sphere(mb, hc, 1.0, body, seg=22, rings=14, scale=Vector((1.15, 1.05, 1.0)))
    uv_sphere(mb, hc + Vector((0, 0.95, -0.25)), 0.14, lambda n: hexc("#3a2a24"), seg=8, rings=5)
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.6 * sx, 0.62, 0.22)), 0.2, (0.5 * sx, 1, 0.1), white=False)
        ec = hc + Vector((0.65 * sx, -0.25, 0.95))
        uv_sphere(mb, ec, 1.0, lambda n: hexc("#f0e8dc") if n.z > 0.75 else mixc(fur, hexc("#c8a8a0"), 0.3 if n.y > 0.3 else 0.0),
                  seg=12, rings=8, scale=Vector((0.42, 0.18, 0.42)))   # まるい耳（白いふち）
        uv_sphere(mb, Vector((0.65 * sx, 1.05, 0.2)), 1.0, lambda n: belly, seg=8, rings=5, scale=Vector((0.28, 0.4, 0.2)))
        uv_sphere(mb, Vector((0.95 * sx, -1.1, 0.25)), 1.0, lambda n: fur, seg=8, rings=5, scale=Vector((0.35, 0.6, 0.25)))
        for k in range(3):
            tube(mb, [hc + Vector((0.3 * sx, 0.9, -0.35)), hc + Vector((1.1 * sx, 1.15, -0.4 + 0.15 * (k - 1)))], [0.025, 0.0], 4,
                 lambda t, a, p, d: hexc("#2a201a"))   # ひげ
    return build(mb, "Nakiusagi")


def make_sanshouuo():
    """ハコネサンショウウオ（約 15cm）：むらさきがかった黒茶色の、ほそ長い体。せなかに黄土色のすじ。長いしっぽ"""
    mb = MB()
    dark = hexc("#3a2c34")
    stripe = hexc("#c8a050")
    belly = hexc("#8a7a80")
    pts = [Vector((0, -3.3, 0.18)), Vector((0, -2.6, 0.22)), Vector((0, -1.8, 0.28)), Vector((0, -1.0, 0.36)), Vector((0, -0.3, 0.42)),
           Vector((0, 0.5, 0.44)), Vector((0, 1.2, 0.44)), Vector((0, 1.7, 0.42)), Vector((0, 2.15, 0.44)), Vector((0, 2.6, 0.42)),
           Vector((0, 2.95, 0.36))]
    radii = [0.0, 0.15, 0.25, 0.36, 0.46, 0.5, 0.48, 0.42, 0.46, 0.4, 0.0]

    def col(t, a, p, d):
        if d.z < -0.4:
            return belly
        if d.z > 0.55 and t > 0.3 and abs(d.x) < 0.45:
            return mixc(stripe, hexc("#e0c070"), 0.5 + 0.5 * noise.noise(p * 4))   # せなかのすじ
        return mixc(dark, hexc("#5a4450"), 0.5 + 0.5 * noise.noise(p * 3))
    tube(mb, pts, radii, 16, col, oval=(1.15, 0.9))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.27 * sx, 2.4, 0.68)), 0.17, lambda n: dark, seg=10, rings=6)
        cute_eye(mb, (0.3 * sx, 2.48, 0.74), 0.13, (0.6 * sx, 0.8, 0.3), white=False)
    roots = []
    for (y, ky, ty) in ((1.4, 1.6, 1.85), (-0.6, -0.7, -0.5)):
        for sx in (-1, 1):
            roots.append(((0.32 * sx, y, 0.36), (0.62 * sx, ky, 0.4), (0.75 * sx, ty, 0.0)))
    legs(mb, roots, dark, hexc("#c8a0a8"), radius=0.1)
    return build(mb, "Sanshouuo")


def make_asagimadara_body():
    mb = MB()
    tube(mb, [Vector((0, -0.55, 0.0)), Vector((0, -0.25, 0.0)), Vector((0, 0.1, 0.0)), Vector((0, 0.2, 0.0))],
         [0.0, 0.06, 0.07, 0.06], 10, lambda t, a, p, d: mixc(hexc("#1e1a1c"), hexc("#f2eee6"), 0.8 if abs(math.sin(p.y * 30)) > 0.9 else 0.0))
    uv_sphere(mb, Vector((0, 0.27, 0.0)), 0.075, lambda n: hexc("#1e1a1c") if n.z > -0.3 else hexc("#f2eee6"), seg=12, rings=8)
    for sx in (-1, 1):
        cute_eye(mb, (0.05 * sx, 0.31, 0.025), 0.03, (0.4 * sx, 1, 0), white=False)
        a0 = Vector((0.025 * sx, 0.32, 0.05))
        a1 = Vector((0.2 * sx, 0.75, 0.18))
        tube(mb, [a0, a0.lerp(a1, 0.5), a1], [0.008, 0.008, 0.007], 5, lambda t, a, p, d: hexc("#1e1a1c"))
        uv_sphere(mb, a1, 0.025, lambda n: hexc("#1e1a1c"), seg=8, rings=5)
    return build(mb, "Asagimadara_Body")


def make_asagimadara_wing():
    """アサギマダラのはね：すきとおった浅葱色（うすい青緑）を、黒いすじがくぎる。うしろばねのふちは栗色"""
    mb = MB()

    def rfn(th):
        fore = 1.85 * math.exp(-((th - 0.92) / 0.5) ** 2)
        hind = 1.3 * math.exp(-((th - 2.2) / 0.5) ** 2)
        return max(0.2, fore, hind)

    def cf(rho, th, p):
        hind = th > 1.62
        c = mixc(hexc("#9fd6d8"), hexc("#c6ecec"), sstep(0.1, 0.6, rho))
        vein = abs(math.sin(th * (9.0 if not hind else 7.0) + 0.4)) < 0.2 + 0.1 * rho
        edge = rho > (0.82 if not hind else 0.78)
        if hind and edge:
            return hexc("#7a3a26")      # 栗色のふち
        if not hind and (edge or (rho > 0.55 and th < 0.7)):
            c = hexc("#2a2224")         # 前ばねの先は黒（白い点）
            if (int(rho * 9) + int(th * 7)) % 3 == 0:
                c = hexc("#e8f2f2")
            return c
        if vein or rho < 0.1:
            return hexc("#2a2224") if not hind else hexc("#5a3428")
        return c
    polar_sheet(mb, rfn, cf, n_ang=64, n_rad=10, a0=0.05, a1=3.05, zfn=lambda rho, th: 0.04 * rho * rho, full=False)
    return build(mb, "Asagimadara_Wing")


def make_maruhanabachi():
    """オオマルハナバチ（約 2cm）：まるくて、ふわふわの黒い毛。胸の前は黄色、おしりの先はだいだい色"""
    mb = MB()
    black = hexc("#1e1a18")
    yellow = hexc("#f0b830")
    orange = hexc("#e87a2a")

    def fuzz(c):
        def f(n):
            return scalec(c, 0.85 + 0.25 * noise.noise(n * 9.0))
        return f
    uv_sphere(mb, Vector((0, -0.2, 0.35)), 1.0, lambda n: orange if n.y < -0.65 else fuzz(black)(n), seg=18, rings=12,
              scale=Vector((0.3, 0.38, 0.3)))
    uv_sphere(mb, Vector((0, 0.18, 0.4)), 1.0, lambda n: fuzz(yellow)(n) if n.y > 0.1 else fuzz(black)(n), seg=16, rings=10,
              scale=Vector((0.26, 0.22, 0.24)))
    uv_sphere(mb, Vector((0, 0.42, 0.33)), 1.0, fuzz(black), seg=12, rings=8, scale=Vector((0.15, 0.12, 0.14)))
    for sx in (-1, 1):
        cute_eye(mb, (0.09 * sx, 0.5, 0.38), 0.05, (0.5 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.04 * sx, 0.52, 0.42)), Vector((0.1 * sx, 0.62, 0.55)), Vector((0.14 * sx, 0.75, 0.55))], [0.012, 0.01, 0.008], 4,
             lambda t, a, p, d: black)
        uv_sphere(mb, Vector((0.24 * sx, -0.1, 0.12)), 0.07, lambda n: hexc("#f6d24a"), seg=8, rings=5)   # 花粉だんご
    roots = []
    for y in (0.22, 0.12, 0.0):
        for sx in (-1, 1):
            roots.append(((0.12 * sx, y, 0.22), (0.22 * sx, y + 0.02, 0.15), (0.26 * sx, y + 0.04, 0.0)))
    legs(mb, roots, black, radius=0.02)
    return build(mb, "Maruhanabachi")


def make_maruhanabachi_wing():
    mb = MB()

    def rfn(th):
        return 0.5 * math.exp(-((th - 1.3) / 0.38) ** 2) + 0.05

    def cf(rho, th, p):
        c = mixc(hexc("#e8eef4"), hexc("#c8d2dc"), rho)
        return scalec(c, 0.75) if abs(math.sin(th * 6)) < 0.12 else c
    polar_sheet(mb, rfn, cf, n_ang=30, n_rad=6, a0=0.6, a1=2.0, full=False)
    return build(mb, "Maruhanabachi_Wing")


def make_oniyanma_body():
    """オニヤンマ（約 10cm）：黒に黄色のしま、エメラルド色の大きな目（左右がくっつく）"""
    mb = MB()
    black = hexc("#1a1a1c")
    yellow = hexc("#f0d030")
    pts = [Vector((0, -3.1, 0.0)), Vector((0, -2.2, 0.0)), Vector((0, -1.2, 0.0)), Vector((0, -0.4, 0.0)), Vector((0, 0.0, 0.0))]

    def ab(t, a, p, d):
        band = abs(math.sin(p.y * 4.2))
        return yellow if band > 0.96 and d.z > -0.3 else black
    tube(mb, pts, [0.0, 0.11, 0.12, 0.13, 0.15], 12, ab)
    uv_sphere(mb, Vector((0, 0.3, 0.02)), 1.0, lambda n: yellow if abs(n.x) > 0.6 and n.z > -0.2 else black, seg=16, rings=10,
              scale=Vector((0.24, 0.36, 0.26)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.13 * sx, 0.78, 0.12)), 0.2, lambda n: mixc(hexc("#1a7a4a"), hexc("#6ae0a0"), sstep(0.0, 0.9, n.z)), seg=16, rings=10)
        uv_sphere(mb, Vector((0.2 * sx, 0.86, 0.24)), 0.045, lambda n: WHITE, seg=6, rings=4)
    uv_sphere(mb, Vector((0, 0.85, -0.05)), 0.12, lambda n: yellow, seg=10, rings=6)
    roots = []
    for y in (0.4, 0.28, 0.16):
        for sx in (-1, 1):
            roots.append(((0.1 * sx, y, -0.15), (0.24 * sx, y + 0.08, -0.3), (0.28 * sx, y + 0.18, -0.42)))
    legs(mb, roots, black, radius=0.022)
    return build(mb, "Oniyanma_Body")


def make_oniyanma_wing():
    mb = MB()
    L = 2.3

    def rfn(th):
        d = th - math.pi / 2
        return L * math.exp(-(d / 0.16) ** 2) + 0.04

    def cf(rho, th, p):
        c = mixc(hexc("#f6f6ea"), hexc("#ece6c8"), rho)
        if abs(math.sin(p.x * 18)) < 0.1 or abs(math.sin(p.y * 28)) < 0.1:
            c = scalec(c, 0.75)
        if 0.9 < rho < 0.96:
            c = hexc("#2a2420")
        return c
    polar_sheet(mb, rfn, cf, n_ang=40, n_rad=8, a0=math.pi / 2 - 0.45, a1=math.pi / 2 + 0.45, full=False)
    return build(mb, "Oniyanma_Wing")


def make_higurashi():
    """ヒグラシ（約 4cm、羽まで 5cm）：緑と黒のもようの体、すきとおった羽を、屋根のように背中でたたむ"""
    mb = MB()
    brown = hexc("#6a4a2a")
    green = hexc("#5a8a4a")
    black = hexc("#1e1a16")

    def body(t, a, p):
        c = mixc(brown, green, 0.5 + 0.5 * math.sin(p.x * 20) * math.sin(p.y * 12))
        return mixc(c, black, 0.5 if abs(p.x) < 0.03 else 0.0)
    seg_body(mb, -0.95, 0.15, 0.3, 0.26, body, rings=26, seg=20, z0=0.22,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.0 + 0.12)) ** 0.5, math.sin(math.pi * t) ** 0.6))
    uv_sphere(mb, Vector((0, 0.32, 0.32)), 1.0, lambda n: green if abs(n.x) > 0.3 or n.z < 0.6 else black, seg=16, rings=10,
              scale=Vector((0.33, 0.3, 0.26)))
    uv_sphere(mb, Vector((0, 0.62, 0.28)), 1.0, lambda n: mixc(green, brown, 0.5), seg=14, rings=9, scale=Vector((0.34, 0.14, 0.16)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.33 * sx, 0.64, 0.33)), 0.1, lambda n: mixc(hexc("#4a3a2a"), hexc("#b8a070"), sstep(0.2, 0.9, n.z)), seg=10, rings=7)
        uv_sphere(mb, Vector((0.37 * sx, 0.7, 0.38)), 0.025, lambda n: WHITE, seg=6, rings=4)
    # たたんだ羽（屋根の形）：すきとおった灰色に、黒いすじ
    for sx in (-1, 1):
        rows = []
        for i in range(9):
            u = i / 8
            y = lerp(0.42, -1.45, u)
            w = 0.36 * math.sin(math.pi * min(1.0, 0.18 + 0.9 * u)) ** 0.6 + 0.05
            row = []
            for j in range(4):
                v = j / 3
                x = sx * lerp(0.03, w, v)
                z = lerp(0.62, 0.2, v) - 0.08 * u
                c = mixc(hexc("#8e9a90"), hexc("#a8b2a6"), v)   # すきとおって、下の体の色がすける
                if u < 0.25:
                    c = mixc(c, hexc("#4a7a3a"), 0.6)          # 付け根のすじは緑
                if j == 3 or i % 3 == 0:
                    c = hexc("#2a2a22")
                row.append(mb.v((x, y, z), c))
            rows.append(row)
        for i in range(8):
            for j in range(3):
                if sx > 0:
                    mb.f(rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1])
                else:
                    mb.f(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])
    roots = []
    for y, dy in ((0.42, 0.12), (0.3, 0.0), (0.15, -0.12)):
        for sx in (-1, 1):
            roots.append(((0.15 * sx, y, 0.15), (0.32 * sx, y + dy * 0.5, 0.16), (0.42 * sx, y + dy, 0.0)))
    legs(mb, roots, brown, black, radius=0.03)
    return build(mb, "Higurashi")


def make_maimaikaburi():
    """マイマイカブリ（約 5cm）：細長い首と頭（かたつむりの殻にさしこむ）。黒いはねのふちが、むらさきと緑に光る"""
    mb = MB()
    black = hexc("#16141a")
    purple = hexc("#6a3a8a")
    green = hexc("#2a7a5a")

    def elytra(t, a, p):
        c = mixc(black, hexc("#2a2832"), sstep(0.5, 1.0, math.sin(a)) * 0.6)
        rim = sstep(0.75, 1.0, abs(math.cos(a)))
        c = mixc(c, mixc(purple, green, 0.5 + 0.5 * math.sin(p.y * 6)), 0.75 * rim)
        if abs(math.sin(p.x * 70)) < 0.12 and math.sin(a) > 0.3:
            c = scalec(c, 0.7)   # たてのすじ
        return c
    seg_body(mb, -0.95, 0.0, 0.3, 0.2, elytra, rings=30, seg=22, z0=0.18,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.0 + 0.1)) ** 0.45, math.sin(math.pi * t) ** 0.55))
    neck = [Vector((0, -0.02, 0.22)), Vector((0, 0.25, 0.24)), Vector((0, 0.5, 0.26)), Vector((0, 0.72, 0.25))]
    tube(mb, neck, [0.1, 0.11, 0.08, 0.07], 10, lambda t, a, p, d: mixc(black, purple, 0.3 * sstep(0.3, 1.0, d.z)))
    uv_sphere(mb, Vector((0, 0.82, 0.24)), 1.0, lambda n: black, seg=12, rings=8, scale=Vector((0.08, 0.12, 0.07)))
    for sx in (-1, 1):
        cute_eye(mb, (0.07 * sx, 0.86, 0.27), 0.03, (0.6 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.03 * sx, 0.92, 0.28)), Vector((0.15 * sx, 1.15, 0.36)), Vector((0.3 * sx, 1.4, 0.3))], [0.012, 0.01, 0.0], 5,
             lambda t, a, p, d: black)
        tube(mb, [Vector((0.03 * sx, 0.93, 0.2)), Vector((0.05 * sx, 1.02, 0.16))], [0.015, 0.0], 4, lambda t, a, p, d: hexc("#3a2a2a"))   # 大あご
    roots = []
    for y, dy in ((0.3, 0.15), (-0.1, -0.05), (-0.3, -0.25)):
        for sx in (-1, 1):
            roots.append(((0.1 * sx, y, 0.14), (0.35 * sx, y + dy * 0.5, 0.22), (0.5 * sx, y + dy, 0.0)))
    legs(mb, roots, black, purple, radius=0.025)
    return build(mb, "Maimaikaburi")



def main():
    fbx_dir, png_dir, blend_out = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    walkers = [
        (make_ladybug, "Ladybug", None), (make_ant, "Ant", None), (make_beetle, "Beetle", None),
        (make_water_strider, "WaterStrider", None), (make_crab, "Crab", None), (make_grasshopper, "Grasshopper", None),
        (make_otoshibumi, "Otoshibumi", None), (make_spider, "Spider", spider_sneaker_variant), (make_mantis, "Mantis", None),
        (make_kamikiri, "Kamikiri", None), (make_kuwagata, "Kuwagata", None), (make_kamemushi, "Kamemushi", None), (make_tokage, "Tokage", None),
        (make_okera, "Okera", None), (make_nanafushi, "Nanafushi", None), (make_hanakamakiri, "Hanakamakiri", None),
        (make_kameleon, "Kameleon", None), (make_herakuresu, "Herakuresu", None),
        (make_risu, "Risu", None), (make_sanshouuo, "Sanshouuo", None), (make_higurashi, "Higurashi", None),
        (make_maimaikaburi, "Maimaikaburi", None),
    ]
    makers = [
        make_snail, make_pillbug, make_butterfly_body, make_butterfly_wing,
        make_dragonfly_body, make_dragonfly_wing, make_frog, make_river_snail,
        make_firefly_body, make_firefly_glow, make_monshiro_body, make_monshiro_wing,
        make_cradle, make_anthill, make_grasshopper_hind, make_mantis_arm, make_ant_helmet, make_crumb, make_pillbug_ball,
        lambda: make_bird_body("Sparrow_Body", 5.5, False), lambda: make_bird_wing("Sparrow_Wing", 5.5, False),
        lambda: make_bird_wing_folded("Sparrow_WingFolded", 5.5, False),
        lambda: make_bird_body("Crow_Body", 18.0, True), lambda: make_bird_wing("Crow_Wing", 18.0, True),
        lambda: make_bird_wing_folded("Crow_WingFolded", 18.0, True),
        lambda: make_river_stone("RiverStone_A", 1, (1.6, 1.2, 0.55), (hexc("#8d96a3"), hexc("#b7b2a5"))),
        lambda: make_river_stone("RiverStone_B", 2, (1.3, 1.1, 0.7), (hexc("#a99a84"), hexc("#d2c4a8"))),
        lambda: make_river_stone("RiverStone_C", 3, (1.9, 1.5, 0.45), (hexc("#7d8a86"), hexc("#a6b0a4"))),
        make_horsetail, make_iris, make_root_arch,
        make_koumori_body, make_koumori_wing, make_mogura, make_mogura_hill, make_gengorou, make_hanakamakiri_arm,
        make_harinezumi, make_harinezumi_ball,
        lambda: make_bird_body("Hato_Body", 13.0, "pigeon"), lambda: make_bird_wing("Hato_Wing", 13.0, "pigeon"),
        lambda: make_bird_wing_folded("Hato_WingFolded", 13.0, "pigeon"),
        lambda: make_bird_body("Flamingo_Body", 20.0, "flamingo"), lambda: make_bird_wing("Flamingo_Wing", 20.0, "flamingo"),
        lambda: make_bird_wing_folded("Flamingo_WingFolded", 20.0, "flamingo"),
        lambda: make_bird_body("Raichou_Body", 15.0, "raichou"), lambda: make_bird_wing("Raichou_Wing", 15.0, "raichou"),
        lambda: make_bird_wing_folded("Raichou_WingFolded", 15.0, "raichou"),
        make_okojo, make_okojo_rocks, make_nakiusagi, make_asagimadara_body, make_asagimadara_wing, make_maruhanabachi_wing,
        make_oniyanma_body, make_oniyanma_wing, make_maruhanabachi,
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
        if name.startswith(("Butterfly_Wing_", "Monshiro_Wing_", "Dragonfly_Wing_", "Sparrow_Wing_", "Crow_Wing_", "Cradle_Portrait", "Mantis_Arm_",
                            "Koumori_Wing_", "Hato_Wing_", "Flamingo_Wing_", "Hanakamakiri_Arm_", "Raichou_Wing_",
                            "Asagimadara_Wing_", "Maruhanabachi_Wing_", "Oniyanma_Wing_",
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
