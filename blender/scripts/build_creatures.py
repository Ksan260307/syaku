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


# 脚を別メッシュにして Unity で動かすため、legs()・leg_part()・limb() の呼び出しを記録できるようにする
_LEG_CAPTURE = None
RIG = {}        # 体の名前 -> [(脚メッシュ名, 付け根, 先), ...]（Blender 座標・右側だけ）
LIMBS = {}      # 体の名前 -> [(上の骨, 下の骨, 足先, 付け根, ひざ, 足首), ...]（ひざで曲がる手足。Blender 座標・右側だけ）


def legs(mb, roots, color, tip_color=None, radius=0.012, tip_disc=0.0, toes=0, foot=None):
    """roots: [(付け根, 膝, 先), ...]。toes > 0 なら、足先に指を広げる（トカゲ・サンショウウオ）。
    foot は虫の足先（ふし）の長さ（None なら脚の太さから決める。0 で足先なし：カニ・アメンボ・指のある脚）"""
    tip_color = tip_color or color
    if foot is None:
        foot = 0.0 if tip_disc > 0.0 or toes > 0 else radius * 3.2
    if _LEG_CAPTURE is not None:
        _LEG_CAPTURE.append(("tube", roots, color, tip_color, radius, tip_disc, toes, foot))
        return
    if tip_disc > 0.0:
        for base, knee, tip in roots:
            water_dimple(mb, Vector(tip), tip_disc)
    for base, knee, tip in roots:
        tube_leg(mb, Vector(base), Vector(knee), Vector(tip), radius, color, tip_color, foot)
        leg_toes(mb, Vector(knee), Vector(tip), toes, radius, tip_color)


def tube_leg(mb, base, knee, tip, radius, color, tip_color, foot):
    """細い脚 1 本：太めのもも → ひざのふくらみ → 細いすね → 地面にそう足先（ふし 2 つ）と、小さなつめ"""
    tube(mb, [base, base.lerp(knee, 0.5), knee], [radius, radius * 1.08, radius * 0.82], 8,
         lambda t, a, p, d: mixc(color, tip_color, 0.12 * t))
    uv_sphere(mb, knee, radius * 0.88, lambda n: mixc(color, tip_color, 0.15), seg=8, rings=5)
    tube(mb, [knee, knee.lerp(tip, 0.5), tip], [radius * 0.78, radius * 0.62, radius * (0.48 if foot > 0 else 0.35)], 8,
         lambda t, a, p, d: mixc(color, tip_color, sstep(0.3, 1.0, t)))
    if foot <= 0.0:
        return
    d = Vector((tip.x - knee.x, tip.y - knee.y, 0.0))
    if d.length < 1e-6:
        d = Vector((1.0 if tip.x >= 0 else -1.0, 0.0, 0.0))
    d.normalize()
    f1 = tip + d * (foot * 0.5) + Vector((0, 0, radius * 0.2))
    f2 = tip + d * foot + Vector((0, 0, -radius * 0.05))
    dark = mixc(tip_color, hexc("#141210"), 0.5)
    tube(mb, [tip, f1, f2], [radius * 0.46, radius * 0.4, radius * 0.3], 6, lambda t, a, p, dd: tip_color)
    uv_sphere(mb, f1, radius * 0.42, lambda n: tip_color, seg=6, rings=4)
    for sgn in (-1, 1):
        side = Vector((-d.y, d.x, 0.0)) * (sgn * radius * 0.35)
        tube(mb, [f2, f2 + d * (radius * 0.7) + side + Vector((0, 0, -radius * 0.35))], [radius * 0.18, 0.0], 4, lambda t, a, p, dd: dark)


def leg_toes(mb, knee, tip, n, radius, color):
    """足先から、地面にそって指を扇に広げる（ひざから足先へ向かう向きを、まん中に）"""
    if n <= 0:
        return
    d = Vector((tip.x - knee.x, tip.y - knee.y, 0.0))
    if d.length < 1e-6:
        d = Vector((1.0 if tip.x >= 0 else -1.0, 0.0, 0.0))
    d.normalize()
    ln = radius * 2.6
    for k in range(n):
        a = (k - (n - 1) / 2) * 0.42
        c, s_ = math.cos(a), math.sin(a)
        dd = Vector((d.x * c - d.y * s_, d.x * s_ + d.y * c, 0.0))
        end = tip + dd * ln + Vector((0, 0, -radius * 0.2))
        tube(mb, [tip, tip.lerp(end, 0.6) + Vector((0, 0, radius * 0.25)), end], [radius * 0.38, radius * 0.3, radius * 0.18], 5,
             lambda t, aa, p, dd_: color)


def leg_part(mb, base, tip, draw):
    """形を自由にかく脚 1 本（左右とも呼ぶ）。draw(m) は、体と同じ座標で m にかく。
    脚を分けるときは、右側（x > 0）だけを、付け根が原点の別メッシュにする（左は Unity で鏡にうつす）"""
    if _LEG_CAPTURE is not None:
        _LEG_CAPTURE.append(("part", Vector(base), Vector(tip), draw))
        return
    draw(mb)


def limb(mb, hip, knee, ankle, upper, lower, foot=None):
    """ひざ（ひじ）で曲がる手足 1 本（左右とも呼ぶ）。upper(m)・lower(m)・foot(m) は、体と同じ座標で、
    付け根からひざ・ひざから足首・足首から先（足の指）を m にかく。
    脚を分けるときは、右側だけを 3 つの別メッシュにする（原点は、それぞれ付け根・ひざ・足首）。
    Unity では、ひざを曲げて足首を動かし（2 本の骨の IK）、足先は地面と平らにする"""
    if _LEG_CAPTURE is not None:
        _LEG_CAPTURE.append(("limb", Vector(hip), Vector(knee), Vector(ankle), upper, lower, foot))
        return
    upper(mb)
    lower(mb)
    if foot:
        foot(mb)


def joint_ball(m, at, r, colfn, t=1.0):
    """関節の玉：向きのちがう筒の口どうしのつなぎ目（ひざ・足首）をつつむ（くさび形のすき間が見えない。曲げても）"""
    c = colfn(t, 0.0, at, Vector((0, 0, -1)))
    uv_sphere(m, at, r, lambda n: c, seg=10, rings=6)


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


def _moved(draw, origin):
    """draw(m) でかいた形を、origin が原点になるように動かした MB"""
    tmp = MB()
    draw(tmp)
    return MB().add(tmp, Matrix.Translation(-origin))


def split_legs(maker, name, leg_variant=None, share=False):
    """maker() を脚なしで作り、右側の脚を 1 本ずつ付け根が原点の別メッシュにする。
    leg_variant(i, mb, base, knee, tip) で足先に飾り（スニーカーなど）をつけた別の脚も作れる。
    share=True なら、形が同じ脚は 1 つのメッシュを使いまわす（だんごむしの 7 対の脚など。描く手間がへる）。
    ひざで曲がる手足（limb）は、上の骨・下の骨・足先の 3 つの別メッシュにする"""
    global _LEG_CAPTURE
    _LEG_CAPTURE = []
    body = maker()
    captured = _LEG_CAPTURE
    _LEG_CAPTURE = None
    right = []
    limbs_ = []
    for entry in captured:
        if entry[0] == "tube":
            _, roots, color, tipc, radius, disc, toes, foot = entry
            for base, knee, tip in roots:
                if base[0] > 1e-6:
                    right.append(("tube", Vector(base), Vector(knee), Vector(tip), color, tipc, radius, disc, toes, foot))
        elif entry[0] == "part":
            if entry[1].x > 1e-6:
                right.append(entry)
        elif entry[1].x > 1e-6:
            limbs_.append(entry[1:])
    out = [body]
    rig = []
    shared = {}
    for i, leg in enumerate(right):
        b = leg[1]
        if leg[0] == "part":
            _, b, t, draw = leg
            ob = build(_moved(draw, b), f"{name}_Leg{i + 1}")
            out.append(ob)
            rig.append((ob.name, b, t))
            continue
        _, b, k, t, color, tipc, radius, disc, toes, foot = leg
        key = (tuple(round(c, 4) for c in (k - b)), tuple(round(c, 4) for c in (t - b)), tuple(color), tuple(tipc), radius, disc, toes, foot)
        if share and key in shared:
            rig.append((shared[key], b, t))
            continue
        mb = MB()
        tube_leg(mb, Vector((0, 0, 0)), k - b, t - b, radius, color, tipc, foot)
        leg_toes(mb, k - b, t - b, toes, radius, tipc)
        if disc > 0.0:
            water_dimple(mb, t - b, disc)
        ob = build(mb, f"{name}_Leg{i + 1}")
        shared[key] = ob.name
        out.append(ob)
        rig.append((ob.name, b, t))
        if leg_variant:
            mb2 = MB()
            tube_leg(mb2, Vector((0, 0, 0)), k - b, t - b, radius, color, tipc, 0.0)   # 足先には、スニーカーをはく
            leg_variant(i, mb2, Vector((0, 0, 0)), k - b, t - b)
            out.append(build(mb2, f"{name}_Leg{i + 1}_Sneaker"))
    lim = []
    for j, (hip, knee, ankle, upper, lower, foot) in enumerate(limbs_):
        nm = f"{name}_Limb{j + 1}"
        u = build(_moved(upper, hip), nm + "U")
        lo = build(_moved(lower, knee), nm + "L")
        out += [u, lo]
        fo = None
        if foot:
            fo = build(_moved(foot, ankle), nm + "F")
            out.append(fo)
        lim.append((u.name, lo.name, fo.name if fo else None, hip, knee, ankle))
    if rig:
        RIG[name] = rig
    if lim:
        LIMBS[name] = lim
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
        # 触角：ふしのある、くの字に曲がった触角
        ant = [Vector((0.05 * sx, 0.29, 0.04)), Vector((0.1 * sx, 0.34, 0.075)), Vector((0.14 * sx, 0.38, 0.07)), Vector((0.17 * sx, 0.42, 0.035))]
        tube(mb, ant, [0.011, 0.009, 0.007, 0.0], 5, lambda t, a, p, d: mixc(hexc("#4a505e"), hexc("#7a8090"), 0.3 * abs(math.sin(t * 12))))
        # おしりの小さな突起
        tube(mb, [Vector((0.05 * sx, -0.29, 0.015)), Vector((0.075 * sx, -0.33, 0.01))], [0.018, 0.0], 5, lambda t, a, p, d: plate)
    # 7 対の小さな脚：殻のふちの下から少しだけ見える（同じ形なので、Unity では 1 つのメッシュを使いまわす）
    roots = []
    for k in range(7):
        y = -0.2 + 0.4 * k / 6
        w = 0.085 + 0.015 * math.sin(math.pi * k / 6)
        for sx in (-1, 1):
            roots.append(((w * sx, y, 0.025), ((w + 0.05) * sx, y + 0.01, 0.04), ((w + 0.075) * sx, y + 0.02, -0.008)))
    legs(mb, roots, hexc("#8a8478"), hexc("#c4bcaa"), radius=0.012)
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
    """アマガエル：つやのある黄緑のなめらかな体、鼻先から目を通って肩までの茶色のすじ、白いおなか、
    大きな目、指先のまるい吸盤。うしろ足は、すわると Z の字にたたむ（ひざで曲がる手足。跳ぶと、うしろへのびる）"""
    mb = MB()
    green = hexc("#79c84e")
    dark = hexc("#5a9e3a")
    belly = hexc("#f4f1d8")
    stripe = hexc("#4b3a2c")
    pad = hexc("#b8e48a")

    # 体：おしりから鼻先まで、ひとつながり（すわると頭が少し高い）
    spine = [(-0.44, 0.2), (-0.36, 0.24), (-0.22, 0.29), (-0.04, 0.33), (0.14, 0.36), (0.3, 0.37), (0.42, 0.355), (0.51, 0.33), (0.57, 0.3)]
    rx = [0.12, 0.25, 0.35, 0.4, 0.4, 0.39, 0.36, 0.3, 0.17]
    rz = [0.1, 0.17, 0.24, 0.29, 0.29, 0.25, 0.2, 0.15, 0.09]
    pts = [Vector((0, y, z)) for y, z in spine]

    def col(p, n, t):
        c = mixc(green, dark, sstep(0.4, 1.0, n.z) * 0.3)
        c = mixc(c, hexc("#a6e070"), 0.25 * sstep(0.2, -0.3, n.z))
        # 鼻先から目の下を通って、肩までの茶色のすじ（体の横）
        band = 0.32 - 0.25 * sstep(0.5, 1.0, t)
        if t > 0.48 and abs(n.x) > 0.55 and abs(n.z - band) < 0.11:
            c = mixc(c, stripe, 0.85 * sstep(0.48, 0.58, t))
        # 口のすじ
        if t > 0.55 and n.z < -0.05 and abs(n.z + 0.12) < 0.04 and n.y > -0.3:
            c = hexc("#3a5a2a")
        return mixc(c, belly, sstep(-0.15, -0.55, n.z))
    loft(mb, pts, rx, rz, col, seg=30)
    for sx in (-1, 1):
        # 目：頭の上にもり上がる
        uv_sphere(mb, Vector((0.2 * sx, 0.36, 0.55)), 0.11, lambda n: mixc(green, dark, 0.2), seg=16, rings=10)
        cute_eye(mb, (0.215 * sx, 0.41, 0.6), 0.078, (0.35 * sx, 1, 0.15))
        uv_sphere(mb, Vector((0.28 * sx, 0.48, 0.42)), 0.045, lambda n: hexc("#ffb3b3"), seg=8, rings=5, scale=Vector((0.6, 0.3, 0.5)))   # ほっぺ
        uv_sphere(mb, Vector((0.06 * sx, 0.6, 0.36)), 0.012, lambda n: hexc("#2a3a1a"), seg=6, rings=4)   # 鼻のあな

        # 前足：うで → 手首 → 4 本の指と吸盤
        sh = Vector((0.24 * sx, 0.27, 0.22))
        el = Vector((0.34 * sx, 0.27, 0.11))
        wr = Vector((0.32 * sx, 0.45, 0.035))

        def arm(m, sh=sh, el=el):
            tube(m, [sh, el], [0.06, 0.042], 10, lambda t, a, p, d: green)
            uv_sphere(m, el, 0.044, lambda n: green, seg=8, rings=5)

        def forearm(m, el=el, wr=wr):
            tube(m, [el, el.lerp(wr, 0.5), wr], [0.042, 0.036, 0.03], 8, lambda t, a, p, d: mixc(green, pad, 0.15 * t))

        def hand(m, wr=wr, sx=sx):
            for k in range(4):
                ang = (k - 1.5) * 0.42 - 0.15 * sx
                tip = wr + Vector((math.sin(ang) * 0.085 * sx, math.cos(ang) * 0.085, -0.02))
                tube(m, [wr, tip], [0.016, 0.011], 6, lambda t, a, p, d: mixc(green, pad, 0.4))
                uv_sphere(m, tip, 0.02, lambda n: pad, seg=8, rings=5, scale=Vector((1.0, 1.0, 0.6)))
        limb(mb, sh, el, wr, arm, forearm, hand)

        # うしろ足：もも（前・外へ）→ すね（うしろへ）→ 長い足（前へ）と、吸盤のついた 5 本の指
        hip = Vector((0.22 * sx, -0.3, 0.2))
        knee = Vector((0.42 * sx, -0.02, 0.15))
        ank = Vector((0.36 * sx, -0.36, 0.06))

        def thigh(m, hip=hip, knee=knee):
            tube(m, [hip, hip.lerp(knee, 0.5) + Vector((0, 0, 0.03)), knee], [0.12, 0.1, 0.065], 12,
                 lambda t, a, p, d: mixc(green, dark, 0.15))

        def shank(m, knee=knee, ank=ank):
            tube(m, [knee, knee.lerp(ank, 0.5) + Vector((0, 0, 0.02)), ank], [0.065, 0.055, 0.04], 10,
                 lambda t, a, p, d: mixc(green, dark, 0.2))
            uv_sphere(m, knee, 0.068, lambda n: mixc(green, dark, 0.15), seg=10, rings=6)

        def hind_foot(m, ank=ank, sx=sx):
            heel = ank + Vector((0.0, 0.2, -0.035))
            tube(m, [ank, ank.lerp(heel, 0.5), heel], [0.04, 0.034, 0.028], 8, lambda t, a, p, d: mixc(green, pad, 0.2 * t))
            for k in range(5):
                ang = (k - 2) * 0.28 + 0.12 * sx
                ln = 0.13 + 0.03 * (2 - abs(k - 2))
                tip = heel + Vector((math.sin(ang) * ln * sx, math.cos(ang) * ln, -0.014))
                tube(m, [heel, heel.lerp(tip, 0.6) + Vector((0, 0, 0.008)), tip], [0.02, 0.016, 0.012], 6, lambda t, a, p, d: mixc(green, pad, 0.4))
                uv_sphere(m, tip, 0.024, lambda n: pad, seg=8, rings=5, scale=Vector((1.0, 1.0, 0.6)))   # 吸盤
        limb(mb, hip, knee, ank, thigh, shank, hind_foot)
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
        legs(mb, roots, shell, light, radius=0.03, foot=0.0)
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


def surface_finder(mb):
    """mb の、上から見たいちばん上の面の高さ（x, y）-> z（面がなければ 0）"""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromPolygons([v.copy() for v in mb.V], [list(f) for f in mb.F])

    def top(x, y):
        hit = tree.ray_cast(Vector((x, y, 100.0)), Vector((0, 0, -1)), 200.0)
        return hit[0].z if hit[0] is not None else 0.0
    return top


def make_anthill():
    mb = MB()
    prof = [(1.35, 0.0), (1.0, 0.14), (0.6, 0.3), (0.32, 0.34), (0.18, 0.2), (0.0, 0.18)]

    def col(t, a, p):
        rr = math.hypot(p.x, p.y)
        c = mixc(hexc("#8a6a48"), hexc("#b08a60"), 0.5 + 0.5 * noise.noise(p * 6.0))
        return mixc(c, hexc("#2a1c12"), sstep(0.3, 0.18, rr))
    lathe(mb, prof, 28, col, rfn=lambda t, a: 1.0 + 0.08 * math.sin(a * 5 + 1))
    top = surface_finder(mb)
    rnd = random.Random(4)
    for i in range(30):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0.4, 1.4)
        x, y = math.cos(a) * r, math.sin(a) * r
        rad = rnd.uniform(0.03, 0.06)
        # 土のつぶは、山の面にのせる（浮かない）
        uv_sphere(mb, Vector((x, y, max(top(x, y), 0.0) + rad * 0.35)), rad, lambda n: hexc("#9c7a52"), seg=6, rings=4)
    return build(mb, "AntHill")


def loft(mb, pts, rx, rz, colfn, seg=24, cap0=True, cap1=True):
    """背骨 pts にそって、だ円の輪（横 rx・たて rz）をつないだ、なめらかな体。colfn(p, n, t) -> 色。
    はしは、とがった極でとじる"""
    n = len(pts)
    start = len(mb.F)
    rings = []
    for i in range(n):
        p = pts[i]
        tng = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        # 背骨は左右対称の面（x = 0）にあるので、横の向きはいつも x（首が真上を向いても、輪がねじれない）
        side = Vector((1, 0, 0)) - tng * tng.x
        side.normalize()
        # 輪の向きは、背骨にそって、つながったまま回す（背骨がうしろへもどる所でも、うら返さない。うら返すと首が折れて見える）
        upv = side.cross(tng).normalized()
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
    # 面は外向きに（この順でつなぐと内向きになるので、裏返す。内向きのままだと、ゲームでは体が黒く見える）
    for f in range(start, len(mb.F)):
        mb.F[f] = tuple(reversed(mb.F[f]))
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
    # ツバメ：低く細長い、流線形の体に、平たい頭
    "tsubame": ([(-0.26, 0.3), (-0.18, 0.29), (-0.08, 0.28), (0.02, 0.285), (0.1, 0.31), (0.15, 0.35), (0.18, 0.39),
                 (0.205, 0.42), (0.235, 0.43), (0.265, 0.425), (0.285, 0.415)],
                [0.055, 0.1, 0.13, 0.135, 0.125, 0.105, 0.1, 0.11, 0.11, 0.09, 0.05],
                [0.05, 0.09, 0.115, 0.12, 0.115, 0.1, 0.095, 0.1, 0.095, 0.08, 0.045]),
    # カモメ：すらりとした白い体に、灰色のせなか
    "gull": ([(-0.26, 0.36), (-0.18, 0.33), (-0.08, 0.30), (0.03, 0.305), (0.12, 0.345), (0.175, 0.41), (0.21, 0.475),
              (0.24, 0.52), (0.275, 0.54), (0.31, 0.535), (0.335, 0.52)],
             [0.058, 0.1, 0.14, 0.15, 0.138, 0.1, 0.088, 0.1, 0.104, 0.088, 0.05],
             [0.05, 0.094, 0.135, 0.145, 0.132, 0.095, 0.086, 0.098, 0.1, 0.084, 0.047]),
    # シマエナガ：首のない、まるい雪玉のような体（頭と胴がひとつながり）に、小さなくちばしと長い尾
    "enaga": ([(-0.2, 0.36), (-0.14, 0.33), (-0.06, 0.31), (0.02, 0.31), (0.09, 0.335), (0.135, 0.375), (0.16, 0.42),
               (0.18, 0.46), (0.2, 0.48), (0.228, 0.48), (0.25, 0.47)],
              [0.07, 0.14, 0.185, 0.2, 0.195, 0.18, 0.168, 0.16, 0.152, 0.13, 0.07],
              [0.06, 0.13, 0.18, 0.19, 0.185, 0.17, 0.158, 0.15, 0.142, 0.12, 0.065]),
}
BIRD_HEAD = {"sparrow": 8, "crow": 8, "pigeon": 8, "flamingo": 9, "raichou": 8, "tsubame": 8, "gull": 8, "enaga": 8}   # 頭のまん中の背骨の番号


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
    "tsubame": dict(back="#1c2850", belly="#f4efe4", cap="#1c2850", leg="#3a3030", beak="#1a1a1e"),
    "gull": dict(back="#9ba7b4", belly="#f7f8fa", cap="#fbfbfc", leg="#e9c845", beak="#f2c53d"),
    "enaga": dict(back="#26222a", belly="#faf9f6", cap="#fdfdfb", leg="#3a3436", beak="#1a1a1c"),
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
        if style == "tsubame":
            rel = p - head_c
            if t > 0.62:
                # おでこと、のどは赤茶色。頭のてっぺんと目のまわりは、光る紺色
                if (n.y > 0.55 and n.z > -0.1 and rel.y > 0.02) or (n.z < -0.2 and n.y > -0.3):
                    return hexc("#b5432c")
                return mixc(back, hexc("#3a4a8a"), sstep(0.2, 0.9, n.z) * 0.6)
            if t > 0.5 and n.y > 0.0 and n.z < -0.1:
                return hexc("#b5432c") if t > 0.56 else hexc("#1c2850")   # のどの赤と、胸の紺の帯
            if n.z < -0.2:
                return belly                                              # 白いおなか
            return mixc(back, hexc("#3a4a8a"), sstep(0.3, 0.9, n.z) * 0.55 + 0.1 * math.sin(p.y * 40))
        if style == "enaga":
            if t > 0.55:
                return mixc(cap, hexc("#ece8e4"), 0.25 * sstep(-0.3, -0.9, n.z))   # まっ白な頭
            if n.z > 0.45:
                return back if t > 0.28 else hexc("#c9939b")                         # 黒いせなかと、うす赤むらさきの腰
            if n.z > 0.15 and abs(n.x) > 0.35:
                return hexc("#c9939b")                                               # 肩の、うす赤むらさき
            if t < 0.45 and -0.45 < n.z < 0.15 and abs(n.x) > 0.5:
                return hexc("#eed8da")                                               # わき腹は、ほんのり赤い
            return belly
        if style == "gull":
            if t > 0.55:
                return mixc(cap, hexc("#e6ebf0"), 0.35 * sstep(-0.2, -0.8, n.z))   # 白い頭
            return mixc(belly, back, sstep(0.22, 0.48, n.z))                       # 灰色のせなかと、白いおなか
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
        blen = {"crow": 0.2, "sparrow": 0.085, "pigeon": 0.07, "raichou": 0.05, "tsubame": 0.04, "gull": 0.13, "enaga": 0.04}[style]
        brad = {"crow": 0.045, "sparrow": 0.032, "pigeon": 0.022, "raichou": 0.026, "tsubame": 0.03, "gull": 0.03, "enaga": 0.022}[style]
        tube(mb, [base, base + Vector((0, blen * 0.55, -0.004)), base + Vector((0, blen, -0.028 if style == "crow" else -0.018))],
             [brad, brad * 0.62, 0.0], 12, lambda t, a, p, d: beak_col)
        if style == "pigeon":
            uv_sphere(mb, base + Vector((0, 0.012, 0.012)), 0.018, lambda n: hexc("#f0ece6"), seg=8, rings=5)   # 白い鼻こぶ
        if style == "gull":
            # くちばしの先の、赤と黒のしるし（ひなは、ここをつついて、えさをねだる）
            uv_sphere(mb, base + Vector((0, blen * 0.8, -0.022)), 0.013, lambda n: hexc("#d8322a"), seg=8, rings=5)
            uv_sphere(mb, base + Vector((0, blen * 0.74, -0.012)), 0.011, lambda n: hexc("#1b1b1f"), seg=8, rings=5)
    # 目
    er = {"crow": 0.026, "sparrow": 0.03, "pigeon": 0.014, "flamingo": 0.018, "raichou": 0.019, "tsubame": 0.024, "gull": 0.022,
          "enaga": 0.022}[style]
    ex = {"crow": 0.095, "sparrow": 0.095, "pigeon": 0.07, "flamingo": 0.045, "raichou": 0.08, "tsubame": 0.085, "gull": 0.088,
          "enaga": 0.098}[style]
    for sx in (-1, 1):
        if style == "pigeon":
            uv_sphere(mb, head_c + Vector((ex * sx * 0.97, 0.034, 0.022)), er * 1.35, lambda n: hexc("#e8762e"), seg=10, rings=6)   # だいだい色の目
        cute_eye(mb, head_c + Vector((ex * sx, 0.035 if style != "flamingo" else 0.02, 0.022)), er, (0.75 * sx, 0.7, 0.1), white=False)
    # 尾羽：羽を 1 枚ずつ、扇に重ねる
    nfe = 7
    tl = {"crow": 0.3, "sparrow": 0.24, "pigeon": 0.26, "flamingo": 0.12, "raichou": 0.13, "tsubame": 0.2, "gull": 0.24, "enaga": 0.6}[style]
    root_z = spine[0][1]
    for k in range(nfe):
        u = (k / (nfe - 1)) * 2 - 1
        root = Vector((u * 0.035, spine[0][0] + 0.02, root_z))
        tip = root + Vector((u * 0.07, -tl, -0.045 + 0.01 * abs(u)))
        if style == "tsubame":
            # ツバメの尾：まん中はみじかく、いちばん外の 2 本は、長くのびて二またに分かれる
            ln = tl * (2.4 if abs(u) > 0.9 else 0.85 + 0.25 * abs(u))
            tip = root + Vector((u * (0.13 if abs(u) > 0.9 else 0.07), -ln, -0.02))
        if style == "enaga":
            # シマエナガの尾：細く長い。まん中の羽がいちばん長く、外の羽ほど短い
            tip = root + Vector((u * 0.03, -tl * (1.0 - 0.32 * abs(u)), 0.02 - 0.01 * abs(u)))
        mid = root.lerp(tip, 0.5) + Vector((0, 0, 0.01))
        if style == "crow":
            fc = mixc(back, hexc("#101015"), 0.3 + 0.4 * abs(u))
        elif style == "pigeon":
            fc = hexc("#2a2a30") if False else back
        elif style == "raichou":
            fc = hexc("#1e1a18")   # 尾は黒
        elif style == "tsubame":
            fc = mixc(back, hexc("#10162c"), 0.4 * abs(u))
        elif style == "gull":
            fc = belly   # 白い尾に、黒い帯
        elif style == "enaga":
            fc = hexc("#1e1b1e")   # 黒い尾（外の羽は、外がわのへりが白い）
        else:
            fc = mixc(back, hexc("#4a2e1c"), 0.3 + 0.4 * abs(u))
        ribbon(mb, [root, mid, tip], [0.045, 0.06, 0.04] if style != "enaga" else [0.035, 0.04, 0.03], [Vector((1, 0, 0))] * 3,
               lambda tt, v, p, fc=fc, u=u: (hexc("#f4f2ee") if abs(u) > 0.6 and v * u > 0.3 else fc) if style == "enaga"
               else mixc(fc, hexc("#2a2a30"), sstep(0.7, 0.9, tt)) if style == "pigeon"
               else mixc(fc, hexc("#1b1b20"), sstep(0.55, 0.62, tt) * (1.0 - sstep(0.86, 0.93, tt))) if style == "gull" else fc,
               fold=0.12, normal_vecs=[Vector((0, 0, 1))] * 3)
    mb.xform(Matrix.Diagonal((L, L, L, 1)))
    bird_legs(mb, style, L, pal)
    return build(mb, name, smooth=True, sharp_deg=75)


# 鳥の脚（体の大きさ L の倍率をかける前の座標）：付け根（体の中）・かかと（ひざのように見える、うしろへ曲がる所）・足首
BIRD_LEG = {
    "default": ((0.05, 0.035, 0.2), (0.058, -0.03, 0.1), (0.06, 0.03, 0.014)),
    "flamingo": ((0.05, 0.03, 0.94), (0.055, -0.075, 0.49), (0.055, 0.03, 0.016)),
}


def bird_legs(mb, style, L, pal):
    """鳥の脚 1 対：もも（羽毛の中から出る）・すね（うろこ）・足の指（前に 3 本、うしろに 1 本、先につめ）。
    ひざで曲がる手足（limb）なので、Unity では 1 歩ずつ地面をふんで歩き、飛ぶときは足をたたむ"""
    leg = hexc(pal["leg"])
    dark = mixc(leg, hexc("#1b1b1f"), 0.45)
    claw = hexc("#2a2622") if style != "crow" else hexc("#0e0e12")
    fluffy = style == "raichou"   # ライチョウの足は、白い羽毛でふわふわ（雪の上を歩くため）
    flam = style == "flamingo"
    webbed = flam or style == "gull"
    web_col = hexc("#e27c8a") if flam else mixc(leg, hexc("#c99a20"), 0.4)
    hip0, heel0, ank0 = BIRD_LEG["flamingo" if flam else "default"]
    thick = 1.0 if not flam else 1.15
    feather = hexc(pal["belly"])
    for sx in (-1, 1):
        hip = Vector((hip0[0] * sx, hip0[1], hip0[2])) * L
        heel = Vector((heel0[0] * sx, heel0[1], heel0[2])) * L
        ank = Vector((ank0[0] * sx, ank0[1], ank0[2])) * L

        def upper(m, hip=hip, heel=heel):
            # ももは、つけ根が羽毛（おなかの色）で、かかとの近くで脚の色になる
            mid = hip.lerp(heel, 0.45)
            r0 = (0.034 if not flam else 0.024) * L
            tube(m, [hip, mid, heel], [r0, r0 * (0.55 if not fluffy else 0.9), 0.0135 * L * thick * (1.6 if fluffy else 1.0)], 9,
                 lambda t, a, p, d: feather if (t < 0.4 or fluffy) and not flam else mixc(leg, dark, 0.15 * (1 - t)))
            uv_sphere(m, heel, 0.015 * L * thick * (1.7 if fluffy else 1.0), lambda n: feather if fluffy else mixc(leg, dark, 0.25), seg=8, rings=5)

        def lower(m, heel=heel, ank=ank):
            # すね：うろこの、うすい輪のもよう
            r = 0.012 * L * thick * (1.8 if fluffy else 1.0)
            tube(m, [heel, heel.lerp(ank, 0.5), ank], [r, r * 0.9, r * 0.85], 8,
                 lambda t, a, p, d: feather if fluffy else mixc(leg, dark, 0.35 * sstep(0.6, 0.95, abs(math.sin(t * 22.0)))))

        def foot(m, ank=ank):
            fr = 0.0085 * L * (1.5 if fluffy else 1.0)
            uv_sphere(m, ank, fr * 1.25, lambda n: feather if fluffy else leg, seg=8, rings=5)
            toes = []
            for ang in (-0.5, 0.0, 0.5):
                ln = (0.085 if ang == 0.0 else 0.07) * L * (1.15 if flam else 1.0)
                tip = ank + Vector((math.sin(ang) * ln, math.cos(ang) * ln, -0.012 * L))
                toes.append(tip)
                knuckle = ank.lerp(tip, 0.55) + Vector((0, 0, 0.006 * L))
                tube(m, [ank, knuckle, tip], [fr, fr * 0.8, fr * 0.45], 6,
                     lambda t, a, p, d: mixc(leg, claw, sstep(0.82, 0.95, t)))
            back = ank + Vector((0, -0.05 * L, -0.01 * L))
            tube(m, [ank, ank.lerp(back, 0.6) + Vector((0, 0, 0.004 * L)), back], [fr * 0.9, fr * 0.7, fr * 0.4], 6,
                 lambda t, a, p, d: mixc(leg, claw, sstep(0.8, 0.95, t)))
            if webbed:
                # 水かき：前の 3 本の指のあいだ
                for a_, b_ in ((toes[0], toes[1]), (toes[1], toes[2])):
                    i0 = m.v(ank + Vector((0, 0.01 * L, 0.001 * L)), web_col)
                    i1 = m.v(a_.lerp(ank, 0.15), web_col)
                    i2 = m.v(b_.lerp(ank, 0.15), web_col)
                    m.f(i0, i1, i2)
                    m.f(i0, i2, i1)
        limb(mb, hip, heel, ank, upper, lower, foot)


def bird_wing_colors(style):
    if style == "crow":
        return hexc("#1d1d26"), hexc("#141419"), hexc("#0b0b0f"), hexc("#3b4470")
    if style == "pigeon":
        return hexc("#b4bac6"), hexc("#7a808d"), hexc("#2a2a30"), hexc("#d0d4dc")
    if style == "flamingo":
        return hexc("#f6a3ae"), hexc("#1b1b20"), hexc("#0e0e12"), hexc("#ff8091")
    if style == "raichou":
        return hexc("#6a5646"), hexc("#f6f4ee"), hexc("#d8d2c8"), hexc("#8a7660")   # 風切り羽は白
    if style == "tsubame":
        return hexc("#1e2a54"), hexc("#141a34"), hexc("#0c0f1e"), hexc("#3a4a8a")
    if style == "gull":
        return hexc("#a3aebb"), hexc("#8e99a6"), hexc("#17171b"), hexc("#c9d0d8")
    if style == "owl":
        return hexc("#8a7862"), hexc("#6a5644"), hexc("#3e3226"), hexc("#d8ccb6")
    if style == "enaga":
        return hexc("#2a2628"), hexc("#1e1b1e"), hexc("#f2f0ec"), hexc("#c9939b")   # 黒い羽に、白いへり。肩はうす赤むらさき
    return hexc("#a2714a"), hexc("#5a3a24"), hexc("#2e2018"), hexc("#c8a07a")


def make_bird_wing_folded(name, L, crow):
    """地上で体の横にたたんだ翼（右がわ。体と同じ座標）。肩から尾のほうへ、体の表面にそってのび、
    風切り羽の先は尾の上で重なる。雨おおい羽（明るい）・帯（スズメの白・ハトの黒）・1 枚ずつの風切り羽のすじ"""
    style = bird_style(crow)
    mb = MB()
    cov, flight, edge, sheen = bird_wing_colors(style)
    spine, _, _ = bird_sections(style)
    body_z = spine[3][1]
    y0, y1 = spine[4][0] + 0.03, spine[0][0] - (0.22 if style == "tsubame" else 0.1)
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
            if style == "enaga" and 0.3 < u < 0.58 and v > 0.55 and (i % 3) != 0:
                c = hexc("#f2f0ec")   # シマエナガの、白いへりの羽
            if style == "gull" and u > 0.72:
                c = hexc("#f2f4f6") if (u > 0.93 and 0.3 < v < 0.7) else hexc("#1b1b20")   # 黒い羽の先に、白い点
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
    slim = style in ("tsubame", "gull")
    gull = style == "gull"
    owl = style == "owl"
    mb = MB()
    span = 0.66 if gull else 0.5 if crowish else 0.58 if slim else 0.56 if owl else 0.42
    cov, flight, edge, sheen = bird_wing_colors(style)
    rows = 6
    cols = 10
    grid = []
    for i in range(cols + 1):
        x = span * 0.62 * i / cols
        lead = 0.0 - 0.02 * (x / span) ** 2
        chord = (0.15 if crowish else 0.09 if slim else 0.17 if owl else 0.13) * (1.0 - 0.45 * (x / (span * 0.62)) ** 1.5)
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
            if owl and (i % 3) == 1:
                c = mixc(c, hexc("#e6dccb"), 0.5)   # フクロウの羽の、白っぽいしま
            row.append(mb.v((x, y, z), c))
        grid.append(row)
    for i in range(cols):
        for j in range(rows):
            mb.f(grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
    nf = 12 if crowish else 10
    for k in range(nf):
        u = k / (nf - 1)
        x0 = span * 0.6 * u
        chord0 = (0.15 if crowish else 0.09 if slim else 0.13) * (1.0 - 0.45 * min(1.0, u) ** 1.5)
        root = Vector((x0, -chord0 * 0.55, 0.012))
        ang = math.radians(-20 + (88 if slim else 75) * u ** 1.4)
        flen = (0.17 if crowish else 0.12 if slim else 0.15) + (0.2 if crowish else 0.24 if slim else 0.16) * u ** 1.2
        d = Vector((math.sin(ang), -math.cos(ang), 0.0))
        tip = root + d * flen
        mid = root.lerp(tip, 0.55)
        wid = 0.05 if crowish else 0.034 if slim else 0.045
        if crowish and u > 0.75:
            wid *= 0.7
        fc = mixc(flight, edge, 0.4 * u)
        if gull and u > 0.6:
            fc = edge   # 外がわの羽は黒い
        if style == "flamingo" and u < 0.35:
            fc = mixc(cov, hexc("#ff7f8f"), 0.4)   # 内がわの羽はピンク、外がわの羽は黒
        ribbon(mb, [root, mid, tip], [wid, wid * 1.05, wid * 0.45], [Vector((math.cos(ang), math.sin(ang), 0))] * 3,
               lambda tt, v, p, fc=fc, u=u: (hexc("#f2f4f6") if gull and u > 0.6 and tt > 0.9 else mixc(fc, edge, sstep(0.75, 1.0, tt) * 0.6)), fold=0.08,
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
    tube(mb, pts, radii, 26, col, oval=(1.25, 0.75))
    for sx in (-1, 1):
        cute_eye(mb, (0.11 * sx, 0.9, 0.29), 0.04, (0.7 * sx, 0.6, 0.15), white=False)
    # 口のまわり（にっこり）
    tube(mb, [Vector((-0.07, 1.05, 0.18)), Vector((0, 1.09, 0.17)), Vector((0.07, 1.05, 0.18))], [0.01, 0.01, 0.01], 4, lambda t, a, p, d: hexc("#2a1e16"))
    roots = []
    for (y, ky, ty) in ((0.42, 0.55, 0.7), (-0.42, -0.5, -0.32)):
        for sx in (-1, 1):
            roots.append(((0.16 * sx, y, 0.18), (0.38 * sx, ky, 0.2), (0.5 * sx, ty, 0.0)))
    legs(mb, roots, brown, hexc("#2a201a"), radius=0.05, toes=5)
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
    ("yamori", ["Yamori"]),
    ("mukade", ["Mukade_Portrait"]),
    ("hasamimushi", ["Hasamimushi", "Hasamimushi_Tail_P"]),
    ("hae", ["Hae_Body", "Hae_Wing_R", "Hae_Wing_L", "Hae_Arm_R", "Hae_Arm_L"]),
    ("tsubame", ["Tsubame_Body", "Tsubame_Wing_R", "Tsubame_Wing_L"]),
    ("nezumi", ["Nezumi"]),
    ("yadokari", ["Yadokari", "Yadokari_Crab"]),
    ("shiomaneki", ["Shiomaneki", "Shiomaneki_Claw_P"]),
    ("kamome", ["Kamome_Body", "Kamome_Wing_R", "Kamome_Wing_L"]),
    ("hitode", ["Hitode"] + [f"Hitode_Arm_P{k}" for k in range(5)]),
    ("uni", ["Uni", "Uni_SpineA", "Uni_SpineB", "Uni_SpineC"]),
    ("funamushi", ["Funamushi"]),
    ("manta", ["Manta_Body", "Manta_Fin_R", "Manta_Fin_L"]),
    ("kitsune", ["Kitsune"]),
    ("fukurou", ["Fukurou_Body", "Fukurou_Head_P", "Fukurou_Wing_R", "Fukurou_Wing_L"]),
    ("tanuki", ["Tanuki"]),
    ("ga", ["Ga_Body", "Ga_Wing_P", "Ga_Wing_L"]),
    ("kanabun", ["Kanabun"]),
    ("suzumushi", ["Suzumushi", "Suzumushi_Wing_P", "Suzumushi_Wing_L"]),
    ("korogi", ["Korogi", "Korogi_Wing_P", "Korogi_Wing_L"]),
    ("minomushi", ["Minomushi", "Minomushi_Larva_P", "Minomushi_Thread_P"]),
    ("zatoumushi", ["Zatoumushi"]),
    ("kirigirisu", ["Kirigirisu", "Kirigirisu_Wing_P", "Kirigirisu_Wing_L"]),
    ("inoshishi", ["Inoshishi"]),
    ("uribou", ["Uribou"]),
    ("shimaenaga", ["Shimaenaga_Body", "Shimaenaga_Wing_R", "Shimaenaga_Wing_L"]),
]

# 図鑑の絵で脚をつける体（脚メッシュのコピーを付け根に置く）。スニーカーグモだけ脚の種類がちがう
PORTRAIT_LEGS = {
    "ant": "Ant", "ladybug": "Ladybug", "beetle": "Beetle", "waterstrider": "WaterStrider", "crab": "Crab",
    "grasshopper": "Grasshopper", "otoshibumi": "Otoshibumi", "spider": "Spider", "mantis": "Mantis",
    "ant_helmet": "Ant", "spider_sneaker": "Spider",
    "kamikiri": "Kamikiri", "kuwagata": "Kuwagata", "kamemushi": "Kamemushi", "tokage": "Tokage",
    "okera": "Okera", "nanafushi": "Nanafushi", "hanakamakiri": "Hanakamakiri", "kameleon": "Kameleon", "herakuresu": "Herakuresu",
    "risu": "Risu", "sanshouuo": "Sanshouuo", "higurashi": "Higurashi", "maimaikaburi": "Maimaikaburi",
    "pillbug": "PillBug", "frog": "Frog", "gengorou": "Gengorou", "mogura": "Mogura", "harinezumi": "Harinezumi", "okojo": "Okojo",
    "nakiusagi": "Nakiusagi", "sparrow": "Sparrow_Body", "crow": "Crow_Body", "hato": "Hato_Body", "flamingo": "Flamingo_Body",
    "raichou": "Raichou_Body",
    "yamori": "Yamori", "hasamimushi": "Hasamimushi", "nezumi": "Nezumi", "tsubame": "Tsubame_Body",
    "yadokari": "Yadokari", "shiomaneki": "Shiomaneki", "funamushi": "Funamushi", "kamome": "Kamome_Body",
    "kitsune": "Kitsune", "tanuki": "Tanuki", "kanabun": "Kanabun", "fukurou": "Fukurou_Body",
    "suzumushi": "Suzumushi", "korogi": "Korogi",
    "zatoumushi": "Zatoumushi", "kirigirisu": "Kirigirisu", "inoshishi": "Inoshishi", "uribou": "Uribou", "shimaenaga": "Shimaenaga_Body",
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
    for bird, L in (("Sparrow", 5.5), ("Crow", 18.0), ("Hato", 13.0), ("Flamingo", 20.0), ("Raichou", 15.0), ("Tsubame", 7.0), ("Kamome", 19.0),
                    ("Fukurou", 20.0), ("Shimaenaga", 4.8)):
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
    tail = objs.get("Hasamimushi_Tail")
    if tail:
        # ハサミムシ：はさみを少し持ち上げた姿
        c = tail.copy()
        c.data = tail.data
        c.name = "Hasamimushi_Tail_P"
        bpy.context.scene.collection.objects.link(c)
        c.location = HASAMI_JOINT
        c.rotation_euler = (0.3, 0, 0)
        objs[c.name] = c
    hw = objs.get("Hae_Wing")
    if hw:
        # イエバエ：はねは、せなかの上で、うしろへたたむ
        for nm, sx in (("Hae_Wing_R", 1), ("Hae_Wing_L", -1)):
            c = hw.copy()
            c.data = hw.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (HAE_WING_ROOT.x * sx, HAE_WING_ROOT.y, HAE_WING_ROOT.z)
            c.rotation_euler = (0, -0.15 * sx, -0.95 * sx)
            objs[nm] = c
        hw.hide_render = True
    ha = objs.get("Hae_Arm")
    if ha:
        for nm, sx in (("Hae_Arm_R", 1), ("Hae_Arm_L", -1)):
            c = ha.copy()
            c.data = ha.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (HAE_SHOULDER.x * sx, HAE_SHOULDER.y, HAE_SHOULDER.z)
            objs[nm] = c
        ha.hide_render = True
    cl = objs.get("Shiomaneki_Claw")
    if cl:
        # シオマネキ：大きなはさみを、少し持ち上げた姿
        c = cl.copy()
        c.data = cl.data
        c.name = "Shiomaneki_Claw_P"
        bpy.context.scene.collection.objects.link(c)
        c.location = SHIOMANEKI_SHOULDER
        c.rotation_euler = (0.35, 0, -0.25)
        objs[c.name] = c
        cl.hide_render = True
    arm = objs.get("Hitode_Arm")
    if arm:
        # ヒトデ：5 本の腕を、まん中のまわりに
        for k in range(5):
            a = TAU * k / 5
            c = arm.copy()
            c.data = arm.data
            c.name = f"Hitode_Arm_P{k}"
            bpy.context.scene.collection.objects.link(c)
            c.location = (-math.sin(a) * HITODE_ARM_ROOT, math.cos(a) * HITODE_ARM_ROOT, 0.0)
            c.rotation_euler = (0, 0, a)
            objs[c.name] = c
        arm.hide_render = True
    fin = objs.get("Manta_Fin")
    if fin:
        for nm, sx in (("Manta_Fin_R", 1), ("Manta_Fin_L", -1)):
            c = fin.copy()
            c.data = fin.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (MANTA_FIN.x * sx, MANTA_FIN.y, MANTA_FIN.z)
            c.rotation_euler = (0, -0.12 * sx, 0)
            objs[nm] = c
        fin.hide_render = True
    head = objs.get("Fukurou_Head")
    if head:
        # フクロウ：首の上に頭をのせる（少しかしげて）
        c = head.copy()
        c.data = head.data
        c.name = "Fukurou_Head_P"
        bpy.context.scene.collection.objects.link(c)
        c.location = FUKUROU_NECK
        c.rotation_euler = (0, -0.1, -0.45)
        objs[c.name] = c
        head.hide_render = True
    gw = objs.get("Ga_Wing")
    if gw:
        # オオミズアオ：はねを、平らに広げてとまる
        for nm, sx in (("Ga_Wing_P", 1), ("Ga_Wing_L", -1)):
            c = gw.copy()
            c.data = gw.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (GA_WING_ROOT.x * sx, GA_WING_ROOT.y, GA_WING_ROOT.z)
            c.rotation_euler = (0, -0.1 * sx, 0)
            objs[nm] = c
        gw.hide_render = True
    for wname, root, lift in (("Suzumushi_Wing", SUZUMUSHI_WING_ROOT, 0.95), ("Korogi_Wing", KOROGI_WING_ROOT, 0.7),
                              ("Kirigirisu_Wing", KIRIGIRISU_WING_ROOT, 0.55)):
        cw = objs.get(wname)
        if not cw:
            continue
        # スズムシ・コオロギ：前ばねを立てて鳴いている姿
        for nm, sx in ((wname + "_P", 1), (wname + "_L", -1)):
            c = cw.copy()
            c.data = cw.data
            c.name = nm
            bpy.context.scene.collection.objects.link(c)
            c.scale = (sx, 1, 1)
            c.location = (root.x * sx, root.y, root.z)
            c.rotation_euler = (0, -lift * sx, 0)
            objs[nm] = c
        cw.hide_render = True
    larva = objs.get("Minomushi_Larva")
    if larva:
        # ミノムシ：みのの口から顔を出し、上の糸でぶら下がる
        c = larva.copy()
        c.data = larva.data
        c.name = "Minomushi_Larva_P"
        bpy.context.scene.collection.objects.link(c)
        c.location = MINO_MOUTH
        objs[c.name] = c
        larva.hide_render = True
    thread = objs.get("Minomushi_Thread")
    if thread:
        c = thread.copy()
        c.data = thread.data
        c.name = "Minomushi_Thread_P"
        bpy.context.scene.collection.objects.link(c)
        c.location = (0, 0, 0.7)
        c.scale = (1, 1, 0.7)
        objs[c.name] = c
        thread.hide_render = True
    if "Mukade_Seg" in objs:
        objs["Mukade_Portrait"] = make_mukade_portrait()   # 図鑑の絵だけ（書き出さない）
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
    if not body or (body not in RIG and body not in LIMBS):
        return []
    sneaker = cid == "spider_sneaker"
    names = []
    # ひざで曲がる手足：上の骨・下の骨・足先を、それぞれ付け根・ひざ・足首に置く（作ったときの姿勢のまま）
    for j, (up, lo, ft, hip, knee, ankle) in enumerate(LIMBS.get(body, [])):
        for part, at in ((up, hip), (lo, knee), (ft, ankle)):
            src = objs.get(part) if part else None
            if src is None:
                continue
            for sx in (1, -1):
                nm = f"{cid}_{part}_{'R' if sx > 0 else 'L'}"
                if nm not in objs:
                    c = src.copy()
                    c.data = src.data
                    c.name = nm
                    bpy.context.scene.collection.objects.link(c)
                    c.scale = (sx, 1, 1)
                    c.location = (at.x * sx, at.y, at.z)
                    objs[nm] = c
                names.append(nm)
    for i, (leg_name, base, tip) in enumerate(RIG.get(body, [])):
        src = objs.get(leg_name + ("_Sneaker" if sneaker else ""))
        if src is None:
            continue
        for sx in (1, -1):
            nm = f"{cid}_{leg_name}_{i}_{'R' if sx > 0 else 'L'}"   # 同じメッシュを使いまわす脚（だんごむし）も、1 本ずつ置く
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
        "        static readonly Dictionary<string, LimbMount[]> GeneratedLimbs = new Dictionary<string, LimbMount[]>",
        "        {",
    ]
    for body, lim in LIMBS.items():
        def q(n):
            return f'"{n}"' if n else "null"
        items = ", ".join(f'new LimbMount("{a}", "{b}", {q(c)}, {u(h)}, {u(k)}, {u(an)})' for a, b, c, h, k, an in lim)
        lines.append(f'            {{ "{body}", new[] {{ {items} }} }},')
    lines += [
        "        };",
        "",
        f"        public static readonly Vector3 GrasshopperHip = {u(GRASSHOPPER_HIP)};",
        f"        public static readonly Vector3 MantisShoulder = {u(MANTIS_SHOULDER)};",
        f"        public static readonly Vector3 HanaShoulder = {u(HANA_SHOULDER)};",
        f"        public const float MukadeSegment = {MUKADE_SEG:.4f}f;",
        f"        public const int MukadeSegments = {MUKADE_N};",
        f"        public static readonly Vector3 MukadeHip = {u(MUKADE_HIP)};",
        f"        public static readonly Vector3 HasamiJoint = {u(HASAMI_JOINT)};",
        f"        public static readonly Vector3 HaeShoulder = {u(HAE_SHOULDER)};",
        f"        public static readonly Vector3 HaeWingRoot = {u(HAE_WING_ROOT)};",
        f"        public static readonly Vector3 YadokariMouth = {u(yadokari_mouth())};",
        f"        public static readonly Vector3 ShiomanekiShoulder = {u(SHIOMANEKI_SHOULDER)};",
        f"        public const float HitodeArmRoot = {HITODE_ARM_ROOT:.4f}f;",
        f"        public static readonly Vector3 UniCenter = {u(UNI_CENTER)};",
        f"        public static readonly Vector3 MantaFin = {u(MANTA_FIN)};",
        f"        public static readonly Vector3 FukurouNeck = {u(FUKUROU_NECK)};",
        f"        public static readonly Vector3 KanabunWingRoot = {u(KANABUN_WING_ROOT)};",
        f"        public static readonly Vector3 GaWingRoot = {u(GA_WING_ROOT)};",
        f"        public static readonly Vector3 SuzumushiWingRoot = {u(SUZUMUSHI_WING_ROOT)};",
        f"        public static readonly Vector3 KorogiWingRoot = {u(KOROGI_WING_ROOT)};",
        f"        public static readonly Vector3 KirigirisuWingRoot = {u(KIRIGIRISU_WING_ROOT)};",
        f"        public static readonly Vector3 MinoMouth = {u(MINO_MOUTH)};",
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
        c = mixc(fur, sheen, sstep(0.25, 1.0, d.z) * 0.55 + 0.12 * noise.noise(p * 2.2))   # ビロードの毛は、光のあたる所がつやつや
        return mixc(c, hexc("#4a4650"), 0.35 * sstep(-0.3, -0.8, d.z))                    # おなかは、少しうすい
    tube(mb, pts, radii, 28, col, oval=(1.1, 0.95))
    # とがった鼻先（ピンク）と、鼻のあな
    tube(mb, [Vector((0, 2.45, 0.74)), Vector((0, 2.75, 0.66)), Vector((0, 2.98, 0.6))], [0.3, 0.2, 0.14], 14,
         lambda t, a, p, d: mixc(pink, hexc("#d47f80"), t))
    uv_sphere(mb, Vector((0, 2.98, 0.6)), 0.14, lambda n: hexc("#d47f80"), seg=12, rings=8)   # 鼻の先
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.05 * sx, 3.1, 0.62)), 0.03, lambda n: hexc("#5a2a2a"), seg=6, rings=4)
    tube(mb, [Vector((0, -2.6, 0.8)), Vector((0, -3.1, 0.95))], [0.12, 0.04], 6, lambda t, a, p, d: pink)   # しっぽ
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.36 * sx, 2.15, 1.12)), 0.045, lambda n: BLACK, seg=6, rings=4)   # 小さな目
        uv_sphere(mb, Vector((0.36 * sx, 2.155, 1.125)), 0.07, lambda n: mixc(fur, sheen, 0.6), seg=8, rings=5, scale=Vector((1.0, 0.6, 1.0)))
        for k in range(4):
            w0 = Vector((0.12 * sx, 2.75, 0.62 + 0.05 * (k - 1.5)))
            tube(mb, [w0, w0 + Vector((0.42 * sx, 0.08, 0.06 * (k - 1.5)))], [0.012, 0.0], 3, lambda t, a, p, d: hexc("#d8c8c0"))   # ひげ
        # 前足：外を向いた、大きなシャベル（ピンクの手のひらと、5 本の白いつめ）。肩から動かして、土をかく
        sh = Vector((0.62 * sx, 1.6, 0.62))
        c = Vector((0.98 * sx, 1.55, 0.42))

        def paw(m, sh=sh, c=c, sx=sx):
            tube(m, [sh, sh.lerp(c, 0.6)], [0.34, 0.26], 10, lambda t, a, p, d: mixc(fur, sheen, 0.3))
            uv_sphere(m, c, 1.0, lambda n: pink if n.x * sx > -0.2 else mixc(pink, hexc("#c27c78"), 0.4), seg=16, rings=10,
                      scale=Vector((0.18, 0.5, 0.42)))
            # 5 本のつめは、手のひらの前のふちから、前（鼻の方）へならんで出る
            for k in range(5):
                a0 = c + Vector((0.03 * sx, 0.4, 0.3 - 0.15 * k))
                tube(m, [a0, a0 + Vector((0.03 * sx, 0.14, -0.01)), a0 + Vector((0.05 * sx, 0.26, -0.04))], [0.055, 0.035, 0.0], 6,
                     lambda t, a, p, d: mixc(hexc("#f3e6d6"), hexc("#fffaf0"), t))
        leg_part(mb, sh, c, paw)
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
    top = surface_finder(mb)
    for k in range(10):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(1.4, 2.6)
        rnd.uniform(0.1, 0.5)
        rad = rnd.uniform(0.15, 0.3)
        x, y = math.cos(a) * r, math.sin(a) * r
        # 土のかたまりは、山の面にのせる（少しうめる。浮かない）
        uv_sphere(mb, Vector((x, y, top(x, y) + rad * 0.35)), rad, lambda n: mixc(soil, dark, 0.4), seg=6, rings=4)
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
    """ゲンゴロウ（約 4cm）：つやつやの、うすいたまご形の体（こい緑の黒に、黄色のふち）、
    前胸と前ばねのさかい目、オールのような毛のふさのうしろ足（脚は別メッシュにして、水をこぐ）"""
    mb = MB()
    shell = hexc("#1f2a20")
    shine = hexc("#4f6a48")
    rim = hexc("#d9c34a")
    under = hexc("#3a2a18")

    def col(t, a, p):
        side = math.cos(a)                 # 体の横（ふち）ほど 1
        up = math.sin(a)
        if up < -0.2:
            return mixc(under, hexc("#6a4a22"), 0.3)
        c = mixc(shell, shine, sstep(0.55, 1.0, up) * 0.55)
        if abs(side) > 0.83 and up > -0.25:
            c = rim                        # 黄色のふち
        if 0.62 < t < 0.66 and up > 0.2:
            c = mixc(c, hexc("#0e140e"), 0.8)   # 前胸と前ばねのさかい目
        if t < 0.62 and abs(p.x) < 0.012 and up > 0.6:
            c = mixc(c, hexc("#0e140e"), 0.7)   # 左右の前ばねのあわせ目
        return c
    seg_body(mb, -0.78, 0.66, 0.48, 0.26, col, rings=44, seg=30, z0=0.0, flat=0.55,
             prof=lambda t: (math.sin(math.pi * min(1.0, 0.08 + t * 0.95)) ** 0.42, math.sin(math.pi * t) ** 0.5))
    # 頭：前へ少しつき出た、平たい頭
    uv_sphere(mb, Vector((0, 0.7, 0.0)), 1.0, lambda n: mixc(shell, rim, 0.25 if abs(n.x) > 0.75 else 0.0), seg=16, rings=10,
              scale=Vector((0.27, 0.15, 0.12)))
    for sx in (-1, 1):
        cute_eye(mb, (0.17 * sx, 0.76, 0.05), 0.036, (0.6 * sx, 1, 0.2), white=False)
        a0 = Vector((0.08 * sx, 0.82, 0.04))
        tube(mb, [a0, a0 + Vector((0.08 * sx, 0.1, 0.03)), a0 + Vector((0.14 * sx, 0.2, 0.02))], [0.01, 0.008, 0.0], 4,
             lambda t, a, p, d: hexc("#3a3a2a"))
    # 前・なかの足（細い脚）
    roots = []
    for y in (0.45, 0.15):
        for sx in (-1, 1):
            b = Vector((0.25 * sx, y, -0.06))
            roots.append((b, b + Vector((0.2 * sx, 0.12, -0.04)), b + Vector((0.3 * sx, 0.26, -0.09))))
    legs(mb, roots, hexc("#3a3a2a"), hexc("#6a5a3a"), radius=0.026)
    # うしろ足：平たいオールに、黄土色の毛のふさ
    for sx in (-1, 1):
        base = Vector((0.28 * sx, -0.22, -0.05))
        mid = base + Vector((0.36 * sx, -0.22, -0.02))
        tip = mid + Vector((0.27 * sx, -0.34, 0.0))

        def oar(m, base=base, mid=mid, tip=tip):
            tube(m, [base, mid, tip], [0.045, 0.035, 0.02], 7, lambda t, a, p, d: hexc("#3a3a2a"), oval=(1.0, 0.55))
            ribbon(m, [mid, mid.lerp(tip, 0.5), tip], [0.09, 0.11, 0.06], [Vector((0, 1, 0))] * 3,
                   lambda t, v, p: mixc(hexc("#b8a868"), hexc("#e6d898"), 0.5 + 0.5 * math.sin(t * 30)), fold=0.0, normal_vecs=[Vector((0, 0, 1))] * 3)
        leg_part(mb, base, tip, oar)
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
    # 脚：ふとももに、花びらのようなうすいふくらみ（脚といっしょに動く）
    for (b, k, t) in (((0.04, -0.02, 0.3), (0.22, 0.06, 0.36), (0.3, 0.18, 0.0)),
                      ((0.04, -0.14, 0.3), (0.25, -0.26, 0.38), (0.34, -0.48, 0.0))):
        for sx in (-1, 1):
            base = Vector((b[0] * sx, b[1], b[2]))
            knee = Vector((k[0] * sx, k[1], k[2]))
            tip = Vector((t[0] * sx, t[1], t[2]))

            def leg(m, base=base, knee=knee, tip=tip):
                tube_leg(m, base, knee, tip, 0.022, pink, white, 0.022 * 3.2)
                # 花びら：ももにそった、うすいハートの形（ふちは白く、まん中はピンク）
                along = (knee - base).normalized()
                c = base.lerp(knee, 0.58) + Vector((0, 0, 0.02))
                rot = Vector((1, 0, 0)).rotation_difference(Vector((along.x, along.y, 0)).normalized() if abs(along.x) + abs(along.y) > 1e-4 else Vector((1, 0, 0)))
                for off, sc in ((0.0, (0.11, 0.075, 0.018)), (0.05, (0.07, 0.055, 0.016))):
                    uv_sphere(m, c + along * off - Vector((0, 0, 0.004)) * off * 20, 1.0,
                              lambda n: mixc(deep, white, 0.25 + 0.75 * sstep(0.2, 0.9, abs(n.x) + abs(n.y))), seg=12, rings=6,
                              scale=Vector(sc), rot=rot)
            leg_part(mb, base, tip, leg)
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
    for (y, ky, wy) in ((0.55, 0.42, 0.78), (-0.45, -0.6, -0.4)):
        for sx in (-1, 1):
            a = Vector((0.3 * sx, y, 0.8))
            b = Vector((0.58 * sx, ky, 0.5))
            c = Vector((0.62 * sx, wy, 0.07))

            def up(m, a=a, b=b):
                tube(m, [a, b], [0.12, 0.09], 8, lambda t, aa, p, d: green)
                uv_sphere(m, b, 0.09, lambda n: green, seg=8, rings=5)

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(green, hexc("#2f7a2a"), 0.4 * t)
                tube(m, [b, c], [0.09, 0.07], 8, col)
                joint_ball(m, c, 0.07, col)   # 足首

            def ft(m, c=c):
                # 2 つにわかれた手（前と後ろから、枝をはさむ）
                for dy in (0.12, -0.1):
                    tip = c + Vector((0, dy, -0.06))
                    tube(m, [c, c.lerp(tip, 0.5) + Vector((0, 0, -0.01)), tip], [0.06, 0.05, 0.025], 6, lambda t, aa, p, d: hexc("#2f7a2a"))
            limb(mb, a, b, c, up, lo, ft)
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


def harinezumi_spines(mb, center, rx, ry, rz, n, rnd, zmin=-0.2, length=0.3, face=False):
    """ハリネズミのはり：だ円の上半分から生えて、うしろへねかせて流れる。先は細くとがる
    （根もとはこげ茶、まん中にクリーム色の帯、先はまたこげ茶。本物のナミハリネズミのはりの色）。
    face なら、顔のまわり（前の、ひたいより下）には生やさない"""
    dark, cream, tipc = hexc("#4a3424"), hexc("#efe3cc"), hexc("#5e4330")

    def col(t, a, p, d):
        return mixc(mixc(dark, cream, sstep(0.3, 0.5, t)), tipc, sstep(0.66, 0.86, t))
    for _ in range(n):
        th = rnd.uniform(0, TAU)
        ph = math.asin(rnd.uniform(zmin, 1.0))
        d = Vector((math.cos(th) * math.cos(ph), math.sin(th) * math.cos(ph), math.sin(ph)))
        if face and d.y > 0.7 and d.z < 0.62:
            continue   # 顔（前の、ひたいより下）は毛
        base = center + Vector((d.x * rx, d.y * ry, d.z * rz)) * 0.93
        tipd = (Vector((d.x / rx, d.y / ry, d.z / rz))).normalized()
        tipd = (tipd + Vector((0, -0.95, 0.05))).normalized()   # うしろへねかせる（体にそって流れる）
        ln = length * rnd.uniform(0.8, 1.15)
        tube(mb, [base, base + tipd * ln * 0.5, base + tipd * ln], [0.035, 0.022, 0.0], 4, col)   # 先は細くとがる


def make_harinezumi():
    """ハリネズミ（レア、子ども・約 10cm）：本物のナミハリネズミに近い形。
    低くて前後に長い体の、せなかとわき腹いっぱいに、うしろへねた細いはり（こげ茶とクリーム色のしま）。
    顔とおなかは、うすい茶色の毛。顔は前へとがって、先に黒くしめった鼻。目は小さな黒い玉で、毛の中から少し出た小さな耳。
    短い 4 本の足は、ひざで曲がる手足（体の下から見えて、ちょこちょこ動く）。はだのピンクの足に、黒いつめ"""
    mb = MB()
    rnd = random.Random(17)
    fur = hexc("#c9a988")        # 顔とおなかの毛
    fur_dark = hexc("#8c6c52")
    skin = hexc("#c99a8a")       # 足のはだ
    lift = 0.04
    body_c = Vector((0, -0.32, 0.7 + lift))
    # はりの下の地肌は、はりと同じこげ茶（すき間が目立たない）。おなかは毛の色
    uv_sphere(mb, body_c, 1.0, lambda n: mixc(hexc("#4f3826"), hexc("#7a5c44"), 0.5 + 0.5 * math.sin(n.x * 23 + n.y * 17 + n.z * 11))
              if n.z > 0.02 else fur, seg=24, rings=16, scale=Vector((0.92, 1.24, 0.72)))   # わき腹の下からは、毛のふち
    harinezumi_spines(mb, body_c + Vector((0, 0, 0.04)), 0.92, 1.24, 0.74, 680, rnd, zmin=-0.02, face=True, length=0.42)
    # 顔：前へ細くなる、毛の円すい（体の前から、鼻先まで）
    head = Vector((0, 0.62, 0.62 + lift))
    uv_sphere(mb, head, 1.0, lambda n: mixc(fur, fur_dark, sstep(0.3, 0.9, n.z)), seg=20, rings=14, scale=Vector((0.46, 0.5, 0.42)))
    snout = [Vector((0, 0.82, 0.58 + lift)), Vector((0, 1.06, 0.5 + lift)), Vector((0, 1.24, 0.45 + lift)), Vector((0, 1.3, 0.44 + lift))]
    tube(mb, snout, [0.3, 0.18, 0.09, 0.0], 14, lambda t, a, p, d: mixc(fur, hexc("#a88466"), t))   # 先は、黒い鼻の中でとじる
    nose = Vector((0, 1.31, 0.45 + lift))
    uv_sphere(mb, nose, 0.075, lambda n: hexc("#1c1412"), seg=10, rings=7, scale=Vector((1.2, 0.9, 0.85)))
    uv_sphere(mb, nose + Vector((0.02, 0.04, 0.035)), 0.016, lambda n: hexc("#8a8080"), seg=5, rings=3)   # しめった鼻の、小さなつや
    for sx in (-1, 1):
        # 小さな黒い目（毛の中に、少しうもれる）
        ed = Vector((0.62 * sx, 0.74, 0.26)).normalized()
        ec = head + Vector((ed.x * 0.46, ed.y * 0.5, ed.z * 0.42)) * 0.96   # 顔の面に、少しうもれる
        uv_sphere(mb, ec, 0.072, lambda n: hexc("#0c0a0a"), seg=10, rings=7)
        uv_sphere(mb, ec + Vector((0.025 * sx, 0.045, 0.035)), 0.016, lambda n: hexc("#d8d0c8"), seg=5, rings=3)
        # 小さなまるい耳（毛の中から、少し出る）
        er = Vector((0.3 * sx, 0.58, 0.96 + lift))
        uv_sphere(mb, er, 1.0, lambda n: hexc("#b08a72"), seg=10, rings=6, scale=Vector((0.1, 0.05, 0.09)),
                  rot=Vector((0, 1, 0)).rotation_difference(Vector((0.4 * sx, 1, 0.2)).normalized()))
        uv_sphere(mb, er + Vector((0.0, 0.02, -0.005)), 1.0, lambda n: hexc("#d2a69a"), seg=8, rings=5, scale=Vector((0.065, 0.03, 0.06)),
                  rot=Vector((0, 1, 0)).rotation_difference(Vector((0.4 * sx, 1, 0.2)).normalized()))
    # 足（前足・うしろ足）：短い足に、はだの足のうらと、黒いつめ
    for (sh, el, wr) in (((0.36, 0.36, 0.42), (0.44, 0.34, 0.24), (0.46, 0.48, 0.05)),
                         ((0.42, -0.86, 0.42), (0.52, -0.66, 0.24), (0.5, -0.92, 0.06))):
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                col = lambda t, aa, p, d: fur
                tube(m, [a, b], [0.15, 0.11], 8, col)
                joint_ball(m, b, 0.11, col)   # ひざ

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(fur, skin, t)
                tube(m, [b, c], [0.11, 0.08], 8, col)
                joint_ball(m, c, 0.08, col)   # 足首

            def ft(m, c=c, sx=sx):
                uv_sphere(m, c + Vector((0, 0.05, -0.02)), 1.0, lambda n: skin, seg=8, rings=5, scale=Vector((0.09, 0.12, 0.05)))
                for k in range(5):
                    q = c + Vector((0.04 * (k - 2) * sx, 0.15, -0.035))
                    tube(m, [c + Vector((0.02 * (k - 2) * sx, 0.08, -0.02)), q], [0.02, 0.0], 4,
                         lambda t, aa, p, d: mixc(skin, hexc("#1e1814"), sstep(0.5, 0.8, t)))   # 黒いつめ
            limb(mb, a, b, c, up, lo, ft)
    return build(mb, "Harinezumi")


def make_harinezumi_ball():
    """まるくなったハリネズミ：はりの玉"""
    mb = MB()
    rnd = random.Random(23)
    uv_sphere(mb, Vector((0, 0, 0.9)), 0.9, lambda n: mixc(hexc("#4f3826"), hexc("#7a5c44"), 0.5 + 0.5 * math.sin(n.x * 23 + n.y * 17 + n.z * 11)),
              seg=20, rings=14)
    harinezumi_spines(mb, Vector((0, 0, 0.9)), 0.9, 0.9, 0.9, 520, rnd, zmin=-0.85, length=0.36)
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
    tail = [Vector((0, -2.2, 2.05)), Vector((0, -2.8, 2.1)), Vector((0, -3.8, 2.5)), Vector((0, -4.5, 3.5)), Vector((0, -4.65, 4.9)), Vector((0, -4.1, 6.0)),
            Vector((0, -3.3, 6.45)), Vector((0, -2.7, 6.25))]

    def tail_col(t, a, p, d):
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 2.5))
        if abs(d.x) > 0.85:
            c = mixc(c, hexc("#e6c8a8"), 0.5)   # ふちの毛は、白っぽい
        return c
    tube(mb, tail, [0.5, 0.55, 1.05, 1.4, 1.5, 1.35, 1.0, 0.0], 18, tail_col, oval=(1.0, 0.75),   # 根もとは、体の中から
         rfn=lambda t, a: 1.0 + 0.1 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, t * 9))))
    # 足：前足（ひじはうしろへ曲がり、小さな手に 4 本の指）・うしろ足（ひざは前、長い足のうらを地面につける）
    for (sh, el, wr, fl) in (((0.6, 1.4, 1.8), (0.75, 1.15, 0.95), (0.72, 1.85, 0.16), 0.3),
                             ((0.95, -1.7, 1.45), (1.2, -0.75, 0.85), (1.1, -1.7, 0.22), 1.0)):
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                tube(m, [a, b], [0.4, 0.27], 10, lambda t, aa, p, d: mixc(fur, fur2, 0.4))
                uv_sphere(m, b, 0.27, lambda n: mixc(fur, fur2, 0.4), seg=10, rings=6)

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(fur, dark, 0.3 * t)
                tube(m, [b, c], [0.26, 0.18], 10, col)
                joint_ball(m, c, 0.18, col)   # 足首

            def ft(m, c=c, sx=sx, fl=fl):
                tip = c + Vector((0, fl, -0.12))
                tube(m, [c, c.lerp(tip, 0.5), tip, tip + Vector((0, 0.05, -0.01))], [0.18, 0.17, 0.1, 0.0], 10,
                     lambda t, aa, p, d: mixc(fur, dark, 0.5))   # 手の先はとじる
                for k in range(4):
                    q = tip + Vector((0.06 * (k - 1.5) * sx, 0.0, -0.02))
                    tube(m, [q, q + Vector((0.03 * (k - 1.5) * sx, 0.2, -0.06))], [0.05, 0.0], 4, lambda t, aa, p, d: hexc("#2a1a14"))
            limb(mb, a, b, c, up, lo, ft)
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
    pts = [Vector((0, -0.3, -1.6)), Vector((0, -0.3, 0.0)), Vector((0, -0.1, 1.5)), Vector((0, 0.15, 2.7)), Vector((0, 0.3, 3.6)),
           Vector((0, 0.36, 4.25)), Vector((0, 0.4, 4.75))]
    tube(mb, pts, [0.9, 0.98, 1.02, 0.98, 0.78, 0.6, 0.58], 22, col, up_hint=Vector((0, 1, 0)), oval=(0.95, 1.0))
    hc = Vector((0, 0.55, 5.2))
    uv_sphere(mb, hc, 0.85, lambda n: cream if n.z < -0.25 and n.y > 0.0 else mixc(brown, hexc("#a8703f"), 0.3), seg=20, rings=12,
              scale=Vector((1.0, 1.15, 0.85)))
    uv_sphere(mb, hc + Vector((0, 0.62, -0.2)), 0.4, lambda n: cream if n.z < 0.1 else mixc(brown, hexc("#a8703f"), 0.3), seg=14, rings=9,
              scale=Vector((0.85, 0.9, 0.7)))   # 口もと
    uv_sphere(mb, hc + Vector((0, 0.95, -0.15)), 0.12, lambda n: hexc("#2a1a18"), seg=8, rings=5)
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.4 * sx, 0.62, 0.2)), 0.15, (0.4 * sx, 1, 0.1), white=False)
        uv_sphere(mb, hc + Vector((0.6 * sx, -0.15, 0.55)), 1.0, lambda n: mixc(brown, cream, 0.5 if n.y > 0.2 else 0.0), seg=10, rings=6,
                  scale=Vector((0.35, 0.18, 0.32)))   # まるい耳
        # 前足（胸の前で、そろえている）：ひじで曲がる手足にして、見まわすときに手を上げたり、出てくるときに穴のふちをつかんだりする
        sh = Vector((0.45 * sx, 0.45, 3.35))
        el = Vector((0.55 * sx, 0.6, 2.7))
        wr = Vector((0.38 * sx, 1.08, 2.45))

        def upper_arm(m, sh=sh, el=el):
            tube(m, [sh, el], [0.26, 0.2], 10, lambda t, a, p, d: cream)
            uv_sphere(m, el, 0.2, lambda n: cream, seg=10, rings=6)

        def forearm(m, el=el, wr=wr):
            tube(m, [el, wr], [0.19, 0.15], 10, lambda t, a, p, d: cream)

        def hand(m, wr=wr, sx=sx):
            uv_sphere(m, wr + Vector((0, 0.04, -0.06)), 0.17, lambda n: cream, seg=10, rings=7, scale=Vector((1.0, 0.9, 1.1)))
            for k in range(4):
                q = wr + Vector((0.06 * (k - 1.5), 0.15, -0.16))
                tube(m, [q, q + Vector((0.01 * (k - 1.5), 0.05, -0.07))], [0.045, 0.0], 4, lambda t, a, p, d: hexc("#3a2a20"))   # つめ
        limb(mb, sh, el, wr, upper_arm, forearm, hand)
        for k in range(3):
            tube(mb, [hc + Vector((0.3 * sx, 0.85, -0.3)), hc + Vector((0.9 * sx, 1.0, -0.35 + 0.12 * (k - 1)))], [0.025, 0.0], 4,
                 lambda t, a, p, d: hexc("#3a2a20"))   # ひげ
    # しっぽ（先は黒）：穴のふちから、うしろへ
    tail = [Vector((0, -0.8, 0.2)), Vector((0, -1.6, 0.6)), Vector((0, -2.3, 1.2)), Vector((0, -2.6, 1.9))]
    tube(mb, tail, [0.35, 0.33, 0.3, 0.0], 10, lambda t, a, p, d: hexc("#141012") if t > 0.6 else brown)
    return build(mb, "Okojo")


OKOJO_HOLE_R = 1.0     # 穴の口の半径（オコジョの体が通る）
OKOJO_SOIL_R = 3.1     # 掘り出した土の山のすそ（地面の下にうめる）


def make_okojo_rocks():
    """オコジョのすみか：花こう岩の石が、まるく積み重なり、まん中に掘りかえした土の山と、下へつづく暗い穴"""
    mb = MB()
    rnd = random.Random(9)
    soil = hexc("#6b4a2f")
    soil_light = hexc("#8f6a45")
    dark = hexc("#120d0a")
    off = Vector((3.1, -7.4, 1.7))
    # 土の山：すそは地面の下、ふちは少しもり上がり、まん中に暗い穴の口。
    # 地面はすみかの下にもあるので、穴の底は地面のすぐ上（まっ暗にぬって、深い穴に見せる）。オコジョは、この底を通って出入りする
    prof = [(OKOJO_SOIL_R, -0.55), (2.5, -0.12), (2.0, 0.12), (1.6, 0.3), (1.28, 0.36), (OKOJO_HOLE_R + 0.08, 0.3),
            (OKOJO_HOLE_R - 0.02, 0.19), (OKOJO_HOLE_R - 0.25, 0.18), (0.0, 0.18)]

    def soil_col(t, a, p):
        r = math.hypot(p.x, p.y)
        c = mixc(soil, soil_light, 0.5 + 0.5 * noise.noise(p * 1.7 + off))
        c = mixc(c, hexc("#4d3522"), 0.45 * sstep(1.9, 1.3, r))                 # 口のまわりは、しめった土
        return mixc(c, dark, sstep(OKOJO_HOLE_R + 0.12, OKOJO_HOLE_R - 0.3, r))   # 穴の中は、まっ暗
    def soil_z(r):
        """土の山の、半径 r での高さ（外側のすそだけ）"""
        outer = [q for q in prof if q[0] >= OKOJO_HOLE_R + 0.06][::-1]
        for (r0, z0), (r1, z1) in zip(outer, outer[1:]):
            if r0 <= r <= r1:
                return lerp(z0, z1, (r - r0) / (r1 - r0))
        return outer[-1][1]
    lathe(mb, prof, 32, soil_col,
          rfn=lambda t, a: 1.0 + (0.07 * noise.noise(Vector((math.cos(a) * 2.2, math.sin(a) * 2.2, t * 2.0)) + off) if t < 0.5 else 0.0))
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
    # 穴から外へ、かき出した土がこぼれる（石と石のあいだから、ひくく広がる）と、土の上の小石・土くれ
    srnd = random.Random(19)
    spill = TAU * 0.5 / 8
    uv_sphere(mb, Vector((math.cos(spill) * 2.9, math.sin(spill) * 2.9, -0.2)), 1.0,
              lambda n: mixc(soil, soil_light, 0.35 + 0.3 * n.z), seg=14, rings=6,
              scale=Vector((1.7, 1.05, 0.38)), rot=Vector((1, 0, 0)).rotation_difference(Vector((math.cos(spill), math.sin(spill), 0))))
    for k in range(14):
        a = srnd.uniform(0, TAU)
        r = srnd.uniform(OKOJO_HOLE_R + 0.35, 2.4)
        z = soil_z(r) + 0.02
        if srnd.random() < 0.5:
            uv_sphere(mb, Vector((math.cos(a) * r, math.sin(a) * r, z)), srnd.uniform(0.09, 0.16),
                      lambda n: mixc(hexc("#8e8e92"), hexc("#bdb8b0"), 0.5 + 0.5 * n.z), seg=6, rings=4, scale=Vector((1.2, 1.0, 0.7)))
        else:
            uv_sphere(mb, Vector((math.cos(a) * r, math.sin(a) * r, z)), srnd.uniform(0.12, 0.22),
                      lambda n: mixc(soil, soil_light, 0.3 + 0.4 * n.z), seg=6, rings=4, scale=Vector((1.2, 1.0, 0.6)))
    return build(mb, "Okojo_Rocks", smooth=True, sharp_deg=50)


def make_nakiusagi():
    """エゾナキウサギ（約 15cm）：たまごのような、まるい体に、白いふちのまるい耳。しっぽは見えない。
    灰色がかった茶色のふわふわの毛（毛なみのむら）、白っぽいおなか、黒い大きな目、ひげ。
    短い 4 本の足は、ひざで曲がる手足（岩の上をちょこちょこ歩き、跳ぶときは前足をのばす）"""
    mb = MB()
    fur = hexc("#8c6c4e")
    fur2 = hexc("#a88664")
    belly = hexc("#dccaa8")
    dark = hexc("#5e4632")

    def col(p, n, t):
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 2.2))
        c = mixc(c, dark, 0.25 * sstep(0.6, 1.0, n.z) * (0.5 + 0.5 * noise.noise(p * 5.0)))   # せなかは少し濃い
        if t > 0.8 and n.y > 0.2 and n.z < 0.1:
            c = mixc(c, belly, 0.6)                                                            # 口のまわりは白っぽい
        return mixc(c, belly, sstep(-0.25, -0.65, n.z))
    # 体と頭：ひとつながりの、まるいたまご形（首は見えない）
    spine = [(-1.95, 1.2), (-1.6, 1.45), (-0.9, 1.65), (-0.1, 1.72), (0.7, 1.75), (1.35, 1.85), (1.9, 1.95), (2.35, 1.9), (2.62, 1.75)]
    rx = [0.5, 1.15, 1.5, 1.6, 1.5, 1.25, 1.1, 0.8, 0.35]
    rz = [0.45, 1.05, 1.38, 1.45, 1.35, 1.12, 1.0, 0.75, 0.32]
    loft(mb, [Vector((0, y, z)) for y, z in spine], rx, rz, col, seg=30)
    hc = Vector((0, 1.75, 2.0))
    uv_sphere(mb, Vector((0, 2.62, 1.72)), 0.13, lambda n: hexc("#3a2a24"), seg=8, rings=5)   # 鼻
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.66 * sx, 0.55, 0.22)), 0.2, (0.6 * sx, 1, 0.1), white=False)
        ec = hc + Vector((0.65 * sx, -0.45, 0.95))
        uv_sphere(mb, ec, 1.0, lambda n: hexc("#f0e8dc") if n.z > 0.72 or (abs(n.x) > 0.8 and n.z > 0.3) else
                  mixc(fur, hexc("#c8a8a0"), 0.35 if n.y > 0.3 else 0.0), seg=14, rings=9, scale=Vector((0.45, 0.2, 0.45)))   # まるい耳（白いふち）
        for k in range(4):
            tube(mb, [Vector((0.25 * sx, 2.5, 1.65)), Vector((1.25 * sx, 2.75, 1.6 + 0.12 * (k - 1.5)))], [0.022, 0.0], 3,
                 lambda t, a, p, d: hexc("#2a201a"))   # ひげ
    # 足：前足は短く、うしろ足は少し長い（足のうらは、こい毛でおおわれている）
    for (sh, el, wr, foot_len) in (((0.75, 1.25, 0.9), (0.9, 1.15, 0.5), (0.85, 1.6, 0.1), 0.35),
                                   ((0.95, -1.05, 0.95), (1.15, -0.55, 0.5), (1.05, -1.2, 0.12), 0.6)):
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                col = lambda t, aa, p, d: mixc(fur, belly, 0.3 * t)
                tube(m, [a, b], [0.42, 0.28], 10, col)
                joint_ball(m, b, 0.28, col)   # ひざ

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(fur, belly, 0.3 + 0.3 * t)
                tube(m, [b, c], [0.26, 0.2], 10, col)
                joint_ball(m, c, 0.2, col)   # 足首

            def ft(m, c=c, sx=sx, fl=foot_len):
                tip = c + Vector((0, fl, -0.04))
                tube(m, [c, c.lerp(tip, 0.5), tip], [0.2, 0.19, 0.0], 10, lambda t, aa, p, d: mixc(belly, dark, 0.35 * t))
                for k in range(4):
                    # 指は、足の先の中から（前は、とがった先のわきに浮いていた）
                    q = c.lerp(tip, 0.75) + Vector((0.05 * (k - 1.5) * sx, 0.0, -0.05))
                    tube(m, [q, q + Vector((0.01 * (k - 1.5) * sx, 0.14, -0.03))], [0.03, 0.0], 4, lambda t, aa, p, d: hexc("#3a2a20"))
            limb(mb, a, b, c, up, lo, ft)
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
    tube(mb, pts, radii, 26, col, oval=(1.15, 0.9))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.27 * sx, 2.4, 0.68)), 0.17, lambda n: dark, seg=10, rings=6)
        cute_eye(mb, (0.3 * sx, 2.48, 0.74), 0.13, (0.6 * sx, 0.8, 0.3), white=False)
    roots = []
    for (y, ky, ty) in ((1.4, 1.6, 1.85), (-0.6, -0.7, -0.5)):
        for sx in (-1, 1):
            roots.append(((0.32 * sx, y, 0.36), (0.62 * sx, ky, 0.4), (0.75 * sx, ty, 0.0)))
    legs(mb, roots, dark, hexc("#c8a0a8"), radius=0.1, toes=4)
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
    # たたんだ羽（屋根の形）：木の葉のような形の、すきとおった羽に、黒いすじ（はばのせまいすじが、付け根から先へ）。
    # 体の材質は裏の面を描かないので、うらにも面をはる（下からも羽が見える）
    nu, nv = 16, 9
    for sx in (-1, 1):
        top, bot = [], []
        for i in range(nu + 1):
            u = i / nu
            y = lerp(0.42, -1.5, u)
            w = 0.04 + 0.4 * math.sin(math.pi * min(1.0, 0.12 + 0.95 * u)) ** 0.65
            tr, br = [], []
            for j in range(nv + 1):
                v = j / nv
                x = sx * lerp(0.025, w, v)
                ridge = 0.62 - 0.2 * u                          # 背中のすじ（屋根のてっぺん）
                drop = 0.42 * w / 0.44                          # 屋根の高さは、羽のはばに合わせる（先は木の葉のようにすぼまる）
                z = ridge - drop * v ** 0.9 + 0.03 * math.sin(math.pi * v)
                c = mixc(hexc("#9aa89c"), hexc("#c2ccbe"), v * 0.6 + 0.2 * u)          # すきとおって、白っぽく光る
                if u < 0.22:
                    c = mixc(c, hexc("#4a7a3a"), 0.7 * (1 - u / 0.22))                  # 付け根は緑
                vein = j in (0, 4, nv) or (u > 0.75 and i % 4 == 0 and 0 < j < nv) or (0.25 < u < 0.29)
                if vein:
                    c = mixc(c, hexc("#23241e"), 0.85)
                tr.append(mb.v((x, y, z), c))
                br.append(mb.v((x, y, z - 0.012), c))
            top.append(tr)
            bot.append(br)
        for i in range(nu):
            for j in range(nv):
                q = (top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1])
                r = (bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1])
                if sx > 0:
                    mb.f(*q)
                    mb.f(*reversed(r))
                else:
                    mb.f(*reversed(q))
                    mb.f(*r)
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
    # 首は、胴の中から頭の中まで（筒の口が、胴の先や頭のうしろに出ない）
    neck = [Vector((0, -0.25, 0.21)), Vector((0, -0.02, 0.22)), Vector((0, 0.25, 0.24)), Vector((0, 0.5, 0.26)), Vector((0, 0.72, 0.25)),
            Vector((0, 0.8, 0.245))]
    tube(mb, neck, [0.1, 0.1, 0.11, 0.08, 0.07, 0.0], 10, lambda t, a, p, d: mixc(black, purple, 0.3 * sstep(0.3, 1.0, d.z)))
    uv_sphere(mb, Vector((0, 0.82, 0.24)), 1.0, lambda n: black, seg=12, rings=8, scale=Vector((0.08, 0.12, 0.07)))
    for sx in (-1, 1):
        cute_eye(mb, (0.07 * sx, 0.86, 0.27), 0.03, (0.6 * sx, 1, 0.1), white=False)
        tube(mb, [Vector((0.03 * sx, 0.92, 0.28)), Vector((0.15 * sx, 1.15, 0.36)), Vector((0.3 * sx, 1.4, 0.3))], [0.012, 0.01, 0.0], 5,
             lambda t, a, p, d: black)
        tube(mb, [Vector((0.03 * sx, 0.88, 0.21)), Vector((0.04 * sx, 0.95, 0.18)), Vector((0.05 * sx, 1.02, 0.16))], [0.015, 0.015, 0.0], 4,
             lambda t, a, p, d: hexc("#3a2a2a"))   # 大あご（根もとは頭の中）
    roots = []
    for y, dy in ((0.3, 0.15), (-0.1, -0.05), (-0.3, -0.25)):
        for sx in (-1, 1):
            roots.append(((0.1 * sx, y, 0.14), (0.35 * sx, y + dy * 0.5, 0.22), (0.5 * sx, y + dy, 0.0)))
    legs(mb, roots, black, purple, radius=0.025)
    return build(mb, "Maimaikaburi")



# ---------------------------------------------------------------------------
# 山小屋のいきもの：ニホンヤモリ・トビズムカデ・ハサミムシ・イエバエ・ツバメ（鳥の作り方）・ハツカネズミ
# 大きさは本物と同じ（1 単位 = 2.5cm）
# ---------------------------------------------------------------------------
def make_yamori():
    """ニホンヤモリ（約 12cm）：平たい体に、つぶつぶの灰色のはだと、こげ茶のまだら。大きな金色の目（たての黒目）。
    指の先は、まるく広がった吸盤（かべや天井にはりつく）"""
    mb = MB()
    skin = hexc("#a39882")
    dark = hexc("#4a4236")
    belly = hexc("#e8dfcc")
    pts = [Vector((0, -3.0, 0.07)), Vector((0, -2.4, 0.09)), Vector((0, -1.7, 0.12)), Vector((0, -1.0, 0.15)), Vector((0, -0.5, 0.19)),
           Vector((0, 0.0, 0.21)), Vector((0, 0.5, 0.21)), Vector((0, 0.82, 0.21)), Vector((0, 1.08, 0.23)), Vector((0, 1.32, 0.22)),
           Vector((0, 1.52, 0.19)), Vector((0, 1.66, 0.17))]
    radii = [0.0, 0.06, 0.11, 0.16, 0.21, 0.24, 0.23, 0.16, 0.2, 0.19, 0.12, 0.0]

    def col(t, a, p, d):
        if d.z < -0.4:
            return belly
        c = mixc(skin, hexc("#b9ae98"), 0.5 + 0.5 * noise.noise(p * 9.0))
        # せなかの、こげ茶の帯（しっぽは、しまもよう）
        band = math.sin(p.y * 6.5 + 1.3 * noise.noise(p * 2.0))
        if band > 0.3 and d.z > -0.1:
            c = mixc(c, dark, 0.7 * sstep(0.3, 0.6, band))
        # つぶつぶの白い点
        if noise.noise(p * 26.0) > 0.42 and d.z > 0.2:
            c = mixc(c, hexc("#efe8d8"), 0.6)
        return c
    tube(mb, pts, radii, 26, col, oval=(1.45, 0.6))
    hc = Vector((0, 1.2, 0.24))
    for sx in (-1, 1):
        # 大きな目：金色の目に、たての細い黒目と、光の点
        ec = hc + Vector((0.2 * sx, 0.04, 0.07))
        uv_sphere(mb, ec, 0.095, lambda n: mixc(hexc("#b88a2a"), hexc("#f0c860"), sstep(-0.6, 0.8, n.z)), seg=14, rings=9)
        uv_sphere(mb, ec + Vector((0.075 * sx, 0.01, 0.0)), 1.0, lambda n: hexc("#141010"), seg=10, rings=7,
                  scale=Vector((0.03, 0.022, 0.07)))
        uv_sphere(mb, ec + Vector((0.07 * sx, 0.04, 0.05)), 0.018, lambda n: WHITE, seg=6, rings=4)
        uv_sphere(mb, hc + Vector((0.05 * sx, 0.44, 0.06)), 0.012, lambda n: hexc("#2a2018"), seg=6, rings=4)   # 鼻の穴
    # にっこりした口
    tube(mb, [Vector((-0.17, 1.42, 0.17)), Vector((-0.08, 1.56, 0.15)), Vector((0, 1.6, 0.15)), Vector((0.08, 1.56, 0.15)), Vector((0.17, 1.42, 0.17))],
         [0.012] * 5, 4, lambda t, a, p, d: hexc("#4a3a2c"))
    # 4 本の足：うでから先へ広がる 5 本の指と、まるい吸盤
    for (b, k, tp) in (((0.22, 0.62, 0.17), (0.56, 0.74, 0.26), (0.74, 1.02, 0.0)),
                       ((0.22, -0.5, 0.17), (0.6, -0.6, 0.26), (0.84, -0.32, 0.0))):
        for sx in (-1, 1):
            base = Vector((b[0] * sx, b[1], b[2]))
            knee = Vector((k[0] * sx, k[1], k[2]))
            tip = Vector((tp[0] * sx, tp[1], tp[2] + 0.025))

            def draw(m, base=base, knee=knee, tip=tip):
                def lc(t, a, p, d):
                    return mixc(skin, dark, 0.2 + 0.2 * t)
                tube(m, [base, base.lerp(knee, 0.5), knee], [0.1, 0.09, 0.07], 9, lc)
                uv_sphere(m, knee, 0.07, lambda n: skin, seg=8, rings=5)
                tube(m, [knee, knee.lerp(tip, 0.5), tip], [0.065, 0.058, 0.05], 9, lc)
                uv_sphere(m, tip, 0.055, lambda n: skin, seg=8, rings=5)
                d0 = Vector((tip.x - knee.x, tip.y - knee.y, 0.0)).normalized()
                for j in range(5):
                    ang = (j - 2) * 0.44
                    c, s_ = math.cos(ang), math.sin(ang)
                    dd = Vector((d0.x * c - d0.y * s_, d0.x * s_ + d0.y * c, 0.0))
                    end = tip + dd * 0.2 + Vector((0, 0, -0.025))
                    tube(m, [tip, tip.lerp(end, 0.5) + Vector((0, 0, 0.012)), end], [0.026, 0.022, 0.02], 5, lambda t, a, p, d: skin)
                    uv_sphere(m, end + dd * 0.03, 1.0, lambda n: mixc(skin, hexc("#d8ccb4"), 0.6), seg=8, rings=5,
                              scale=Vector((0.05, 0.05, 0.016)))
            leg_part(mb, base, tip, draw)
    return build(mb, "Yamori")


MUKADE_SEG = 0.21          # 胴の体節 1 つの長さ（頭から、うしろへ）
MUKADE_N = 19              # 頭とおしりのあいだの体節の数
MUKADE_HIP = Vector((0.13, 0.0, 0.11))   # 体節のまん中から、右の脚の付け根
MUKADE_DARK = hexc("#22323a")
MUKADE_EDGE = hexc("#c89a3a")
MUKADE_LEG = hexc("#f0b040")
MUKADE_HEAD = hexc("#d2522a")


def mukade_tergite(m, y, w, length, top, edge, z=0.11):
    """体節 1 つ：上の平たい板（ふちが明るい）と、下の明るいおなか"""
    def cf(n, top=top, edge=edge):
        if n.z < -0.3:
            return hexc("#d8b070")
        return mixc(top, edge, sstep(0.55, 0.85, abs(n.x)) * 0.8 + 0.25 * sstep(0.6, 0.95, abs(n.y)))
    uv_sphere(m, Vector((0, y, z)), 1.0, cf, seg=16, rings=8, scale=Vector((w, length, 0.07)))


def make_mukade_seg():
    """トビズムカデの胴の体節 1 つ（体節のまん中が原点。脚は別：Mukade_Leg）"""
    mb = MB()
    mukade_tergite(mb, 0.0, 0.15, MUKADE_SEG * 0.6, MUKADE_DARK, MUKADE_EDGE)
    return build(mb, "Mukade_Seg")


def make_mukade_head():
    """トビズムカデの頭：赤だいだい色の平たい頭と、最初の体節。長い触角と、するどい毒のあご（前は +Y）"""
    mb = MB()
    uv_sphere(mb, Vector((0, 0.1, 0.11)), 1.0, lambda n: mixc(MUKADE_HEAD, hexc("#f08a50"), sstep(0.2, 0.9, n.z) * 0.5), seg=18, rings=10,
              scale=Vector((0.15, 0.13, 0.07)))
    mukade_tergite(mb, -0.08, 0.15, 0.1, MUKADE_HEAD, hexc("#f0a050"))
    for sx in (-1, 1):
        # 触角：ふしのある、細くなる鞭
        pts = [Vector((0.06 * sx, 0.2, 0.13)), Vector((0.14 * sx, 0.34, 0.17)), Vector((0.24 * sx, 0.48, 0.18)), Vector((0.34 * sx, 0.6, 0.15)),
               Vector((0.44 * sx, 0.7, 0.11))]
        tube(mb, pts, [0.018, 0.015, 0.012, 0.01, 0.0], 6,
             lambda t, a, p, d: mixc(hexc("#e8a040"), hexc("#a8602a"), 0.5 + 0.5 * math.sin(t * 50.0)))
        # 毒のあご（頭の下から前へ、内がわへ曲がる）
        tube(mb, [Vector((0.07 * sx, 0.12, 0.07)), Vector((0.11 * sx, 0.22, 0.06)), Vector((0.07 * sx, 0.3, 0.06)), Vector((0.02 * sx, 0.31, 0.06))],
             [0.025, 0.02, 0.012, 0.0], 6, lambda t, a, p, d: mixc(hexc("#7a2a1a"), hexc("#1a1210"), sstep(0.5, 1.0, t)))
        uv_sphere(mb, Vector((0.08 * sx, 0.17, 0.15)), 0.014, lambda n: hexc("#1a1210"), seg=6, rings=4)   # 小さな目
    return build(mb, "Mukade_Head")


def make_mukade_tail():
    """トビズムカデのおしりの体節と、うしろへのびる長い脚（体節のまん中が原点）"""
    mb = MB()
    mukade_tergite(mb, 0.0, 0.12, MUKADE_SEG * 0.55, MUKADE_DARK, MUKADE_EDGE)
    for sx in (-1, 1):
        pts = [Vector((0.08 * sx, -0.05, 0.1)), Vector((0.16 * sx, -0.2, 0.1)), Vector((0.2 * sx, -0.38, 0.07)), Vector((0.22 * sx, -0.52, 0.04))]
        tube(mb, pts, [0.025, 0.022, 0.018, 0.0], 6, lambda t, a, p, d: mixc(MUKADE_LEG, hexc("#c87a2a"), t))
    return build(mb, "Mukade_Tail")


def make_mukade_leg():
    """トビズムカデの右の脚 1 本（付け根が原点。黄色い脚）"""
    mb = MB()
    base = Vector((0, 0, 0))
    knee = Vector((0.12, 0.02, 0.05))
    tip = Vector((0.2, 0.05, -MUKADE_HIP.z + 0.005))
    tube_leg(mb, base, knee, tip, 0.018, MUKADE_LEG, hexc("#e89030"), 0.0)
    return build(mb, "Mukade_Leg")


def copy_mb(ob):
    """作った物の形と頂点カラーを、MB にうつす（図鑑の絵のために、ならべなおす）"""
    tmp = MB()
    cols = ob.data.color_attributes["Col"]
    vcol = {}
    for li, loop in enumerate(ob.data.loops):
        vcol[loop.vertex_index] = tuple(cols.data[li].color_srgb)
    for v in ob.data.vertices:
        tmp.v(v.co.copy(), vcol.get(v.index, (1.0, 1.0, 1.0, 1.0)))
    for p in ob.data.polygons:
        tmp.f(*p.vertices)
    return tmp


def make_mukade_portrait():
    """図鑑の絵：S の字に体をくねらせたムカデ（頭・体節・おしり・脚をならべる。書き出さない）"""
    mb = MB()
    n = MUKADE_N + 2
    pts = [Vector((0.35 * math.sin(i * 0.38), -i * MUKADE_SEG, 0.0)) for i in range(n)]
    parts = {"Head": make_mukade_head(), "Seg": make_mukade_seg(), "Tail": make_mukade_tail(), "Leg": make_mukade_leg()}
    srcs = {k: copy_mb(v) for k, v in parts.items()}
    for v in parts.values():
        bpy.data.objects.remove(v, do_unlink=True)
    for i, p in enumerate(pts):
        d = pts[max(i - 1, 0)] - pts[min(i + 1, n - 1)]
        ang = math.atan2(-d.x, d.y)
        m = Matrix.Translation(p) @ Matrix.Rotation(ang, 4, "Z")
        mb.add(srcs["Head" if i == 0 else "Tail" if i == n - 1 else "Seg"], m)
        if 0 < i < n - 1:
            for sx in (-1, 1):
                swing = 0.35 * math.sin(i * 1.1) * sx
                lm = (m @ Matrix.Translation(Vector((MUKADE_HIP.x * sx, 0.0, MUKADE_HIP.z))) @ Matrix.Rotation(swing, 4, "Z")
                      @ Matrix.Scale(sx, 4, Vector((1, 0, 0))))
                leg = MB().add(srcs["Leg"], lm)
                if sx < 0:
                    leg.F = [tuple(reversed(f)) for f in leg.F]   # 鏡にうつした脚は、面をうら返さない
                mb.add(leg)
    return build(mb, "Mukade_Portrait")


HASAMI_JOINT = Vector((0.0, -0.05, 0.1))   # 腹の付け根（おしりとはさみを持ち上げる所）


def make_hasamimushi():
    """ヒゲジロハサミムシ（約 2cm）：つやのある黒茶色の細長い体、先が白い触角、黄土色の脚。
    腹とはさみは別（Hasamimushi_Tail）。おどかされると、はさみを持ち上げる"""
    mb = MB()
    head = hexc("#6a2e1c")
    dark = hexc("#2e221c")
    wingc = hexc("#5a3a26")
    legc = hexc("#c8a060")
    uv_sphere(mb, Vector((0, 0.3, 0.1)), 1.0, lambda n: mixc(head, hexc("#9a4a2a"), sstep(0.2, 0.9, n.z) * 0.5), seg=16, rings=10,
              scale=Vector((0.08, 0.075, 0.04)))
    uv_sphere(mb, Vector((0, 0.19, 0.1)), 1.0, lambda n: mixc(dark, hexc("#4a3a30"), sstep(0.3, 0.9, n.z) * 0.6), seg=14, rings=8,
              scale=Vector((0.085, 0.07, 0.035)))
    # 短いはね（上ばね）
    uv_sphere(mb, Vector((0, 0.04, 0.1)), 1.0, lambda n: mixc(wingc, hexc("#8a5a36"), sstep(0.5, 0.95, abs(n.x)) * 0.5 + 0.2 * max(0.0, n.z)),
              seg=16, rings=8, scale=Vector((0.095, 0.12, 0.036)))
    for sx in (-1, 1):
        cute_eye(mb, (0.065 * sx, 0.33, 0.12), 0.022, (0.6 * sx, 0.8, 0.1), white=False)
        # 触角：ふしのある細い糸。先の方は白い
        pts = [Vector((0.04 * sx, 0.37, 0.13)), Vector((0.1 * sx, 0.48, 0.18)), Vector((0.17 * sx, 0.58, 0.2)), Vector((0.25 * sx, 0.66, 0.17))]
        tube(mb, pts, [0.011, 0.009, 0.008, 0.0], 5,
             lambda t, a, p, d: (hexc("#f2ece0") if t > 0.62 else mixc(hexc("#4a3020"), hexc("#7a5030"), 0.5 + 0.5 * math.sin(t * 60.0))))
        # 小さなあご
        tube(mb, [Vector((0.025 * sx, 0.37, 0.08)), Vector((0.012 * sx, 0.4, 0.075))], [0.01, 0.0], 4, lambda t, a, p, d: hexc("#2a1a12"))
    roots = []
    for y, dy in ((0.2, 0.1), (0.1, -0.02), (0.0, -0.12)):
        for sx in (-1, 1):
            roots.append(((0.06 * sx, y, 0.08), (0.15 * sx, y + dy * 0.5, 0.11), (0.21 * sx, y + dy, 0.0)))
    legs(mb, roots, legc, hexc("#a87a40"), radius=0.014)
    return build(mb, "Hasamimushi")


def make_hasamimushi_tail():
    """ハサミムシの腹とはさみ（腹の付け根が原点。うしろは -Y）：ふしのある腹の先に、内がわへ曲がる大きなはさみ"""
    mb = MB()
    dark = hexc("#2e221c")

    def abd(t, a, p, d):
        if d.z < -0.3:
            return hexc("#4a3628")
        return mixc(dark, hexc("#5a4030"), 0.4 * sstep(0.85, 1.0, abs(math.sin(p.y * 40.0))))
    pts = [Vector((0, 0.02, 0.0)), Vector((0, -0.08, 0.005)), Vector((0, -0.2, 0.01)), Vector((0, -0.32, 0.01)), Vector((0, -0.4, 0.0))]
    tube(mb, pts, [0.06, 0.07, 0.072, 0.06, 0.04], 16, abd, oval=(1.25, 0.75))
    uv_sphere(mb, Vector((0, -0.4, 0.0)), 1.0, lambda n: dark, seg=10, rings=6, scale=Vector((0.05, 0.03, 0.03)))
    for sx in (-1, 1):
        f = [Vector((0.03 * sx, -0.41, 0.0)), Vector((0.07 * sx, -0.5, 0.0)), Vector((0.075 * sx, -0.6, 0.0)), Vector((0.045 * sx, -0.68, 0.0)),
             Vector((0.01 * sx, -0.7, 0.0))]
        tube(mb, f, [0.024, 0.022, 0.018, 0.012, 0.0], 8, lambda t, a, p, d: mixc(hexc("#8a4a2a"), hexc("#3a1e12"), sstep(0.4, 1.0, t)))
    return build(mb, "Hasamimushi_Tail")


HAE_SHOULDER = Vector((0.03, 0.07, 0.09))   # 前足の付け根（手をすり合わせる）
HAE_WING_ROOT = Vector((0.035, 0.03, 0.16))  # はねの付け根


def make_hae_body():
    """イエバエ（約 1cm）：大きな赤い目、灰色の胸に黒いすじ、灰色と黒のまだらの腹、毛。
    まん中とうしろの足は体といっしょ（前足は Hae_Arm、はねは Hae_Wing）。足の先が z = 0"""
    mb = MB()
    gray = hexc("#7a7a78")
    black = hexc("#1e1c1c")

    def thorax(n):
        if n.z > 0.2 and abs(math.sin(n.x * 7.5)) < 0.35:
            return black
        return mixc(gray, hexc("#9a9a96"), 0.5 + 0.5 * n.z)
    uv_sphere(mb, Vector((0, 0.03, 0.12)), 1.0, thorax, seg=16, rings=10, scale=Vector((0.065, 0.075, 0.06)))

    def abd(n):
        if n.z < -0.4:
            return hexc("#5a5550")
        k = noise.noise(Vector((n.x * 5, n.y * 5, n.z * 5)))
        return mixc(gray, black, sstep(-0.1, 0.25, k))
    uv_sphere(mb, Vector((0, -0.09, 0.11)), 1.0, abd, seg=16, rings=10, scale=Vector((0.07, 0.085, 0.05)))
    # 頭と、大きな赤い目
    uv_sphere(mb, Vector((0, 0.115, 0.12)), 1.0, lambda n: hexc("#4a4642"), seg=12, rings=8, scale=Vector((0.05, 0.035, 0.05)))
    for sx in (-1, 1):
        uv_sphere(mb, Vector((0.032 * sx, 0.12, 0.13)), 1.0, lambda n: mixc(hexc("#7a1a14"), hexc("#d8503a"), sstep(-0.5, 0.9, n.z)), seg=14, rings=9,
                  scale=Vector((0.035, 0.035, 0.045)))
        uv_sphere(mb, Vector((0.048 * sx, 0.135, 0.155)), 0.008, lambda n: WHITE, seg=6, rings=4)
        # 胸と腹の毛
        for k in range(4):
            p0 = Vector((0.03 * sx, 0.05 - k * 0.04, 0.17 - k * 0.008))
            tube(mb, [p0, p0 + Vector((0.015 * sx, -0.02, 0.025))], [0.003, 0.0], 3, lambda t, a, p, d: black)
    # 口（なめる口）
    tube(mb, [Vector((0, 0.13, 0.09)), Vector((0, 0.15, 0.05)), Vector((0, 0.16, 0.035))], [0.01, 0.009, 0.0], 5, lambda t, a, p, d: hexc("#3a3430"))
    uv_sphere(mb, Vector((0, 0.16, 0.035)), 0.012, lambda n: hexc("#3a3430"), seg=6, rings=4)
    roots = []
    for y, dy in ((0.02, 0.02), (-0.03, -0.08)):
        for sx in (-1, 1):
            roots.append(((0.035 * sx, y, 0.08), (0.09 * sx, y + dy * 0.5, 0.08), (0.12 * sx, y + dy, 0.0)))
    legs(mb, roots, black, radius=0.008)
    return build(mb, "Hae_Body")


def make_hae_arm():
    """イエバエの右の前足（付け根が原点）：手をすり合わせるように動かす"""
    mb = MB()
    tube_leg(mb, Vector((0, 0, 0)), Vector((0.04, 0.05, -0.01)), Vector((0.025, 0.1, -HAE_SHOULDER.z)), 0.008, hexc("#1e1c1c"), hexc("#1e1c1c"), 0.02)
    return build(mb, "Hae_Arm")


def make_hae_wing():
    """イエバエの右のはね（付け根が原点、+X へのびる）：すきとおった灰色に、黒いすじ"""
    mb = MB()
    L = 0.2

    def rfn(th):
        d = th - math.pi / 2
        return L * math.exp(-(d / 0.42) ** 2) + 0.01

    def cf(rho, th, p):
        c = mixc(hexc("#eef2f4"), hexc("#c8d0d6"), rho)
        if abs(math.sin(math.atan2(p.y, p.x) * 9.0)) < 0.12 and rho > 0.2:
            c = scalec(c, 0.65)
        if rho > 0.92:
            c = scalec(c, 0.8)
        return c
    polar_sheet(mb, rfn, cf, n_ang=30, n_rad=6, a0=math.pi / 2 - 0.9, a1=math.pi / 2 + 0.7, full=False)
    return build(mb, "Hae_Wing")


def make_nezumi():
    """ハツカネズミ（体は約 7cm、しっぽ 6cm）：灰茶色のやわらかい毛、白っぽいおなか、大きなまるい耳（中はピンク）、
    黒いつぶらな目、ピンクの鼻とひげ、細長いピンクのしっぽ"""
    mb = MB()
    fur = hexc("#8a7c6e")
    fur2 = hexc("#a8998a")
    belly = hexc("#ece4d8")
    pink = hexc("#e8a8a8")

    def col(t, a, p, d):
        if d.z < -0.35:
            return belly
        return mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 5.0))
    pts = [Vector((0, -1.45, 0.62)), Vector((0, -1.25, 0.78)), Vector((0, -0.75, 0.88)), Vector((0, -0.15, 0.86)), Vector((0, 0.35, 0.78)),
           Vector((0, 0.7, 0.78))]
    tube(mb, pts, [0.0, 0.5, 0.62, 0.6, 0.48, 0.36], 20, col)
    hc = Vector((0, 0.95, 0.88))
    uv_sphere(mb, hc, 0.42, lambda n: belly if n.z < -0.45 and n.y > -0.2 else mixc(fur, fur2, 0.4 + 0.3 * n.z), seg=20, rings=12,
              scale=Vector((0.95, 1.1, 0.88)))
    uv_sphere(mb, hc + Vector((0, 0.42, -0.1)), 0.22, lambda n: belly if n.z < -0.3 else fur2, seg=14, rings=8, scale=Vector((0.9, 1.1, 0.8)))
    uv_sphere(mb, hc + Vector((0, 0.66, -0.08)), 0.07, lambda n: pink, seg=8, rings=5)
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((0.22 * sx, 0.24, 0.12)), 0.09, (0.6 * sx, 0.8, 0.1), white=False)
        # 大きなまるい耳（外は毛、中はピンク）
        ec = hc + Vector((0.3 * sx, -0.08, 0.36))
        uv_sphere(mb, ec, 1.0, lambda n: fur if n.y < -0.1 else pink, seg=16, rings=10, scale=Vector((0.26, 0.06, 0.26)),
                  rot=Matrix.Rotation(-0.35 * sx, 3, "Y").to_quaternion())
        for k in range(3):
            tube(mb, [hc + Vector((0.1 * sx, 0.58, -0.1)), hc + Vector((0.55 * sx, 0.68 + 0.04 * (k - 1), -0.08 + 0.06 * (k - 1)))], [0.012, 0.0], 4,
                 lambda t, a, p, d: hexc("#f0e8e0"))   # ひげ
        uv_sphere(mb, Vector((0.42 * sx, -0.85, 0.62)), 1.0, lambda n: belly if n.z < -0.4 else fur, seg=12, rings=8,
                  scale=Vector((0.25, 0.42, 0.38)))   # うしろ足のもも
    # 細長いしっぽ（地面にそって、ゆるく曲がる）
    tail = [Vector((0, -1.35, 0.6)), Vector((0, -1.8, 0.42)), Vector((0.15, -2.4, 0.18)), Vector((0.35, -3.0, 0.08)), Vector((0.42, -3.6, 0.06)),
            Vector((0.35, -4.1, 0.06))]
    tube(mb, tail, [0.09, 0.075, 0.06, 0.05, 0.035, 0.0], 10,
         lambda t, a, p, d: scalec(mixc(pink, hexc("#d89a90"), 0.3), 0.92 + 0.08 * math.sin(t * 120.0)))
    # 足：前足（小さな手）・うしろ足（長い足のうら）
    for (sh, el, wr, fl) in (((0.28, 0.55, 0.5), (0.34, 0.6, 0.24), (0.32, 0.72, 0.06), 0.12),
                             ((0.42, -0.85, 0.45), (0.52, -0.55, 0.22), (0.48, -0.95, 0.06), 0.42)):
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                tube(m, [a, b], [0.16, 0.1], 10, lambda t, aa, p, d: mixc(fur, fur2, 0.4))
                uv_sphere(m, b, 0.1, lambda n: mixc(fur, fur2, 0.4), seg=10, rings=6)

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(fur, pink, 0.5 * t)
                tube(m, [b, c], [0.09, 0.065], 10, col)
                joint_ball(m, c, 0.065, col)   # 足首

            def ft(m, c=c, sx=sx, fl=fl):
                tip = c + Vector((0, fl, -0.04))
                tube(m, [c, c.lerp(tip, 0.5), tip], [0.065, 0.06, 0.04], 10, lambda t, aa, p, d: pink)
                for k in range(4):
                    q = tip + Vector((0.025 * (k - 1.5) * sx, 0.0, -0.01))
                    tube(m, [q, q + Vector((0.012 * (k - 1.5) * sx, 0.08, -0.02))], [0.018, 0.0], 4, lambda t, aa, p, d: hexc("#f2c0b8"))
            limb(mb, a, b, c, up, lo, ft)
    return build(mb, "Nezumi")


# ---------------------------------------------------------------------------
# 海岸のいきもの：ヤドカリ・シオマネキ・ヒトデ・ウニ・フナムシ（カモメは鳥の作り方）・レアのマンタ
# 大きさは本物と同じくらい（1 単位 = 2.5cm。マンタだけは、子どもの大きさ）
# ---------------------------------------------------------------------------
SHIOMANEKI_SHOULDER = Vector((0.24, 0.17, 0.2))   # シオマネキの大きなはさみの付け根（右）
HITODE_ARM_ROOT = 0.4                             # ヒトデの腕の付け根（まん中からのきょり）
HITODE_ARM_Z = 0.13
UNI_CENTER = Vector((0.0, 0.0, 0.44))             # ウニのからのまん中（とげは、ここをまわってゆれる）
MANTA_FIN = Vector((1.9, 0.4, 0.55))              # マンタのむなびれの付け根（右）


YADO_TILT = 132.0                      # 殻のかたむき（大きいほど、とがった先がうしろ上へ上がり、口が前下を向く）
YADO_SHIFT = Vector((0.0, -0.07, 0.03))   # 殻を、口からうしろ上へずらす（頭・目・はさみが、殻の前に出て見える）
YADO_FWD = 0.09                        # 頭・目・はさみを、殻の口より前へ出す長さ


def yadokari_shell_frame():
    """ヤドカリがせおう巻き貝の向き（とがった先はうしろ上、口は前下）と、殻の口のまん中（体がひっこむ所）。
    前は殻がまっすぐ前をおおって、目が殻の中に入っていた（殻の面から目が出て見えた）"""
    rot = Matrix.Rotation(math.radians(180), 4, "Y") @ Matrix.Rotation(math.radians(YADO_TILT), 4, "X")
    A, R, H = 0.2, 0.2, 0.5
    mouth = rot @ Vector((R, 0.0, -H))
    return rot, mouth, (A, R, H)


def yadokari_offset():
    """殻を、体のまん中の上にのせるためのずらし（口のまん中が、体の前の下に来る）"""
    rot, mouth, _ = yadokari_shell_frame()
    return Vector((-mouth.x, 0.12 - mouth.y, 0.2 - mouth.z)) + YADO_SHIFT


def yadokari_mouth():
    """殻の口のまん中（体がひっこむ所。殻をずらした分だけ、うしろ上）"""
    rot, mouth, _ = yadokari_shell_frame()
    return mouth + yadokari_offset()


def make_yadokari():
    """ヤドカリ：巻き貝の殻をせおう。殻の口から、歩く脚が 2 対出る（頭とはさみは Yadokari_Crab）"""
    import build_coast_kit as coast
    mb = MB()
    rot, mouth, (A, R, H) = yadokari_shell_frame()
    tmp = MB()
    coast.helico_shell(tmp, A=A, R=R, H=H, W=2.0, turns=4.3, per_turn=14, seg=12, oval=1.25,
                       colfn=coast.shell_colors(hexc("#e8d2ae"), hexc("#8f5232"), hexc("#5a3e2c")), lip=1.12,
                       inner_turns=0.35, wall=0.8,
                       inner_col=lambda d: scalec(mixc(hexc("#f3dfd2"), hexc("#b98572"), d), 1.0 - 0.6 * d))
    mb.add(tmp, Matrix.Translation(yadokari_offset()) @ rot)
    red, band = hexc("#c9452a"), hexc("#f6dcc4")
    m = yadokari_mouth()
    roots = []
    for (dy, ky, ty) in ((0.06, 0.16, 0.22), (-0.04, -0.04, -0.12)):
        for sx in (-1, 1):
            b = Vector((0.07 * sx, m.y + dy, m.z - 0.04))
            roots.append((b, Vector((0.24 * sx, m.y + ky, m.z + 0.06)), Vector((0.34 * sx, m.y + ty, 0.0))))
    legs(mb, roots, red, band, radius=0.022, foot=0.0)
    return build(mb, "Yadokari")


def make_yadokari_crab():
    """ヤドカリの、殻から出ている頭とはさみ（殻にひっこむときは、殻の口のまん中へちぢむ）"""
    mb = MB()
    red, light, band = hexc("#c9452a"), hexc("#f2a06c"), hexc("#f6dcc4")
    m0 = yadokari_mouth()
    c0 = m0 + Vector((0.0, YADO_FWD, -0.02))   # 頭は、殻の口より少し前に出る
    # 殻の口から頭へつながる、首（口の中から出てくる）
    tube(mb, [m0 + Vector((0, -0.04, 0.02)), c0 + Vector((0, 0.02, 0.0))], [0.08, 0.09], 10, lambda t, a, p, d: mixc(red, light, 0.3))
    uv_sphere(mb, c0 + Vector((0, 0.06, 0.0)), 1.0, lambda n: mixc(light, red, sstep(-0.2, 0.6, n.z)), seg=14, rings=10,
              scale=Vector((0.11, 0.13, 0.08)))
    for sx in (-1, 1):
        # 目は、長い柄の先で、前上へ（殻のふちより前）
        base = c0 + Vector((0.035 * sx, 0.15, 0.03))
        tip = base + Vector((0.035 * sx, 0.09, 0.08))
        tube(mb, [base, tip], [0.017, 0.014], 6, lambda t, a, p, d: mixc(red, light, t))
        cute_eye(mb, tip + Vector((0, 0.0, 0.02)), 0.032, (0.2 * sx, 1, 0.2), white=False)
        ab = base + Vector((0, 0.02, -0.02))
        tube(mb, [ab, ab + Vector((0.08 * sx, 0.22, 0.06)), ab + Vector((0.17 * sx, 0.38, 0.03))], [0.008, 0.006, 0.0], 4,
             lambda t, a, p, d: light)
        big = 1.3 if sx > 0 else 0.85
        arm = [c0 + Vector((0.08 * sx, 0.1, -0.03)), c0 + Vector((0.15 * sx, 0.18, -0.01)), c0 + Vector((0.12 * sx, 0.27, -0.02))]
        tube(mb, arm, [0.035 * big, 0.032 * big, 0.03 * big], 8,
             lambda t, a, p, d: mixc(red, band, 0.6 * sstep(0.8, 0.95, abs(math.sin(t * 9)))))
        claw = arm[-1]
        uv_sphere(mb, claw + Vector((0, 0.04 * big, 0)), 1.0, lambda n: mixc(light, red, sstep(-0.3, 0.5, n.z)), seg=10, rings=7,
                  scale=Vector((0.055 * big, 0.075 * big, 0.05 * big)))
        for dz, ln in ((0.022, 0.1), (-0.022, 0.08)):
            p0 = claw + Vector((0, 0.09 * big, dz * big))
            tube(mb, [p0, p0 + Vector((-0.02 * sx, ln * big, 0))], [0.024 * big, 0.0], 6, lambda t, a, p, d: mixc(red, band, t))
    return build(mb, "Yadokari_Crab")


def make_shiomaneki():
    """シオマネキ：前の広い甲らに、長い目の柄。右の大きなはさみは Shiomaneki_Claw（ふって見せる）"""
    mb = MB()
    dark, blue = hexc("#2b2f3a"), hexc("#7aa6c4")

    def cara(n):
        c = mixc(hexc("#1c1f27"), dark, sstep(-0.3, 0.5, n.z))
        if noise.noise(n * 9.0 + Vector((3, 1, 2))) > 0.3:
            c = mixc(c, blue, 0.65)   # 青白い点
        return c
    tmp = MB()
    uv_sphere(tmp, Vector((0, 0, 0)), 1.0, cara, seg=26, rings=14, scale=Vector((0.33, 0.24, 0.12)))
    for i, v in enumerate(tmp.V):
        tmp.V[i] = Vector((v.x * (1.0 + 0.7 * v.y), v.y, v.z))   # 前が広い台形
    mb.add(tmp, Matrix.Translation((0, 0, 0.2)))
    for sx in (-1, 1):
        base = Vector((0.07 * sx, 0.2, 0.25))
        tip = Vector((0.09 * sx, 0.22, 0.52))
        tube(mb, [base, base.lerp(tip, 0.5), tip], [0.022, 0.018, 0.016], 6, lambda t, a, p, d: mixc(dark, hexc("#c8a070"), t))
        cute_eye(mb, tip + Vector((0, 0.0, 0.03)), 0.034, (0.3 * sx, 1, 0.1), white=False)
    # 左の小さなはさみ（口もとで、どろを食べる）
    arm = [Vector((-0.14, 0.2, 0.14)), Vector((-0.17, 0.3, 0.12)), Vector((-0.12, 0.38, 0.1))]
    tube(mb, arm, [0.025, 0.022, 0.018], 6, lambda t, a, p, d: hexc("#d77a4a"))
    uv_sphere(mb, arm[-1] + Vector((0, 0.03, 0)), 1.0, lambda n: hexc("#e89a6a"), seg=8, rings=6, scale=Vector((0.03, 0.05, 0.03)))
    roots = []
    for k, y in enumerate((0.1, 0.0, -0.1, -0.18)):
        for sx in (-1, 1):
            roots.append(((0.28 * sx, y, 0.18), (0.44 * sx, y - 0.02 * k, 0.28), (0.54 * sx, y - 0.06 * k, 0.0)))
    legs(mb, roots, hexc("#3a3a44"), hexc("#b48a62"), radius=0.026, foot=0.0)
    return build(mb, "Shiomaneki")


def make_shiomaneki_claw():
    """シオマネキの大きなはさみ（右。付け根が原点、前 +Y へのびる）"""
    mb = MB()
    red, pink, white = hexc("#e2582e"), hexc("#f6c9b8"), hexc("#fbf3ee")
    tube(mb, [Vector((0, 0, 0)), Vector((0.08, 0.08, 0.0)), Vector((0.11, 0.17, 0.0))], [0.05, 0.055, 0.06], 10,
         lambda t, a, p, d: mixc(red, pink, 0.3 * t))
    palm = Vector((0.11, 0.33, 0.02))
    uv_sphere(mb, palm, 1.0, lambda n: mixc(red, hexc("#ff8a5c"), sstep(-0.2, 0.7, n.z)), seg=16, rings=10,
              scale=Vector((0.13, 0.22, 0.21)))
    for dz, ln, w in ((0.09, 0.44, 0.065), (-0.08, 0.38, 0.07)):
        p0 = palm + Vector((0.0, 0.16, dz))
        tube(mb, [p0, p0 + Vector((0.0, ln * 0.5, dz * 0.3)), p0 + Vector((-0.02, ln, -dz * 0.4))], [w, w * 0.8, 0.0], 8,
             lambda t, a, p, d: mixc(pink, white, t))
    return build(mb, "Shiomaneki_Claw")


HITODE_BLUE, HITODE_DEEP, HITODE_ORANGE = hexc("#2f7393"), hexc("#1d4a63"), hexc("#ef7a3a")


def hitode_top(p):
    """ヒトデのせなかの色：青緑に、だいだい色のまだら"""
    k = sstep(0.15, 0.4, noise.noise(p * 3.2 + Vector((5, 2, 1))))
    return mixc(mixc(HITODE_DEEP, HITODE_BLUE, 0.6 + 0.4 * noise.noise(p * 9)), HITODE_ORANGE, k)


def make_hitode():
    """ヒトデのまん中（腕の付け根まで）。5 本の腕は Hitode_Arm（ゆっくり持ち上げる）"""
    mb = MB()

    def rfn(th):
        return HITODE_ARM_ROOT + 0.12 + 0.1 * math.cos(5 * th)

    def zfn(rho, th):
        return 0.12 + 0.13 * (1 - rho * rho)

    polar_sheet(mb, rfn, lambda rho, th, p: hitode_top(p), n_ang=60, n_rad=6, zfn=zfn)
    under = MB()
    polar_sheet(under, rfn, lambda rho, th, p: hexc("#e88a4c"), n_ang=60, n_rad=3, zfn=lambda rho, th: 0.02)
    under.F = [tuple(reversed(f)) for f in under.F]
    mb.add(under)
    # まわりのふち（上と下をつなぐ）
    n = 60
    for j in range(n):
        th0, th1 = -math.pi + TAU * j / n, -math.pi + TAU * (j + 1) / n
        r0, r1 = rfn(th0), rfn(th1)
        a = mb.v((math.sin(th0) * r0, math.cos(th0) * r0, 0.12), hitode_top(Vector((math.sin(th0) * r0, math.cos(th0) * r0, 0.12))))
        b = mb.v((math.sin(th1) * r1, math.cos(th1) * r1, 0.12), hitode_top(Vector((math.sin(th1) * r1, math.cos(th1) * r1, 0.12))))
        c = mb.v((math.sin(th1) * r1, math.cos(th1) * r1, 0.02), hexc("#e88a4c"))
        d = mb.v((math.sin(th0) * r0, math.cos(th0) * r0, 0.02), hexc("#e88a4c"))
        mb.f(a, d, c, b)
    for sx in (-1, 1):
        cute_eye(mb, (0.09 * sx, 0.14, 0.24), 0.045, (0.2 * sx, 1, 0.5), white=False)
    return build(mb, "Hitode")


def make_hitode_arm():
    """ヒトデの腕 1 本（付け根が原点、前 +Y へ。下は、だいだい色の管足）"""
    mb = MB()
    pts = [Vector((0, -0.08, HITODE_ARM_Z)), Vector((0, 0.2, 0.125)), Vector((0, 0.45, 0.1)), Vector((0, 0.66, 0.075)), Vector((0, 0.8, 0.06))]
    radii = [0.27, 0.23, 0.17, 0.1, 0.0]

    def col(t, a, p, d):
        if d.z < -0.3:
            return mixc(hexc("#f0a060"), hexc("#d9733a"), 0.5 + 0.5 * math.sin(p.y * 40))
        return hitode_top(p)
    tube(mb, pts, radii, 14, col, oval=(1.0, 0.5))
    return build(mb, "Hitode_Arm")


def make_uni():
    """ムラサキウニ：まるいからと、とげのあいだから、のぞく目（とげは Uni_SpineA/B/C）"""
    mb = MB()
    uv_sphere(mb, UNI_CENTER, 1.0, lambda n: mixc(hexc("#24142a"), hexc("#5a3a62"), sstep(-0.2, 0.9, n.z)), seg=22, rings=14,
              scale=Vector((0.7, 0.7, 0.46)))
    for sx in (-1, 1):
        cute_eye(mb, (0.17 * sx, 0.62, 0.5), 0.07, (0.3 * sx, 1, 0.2))
    # 下の管足（ちょこちょこ見える）
    for k in range(16):
        a = TAU * k / 16
        uv_sphere(mb, Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 0.06)), 0.04, lambda n: hexc("#c79ab8"), seg=6, rings=4)
    return build(mb, "Uni")


def uni_spine_dirs():
    """とげの向き（上半分と横。下にははえない。前の目のまわりは、あける）"""
    out = []
    n = 150
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * 2.39996
        d = Vector((math.cos(a) * r, math.sin(a) * r, z))
        if d.z < -0.3:
            continue
        if d.y > 0.8 and -0.15 < d.z < 0.45:
            continue
        out.append(d)
    return out


def make_uni_spines(idx):
    rnd = random.Random(31 + idx)
    mb = MB()
    for i, d in enumerate(uni_spine_dirs()):
        if i % 3 != idx:
            continue
        base = UNI_CENTER + Vector((d.x * 0.62, d.y * 0.62, d.z * 0.4))
        nrm = Vector((d.x / 0.7, d.y / 0.7, d.z / 0.46)).normalized()
        ln = rnd.uniform(0.7, 1.05)
        tip = base + nrm * ln
        tube(mb, [base, base.lerp(tip, 0.5), tip], [0.034, 0.02, 0.0], 5,
             lambda t, a, p, dd: mixc(hexc("#2a1530"), hexc("#7a4a86"), sstep(0.4, 1.0, t)))
    return build(mb, "Uni_Spine" + "ABC"[idx])


def make_funamushi():
    """フナムシ：平たい体節の体に、長い触角と、二またのしっぽ。7 対の脚で、すばやく走る"""
    mb = MB()
    dark, mid, light = hexc("#3b3632"), hexc("#6b625a"), hexc("#a39886")

    def col(t, a, p):
        f = (t * 11) % 1.0
        c = mixc(dark, mid, 0.5 + 0.5 * noise.noise(p * 14))
        c = mixc(c, light, sstep(0.75, 0.95, f) * 0.5)
        return mixc(c, hexc("#2a2622"), sstep(-0.2, -0.6, math.sin(a)))
    seg_body(mb, -0.6, 0.6, 0.26, 0.1, col, rings=64, seg=24, z0=0.08,
             prof=lambda t: (math.sin(math.pi * t) ** 0.3, math.sin(math.pi * t) ** 0.5), ridge=0.05, ridge_n=11, flat=0.2)
    for sx in (-1, 1):
        cute_eye(mb, (0.13 * sx, 0.5, 0.15), 0.05, (0.6 * sx, 0.8, 0.2), white=False)
        ant = [Vector((0.07 * sx, 0.58, 0.11)), Vector((0.28 * sx, 0.9, 0.19)), Vector((0.58 * sx, 1.12, 0.12)), Vector((0.86 * sx, 1.04, 0.05))]
        tube(mb, ant, [0.018, 0.014, 0.01, 0.0], 5, lambda t, a, p, d: mixc(mid, light, 0.4 * abs(math.sin(t * 14))))
        # しっぽの二また
        b = Vector((0.08 * sx, -0.58, 0.07))
        tube(mb, [b, b + Vector((0.05 * sx, -0.16, -0.01)), b + Vector((0.08 * sx, -0.3, -0.02))], [0.024, 0.018, 0.0], 5,
             lambda t, a, p, d: mid)
        tube(mb, [b + Vector((0.04 * sx, -0.12, -0.01)), b + Vector((0.14 * sx, -0.25, -0.02))], [0.016, 0.0], 5,
             lambda t, a, p, d: mid)
    roots = []
    for k in range(7):
        y = 0.42 - 0.8 * k / 6
        fwd = 0.16 - 0.32 * k / 6
        w = 0.17 + 0.05 * math.sin(math.pi * k / 6)
        for sx in (-1, 1):
            roots.append(((w * sx, y, 0.07), ((w + 0.14) * sx, y + fwd * 0.5, 0.14), ((w + 0.25) * sx, y + fwd, 0.0)))
    legs(mb, roots, mid, light, radius=0.02)
    return build(mb, "Funamushi")


def manta_top(p, fin_u=None):
    """マンタのせなかの色：こい紺色に、肩の白いもよう"""
    top = hexc("#232c38")
    if fin_u is None:
        k = sstep(0.4, 0.8, abs(p.x) - 0.4) * sstep(0.2, 0.9, p.y) * sstep(3.2, 2.4, p.y)
    else:
        k = sstep(0.42, 0.18, fin_u) * sstep(-0.6, 0.4, p.y)
    return mixc(top, hexc("#dfe5ea"), 0.85 * k)


def make_manta():
    """マンタの子ども：平たいひし形の体に、まるまった頭びれと、長い尾。むなびれは Manta_Fin"""
    mb = MB()
    white = hexc("#f1f3f5")
    pts = [Vector((0, y, 0.55)) for y in (-3.6, -2.6, -1.4, -0.2, 1.0, 2.1, 3.0, 3.7)]
    rx = [0.45, 0.9, 1.5, 2.0, 2.2, 2.0, 1.65, 1.2]
    rz = [0.25, 0.42, 0.55, 0.62, 0.6, 0.52, 0.42, 0.3]

    def col(p, n, t):
        if n.z < -0.15:
            return mixc(white, hexc("#c9d0d8"), sstep(0.6, 0.95, abs(n.x)))   # 白いおなか
        return manta_top(p)
    loft(mb, pts, rx, rz, col, seg=28)
    for sx in (-1, 1):
        # 頭びれ：くるんとまるまった 2 本
        b = Vector((1.05 * sx, 3.5, 0.55))
        tube(mb, [b, b + Vector((0.12 * sx, 0.6, -0.05)), b + Vector((0.0, 1.05, -0.2)), b + Vector((-0.18 * sx, 1.0, -0.42))],
             [0.24, 0.2, 0.15, 0.0], 10, lambda t, a, p, d: mixc(hexc("#232c38"), white, sstep(-0.2, -0.7, d.z) * 0.7))
        cute_eye(mb, (1.28 * sx, 3.15, 0.72), 0.2, (0.8 * sx, 0.6, 0.1), white=False)
    # 大きな口（前の、ひらたい口）
    tube(mb, [Vector((-0.8, 3.86, 0.42)), Vector((0, 3.98, 0.4)), Vector((0.8, 3.86, 0.42))], [0.1, 0.12, 0.1], 8,
         lambda t, a, p, d: hexc("#1a1f28"))
    # 長い尾
    tube(mb, [Vector((0, -3.5, 0.55)), Vector((0, -5.5, 0.55)), Vector((0, -7.5, 0.5)), Vector((0, -9.0, 0.45))],
         [0.2, 0.12, 0.06, 0.0], 8, lambda t, a, p, d: hexc("#232c38"))
    return build(mb, "Manta_Body")


def make_manta_fin():
    """マンタのむなびれ（右。付け根が原点、外 +X へ。うしろへ反りながら、先はとがる）"""
    mb = MB()
    n, m = 22, 14
    rings = []
    for i in range(n + 1):
        s = i / n
        x = 6.6 * s
        lead = 1.9 - 3.1 * s ** 1.5
        trail = -2.5 + 0.9 * s ** 0.9
        th = 0.42 * (1 - s) ** 0.8 + 0.025
        ring = []
        for j in range(m):
            v = j / m
            # 上の面（前からうしろ）→ 下の面（うしろから前）
            if v < 0.5:
                u = v * 2
                y = lerp(lead, trail, u)
                z = th * math.sin(math.pi * u) ** 0.8
                c = manta_top(Vector((x, y, z)), fin_u=s)
            else:
                u = 1 - (v - 0.5) * 2
                y = lerp(lead, trail, u)
                z = -0.6 * th * math.sin(math.pi * u) ** 0.8
                c = mixc(hexc("#f1f3f5"), hexc("#3a4250"), min(1.0, sstep(0.75, 1.0, s) + 0.6 * sstep(0.85, 1.0, u)))
            ring.append(mb.v(Vector((x, y, z)), c))
        rings.append(ring)
    for i in range(n):
        A, B = rings[i], rings[i + 1]
        for j in range(m):
            k = (j + 1) % m
            mb.f(A[j], B[j], B[k], A[k])
    last = rings[-1]
    c = Vector((0, 0, 0))
    for vi in last:
        c += mb.V[vi]
    tip = mb.v(c / len(last) + Vector((0.12, 0, 0)), hexc("#232c38"))
    for j in range(m):
        mb.f(last[j], tip, last[(j + 1) % m])
    return build(mb, "Manta_Fin")


# ---------------------------------------------------------------------------
# 神社のいきもの：キツネ・タヌキ・フクロウ（鳥の作り方の羽と、首でまわる頭）・オオミズアオ（ガ）・カナブン
# 大きさは本物と同じくらい（1 単位 = 2.5cm）
# ---------------------------------------------------------------------------
FUKUROU_NECK = Vector((0.0, 0.3, 13.6))          # フクロウの首（頭は、ここをまわってふりむく）
KANABUN_WING_ROOT = Vector((0.13, 0.1, 0.25))    # カナブンのうしろばねの付け根（右）
GA_WING_ROOT = Vector((0.08, 0.32, 0.12))        # オオミズアオのはねの付け根（右）


def fur_noise(c1, c2, k=1.6):
    return lambda p: mixc(c1, c2, 0.5 + 0.5 * noise.noise(p * k))


def mammal_legs(mb, legs_def, upper_col, lower_col, paw_col, radii):
    """4 本の手足（ひざで曲がる）。legs_def = [(付け根, ひざ, 足首, 足先ののび), ...]（右がわの座標。左は鏡うつし）"""
    ru0, ru1, rl0, rl1, rp = radii
    for (sh, el, wr, fl) in legs_def:
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                # 付け根は、体の中まで太く（脚をふっても、体とのあいだにすき間ができない）
                top = a + (a - b).normalized() * ru0 * 0.6
                tube(m, [top, a, a.lerp(b, 0.45), b], [ru0 * 0.8, ru0, (ru0 + ru1) * 0.5, ru1], 12, lambda t, aa, p, d: upper_col(p))
                uv_sphere(m, a, ru0, lambda n: upper_col(a), seg=12, rings=8)
                uv_sphere(m, b, ru1, lambda n: upper_col(b), seg=10, rings=6)

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(upper_col(p), lower_col, sstep(0.0, 0.5, t))
                tube(m, [b, b.lerp(c, 0.5), c], [rl0, (rl0 + rl1) * 0.5, rl1], 10, col)
                joint_ball(m, c, rl1, col)   # 足首

            def ft(m, c=c, sx=sx, fl=fl):
                tip = c + Vector((0, fl, -c.z + rp * 0.75))
                tube(m, [c, c.lerp(tip, 0.5), tip], [rl1, rp, rp * 0.85], 10, lambda t, aa, p, d: paw_col)
                uv_sphere(m, tip, rp * 0.9, lambda n: paw_col, seg=10, rings=6, scale=Vector((1.0, 1.25, 0.7)))
                for k in range(4):
                    q = tip + Vector((rp * 0.32 * (k - 1.5) * sx, rp * 0.9, -rp * 0.2))
                    uv_sphere(m, q, rp * 0.28, lambda n: scalec(paw_col, 0.85), seg=6, rings=4)
            limb(mb, a, b, c, up, lo, ft)


def make_kitsune():
    """ホンドギツネ（体は約 60cm、しっぽ 35cm）：だいだい色の毛、白いむねとほお、黒い耳のうらと足先、
    ふさふさの白い先のしっぽ。すらりとした長い足"""
    mb = MB()
    fur = hexc("#d9772f")
    fur2 = hexc("#eb9b52")
    white = hexc("#f6efe4")
    dark = hexc("#2e2420")

    def body_col(t, a, p, d):
        if d.z < -0.45 or (p.y > 4.5 and d.z < 0.1 and d.y > -0.2):
            return white                                       # 白いおなかと、むね
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 0.5))
        return mixc(c, scalec(fur, 0.8), 0.3 * sstep(0.6, 1.0, d.z))
    pts = [Vector((0, -10.5, 10.6)), Vector((0, -9.0, 11.6)), Vector((0, -5.5, 12.1)), Vector((0, -1.0, 12.0)),
           Vector((0, 3.0, 12.2)), Vector((0, 6.2, 12.8)), Vector((0, 8.6, 14.2))]
    tube(mb, pts, [0.0, 3.0, 3.3, 3.25, 3.3, 3.0, 2.2], 22, body_col, oval=(1.0, 1.12))
    # 首と頭
    tube(mb, [Vector((0, 7.4, 13.4)), Vector((0, 9.6, 15.6)), Vector((0, 11.2, 17.0))], [2.4, 2.1, 1.9], 18, body_col)
    hc = Vector((0, 12.4, 17.6))

    def head_col(n):
        if n.z < -0.25 and n.y > -0.3:
            return white                                       # 白いほおと、あご
        return mixc(fur, fur2, 0.4 + 0.3 * n.z)
    uv_sphere(mb, hc, 2.6, head_col, seg=22, rings=14, scale=Vector((1.0, 1.08, 0.92)))
    tube(mb, [hc + Vector((0, 1.6, -0.5)), hc + Vector((0, 3.6, -1.0)), hc + Vector((0, 5.4, -1.3))], [1.45, 0.9, 0.5], 16,
         lambda t, a, p, d: white if d.z < -0.2 else mixc(fur2, fur, 0.3))
    uv_sphere(mb, hc + Vector((0, 5.55, -1.25)), 0.5, lambda n: dark, seg=10, rings=6)    # 黒い鼻
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((1.25 * sx, 1.85, 0.55)), 0.55, (0.5 * sx, 0.85, 0.05), white=False)
        uv_sphere(mb, hc + Vector((1.35 * sx, 1.7, 0.6)), 0.75, lambda n: white, seg=10, rings=6, scale=Vector((0.6, 0.6, 0.35)))   # 目の下の白
        # 大きな三角の耳（うらは黒い）
        e0 = hc + Vector((1.35 * sx, -0.3, 1.7))
        e1 = hc + Vector((2.0 * sx, -0.7, 4.6))
        tube(mb, [e0, e0.lerp(e1, 0.5), e1], [1.25, 0.8, 0.0], 12,
             lambda t, a, p, d, sx=sx: dark if d.y < -0.2 or t > 0.75 else (hexc("#f2d6c0") if d.y > 0.4 else fur), oval=(1.0, 0.38))
        for k in range(3):
            tube(mb, [hc + Vector((0.6 * sx, 4.6, -1.2)), hc + Vector((2.4 * sx, 5.2 + 0.2 * (k - 1), -1.2 + 0.25 * (k - 1)))], [0.05, 0.0], 4,
                 lambda t, a, p, d: hexc("#3a302a"))   # ひげ
        # うしろ足のもも（腰の大きなふくらみ）と、前足の肩
        uv_sphere(mb, Vector((2.0 * sx, -7.6, 11.0)), 1.0, lambda n: mixc(fur, fur2, 0.4), seg=14, rings=10, scale=Vector((1.5, 2.9, 2.9)))
        uv_sphere(mb, Vector((1.8 * sx, 6.4, 11.0)), 1.0, lambda n: white if n.z < -0.3 and n.y > 0.2 else mixc(fur, fur2, 0.45), seg=14, rings=10,
                  scale=Vector((1.25, 2.0, 2.6)))
    # ふさふさのしっぽ（先は白い）
    tail = [Vector((0, -10.0, 11.4)), Vector((0, -13.5, 10.0)), Vector((0, -17.0, 8.0)), Vector((0, -20.5, 6.6)), Vector((0, -23.0, 6.6)),
            Vector((0, -24.4, 7.2))]
    tube(mb, tail, [1.4, 2.4, 2.8, 2.6, 1.7, 0.0], 18,
         lambda t, a, p, d: white if t > 0.82 else mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 0.9)),
         rfn=lambda t, a: 1.0 + 0.1 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, t * 9))))
    legs_def = [((1.9, 6.4, 11.2), (2.1, 5.3, 5.6), (2.0, 6.3, 1.1), 1.5),
                ((2.2, -7.4, 11.2), (2.6, -5.1, 5.8), (2.4, -7.8, 1.1), 1.7)]
    mammal_legs(mb, legs_def, lambda p: mixc(fur, fur2, 0.4), dark, dark, (1.6, 0.95, 0.85, 0.55, 0.6))
    return build(mb, "Kitsune")


def make_tanuki():
    """ホンドタヌキ（体は約 55cm）：まるくふさふさの灰茶色の毛、目のまわりの黒いもよう、白っぽいほお、
    黒くて短い足、肩の黒いすじ、黒い先のふとく短いしっぽ"""
    mb = MB()
    fur = hexc("#8c7658")
    fur2 = hexc("#b9a37f")
    dark = hexc("#2f261f")
    pale = hexc("#ddd2bc")

    def body_col(t, a, p, d):
        if d.z < -0.4:
            return scalec(dark, 1.3)                          # おなかは、こい色
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * 0.7))
        if 2.5 < p.y < 5.2 and d.z > -0.2:
            c = mixc(c, dark, 0.65)                           # 肩の黒いすじ
        if (noise.noise(p * 2.3) > 0.35):
            c = mixc(c, pale, 0.4)                            # 白っぽい毛先がまじる
        return c
    pts = [Vector((0, -8.8, 7.6)), Vector((0, -7.4, 8.8)), Vector((0, -4.0, 9.6)), Vector((0, 0.0, 9.8)),
           Vector((0, 3.5, 9.4)), Vector((0, 6.0, 9.6)), Vector((0, 7.6, 10.4))]
    tube(mb, pts, [0.0, 4.0, 4.7, 4.8, 4.5, 3.7, 2.6], 22, body_col, oval=(1.08, 1.0),
         rfn=lambda t, a: 1.0 + 0.06 * noise.noise(Vector((math.cos(a) * 4, math.sin(a) * 4, t * 14))))
    hc = Vector((0, 9.4, 11.4))

    def head_col(n):
        p = n
        # 目のまわりの黒いもよう（ほおの方へ、たれる）
        if 0.25 < p.y < 0.92 and -0.55 < p.z < 0.35 and abs(p.x) > 0.25:
            return dark
        if p.z < -0.35:
            return pale
        return mixc(fur, fur2, 0.45 + 0.3 * p.z)
    uv_sphere(mb, hc, 3.0, head_col, seg=22, rings=14, scale=Vector((1.15, 1.0, 0.92)))
    for sx in (-1, 1):
        uv_sphere(mb, hc + Vector((2.1 * sx, 0.4, -0.9)), 1.0, lambda n: pale, seg=12, rings=8, scale=Vector((1.3, 1.1, 1.0)))   # ふさふさのほお
    tube(mb, [hc + Vector((0, 2.2, -0.8)), hc + Vector((0, 3.6, -1.1)), hc + Vector((0, 4.5, -1.2))], [1.3, 0.85, 0.45], 14,
         lambda t, a, p, d: pale if t < 0.6 else scalec(dark, 1.2))
    uv_sphere(mb, hc + Vector((0, 4.6, -1.15)), 0.5, lambda n: dark, seg=10, rings=6)
    for sx in (-1, 1):
        cute_eye(mb, hc + Vector((1.15 * sx, 2.45, 0.2)), 0.5, (0.5 * sx, 0.85, 0.1), white=False)
        # 小さなまるい耳（ふちは黒）
        uv_sphere(mb, hc + Vector((1.9 * sx, -0.6, 2.4)), 1.0, lambda n: dark if n.y < -0.2 else scalec(pale, 0.9), seg=12, rings=8,
                  scale=Vector((0.9, 0.35, 0.85)))
        uv_sphere(mb, Vector((2.8 * sx, -5.6, 8.0)), 1.0, lambda n: mixc(fur, fur2, 0.4), seg=12, rings=8, scale=Vector((1.6, 2.6, 2.6)))   # もも
    # ふとく短いしっぽ（先は黒）
    tail = [Vector((0, -8.6, 8.4)), Vector((0, -10.8, 7.4)), Vector((0, -12.6, 5.6)), Vector((0, -13.4, 4.2))]
    tube(mb, tail, [1.4, 2.0, 1.8, 0.0], 16, lambda t, a, p, d: dark if t > 0.6 else mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p)),
         rfn=lambda t, a: 1.0 + 0.12 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, t * 8))))
    legs_def = [((2.3, 5.0, 8.0), (2.7, 4.4, 3.8), (2.7, 5.2, 0.9), 1.1),
                ((2.6, -5.4, 8.2), (3.0, -3.9, 4.0), (3.0, -6.0, 0.9), 1.3)]
    mammal_legs(mb, legs_def, lambda p: scalec(dark, 1.25), dark, dark, (1.8, 1.1, 0.95, 0.75, 0.7))
    return build(mb, "Tanuki")


# ---- フクロウ：まるい体（羽は鳥の作り方。たたんだ羽は、体にそわせて）と、首でまわる頭 ----
OWL_BACK = hexc("#8a7862")
OWL_BELLY = hexc("#e6dccb")
OWL_STREAK = hexc("#6a5644")
OWL_FACE = hexc("#e2dccf")


def owl_body_col(n, p):
    if n.y > 0.2 and n.z < 0.75:
        c = OWL_BELLY
        if abs(math.sin(p.x * 2.6 + 0.7 * noise.noise(p * 0.7))) > 0.88 and p.z < 12.5:
            c = OWL_STREAK                                  # むねのたてのすじ
        return c
    c = mixc(OWL_BACK, scalec(OWL_BACK, 0.8), 0.5 + 0.5 * noise.noise(p * 0.9))
    if noise.noise(p * 1.7) > 0.45:
        c = mixc(c, OWL_BELLY, 0.6)                         # せなかの白い点
    return c


def make_fukurou_body():
    """フクロウ（約 50cm）の体：たまご形のふっくらした胴・羽毛につつまれた足と、黒いつめ・短い尾。頭は別（首でまわる）"""
    mb = MB()
    c0 = Vector((0, -0.2, 7.6))
    uv_sphere(mb, c0, 1.0, lambda n: owl_body_col(n, c0 + Vector((n.x * 5.0, n.y * 4.6, n.z * 6.6))), seg=28, rings=18,
              scale=Vector((5.0, 4.6, 6.6)))
    # 首（頭がまわっても、すき間ができない）
    uv_sphere(mb, FUKUROU_NECK + Vector((0, -0.1, -0.2)), 3.2, lambda n: mixc(OWL_BACK, OWL_BELLY, 0.5 + 0.4 * n.y), seg=18, rings=10,
              scale=Vector((1.0, 0.95, 0.6)))
    # 短い尾（下うしろへ）
    for k in range(5):
        u = (k / 4) * 2 - 1
        root = Vector((u * 0.9, -3.6, 3.8))
        tip = root + Vector((u * 0.8, -2.4, -3.0))
        ribbon(mb, [root, root.lerp(tip, 0.5), tip], [1.0, 1.15, 0.7], [Vector((1, 0, 0))] * 3,
               lambda t, v, p: mixc(OWL_BACK, OWL_STREAK, 0.5 if (t * 4) % 1.0 < 0.4 else 0.0), fold=0.1,
               normal_vecs=[Vector((0, -0.8, 0.6)).normalized()] * 3)
    # 羽毛につつまれた足（ひざで曲がる。Unity で 1 歩ずつ歩き、飛ぶとたたむ）と、黒いつめ
    talon = hexc("#1e1a18")
    for sx in (-1, 1):
        hip = Vector((1.5 * sx, 1.0, 2.9))
        heel = Vector((1.6 * sx, 0.2, 1.5))
        ank = Vector((1.55 * sx, 1.3, 0.45))

        def upper(m, hip=hip, heel=heel):
            tube(m, [hip, hip.lerp(heel, 0.5), heel], [1.0, 0.85, 0.6], 10, lambda t, a, p, d: OWL_BELLY)
            uv_sphere(m, heel, 0.6, lambda n: OWL_BELLY, seg=8, rings=5)

        def lower(m, heel=heel, ank=ank):
            tube(m, [heel, heel.lerp(ank, 0.5), ank], [0.55, 0.5, 0.45], 8, lambda t, a, p, d: mixc(OWL_BELLY, OWL_FACE, 0.3))

        def foot(m, ank=ank, sx=sx):
            uv_sphere(m, ank, 0.5, lambda n: OWL_BELLY, seg=8, rings=5)
            for ang in (-0.45, 0.0, 0.45, math.pi):
                d = Vector((math.sin(ang) * sx, math.cos(ang), 0.0))
                ln = 1.1 if ang != math.pi else 0.8
                tip = ank + d * ln + Vector((0, 0, -0.3))
                tube(m, [ank, ank.lerp(tip, 0.55) + Vector((0, 0, 0.1)), tip], [0.3, 0.26, 0.2], 6, lambda t, a, p, dd: OWL_BELLY)
                tube(m, [tip, tip + d * 0.45 + Vector((0, 0, -0.25))], [0.16, 0.0], 5, lambda t, a, p, dd: talon)
        limb(mb, hip, heel, ank, upper, lower, foot)
    return build(mb, "Fukurou_Body")


def make_fukurou_head():
    """フクロウの頭（首が原点）：まるい顔の盤・大きな黒い目・小さな黄色いくちばし"""
    mb = MB()
    hc = Vector((0, 0.0, 3.0))

    def head_col(n):
        if n.y > 0.55:
            return OWL_FACE
        c = mixc(OWL_BACK, OWL_STREAK, 0.4 + 0.4 * noise.noise(Vector((n.x * 6, n.y * 6, n.z * 6))))
        return c
    uv_sphere(mb, hc, 1.0, head_col, seg=24, rings=16, scale=Vector((3.7, 3.0, 3.3)))
    # 顔の盤（ふちは、こい色の輪）
    fc = Vector((0, 2.55, 2.8))
    uv_sphere(mb, fc, 1.0, lambda n: mixc(OWL_FACE, hexc("#cfc6b4"), 0.3 * (1 - n.y)), seg=22, rings=12, scale=Vector((3.3, 0.85, 3.0)))
    ring = [fc + Vector((3.25 * math.cos(t), 0.1, 2.95 * math.sin(t))) for t in [TAU * i / 28 for i in range(29)]]
    tube(mb, ring, [0.24] * 29, 8, lambda t, a, p, d: hexc("#8a7258"))
    # 目と目のあいだの、白い V
    tube(mb, [fc + Vector((-1.6, 0.75, 1.0)), fc + Vector((0, 0.95, 0.0)), fc + Vector((1.6, 0.75, 1.0))], [0.22, 0.28, 0.22], 6,
         lambda t, a, p, d: hexc("#f6f2ea"))
    for sx in (-1, 1):
        uv_sphere(mb, fc + Vector((1.25 * sx, 0.65, 0.35)), 0.95, lambda n: hexc("#4a3a2c"), seg=12, rings=8, scale=Vector((1.0, 0.4, 1.0)))
        cute_eye(mb, fc + Vector((1.25 * sx, 0.95, 0.4)), 0.72, (0.25 * sx, 1.0, 0.05), white=False)
    # くちばし（下へ曲がる）
    b0 = fc + Vector((0, 0.85, -0.45))
    tube(mb, [b0, b0 + Vector((0, 0.55, -0.4)), b0 + Vector((0, 0.45, -0.95))], [0.36, 0.24, 0.0], 10, lambda t, a, p, d: hexc("#e8c44a"))
    mb.xform(Matrix.Translation(Vector((0, 0, 0))))
    return build(mb, "Fukurou_Head")


def make_fukurou_wing_folded():
    """たたんだ羽（右がわ。体と同じ座標）：肩から、体の横にそって、尾のほうへ"""
    mb = MB()
    pts = [Vector((3.6, 0.2, 12.2)), Vector((4.75, -0.6, 9.2)), Vector((4.55, -2.0, 5.8)), Vector((3.4, -3.6, 3.2)), Vector((2.0, -4.6, 1.8))]

    def col(t, a, p, d):
        c = mixc(OWL_BACK, scalec(OWL_BACK, 0.75), 0.5 + 0.5 * math.sin(t * 22))   # 風切り羽のしま
        if noise.noise(p * 1.4) > 0.4 and t < 0.5:
            c = mixc(c, OWL_BELLY, 0.65)                                         # 白い点
        return c
    tube(mb, pts, [1.3, 2.4, 2.3, 1.5, 0.0], 14, col, oval=(0.36, 1.0))
    return build(mb, "Fukurou_WingFolded")


# ---- オオミズアオ（ガ）：うすい水色のはねと、長い尾。白いふわふわの体と、くしの形の触角 ----
def make_ga_body():
    mb = MB()
    white = hexc("#f4f2ea")
    tube(mb, [Vector((0, -0.75, 0.05)), Vector((0, -0.4, 0.1)), Vector((0, 0.05, 0.12)), Vector((0, 0.32, 0.12))],
         [0.0, 0.13, 0.15, 0.13], 12, lambda t, a, p, d: mixc(white, hexc("#e6e0d0"), 0.3 * math.sin(p.y * 30)))
    uv_sphere(mb, Vector((0, 0.42, 0.12)), 0.13, lambda n: hexc("#f6f3ec") if n.z > -0.4 else hexc("#d9b8b8"), seg=12, rings=8)
    # 胸の前の、えんじ色の帯
    uv_sphere(mb, Vector((0, 0.36, 0.16)), 0.16, lambda n: hexc("#8e3f5c"), seg=10, rings=6, scale=Vector((1.0, 0.35, 0.9)))
    for sx in (-1, 1):
        cute_eye(mb, (0.08 * sx, 0.5, 0.13), 0.045, (0.5 * sx, 1, 0), white=False)
        # くしの形の触角（羽毛のよう）
        a0 = Vector((0.04 * sx, 0.52, 0.2))
        a1 = Vector((0.22 * sx, 0.85, 0.33))
        tube(mb, [a0, a0.lerp(a1, 0.5), a1], [0.012, 0.012, 0.006], 5, lambda t, a, p, d: hexc("#c8a45a"))
        for k in range(7):
            q = a0.lerp(a1, 0.15 + 0.12 * k)
            side = Vector((sx * 0.6, -0.5, 0.0)).normalized()
            for sgn in (-1, 1):
                tube(mb, [q, q + (side if sgn > 0 else Vector((-side.x * 0.4, -side.y, 0.0))) * 0.07 * (1.2 - 0.12 * k)], [0.006, 0.0], 3,
                     lambda t, a, p, d: hexc("#d9b46a"))
        # 6 本の白い脚（体の下に）
        for y in (0.3, 0.15, -0.0):
            tube(mb, [Vector((0.06 * sx, y, 0.05)), Vector((0.2 * sx, y + 0.04, 0.02)), Vector((0.28 * sx, y + 0.06, -0.08))], [0.018, 0.014, 0.0], 4,
                 lambda t, a, p, d: white)
    return build(mb, "Ga_Body")


def make_ga_wing():
    """オオミズアオのはね（右）：うすい水色。前ばねのへりはえんじ色、はねごとに目玉もよう、うしろばねに長い尾"""
    mb = MB()

    def rfn(th):
        fore = 1.85 * math.exp(-((th - 0.92) / 0.46) ** 2) + 0.12 * math.exp(-((th - 0.62) / 0.12) ** 2)
        hind = 1.3 * math.exp(-((th - 2.0) / 0.5) ** 2)
        tail = 2.75 * math.exp(-((th - 2.72) / 0.085) ** 2)
        return max(0.22, fore, hind, tail)

    def cf(rho, th, p):
        hind = th > 1.5
        c = mixc(hexc("#bfe7d3"), hexc("#dff4e8"), sstep(0.15, 0.7, rho))
        if not hind and th < 0.42 and rho > 0.18:
            return hexc("#8e3f5c")                             # 前ばねの、えんじ色のへり
        # 目玉もよう（前ばねと、うしろばねに 1 つずつ）
        for ce, cth in ((0.55, 0.95), (0.5, 2.0)):
            q = Vector((math.sin(cth) * ce, math.cos(cth) * ce, 0.0))
            dist = (Vector((p.x, p.y, 0.0)) / (1.85 if cth < 1.5 else 1.3) - q / 1.0).length
            if dist < 0.06:
                return hexc("#f3e9a0") if dist < 0.03 else hexc("#8e3f5c")
            if dist < 0.085:
                return hexc("#3a3a3a")
        if th > 2.55:
            c = mixc(c, hexc("#f0e2a0"), sstep(0.45, 0.9, rho))   # 尾の先は、きいろっぽい
        if abs(math.sin(th * (8.0 if not hind else 6.0) + 0.3)) < 0.06:
            c = scalec(c, 0.9)                                 # すじ
        return c
    polar_sheet(mb, rfn, cf, n_ang=96, n_rad=10, a0=0.05, a1=2.95, zfn=lambda rho, th: 0.03 * rho * rho, full=False)
    return build(mb, "Ga_Wing")


# ---- カナブン：金属光沢の緑と銅色の、たまご形の平たい体。かたい前ばねをとじたまま、横からうしろばねを出して飛ぶ ----
def make_kanabun():
    mb = MB()
    green = hexc("#3e6f3c")
    shine = hexc("#a9c45a")
    copper = hexc("#9a6a32")

    def elytra(t, a, p):
        s = math.sin(a)
        c = mixc(green, shine, sstep(0.45, 1.0, s) * 0.75 * (1 - 0.6 * t))
        c = mixc(c, copper, 0.35 * sstep(0.2, -0.2, math.cos(a + t * 2.0)))
        if abs(p.x) < 0.01 and s > 0:
            c = scalec(green, 0.6)
        return c
    seg_body(mb, -0.55, 0.16, 0.26, 0.17, elytra, rings=32, seg=26,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 1.08 + 0.1)) ** 0.35, math.sin(math.pi * t) ** 0.5), z0=0.13)
    # 小楯板（せなかの三角）と前胸
    uv_sphere(mb, Vector((0, 0.23, 0.19)), 1.0, lambda n: mixc(green, shine, sstep(0.3, 0.95, n.z) * 0.8), seg=18, rings=10,
              scale=Vector((0.21, 0.13, 0.1)))
    tube(mb, [Vector((0, 0.17, 0.26)), Vector((0, 0.13, 0.285)), Vector((0, 0.02, 0.285))], [0.05, 0.05, 0.0], 3,
         lambda t, a, p, d: scalec(copper, 0.9))   # 根もとは前胸の中
    # 頭（四角っぽい）と、くし形の触角
    uv_sphere(mb, Vector((0, 0.39, 0.13)), 1.0, lambda n: mixc(green, copper, 0.4), seg=12, rings=8, scale=Vector((0.1, 0.08, 0.06)))
    for sx in (-1, 1):
        cute_eye(mb, (0.075 * sx, 0.42, 0.13), 0.022, (0.5 * sx, 1, 0.1), white=False)
        a0 = Vector((0.06 * sx, 0.45, 0.12))
        a1 = Vector((0.12 * sx, 0.53, 0.14))
        tube(mb, [a0, a1], [0.01, 0.008], 4, lambda t, a, p, d: hexc("#2a2018"))
        uv_sphere(mb, a1 + Vector((0.01 * sx, 0.02, 0.0)), 0.025, lambda n: hexc("#4a3a24"), seg=6, rings=4, scale=Vector((0.5, 1.0, 1.4)))
    roots = []
    for y, dy in ((0.24, 0.12), (0.06, 0.0), (-0.12, -0.12)):
        for sx in (-1, 1):
            roots.append(((0.12 * sx, y, 0.09), (0.3 * sx, y + dy * 0.5, 0.15), (0.4 * sx, y + dy, 0.0)))
    legs(mb, roots, hexc("#22301e"), copper, radius=0.026)
    return build(mb, "Kanabun")


def make_kanabun_wing():
    """カナブンのうしろばね（右。付け根が原点）：うすい茶色の、すきとおったはね"""
    mb = MB()

    def rfn(th):
        return 0.12 + 0.62 * math.exp(-((th - 1.75) / 0.55) ** 2)

    def cf(rho, th, p):
        c = mixc(hexc("#cdb88a"), hexc("#e4d6b2"), rho)
        if abs(math.sin(th * 5)) < 0.08 or rho < 0.12:
            c = hexc("#7a5a32")
        return c
    polar_sheet(mb, rfn, cf, n_ang=32, n_rad=6, a0=0.6, a1=2.9, zfn=lambda rho, th: 0.02 * rho, full=False)
    return build(mb, "Kanabun_Wing")


# ---- スズムシとエンマコオロギ：秋の夕ぐれに、前ばねを立ててこすりあわせて鳴く。はねは別のメッシュ（鳴くとき立てる） ----
SUZUMUSHI_WING_ROOT = Vector((0.012, 0.07, 0.205))   # スズムシの前ばねの付け根（右。せなかのまん中の近く）
KOROGI_WING_ROOT = Vector((0.015, 0.06, 0.25))       # エンマコオロギの前ばねの付け根（右）


def cricket_wing(name, length, width, cup, base, vein, edge, rim_dark=0.0):
    """コオロギのなかまの前ばね（右。付け根が原点）：うしろへのびる、はばの広いうちわ形。外のへりは、体のわきへ下がる"""
    mb = MB()
    nu, nv = 16, 8
    rows = []
    for i in range(nu + 1):
        u = i / nu
        w = width * (0.42 + 0.58 * math.sin(math.pi * min(1.0, u * 0.62 + 0.18)))
        if u > 0.82:
            w *= math.sqrt(max(0.0, 1.0 - ((u - 0.82) / 0.18) ** 2)) * 0.9 + 0.1   # うしろのはしは、まるく
        row = []
        for j in range(nv + 1):
            v = j / nv
            p = Vector((v * w, -u * length, -cup * (v ** 2) * w))
            c = mixc(base, edge, sstep(0.78, 1.0, v) * 0.8 + rim_dark * sstep(0.85, 1.0, u))
            if abs(math.sin(v * 9.0 + u * 2.0)) < 0.09 or abs(math.sin(u * 14.0 - v * 5.0)) < 0.06:
                c = mixc(c, vein, 0.7)                     # はねのすじ（鳴くための、やすりのようなすじ）
            row.append(mb.v(p, c))
        rows.append(row)
    for i in range(nu):
        for j in range(nv):
            mb.f(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])
    return build(mb, name)


def make_suzumushi():
    """スズムシ（約 2cm）：黒くて平たい体、すきとおった、はばの広い前ばね（オス）、白い帯のある長い触角、白っぽい脚。
    「リーン、リーン」と、前ばねを立てて、すんだ声で鳴く"""
    mb = MB()
    black = hexc("#1c1814")
    brown = hexc("#3a2e22")
    pale = hexc("#d8ccb0")

    def col(t, a, p):
        c = mixc(black, brown, 0.35 + 0.25 * math.sin(p.y * 30.0))
        return mixc(c, hexc("#5a4a3a"), sstep(-0.3, -0.9, math.sin(a)) * 0.6)   # おなかは少し明るい
    seg_body(mb, -0.42, 0.1, 0.13, 0.09, col, rings=30, seg=22, z0=0.12,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 0.95 + 0.08)) ** 0.4, math.sin(math.pi * t) ** 0.5), flat=0.55)
    # 前胸と、小さな頭（口のまわりは白っぽい）
    uv_sphere(mb, Vector((0, 0.12, 0.15)), 1.0, lambda n: mixc(black, brown, 0.3 + 0.3 * n.z), seg=16, rings=10, scale=Vector((0.105, 0.075, 0.07)))
    uv_sphere(mb, Vector((0, 0.22, 0.14)), 1.0, lambda n: black if n.y < 0.6 else mixc(black, pale, 0.5), seg=14, rings=9,
              scale=Vector((0.08, 0.07, 0.07)))
    for sx in (-1, 1):
        cute_eye(mb, (0.06 * sx, 0.25, 0.17), 0.022, (0.5 * sx, 1, 0.1), white=False)
        # とても長い触角（体の 2 倍。付け根は黒、先は白い帯）
        a0 = Vector((0.025 * sx, 0.27, 0.19))
        pts = [a0, a0 + Vector((0.06 * sx, 0.2, 0.08)), a0 + Vector((0.2 * sx, 0.5, 0.12)), a0 + Vector((0.38 * sx, 0.78, 0.08)),
               a0 + Vector((0.55 * sx, 0.95, 0.0))]
        tube(mb, pts, [0.01, 0.008, 0.006, 0.005, 0.0], 4, lambda t, a, p, d: black if t < 0.18 else (pale if (t * 9) % 1.0 < 0.75 else brown))
        # 尾（2 本）
        tube(mb, [Vector((0.03 * sx, -0.38, 0.13)), Vector((0.08 * sx, -0.58, 0.16))], [0.01, 0.0], 4, lambda t, a, p, d: pale)
    # 脚：前と中の脚は細く、うしろ足は、ももが太い（ぴょんと跳ぶ）
    roots = []
    for (y, k, tp) in ((0.14, 0.24, 0.32), (0.02, 0.06, 0.08)):
        for sx in (-1, 1):
            roots.append(((0.08 * sx, y, 0.1), (0.16 * sx, k, 0.13), (0.22 * sx, tp, 0.0)))
    legs(mb, roots, pale, brown, radius=0.013)
    hind = []
    for sx in (-1, 1):
        hind.append(((0.08 * sx, -0.08, 0.1), (0.18 * sx, -0.3, 0.26), (0.24 * sx, -0.52, 0.0)))
    legs(mb, hind, pale, brown, radius=0.024)
    return build(mb, "Suzumushi")


def make_suzumushi_wing():
    """スズムシの前ばね（右）：すきとおった、うすい茶色の、はばの広いはね（鳴くときに立てて、こすりあわせる）"""
    return cricket_wing("Suzumushi_Wing", 0.46, 0.16, 0.45, hexc("#d9c8a0"), hexc("#6a5634"), hexc("#3a2e22"))


def make_korogi():
    """エンマコオロギ（約 3cm）：つやのある黒茶色の体、まるい大きな頭と、目の上の白っぽい「まゆ」（えんま様の顔）、
    長い触角と 2 本の尾、太いうしろ足。「コロコロリー」と鳴く"""
    mb = MB()
    dark = hexc("#22170f")
    brown = hexc("#4a3220")
    gloss = hexc("#7a5a3c")
    pale = hexc("#c9a676")

    def col(t, a, p):
        c = mixc(dark, brown, 0.3 + 0.3 * sstep(-0.2, 0.8, math.sin(a)))
        if abs(math.sin(p.y * 22.0)) < 0.12:
            c = scalec(c, 0.8)   # おなかのふし
        return c
    seg_body(mb, -0.55, 0.12, 0.16, 0.12, col, rings=32, seg=22, z0=0.14,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 0.95 + 0.08)) ** 0.42, math.sin(math.pi * t) ** 0.5), flat=0.5)
    # 前胸（かたい板）と、まるい大きな頭（つやつや）
    uv_sphere(mb, Vector((0, 0.12, 0.19)), 1.0, lambda n: mixc(dark, gloss, sstep(0.5, 0.95, n.z) * 0.5), seg=18, rings=10,
              scale=Vector((0.15, 0.1, 0.1)))
    hc = Vector((0, 0.27, 0.2))
    uv_sphere(mb, hc, 1.0, lambda n: mixc(dark, gloss, sstep(0.55, 0.95, n.z) * 0.6), seg=18, rings=12, scale=Vector((0.12, 0.11, 0.11)))
    for sx in (-1, 1):
        cute_eye(mb, (0.08 * sx, 0.33, 0.24), 0.03, (0.5 * sx, 1, 0.15), white=False)
        # 目の上の白っぽい「まゆ」
        brow = [hc + Vector((d.x * 0.12, d.y * 0.11, d.z * 0.11)) * 1.0 for d in
                (Vector((0.2 * sx, 0.55, 0.81)).normalized(), Vector((0.55 * sx, 0.5, 0.67)).normalized(), Vector((0.85 * sx, 0.25, 0.46)).normalized())]
        brow = [hc + (q - hc) * 0.99 for q in brow]   # 頭の面の上に（両はしはとじる）
        tube(mb, brow, [0.0, 0.018, 0.0], 5, lambda t, a, p, d: pale)
        a0 = Vector((0.03 * sx, 0.36, 0.24))
        pts = [a0, a0 + Vector((0.08 * sx, 0.25, 0.08)), a0 + Vector((0.25 * sx, 0.6, 0.1)), a0 + Vector((0.45 * sx, 0.9, 0.04)),
               a0 + Vector((0.6 * sx, 1.1, -0.04))]
        tube(mb, pts, [0.012, 0.01, 0.008, 0.006, 0.0], 4, lambda t, a, p, d: mixc(dark, brown, t))
        # 尾（2 本、長め）
        tube(mb, [Vector((0.04 * sx, -0.5, 0.16)), Vector((0.09 * sx, -0.7, 0.2)), Vector((0.13 * sx, -0.86, 0.2))], [0.014, 0.01, 0.0], 4,
             lambda t, a, p, d: mixc(brown, pale, t * 0.5))
    roots = []
    for (y, k, tp) in ((0.16, 0.28, 0.38), (0.02, 0.06, 0.1)):
        for sx in (-1, 1):
            roots.append(((0.07 * sx, y, 0.13), (0.2 * sx, k, 0.15), (0.28 * sx, tp, 0.0)))   # 付け根は、体の中から
    legs(mb, roots, brown, dark, radius=0.017)
    hind = []
    for sx in (-1, 1):
        hind.append(((0.1 * sx, -0.1, 0.12), (0.22 * sx, -0.4, 0.32), (0.3 * sx, -0.68, 0.0)))
    legs(mb, hind, brown, dark, radius=0.032)
    return build(mb, "Korogi")


def make_korogi_wing():
    """エンマコオロギの前ばね（右）：こげ茶色に、黒いすじ。へりは少し明るい"""
    return cricket_wing("Korogi_Wing", 0.48, 0.19, 0.55, hexc("#5a3e26"), hexc("#20150c"), hexc("#8a6a44"), rim_dark=0.3)


# ---------------------------------------------------------------------------
# 夜の森のいきもの：ミノムシ・ザトウムシ・キリギリス・イノシシとウリボウ（シマエナガは鳥の作り方）
# 大きさは本物と同じ（1 単位 = 2.5cm）
# ---------------------------------------------------------------------------
MINO_MOUTH = Vector((0.0, 0.1, -0.24))              # みのの口（幼虫が顔を出す所。原点は、糸の付く所）
KIRIGIRISU_WING_ROOT = Vector((0.015, 0.06, 0.27))  # キリギリスの前ばねの付け根（右）
MINO_PROFILE = [(0.03, 0.0), (0.0, 0.07), (-0.18, 0.17), (-0.55, 0.3), (-1.0, 0.27), (-1.32, 0.15), (-1.5, 0.0)]


def mino_radius(z):
    """みのの、高さ z（原点から下へ、負）での太さ"""
    for (z0, r0), (z1, r1) in zip(MINO_PROFILE, MINO_PROFILE[1:]):
        if z1 <= z <= z0:
            return lerp(r0, r1, (z0 - z) / max(1e-6, z0 - z1))
    return 0.0


def make_minomushi():
    """ミノムシ（オオミノガの幼虫のみの・約 4cm）：枯れ葉や小枝のかけらを糸でつづった、細長いつむ形のみの。
    上のはしから、1 本の糸でぶら下がる（原点は糸の付く所。みのは下へのびる）"""
    mb = MB()
    rnd = random.Random(31)
    silk = hexc("#8a7458")
    pts = [Vector((0, 0, z)) for z, r in MINO_PROFILE]
    tube(mb, pts, [r for z, r in MINO_PROFILE], 14,
         lambda t, a, p, d: mixc(silk, hexc("#5e4a32"), 0.5 + 0.5 * noise.noise(p * 7.0)))
    # 小枝のかけら（みのの面に、たてに、少しななめに。両はしは、とじる）
    twig = (hexc("#6b4f33"), hexc("#8f6f48"), hexc("#4e3a26"))
    for k in range(28):
        z = rnd.uniform(-1.2, -0.3)
        ang = rnd.uniform(0, TAU)
        ln = rnd.uniform(0.22, 0.42)
        turn = rnd.uniform(-0.25, 0.25)
        # みのの面にそわせて、たてにのせる（下のほうでも、外へつき出ない）
        pts_ = []
        for j in range(4):
            zz = z + ln * (0.5 - j / 3)
            aa = ang + turn * (j / 3 - 0.5)
            rr = mino_radius(zz) * 0.97 + 0.012
            pts_.append(Vector((math.cos(aa) * rr, math.sin(aa) * rr, zz)))
        c = twig[k % 3]
        tube(mb, [pts_[0] + (pts_[0] - pts_[1]).normalized() * 0.015] + pts_ + [pts_[-1] + (pts_[-1] - pts_[-2]).normalized() * 0.015],
             [0.0, 0.028, 0.026, 0.024, 0.022, 0.0], 5, lambda t, aa, p, d, c=c: mixc(c, scalec(c, 0.75), t))
    # 枯れ葉のかけら（小さな、そった葉。みのの面にはりつく）
    leaf = (hexc("#a8783a"), hexc("#c49a52"), hexc("#7e5a30"))
    for k in range(16):
        z = rnd.uniform(-1.15, -0.3)
        ang = rnd.uniform(0, TAU)
        r = mino_radius(z)
        n = Vector((math.cos(ang), math.sin(ang), 0.0))
        c0 = Vector((n.x * r, n.y * r, z))
        up = Vector((0, 0, 1))
        side = n.cross(up)
        w = rnd.uniform(0.12, 0.2)
        h = rnd.uniform(0.18, 0.3)
        c = leaf[k % 3]
        ribbon(mb, [c0 - up * h * 0.5, c0, c0 + up * h * 0.5], [0.0, w, 0.0], [side] * 3,
               lambda t, v, p, c=c: mixc(c, scalec(c, 0.8), abs(v)), fold=0.15, normal_vecs=[n] * 3)
    return build(mb, "Minomushi")


def make_minomushi_larva():
    """みのから顔を出す幼虫（原点は、みのの口。前 +Y へ出る）：こげ茶のしまのある胸と、つやのある頭、3 対の小さな脚"""
    mb = MB()
    dark = hexc("#3a2a1c")
    tan = hexc("#b89a6a")
    segs = [(Vector((0, -0.04, 0.0)), 0.1), (Vector((0, 0.06, 0.02)), 0.095), (Vector((0, 0.15, 0.03)), 0.088)]
    for i, (c, r) in enumerate(segs):
        uv_sphere(mb, c, r, lambda n: mixc(tan, dark, 0.7 if (n.z > 0.2 and abs(n.x) > 0.3) else 0.15), seg=12, rings=8)
    hc = Vector((0, 0.24, 0.035))
    uv_sphere(mb, hc, 1.0, lambda n: mixc(dark, tan, 0.55) if abs(n.x) < 0.25 and n.z > 0.1 else dark, seg=14, rings=9,
              scale=Vector((0.085, 0.08, 0.078)))
    for sx in (-1, 1):
        uv_sphere(mb, hc + Vector((0.05 * sx, 0.05, 0.025)), 0.014, lambda n: hexc("#0a0806"), seg=6, rings=4)
    for c, r in segs:
        for sx in (-1, 1):
            base = c + Vector((0.06 * sx, 0.02, -0.04))
            tube(mb, [base, base + Vector((0.05 * sx, 0.04, -0.06)), base + Vector((0.055 * sx, 0.06, -0.08))], [0.02, 0.014, 0.0], 5,
                 lambda t, a, p, d: dark)
    return build(mb, "Minomushi_Larva")


def make_minomushi_thread():
    """みのをつるす糸（長さ 1。原点から下へ。Unity で、枝までの長さにのばす）"""
    mb = MB()
    zs = [0.02, 0.0, -0.25, -0.5, -0.75, -1.0, -1.02]
    tube(mb, [Vector((0, 0, z)) for z in zs], [0.0, 0.014, 0.012, 0.011, 0.012, 0.014, 0.0], 6, lambda t, a, p, d: hexc("#e8e4da"))
    return build(mb, "Minomushi_Thread", smooth=True)


def make_zatoumushi():
    """ザトウムシ（体は約 8mm・脚をひろげると 12cm）：まるい小さな体と、とても長くて細い 8 本の脚。
    体のまん中の小さなこぶに、目が 2 つ。高くひざを上げて、体を宙につるすように歩く"""
    mb = MB()
    brown = hexc("#7a5a3c")
    dark = hexc("#2e2218")
    pale = hexc("#c8ad86")
    c0 = Vector((0, 0.0, 0.66))

    def body_col(n):
        p = c0 + Vector((n.x * 0.17, n.y * 0.21, n.z * 0.12))
        c = mixc(brown, pale, 0.3 * sstep(-0.2, -0.8, n.z))
        if n.z > 0.35 and abs(n.x) < 0.45:
            c = mixc(c, dark, 0.7)                        # せなかの、こい色のくら
        return mixc(c, scalec(c, 0.8), 0.5 + 0.5 * noise.noise(p * 20.0))
    uv_sphere(mb, c0, 1.0, body_col, seg=20, rings=12, scale=Vector((0.17, 0.21, 0.12)))
    oc = c0 + Vector((0, 0.06, 0.1))
    uv_sphere(mb, oc, 0.045, lambda n: mixc(brown, dark, 0.4), seg=10, rings=6)            # 目のこぶ
    for sx in (-1, 1):
        uv_sphere(mb, oc + Vector((0.03 * sx, 0.0, 0.015)), 0.018, lambda n: hexc("#0c0a08"), seg=8, rings=5)
        # 口のそばの、短いひげ（触肢）
        tube(mb, [c0 + Vector((0.04 * sx, 0.16, -0.04)), c0 + Vector((0.07 * sx, 0.27, -0.1)), c0 + Vector((0.07 * sx, 0.31, -0.2))],
             [0.016, 0.012, 0.0], 5, lambda t, a, p, d: pale)
    roots = []
    # 前から：2 番目の脚がいちばん長い（さぐるための脚）
    for (y, span, ang) in ((0.1, 1.8, 50.0), (0.04, 2.6, 15.0), (-0.04, 2.0, -18.0), (-0.1, 2.3, -52.0)):
        for sx in (-1, 1):
            d = Vector((math.cos(math.radians(ang)) * sx, math.sin(math.radians(ang)), 0.0))
            base = c0 + Vector((0.11 * sx, y, -0.03))
            knee = c0 + d * (span * 0.36) + Vector((0, 0, 0.6))
            tip = Vector((d.x * span, d.y * span, 0.0))
            roots.append((base, knee, tip))
    legs(mb, roots, brown, dark, radius=0.013)
    return build(mb, "Zatoumushi")


def make_kirigirisu():
    """キリギリス（オス・約 3.5cm）：あざやかな緑の体、せなかの茶色の帯、体よりずっと長い触角、たくましい長い後ろ足。
    前ばね（別のメッシュ）は茶色に黒いまだら。「ギーッ、チョン」と鳴く"""
    mb = MB()
    green = hexc("#6fa83a")
    light = hexc("#b2d878")
    dark = hexc("#3e6a22")
    brown = hexc("#7e5c32")

    def col(t, a, p):
        c = mixc(green, dark, sstep(0.4, 1.0, math.sin(a)) * 0.35)
        c = mixc(c, light, sstep(-0.2, -0.8, math.sin(a)))
        if abs(math.sin(p.y * 20.0)) < 0.1:
            c = scalec(c, 0.85)                           # おなかのふし
        return c
    seg_body(mb, -0.72, 0.1, 0.15, 0.14, col, rings=32, seg=22, z0=0.18,
             prof=lambda t: (math.sin(math.pi * min(1.0, t * 0.95 + 0.08)) ** 0.42, math.sin(math.pi * t) ** 0.5), flat=0.5)
    # 前胸（くらの形）：せなかに茶色の帯
    uv_sphere(mb, Vector((0, 0.13, 0.25)), 1.0, lambda n: mixc(green, brown, sstep(0.55, 0.9, n.z) * 0.85), seg=18, rings=10,
              scale=Vector((0.135, 0.14, 0.11)))
    hc = Vector((0, 0.31, 0.22))
    uv_sphere(mb, hc, 1.0, lambda n: mixc(green, light, sstep(-0.3, -0.8, n.z)), seg=18, rings=12, scale=Vector((0.1, 0.11, 0.13)))
    for sx in (-1, 1):
        cute_eye(mb, (0.075 * sx, 0.35, 0.27), 0.028, (0.5 * sx, 1, 0.1), white=False)
        a0 = Vector((0.03 * sx, 0.37, 0.29))
        pts = [a0, a0 + Vector((0.05 * sx, 0.3, 0.16)), a0 + Vector((0.18 * sx, 0.8, 0.22)), a0 + Vector((0.32 * sx, 1.4, 0.14)),
               a0 + Vector((0.45 * sx, 2.0, 0.0))]
        tube(mb, pts, [0.011, 0.009, 0.006, 0.004, 0.0], 4, lambda t, a, p, d: mixc(brown, green, 0.3))
        tube(mb, [Vector((0.03 * sx, -0.66, 0.18)), Vector((0.06 * sx, -0.8, 0.2))], [0.014, 0.0], 4, lambda t, a, p, d: brown)   # 尾
    roots = []
    for (base, knee, tip) in (((0.06, 0.15, 0.19), (0.2, 0.3, 0.18), (0.28, 0.42, 0.0)),
                              ((0.05, 0.0, 0.17), (0.2, 0.06, 0.18), (0.28, 0.1, 0.0))):
        for sx in (-1, 1):
            roots.append(((base[0] * sx, base[1], base[2]), (knee[0] * sx, knee[1], knee[2]), (tip[0] * sx, tip[1], tip[2])))
    legs(mb, roots, green, brown, radius=0.017)
    hind = []
    for sx in (-1, 1):
        hind.append(((0.07 * sx, -0.12, 0.17), (0.22 * sx, -0.5, 0.44), (0.28 * sx, -0.98, 0.0)))
    legs(mb, hind, green, brown, radius=0.032)
    return build(mb, "Kirigirisu")


def make_kirigirisu_wing():
    """キリギリスの前ばね（右）：茶色に、黒いまだら（鳴くための、やすりのすじ）"""
    return cricket_wing("Kirigirisu_Wing", 0.44, 0.17, 0.6, hexc("#8e6c3c"), hexc("#2a1e12"), hexc("#b08e5e"), rim_dark=0.2)


# イノシシの体（おとなの単位。ウリボウは、まるく短い別の形を k 倍する）：背骨 (y, z, 太さ)・頭・手足
BOAR_SHAPES = {
    False: dict(k=1.0, body=[(-21.0, 17.0, 0.0), (-18.5, 19.0, 9.0), (-10.0, 20.5, 11.0), (1.0, 22.0, 12.2), (8.0, 23.0, 13.0), (12.5, 22.0, 11.0),
                              (15.0, 21.5, 0.0)],
                head=[(8.5, 21.5, 0.0), (11.0, 21.0, 10.5), (16.0, 19.5, 8.8), (21.0, 16.5, 6.2), (25.5, 14.0, 4.0), (28.0, 13.2, 3.4),
                      (28.4, 13.1, 0.0)],
                thighs=[((5.4, 8.0, 18.5), (3.2, 5.5, 7.0)), ((5.6, -12.0, 17.5), (3.4, 6.0, 6.5))], ear=2.4, ear_at=0.24,
                legs=[((6.0, 8.0, 14.0), (6.4, 6.5, 7.5), (6.4, 8.2, 2.2), 1.4), ((6.6, -12.0, 14.5), (7.0, -9.5, 8.0), (7.0, -12.5, 2.2), 1.4)],
                radii=(4.0, 2.6, 2.3, 1.6, 1.8), eye=1.15, tail=(-20.0, 19.0)),
    True: dict(k=0.4, body=[(-18.0, 15.0, 0.0), (-15.5, 17.5, 9.5), (-8.0, 19.0, 12.0), (1.0, 19.5, 12.5), (8.0, 19.0, 11.5), (11.5, 18.5, 9.0),
                             (13.5, 18.4, 0.0)],
               head=[(8.0, 19.2, 0.0), (10.0, 19.0, 9.5), (14.0, 18.3, 8.4), (17.5, 16.6, 6.0), (20.0, 15.2, 4.2), (21.3, 14.8, 3.8),
                     (21.7, 14.7, 0.0)],
               thighs=[((5.0, 7.0, 16.0), (3.4, 5.0, 6.0)), ((5.2, -10.5, 15.5), (3.6, 5.5, 6.0))], ear=2.6, ear_at=0.14,
               legs=[((5.5, 7.0, 12.0), (5.8, 5.8, 6.5), (5.8, 7.4, 2.2), 1.3), ((5.8, -10.5, 12.5), (6.2, -8.5, 7.0), (6.2, -11.0, 2.2), 1.3)],
               radii=(4.2, 2.8, 2.4, 1.7, 1.9), eye=1.5, tail=(-17.0, 17.0)),
}


def along(path, s):
    """[(位置, 太さ), ...] にそって、s（0〜1）の所の位置と太さ"""
    f = s * (len(path) - 1)
    i = min(int(f), len(path) - 2)
    t = f - i
    return path[i][0].lerp(path[i + 1][0], t), lerp(path[i][1], path[i + 1][1], t)


def densify(path, n=4):
    """[(位置, 太さ), ...] の、となりどうしのあいだに点を足す（体の輪を細かくして、毛並みのでこぼこを出す）"""
    out = []
    for (p0, r0), (p1, r1) in zip(path, path[1:]):
        for k in range(n):
            t = k / n
            out.append((p0.lerp(p1, t), lerp(r0, r1, t)))
    out.append(path[-1])
    return out


def fur_bumps(scale):
    """ごわごわの毛並み：こまかいでこぼこ（つるつるの面に、大きな光の帯が出ないように）"""
    return lambda t, a: 1.0 + 0.045 * noise.noise(Vector((math.cos(a) * 5.0, math.sin(a) * 5.0, t * scale)))         + 0.03 * noise.noise(Vector((math.cos(a) * 13.0, math.sin(a) * 13.0, t * scale * 2.7)))


def make_boar(name, piglet):
    """イノシシ（おとな・体は約 1.2m）と、ウリボウ（子ども・約 45cm）。
    おとな：こげ茶の、かたい毛（毛先は白っぽい）。くさび形の頭の先に、平たい円ばんの鼻と小さなきば、小さな目と、ぴんと立った耳。
    せなかには、こい色のたてがみ。短い足と、2 つに分かれたひづめ。細いしっぽの先に、ふさ。
    ウリボウ：まるい体に、うり（まくわうり）のような、たてのしま。顔は、おとなより短く、まるい"""
    mb = MB()
    sh = BOAR_SHAPES[piglet]
    k = sh["k"]
    fur = hexc("#6a5848") if not piglet else hexc("#906844")
    fur2 = hexc("#46382c") if not piglet else hexc("#74502f")
    grey = hexc("#a09282")
    stripe = hexc("#e8d2a0")
    hoof = hexc("#1e1814")

    def body_col(t, a, p, d):
        c = mixc(fur, fur2, 0.5 + 0.5 * noise.noise(p * (0.35 / k)))
        if not piglet:
            if noise.noise(p * (1.4 / k)) > 0.3:
                c = mixc(c, grey, 0.45)                       # 白っぽい毛先がまじる（ごましお）
            return mixc(c, scalec(fur2, 0.8), sstep(-0.3, -0.8, d.z))
        # ウリボウ：せなかから、わき腹へ、たてのしま（明るいしまと、こい茶色のしまが、かわりばんこ）
        if d.z > -0.55:
            band = abs(math.sin(math.atan2(d.z, d.x) * 4.5))
            if band > 0.8:
                c = stripe
            elif band < 0.3:
                c = mixc(c, hexc("#4e321e"), 0.7)
        return mixc(c, hexc("#c9ab7a"), sstep(-0.5, -0.9, d.z))
    body = [(Vector((0, y, z)) * k, r * k) for y, z, r in sh["body"]]
    fine = densify(body)
    tube(mb, [p for p, r in fine], [r for p, r in fine], 28, body_col, oval=(1.0, 0.8 if not piglet else 0.86),
         rfn=fur_bumps(40.0 if not piglet else 20.0))
    # 頭：首から鼻先へ、くさび形に細くなる
    head = [(Vector((0, y, z)) * k, r * k) for y, z, r in sh["head"]]

    def head_col(t, a, p, d):
        if t > 0.8:
            return hexc("#6a5446")
        c = mixc(body_col(t, a, p, d), fur2, 0.2)
        if not piglet and d.z < 0.1 and 0.3 < t < 0.8:
            c = mixc(c, hexc("#9a8a76"), 0.45 * sstep(0.1, -0.4, d.z))   # ほおから口もとは、白っぽい
        return c
    fine_head = densify(head, 3)
    tube(mb, [p for p, r in fine_head], [r for p, r in fine_head], 24, head_col, oval=(1.0, 0.82), rfn=fur_bumps(24.0))
    tip, nose_r = head[-2]
    face = head[1:-1]   # 頭の、太さのある所（両はしの極をのぞく）
    # 平たい円ばんの鼻（前を向く）と、2 つの穴。筒の口は、頭の中と、円ばんの中
    disc = tip + Vector((0, 0.9 * k, 0.0))
    tube(mb, [tip - Vector((0, 1.0 * k, 0.0)), disc], [nose_r, nose_r * 0.98], 18, lambda t, a, p, d: hexc("#8a6a5c"), oval=(1.0, 0.8))
    uv_sphere(mb, disc, 1.0, lambda n: hexc("#9a7a6a") if n.y < 0.6 else hexc("#b08a7a"), seg=16, rings=8,
              scale=Vector((nose_r * 0.86, 0.3 * k, nose_r * 1.04)))
    for sx in (-1, 1):
        uv_sphere(mb, disc + Vector((0.36 * nose_r * sx, 0.24 * k, 0.05 * nose_r)), 0.22 * nose_r, lambda n: hexc("#2a1c18"), seg=8, rings=5,
                  scale=Vector((1.0, 0.4, 1.2)))
    for sx in (-1, 1):
        # 肩と、うしろ足のもも（体のふくらみ。手足の付け根を、体になじませる）
        for (tc, ts) in sh["thighs"]:
            tp = Vector((tc[0] * sx, tc[1], tc[2])) * k
            n0 = len(mb.V)
            uv_sphere(mb, tp, k, lambda n, tp=tp: body_col(0.5, 0.0, tp + n * 4.0 * k, n), seg=16, rings=12, scale=Vector(ts))
            for i in range(n0, len(mb.V)):
                v = mb.V[i] - tp
                mb.V[i] = tp + v * (1.0 + 0.06 * noise.noise(v / k * 0.9 + Vector((sx * 3.0, 0, 0))))
        # 小さな目（顔のわき、少し上。半分ほど、表面から出る）
        c, r = along(face, 0.36)
        ep = c + Vector((math.cos(0.7) * r * 0.82 * sx, 0.0, math.sin(0.7) * r))
        cute_eye(mb, ep, sh["eye"] * k, (0.6 * sx, 0.7, 0.1), white=False)
        # ぴんと立った耳（付け根は頭の中）
        c, r = along(face, sh["ear_at"])
        eb = c + Vector((r * 0.4 * sx, -0.3 * k, r * 0.72))
        er = sh["ear"]
        tube(mb, [eb - Vector((0, 0, 1.5)) * k, eb, eb + Vector((1.0 * sx, -0.6, 2.8)) * k, eb + Vector((1.7 * sx, -1.0, 5.4)) * k],
             [0.0, er * k, er * 0.72 * k, 0.0], 10, lambda t, a, p, d: mixc(fur2, hexc("#2a201a"), sstep(0.3, 1.0, t) * 0.6), oval=(1.0, 0.42))
        # 口もとの、小さなきば（おとなだけ）
        if not piglet:
            c, r = along(face, 0.9)
            tb = c + Vector((r * 0.62 * sx, 0.0, -r * 0.3))
            tube(mb, [tb, tb + Vector((0.6 * sx, 0.6, 1.0)) * k, tb + Vector((0.8 * sx, 0.7, 1.8)) * k], [0.6 * k, 0.4 * k, 0.0], 8,
                 lambda t, a, p, d: hexc("#efe6d2"))
    # せなかの、たてがみ（おとなだけ。こい色の、長くかたい毛）
    if not piglet:
        rnd = random.Random(7)
        for i in range(28):
            u = i / 27
            c, r = along(body[1:-1], lerp(0.3, 0.98, u))
            base = c + Vector((rnd.uniform(-0.6, 0.6), 0.0, r * 0.86))
            tip_ = base + Vector((rnd.uniform(-0.6, 0.6), -1.6, 3.0 + 2.0 * math.sin(math.pi * u)))
            tube(mb, [base - Vector((0, 0, 1.2)), base, base.lerp(tip_, 0.5), tip_], [0.0, 1.1, 0.7, 0.0], 6,
                 lambda t, a, p, d: mixc(fur2, hexc("#1a1410"), sstep(0.3, 1.0, t)))
    # 細いしっぽと、先のふさ（付け根は、おしりの中）
    ty, tz = sh["tail"]
    tb = Vector((0, ty, tz)) * k
    tail = [tb + Vector((0, 1.5, 0.5)) * k, tb, tb + Vector((0, -2.2, -2.0)) * k, tb + Vector((0, -2.8, -6.5)) * k]
    tube(mb, tail, [0.0, 1.2 * k, 0.8 * k, 0.5 * k], 8, lambda t, a, p, d: fur2)
    uv_sphere(mb, tail[-1] + Vector((0, 0, -0.6)) * k, 1.0, lambda n: hexc("#1e1712"), seg=10, rings=6, scale=Vector((0.9, 0.9, 1.6)) * k)
    # 短い足と、ひづめ（ひざで曲がる手足）
    legs_def = [(tuple(v * k for v in a), tuple(v * k for v in b), tuple(v * k for v in c), fl * k) for a, b, c, fl in sh["legs"]]
    boar_legs(mb, legs_def, lambda p: mixc(body_col(0.5, 0.0, p, Vector((0, 0, -0.3))), fur2, 0.3), scalec(fur2, 0.8), hoof,
              tuple(r * k for r in sh["radii"]))
    return build(mb, name)


def boar_legs(mb, legs_def, upper_col, lower_col, hoof_col, radii):
    """イノシシの 4 本の足（ひざで曲がる）：太いももと、細いすねの先に、2 つに分かれたひづめ"""
    ru0, ru1, rl0, rl1, rh = radii
    for (sh, el, wr, fl) in legs_def:
        for sx in (-1, 1):
            a = Vector((sh[0] * sx, sh[1], sh[2]))
            b = Vector((el[0] * sx, el[1], el[2]))
            c = Vector((wr[0] * sx, wr[1], wr[2]))

            def up(m, a=a, b=b):
                # 付け根は、体の中まで太く（脚をふっても、体とのあいだにすき間ができない）
                top = a + (a - b).normalized() * ru0 * 0.6
                tube(m, [top, a, a.lerp(b, 0.45), b], [ru0 * 0.8, ru0, (ru0 + ru1) * 0.5, ru1], 12, lambda t, aa, p, d: upper_col(p))
                uv_sphere(m, a, ru0, lambda n: upper_col(a), seg=12, rings=8)
                uv_sphere(m, b, ru1, lambda n: upper_col(b), seg=10, rings=6)

            def lo(m, b=b, c=c):
                col = lambda t, aa, p, d: mixc(upper_col(p), lower_col, sstep(0.0, 0.5, t))
                tube(m, [b, b.lerp(c, 0.5), c], [rl0, (rl0 + rl1) * 0.5, rl1], 10, col)
                joint_ball(m, c, rl1, col)   # 足首

            def ft(m, c=c, fl=fl):
                for hx in (-1, 1):
                    tip = c + Vector((rh * 0.42 * hx, fl, -c.z + rh * 0.375))
                    tube(m, [c, c.lerp(tip, 0.5), tip + Vector((0, 0, rh * 0.1))], [rl1 * 0.8, rh * 0.55, rh * 0.36], 8,
                         lambda t, aa, p, d: mixc(lower_col, hoof_col, t))
                    uv_sphere(m, tip, rh * 0.5, lambda n: hoof_col, seg=8, rings=5, scale=Vector((0.9, 1.3, 0.75)))
            limb(mb, a, b, c, up, lo, ft)


def make_inoshishi():
    return make_boar("Inoshishi", False)


def make_uribou():
    return make_boar("Uribou", True)


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
        (make_yamori, "Yamori", None), (make_hasamimushi, "Hasamimushi", None),
        # 手足を別に動かす：鳥（1 歩ずつ歩く・跳ねる・飛ぶと足をたたむ）・カエル・ナキウサギ・ハリネズミ（ひざで曲がる手足）・
        # だんごむし（7 対の脚）・ゲンゴロウ（オールの足）・モグラ（土をかく前足）・オコジョ（前足）
        (make_pillbug, "PillBug", None), (make_frog, "Frog", None), (make_gengorou, "Gengorou", None), (make_mogura, "Mogura", None),
        (make_harinezumi, "Harinezumi", None), (make_okojo, "Okojo", None), (make_nakiusagi, "Nakiusagi", None),
        (lambda: make_bird_body("Sparrow_Body", 5.5, False), "Sparrow_Body", None),
        (lambda: make_bird_body("Crow_Body", 18.0, True), "Crow_Body", None),
        (lambda: make_bird_body("Hato_Body", 13.0, "pigeon"), "Hato_Body", None),
        (lambda: make_bird_body("Flamingo_Body", 20.0, "flamingo"), "Flamingo_Body", None),
        (lambda: make_bird_body("Raichou_Body", 15.0, "raichou"), "Raichou_Body", None),
        (make_nezumi, "Nezumi", None),
        (lambda: make_bird_body("Tsubame_Body", 7.0, "tsubame"), "Tsubame_Body", None),
        # 海岸
        (make_yadokari, "Yadokari", None), (make_shiomaneki, "Shiomaneki", None), (make_funamushi, "Funamushi", None),
        (lambda: make_bird_body("Kamome_Body", 19.0, "gull"), "Kamome_Body", None),
        # 神社
        (make_kitsune, "Kitsune", None), (make_tanuki, "Tanuki", None), (make_kanabun, "Kanabun", None),
        (make_fukurou_body, "Fukurou_Body", None),
        (make_suzumushi, "Suzumushi", None), (make_korogi, "Korogi", None),
        # 夜の森
        (make_zatoumushi, "Zatoumushi", None), (make_kirigirisu, "Kirigirisu", None),
        (make_inoshishi, "Inoshishi", None), (make_uribou, "Uribou", None),
        (lambda: make_bird_body("Shimaenaga_Body", 4.8, "enaga"), "Shimaenaga_Body", None),
    ]
    makers = [
        make_snail, make_butterfly_body, make_butterfly_wing,
        make_dragonfly_body, make_dragonfly_wing, make_river_snail,
        make_firefly_body, make_firefly_glow, make_monshiro_body, make_monshiro_wing,
        make_cradle, make_anthill, make_grasshopper_hind, make_mantis_arm, make_ant_helmet, make_crumb, make_pillbug_ball,
        lambda: make_bird_wing("Sparrow_Wing", 5.5, False),
        lambda: make_bird_wing_folded("Sparrow_WingFolded", 5.5, False),
        lambda: make_bird_wing("Crow_Wing", 18.0, True),
        lambda: make_bird_wing_folded("Crow_WingFolded", 18.0, True),
        lambda: make_river_stone("RiverStone_A", 1, (1.6, 1.2, 0.55), (hexc("#8d96a3"), hexc("#b7b2a5"))),
        lambda: make_river_stone("RiverStone_B", 2, (1.3, 1.1, 0.7), (hexc("#a99a84"), hexc("#d2c4a8"))),
        lambda: make_river_stone("RiverStone_C", 3, (1.9, 1.5, 0.45), (hexc("#7d8a86"), hexc("#a6b0a4"))),
        make_horsetail, make_iris, make_root_arch,
        make_koumori_body, make_koumori_wing, make_mogura_hill, make_hanakamakiri_arm,
        make_harinezumi_ball,
        lambda: make_bird_wing("Hato_Wing", 13.0, "pigeon"),
        lambda: make_bird_wing_folded("Hato_WingFolded", 13.0, "pigeon"),
        lambda: make_bird_wing("Flamingo_Wing", 20.0, "flamingo"),
        lambda: make_bird_wing_folded("Flamingo_WingFolded", 20.0, "flamingo"),
        lambda: make_bird_wing("Raichou_Wing", 15.0, "raichou"),
        lambda: make_bird_wing_folded("Raichou_WingFolded", 15.0, "raichou"),
        make_okojo_rocks, make_asagimadara_body, make_asagimadara_wing, make_maruhanabachi_wing,
        make_oniyanma_body, make_oniyanma_wing, make_maruhanabachi,
        make_mukade_head, make_mukade_seg, make_mukade_tail, make_mukade_leg, make_hasamimushi_tail,
        make_hae_body, make_hae_arm, make_hae_wing,
        lambda: make_bird_wing("Tsubame_Wing", 7.0, "tsubame"),
        lambda: make_bird_wing_folded("Tsubame_WingFolded", 7.0, "tsubame"),
        make_yadokari_crab, make_shiomaneki_claw, make_hitode, make_hitode_arm, make_uni,
        lambda: make_uni_spines(0), lambda: make_uni_spines(1), lambda: make_uni_spines(2),
        make_manta, make_manta_fin,
        lambda: make_bird_wing("Kamome_Wing", 19.0, "gull"),
        lambda: make_bird_wing_folded("Kamome_WingFolded", 19.0, "gull"),
        make_fukurou_head, make_fukurou_wing_folded,
        lambda: make_bird_wing("Fukurou_Wing", 20.0, "owl"),
        make_ga_body, make_ga_wing, make_kanabun_wing,
        make_suzumushi_wing, make_korogi_wing,
        make_minomushi, make_minomushi_larva, make_minomushi_thread, make_kirigirisu_wing,
        lambda: make_bird_wing("Shimaenaga_Wing", 4.8, "enaga"),
        lambda: make_bird_wing_folded("Shimaenaga_WingFolded", 4.8, "enaga"),
    ]
    objs = {}
    for mk, name, variant in walkers:
        for ob in split_legs(mk, name, variant, share=name == "PillBug"):
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
                            "Asagimadara_Wing_", "Maruhanabachi_Wing_", "Oniyanma_Wing_", "Tsubame_Wing_", "Hae_Wing_", "Hae_Arm_",
                            "Hasamimushi_Tail_P", "Mukade_Portrait", "Kamome_Wing_", "Manta_Fin_", "Hitode_Arm_P", "Shiomaneki_Claw_P",
                            "Fukurou_Wing_", "Fukurou_Head_P", "Ga_Wing_P", "Ga_Wing_L", "Suzumushi_Wing_", "Korogi_Wing_",
                            "Kirigirisu_Wing_", "Shimaenaga_Wing_", "Minomushi_Larva_P", "Minomushi_Thread_P",
                            "Grasshopper_Hind_", "Crumb_Portrait")) or "_Leg" in name or "_Limb" in name:
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
