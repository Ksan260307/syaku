"""
しゃくとりの森 — きせかえの小物（ぼうし・めがね）と、きせかえの絵

しゃくとりむしの頭にのせる小物（ぼうし 10 こ・めがね 4 こ）の FBX と、
きせかえの画面に出す絵（いろは体を Ω の字に曲げた全身、小物は頭のアップ）を作ります。

座標は、しゃくとりむしのメッシュと同じ向き（x = 右、y = 頭の方、z = せなか）で、頭のまん中が原点。
ゲームでは、曲がった体の頭の位置と向きに合わせて、毎フレーム置きなおす（WormWear.cs）。

使い方:
  blender -b --factory-startup --python blender/scripts/build_wear_kit.py -- [fbx_out_dir] [icon_out_dir]
"""
import colorsys
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import HEAD_C, MB, TAU, build, hexc, lathe, mixc, scalec, sstep, tube, uv_sphere  # noqa: E402


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    icons = a[1] if len(a) > 1 else os.path.join(repo, "unity", "Assets", "UI", "Wear")
    return fbx, icons


HEAD = Vector((0.0, HEAD_C, 0.007))                 # 頭のまん中（しゃくとりむしのメッシュの座標）
EYE_DIR = Vector((0.62, 0.66, 0.42)).normalized()   # 頭のまん中から右の目へ（make_inchworm と同じ）
HAIR_ROOT = Vector((0.0, -0.004, 0.058))            # ぴょこんげの付け根（ゆれるときの支点）


def eye_dir(sx):
    return Vector((EYE_DIR.x * sx, EYE_DIR.y, EYE_DIR.z))


def place(mb, tmp, at, rx=0.0, ry=0.0, rz=0.0):
    """tmp（原点まわりに作った形）を、回して at へ置く（回す順は z → y → x）"""
    m = Matrix.Translation(at) @ Matrix.Rotation(math.radians(rx), 4, "X") @ Matrix.Rotation(math.radians(ry), 4, "Y") @ Matrix.Rotation(math.radians(rz), 4, "Z")
    mb.add(tmp, m)


def ring_tube(mb, center, r, z, tr, col, n=40):
    """水平な輪（ドーナツ）"""
    pts = [center + Vector((math.cos(TAU * i / n) * r, math.sin(TAU * i / n) * r, z)) for i in range(n + 1)]
    tube(mb, pts, [tr] * (n + 1), 8, col)


# ---------------------------------------------------------------------------
# ぼうし
# ---------------------------------------------------------------------------
def make_hair():
    """ぴょこんげ：頭のてっぺんから、ニュッと 1 本。先がくるんと前へまるまる"""
    mb = MB()
    r = HAIR_ROOT
    pts = [r + Vector((0, 0, -0.004)), r + Vector((0.001, 0.002, 0.022)), r + Vector((0.004, 0.012, 0.044)), r + Vector((0.006, 0.028, 0.056)),
           r + Vector((0.003, 0.041, 0.052)), r + Vector((-0.002, 0.043, 0.043)), r + Vector((-0.004, 0.036, 0.038))]
    tube(mb, pts, [0.0058, 0.0052, 0.0045, 0.0037, 0.0029, 0.0021, 0.0], 9,
         lambda t, a, p, d: mixc(hexc("#3a2a1e"), hexc("#6a4a32"), 0.35 * max(0.0, d.z) + 0.2 * t))
    return build(mb, "Wear_Hair", smooth=True)


def make_cap():
    """キャップ：赤い帽子に白いボタン、前へのびるつば（少し前を上げて、目が見えるように）"""
    mb = MB()
    red = hexc("#e0443a")
    tmp = MB()
    prof = [(0.0, -0.004), (0.067, -0.004), (0.068, 0.012), (0.061, 0.03), (0.047, 0.045), (0.025, 0.055), (0.0, 0.058)]

    def cap_col(t, a, p):
        c = red
        if t > 0.25 and abs(math.sin(a * 3.0)) < 0.06:
            c = scalec(red, 0.78)                     # ぬい目
        return scalec(c, 0.9 + 0.1 * sstep(0.0, 0.05, p.z))
    lathe(tmp, prof, 30, cap_col)
    uv_sphere(tmp, Vector((0, 0, 0.058)), 0.0075, lambda n: hexc("#f6f2ea"), seg=10, rings=6)
    # つば（前の、平たいだ円。うしろは帽子の中）
    uv_sphere(tmp, Vector((0, 0.05, 0.003)), 1.0, lambda n: scalec(red, 0.85) if n.z < 0 else red, seg=24, rings=8,
              scale=Vector((0.052, 0.05, 0.0055)))
    # 前の白いワッペン（葉っぱのマーク）
    uv_sphere(tmp, Vector((0, 0.06, 0.03)), 1.0, lambda n: hexc("#f6f2ea"), seg=12, rings=6, scale=Vector((0.013, 0.004, 0.011)),
              rot=Matrix.Rotation(math.radians(-55), 3, "X").to_quaternion())
    uv_sphere(tmp, Vector((0, 0.0635, 0.031)), 1.0, lambda n: hexc("#6cc04a"), seg=10, rings=5, scale=Vector((0.0065, 0.002, 0.0045)),
              rot=Matrix.Rotation(math.radians(-55), 3, "X").to_quaternion())
    place(mb, tmp, Vector((0, -0.007, 0.031)), rx=14)   # 前を少し上げて、目にかからないように
    return build(mb, "Wear_Cap", smooth=True, sharp_deg=60)


def make_straw():
    """むぎわらぼうし：大きなつばと、赤いリボン。むぎわらの編み目"""
    mb = MB()
    straw = hexc("#ecc96a")
    dark = hexc("#c49a46")

    def weave(t, a, p):
        rr = math.hypot(p.x, p.y)
        k = 0.45 if abs(math.sin(a * 26.0)) > 0.9 else 0.0
        k += 0.3 if abs(math.sin(rr * 300.0 + p.z * 300.0)) > 0.85 else 0.0
        return mixc(straw, dark, k)
    tmp = MB()
    # つば（ふちが少し下がる、厚みのある輪）
    lathe(tmp, [(0.046, 0.003), (0.075, 0.0), (0.104, -0.008), (0.118, -0.014), (0.119, -0.009), (0.105, -0.003), (0.075, 0.006), (0.046, 0.009)],
          40, weave, closed=True)
    # 頭の部分（ドーム）
    lathe(tmp, [(0.0, 0.004), (0.05, 0.004), (0.052, 0.02), (0.05, 0.036), (0.04, 0.045), (0.0, 0.048)], 32, weave)
    # 赤いリボン
    ring_tube(tmp, Vector((0, 0, 0)), 0.0525, 0.012, 0.0062, lambda t, a, p, d: hexc("#d8443a"))
    uv_sphere(tmp, Vector((0.0, -0.054, 0.012)), 1.0, lambda n: hexc("#c03a32"), seg=12, rings=6, scale=Vector((0.012, 0.006, 0.008)))
    place(mb, tmp, Vector((0, -0.01, 0.05)), rx=12)   # 前を上げて、目が見えるように
    return build(mb, "Wear_Straw", smooth=True)


def make_acorn():
    """どんぐりぼうし：うろこもようのお皿のようなぼうしと、ちょこんとしたへた"""
    mb = MB()
    brown = hexc("#8a6a3e")
    tmp = MB()

    def scales(t, a, p):
        u, v = a * 16.0 / TAU, p.z * 140.0
        k = abs(math.sin(math.pi * (u + v))) * abs(math.sin(math.pi * (u - v)))
        return scalec(mixc(brown, hexc("#b08a52"), 0.4 * sstep(0.2, 0.7, k)), 0.8 + 0.2 * sstep(0.0, 0.05, p.z))
    lathe(tmp, [(0.0, -0.004), (0.071, -0.004), (0.073, 0.012), (0.066, 0.03), (0.048, 0.044), (0.024, 0.051), (0.0, 0.053)], 30, scales)
    tube(tmp, [Vector((0, 0, 0.048)), Vector((0.002, 0.002, 0.062)), Vector((0.007, 0.004, 0.072))], [0.0075, 0.006, 0.005], 8,
         lambda t, a, p, d: hexc("#5a4428"))
    place(mb, tmp, Vector((0, -0.006, 0.024)), rx=4)
    return build(mb, "Wear_Acorn", smooth=True)


def make_ribbon():
    """リボン：頭の上に、大きな赤いリボン（水玉もよう）"""
    mb = MB()
    red = hexc("#ec4f6a")

    def dots(n):
        return hexc("#fff0f2") if noise.noise(n * 6.0) > 0.42 else red
    tmp = MB()
    for sx in (-1, 1):
        uv_sphere(tmp, Vector((0.022 * sx, 0, 0.004)), 1.0, dots, seg=18, rings=10, scale=Vector((0.022, 0.009, 0.016)),
                  rot=Matrix.Rotation(math.radians(-14 * sx), 3, "Y").to_quaternion())
        tube(tmp, [Vector((0.004 * sx, 0, -0.004)), Vector((0.012 * sx, -0.004, -0.018)), Vector((0.018 * sx, -0.006, -0.028))],
             [0.006, 0.005, 0.0], 8, lambda t, a, p, d: scalec(red, 0.9))
    uv_sphere(tmp, Vector((0, 0, 0.002)), 1.0, lambda n: scalec(red, 0.85), seg=12, rings=8, scale=Vector((0.009, 0.009, 0.01)))
    tmp.xform(Matrix.Scale(1.45, 4))
    place(mb, tmp, Vector((0.022, -0.008, 0.066)), rx=-10, ry=18, rz=-12)
    return build(mb, "Wear_Ribbon", smooth=True)


def make_knit():
    """ニットぼう：青と白のしまの、あみもようのぼうしに、ふわふわのぼんぼり"""
    mb = MB()
    blue = hexc("#3a6ad8")
    white = hexc("#f2f0ea")
    tmp = MB()

    def knit(t, a, p):
        c = blue if int(max(0.0, p.z + 0.004) * 62.0) % 2 == 0 else white
        return scalec(c, 0.82 + 0.18 * abs(math.sin(a * 28.0)))
    lathe(tmp, [(0.0, -0.004), (0.069, -0.004), (0.069, 0.012), (0.063, 0.03), (0.049, 0.045), (0.027, 0.055), (0.0, 0.058)], 32, knit)
    # 折り返し（白いゴムあみ）
    lathe(tmp, [(0.068, -0.006), (0.074, -0.006), (0.075, 0.006), (0.074, 0.016), (0.068, 0.016)], 36,
          lambda t, a, p: scalec(white, 0.85 + 0.15 * abs(math.sin(a * 36.0))), closed=True)
    # ぼんぼり（ふわふわ）
    lathe(tmp, [(0.0, -0.02), (0.012, -0.016), (0.019, -0.006), (0.02, 0.004), (0.016, 0.014), (0.0, 0.02)], 18,
          lambda t, a, p: mixc(white, hexc("#d8e4f6"), 0.5 + 0.5 * noise.noise(p * 300.0)),
          center=Vector((0, 0, 0.074)), rfn=lambda t, a: 1.0 + 0.12 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, t * 6))))
    place(mb, tmp, Vector((0, -0.006, 0.022)), rx=-5)
    return build(mb, "Wear_Knit", smooth=True)


def make_flower_crown():
    """花かんむり：つるの輪に、ピンク・白・むらさきの小さな花と葉っぱ"""
    mb = MB()
    tmp = MB()
    R = 0.05
    ring_tube(tmp, Vector((0, 0, 0)), R, 0.0, 0.0042, lambda t, a, p, d: mixc(hexc("#4a8a3a"), hexc("#6aaa4a"), 0.5 + 0.5 * math.sin(a * 3)))
    cols = [hexc("#f6a0c0"), hexc("#fbfbf2"), hexc("#c6a0f0"), hexc("#f6a0c0"), hexc("#fbfbf2"), hexc("#ffd070"), hexc("#c6a0f0")]
    n = len(cols)
    for i, c in enumerate(cols):
        a = TAU * i / n + 0.2
        cp = Vector((math.cos(a) * R, math.sin(a) * R, 0.004))
        out = Vector((math.cos(a), math.sin(a), 0.0))
        nrm = (out * 0.5 + Vector((0, 0, 1))).normalized()
        u = out.cross(Vector((0, 0, 1))).normalized()
        v = nrm.cross(u).normalized()
        for k in range(5):
            b = TAU * k / 5
            pp = cp + (u * math.cos(b) + v * math.sin(b)) * 0.0085 + nrm * 0.002
            uv_sphere(tmp, pp, 1.0, lambda nn, c=c: c, seg=8, rings=5, scale=Vector((0.0062, 0.0062, 0.0062)))
        uv_sphere(tmp, cp + nrm * 0.004, 0.004, lambda nn: hexc("#f2c030"), seg=8, rings=5)
        # 葉っぱ
        la = a + math.pi / n
        lp = Vector((math.cos(la) * (R + 0.001), math.sin(la) * (R + 0.001), -0.001))
        uv_sphere(tmp, lp, 1.0, lambda nn: hexc("#5aa04a"), seg=8, rings=5, scale=Vector((0.009, 0.004, 0.0025)),
                  rot=Matrix.Rotation(la, 3, "Z").to_quaternion())
    place(mb, tmp, Vector((0, -0.008, 0.048)), rx=8)   # 頭の上にのせて、前を少し上げる（目にかからない）
    return build(mb, "Wear_Flower", smooth=True)


def make_beret():
    """ベレーぼう：横にかたむけた、えんじ色の平たいぼうしと、てっぺんのちょこん"""
    mb = MB()
    c = hexc("#a83a4a")
    tmp = MB()
    uv_sphere(tmp, Vector((0, 0, 0)), 1.0, lambda n: scalec(c, 0.82 + 0.18 * max(0.0, n.z)), seg=28, rings=12, scale=Vector((0.076, 0.074, 0.019)))
    tube(tmp, [Vector((0, 0, 0.016)), Vector((0.002, 0.001, 0.025)), Vector((0.004, 0.002, 0.029))], [0.003, 0.0026, 0.0], 6,
         lambda t, a, p, d: scalec(c, 0.8))
    place(mb, tmp, Vector((0.012, -0.008, 0.06)), rx=-6, ry=16)
    return build(mb, "Wear_Beret", smooth=True)


def make_silk():
    """シルクハット：黒くて背の高いぼうしに、えんじ色の帯"""
    mb = MB()
    black = hexc("#26222a")
    tmp = MB()
    lathe(tmp, [(0.04, 0.0), (0.072, -0.003), (0.075, 0.003), (0.042, 0.006)], 36,
          lambda t, a, p: scalec(black, 0.9 + 0.3 * max(0.0, math.cos(a - 0.8)) * 0.4), closed=True)
    lathe(tmp, [(0.0, 0.002), (0.044, 0.002), (0.042, 0.03), (0.045, 0.086), (0.0, 0.088)], 32,
          lambda t, a, p: hexc("#a8324a") if 0.006 < p.z < 0.02 else scalec(black, 0.9 + 0.25 * max(0.0, math.cos(a - 0.8))))
    place(mb, tmp, Vector((0.004, -0.008, 0.05)), rx=-6, ry=8)
    return build(mb, "Wear_Silk", smooth=True, sharp_deg=50)


def make_crown():
    """おうかん：金色の輪に、まるい玉のついたとがり 6 つと、赤と青の宝石"""
    mb = MB()
    gold = hexc("#f2c040")
    tmp = MB()
    lathe(tmp, [(0.045, 0.0), (0.05, 0.0), (0.05, 0.022), (0.045, 0.022)], 36,
          lambda t, a, p: scalec(gold, 0.85 + 0.25 * max(0.0, math.cos(a * 2.0))), closed=True)
    for i in range(6):
        a = TAU * i / 6 + math.pi / 2
        base = Vector((math.cos(a) * 0.0475, math.sin(a) * 0.0475, 0.018))
        tip = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.05))
        tube(tmp, [base, base.lerp(tip, 0.5), tip], [0.009, 0.006, 0.0], 8, lambda t, aa, p, d: gold)
        uv_sphere(tmp, tip + Vector((0, 0, 0.003)), 0.0045, lambda n: hexc("#fff0b0"), seg=8, rings=5)
        gem = hexc("#e03a4a") if i % 2 == 0 else hexc("#3a7ae0")
        uv_sphere(tmp, Vector((math.cos(a + math.pi / 6) * 0.0505, math.sin(a + math.pi / 6) * 0.0505, 0.011)), 0.0048,
                  lambda n, gem=gem: mixc(gem, hexc("#ffffff"), 0.4 * max(0.0, n.z)), seg=10, rings=6)
    place(mb, tmp, Vector((0, -0.004, 0.048)), rx=-4)
    return build(mb, "Wear_Crown", smooth=True, sharp_deg=50)


# ---------------------------------------------------------------------------
# めがね：左右の目の前に、目の向き（横と前のあいだ）を向いたレンズ。前で橋がつながり、つるは頭の横をうしろへ
# ---------------------------------------------------------------------------
def lens_frame(mb, sx, outline, frame_col, lens_col=None, frame_w=0.0028, dist=0.073, temple=True):
    d = eye_dir(sx)
    c = d * dist
    u = (Vector((0, 0, 1)) - d * d.z).normalized()          # レンズの上
    v = d.cross(u).normalized() * sx                          # レンズの横（顔の外がわが +）
    pts = [c + v * x + u * y for x, y in outline]
    pts.append(pts[0])
    tube(mb, pts, [frame_w] * len(pts), 7, lambda t, a, p, dd: frame_col)
    if lens_col is not None:
        # レンズ（両面）。ハイライトのななめの帯
        ctr = mb.v(c + d * 0.0005, lens_col)
        ring = []
        for x, y in outline:
            k = 0.82
            hl = abs((x - y) * 70.0) < 0.35 and y > 0.0
            ring.append(mb.v(c + v * x * k + u * y * k + d * 0.0005, mixc(lens_col, hexc("#ffffff"), 0.55) if hl else lens_col))
        n = len(ring)
        for i in range(n):
            mb.f(ctr, ring[i], ring[(i + 1) % n])
            mb.f(ctr, ring[(i + 1) % n], ring[i])
    # 内がわ（鼻の方）と外がわのふち
    inner = min(pts[:-1], key=lambda p: abs(p.x))
    outer = max(pts[:-1], key=lambda p: abs(p.x))
    if temple:
        end = Vector((0.058 * sx, -0.03, 0.012))
        tube(mb, [outer, outer.lerp(end, 0.5) + Vector((0.004 * sx, 0, 0.002)), end], [frame_w * 0.8] * 3, 6, lambda t, a, p, dd: frame_col)
    return inner


def bridge(mb, inners, frame_col, frame_w=0.0028):
    a, b = inners
    mid = Vector((0.0, max(a.y, b.y) + 0.006, max(a.z, b.z) + 0.004))
    tube(mb, [a, a.lerp(mid, 0.5) + Vector((0, 0.002, 0.002)), mid, b.lerp(mid, 0.5) + Vector((0, 0.002, 0.002)), b], [frame_w] * 5, 6,
         lambda t, aa, p, d: frame_col)


def outline_round(r, n=32):
    return [(math.cos(TAU * i / n) * r, math.sin(TAU * i / n) * r) for i in range(n)]


def outline_squircle(w, h, n=40):
    out = []
    for i in range(n):
        a = TAU * i / n
        ca, sa = math.cos(a), math.sin(a)
        r = 1.0 / (abs(ca) ** 4 + abs(sa) ** 4) ** 0.25
        out.append((ca * r * w, sa * r * h - 0.002 * max(0.0, -sa)))
    return out


def outline_heart(s, n=48):
    out = []
    for i in range(n):
        t = TAU * i / n
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        out.append((x * s, y * s + 0.002))
    return out


def outline_star(r0, r1, n=5):
    out = []
    for i in range(n * 2 * 3):
        a = math.pi / 2 + TAU * i / (n * 2 * 3)
        k = (i % 6) / 6.0
        # とがりと、くぼみをなめらかにつなぐ
        rr = r1 + (r0 - r1) * (0.5 + 0.5 * math.cos(k * TAU))
        out.append((math.cos(a) * rr, math.sin(a) * rr))
    return out


def make_glasses(name, outline, frame_col, lens_col, frame_w=0.0028):
    mb = MB()
    inners = [lens_frame(mb, sx, outline, frame_col, lens_col, frame_w) for sx in (-1, 1)]
    bridge(mb, inners, frame_col, frame_w)
    return build(mb, name, smooth=True)


def make_round_glasses():
    """まるめがね：細い金色のまるいわく"""
    return make_glasses("Wear_RoundGlasses", outline_round(0.021), hexc("#9a7a3a"), None, 0.0024)


def make_sunglasses():
    """サングラス：黒いわくと、黒いレンズ（ななめの光）"""
    return make_glasses("Wear_Sunglasses", outline_squircle(0.024, 0.019), hexc("#1e1e22"), hexc("#23262e"), 0.003)


def make_heart_glasses():
    """ハートのサングラス：ピンクのハートのわくと、こいピンクのレンズ"""
    return make_glasses("Wear_HeartGlasses", outline_heart(0.0014), hexc("#f04a8a"), hexc("#a02a5a"), 0.003)


def make_star_glasses():
    """ほしのめがね：黄色い星のわくと、青いレンズ"""
    return make_glasses("Wear_StarGlasses", outline_star(0.026, 0.012), hexc("#f2c030"), hexc("#3a5ac8"), 0.0028)


HATS = [make_hair, make_cap, make_straw, make_acorn, make_ribbon, make_knit, make_flower_crown, make_beret, make_silk, make_crown]
GLASSES = [make_round_glasses, make_sunglasses, make_heart_glasses, make_star_glasses]
MAKERS = HATS + GLASSES

# きせかえの絵：id → いろ（色相・彩度・明度・体にそって色相・しまの数と濃さ・水玉）、または小物のメッシュ
# （Skins.cs と同じ数。いろの絵は、ゲームのシェーダーと同じ色の変え方でぬる）
COLORS = {
    "wakaba": dict(hue=0.0), "kimidori": dict(hue=-22.0, sat=1.1, val=1.12), "sakura": dict(hue=-110.0, sat=0.55, val=1.18),
    "sorairo": dict(hue=115.0, sat=0.75, val=1.12), "momiji": dict(hue=-75.0, sat=1.2), "yozora": dict(hue=175.0, sat=0.85, val=0.8),
    "kogane": dict(hue=-45.0, sat=1.35, val=1.25), "ruri": dict(hue=130.0, sat=1.05, val=0.85), "fuji": dict(hue=-170.0, sat=0.5, val=1.15),
    "niji": dict(hue=0.0, sat=1.1, val=1.1, along=360.0),
    "mint": dict(hue=62.0, sat=0.62, val=1.18), "yuki": dict(hue=0.0, sat=0.12, val=1.38), "sumi": dict(hue=0.0, sat=0.12, val=0.3),
    "ichigo": dict(hue=-118.0, sat=0.78, val=1.2, dots=0.85), "shima": dict(hue=-48.0, sat=1.35, val=1.25, stripes=13.0, dark=0.72),
    "rainbow": dict(hue=-90.0, sat=1.25, val=1.15, along=360.0),
}
WEAR_ICONS = {
    "hair": "Wear_Hair", "cap": "Wear_Cap", "straw": "Wear_Straw", "acorn": "Wear_Acorn", "ribbon": "Wear_Ribbon", "knit": "Wear_Knit",
    "flower": "Wear_Flower", "beret": "Wear_Beret", "silk": "Wear_Silk", "crown": "Wear_Crown",
    "round": "Wear_RoundGlasses", "sun": "Wear_Sunglasses", "heart": "Wear_HeartGlasses", "star": "Wear_StarGlasses",
}


def s2l(x):
    return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4


def l2s(x):
    x = max(0.0, x)
    return x * 12.92 if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055


def skin_color(c_srgb, s, ang01, hue=0.0, sat=1.0, val=1.0, along=0.0, stripes=0.0, dark=0.0, dots=0.0):
    """ゲームのシェーダー（ShakuAlbedo の _HUE_SHIFT）と同じ色の変え方。s は体のどこか、ang01 は体のまわりの角度"""
    lin = [s2l(c_srgb[i]) for i in range(3)]
    h, sa, v = colorsys.rgb_to_hsv(*lin)
    h = (h + (hue + along * s) / 360.0) % 1.0
    r, g, b = colorsys.hsv_to_rgb(h, min(1.0, sa * sat), v * val)
    if stripes > 0.0 and (s * stripes) % 1.0 >= 0.5:
        r, g, b = r * (1 - dark), g * (1 - dark), b * (1 - dark)
    if dots > 0.0:
        fx = (s * 26.0) % 1.0 - 0.5
        fy = (ang01 * 8.0) % 1.0 - 0.5
        k = sstep(0.24, 0.18, math.hypot(fx, fy)) * dots
        r, g, b = r + (0.95 - r) * k, g + (0.92 - g) * k, b + (0.93 - b) * k
    return (l2s(r), l2s(g), l2s(b), 1.0)


def recolor_worm(ob, params):
    me = ob.data
    col = me.color_attributes["Col"]
    vidx = [0] * len(me.loops)
    me.loops.foreach_get("vertex_index", vidx)
    for li, vi in enumerate(vidx):
        co = me.vertices[vi].co
        c = col.data[li].color_srgb
        ang = math.atan2(co.z, co.x) / TAU + 0.5
        col.data[li].color_srgb = skin_color(c, co.y, ang, **params)


def bend_omega(ob):
    """まっすぐな体を、Ω の字に曲げる（絵のため）"""
    me = ob.data
    for v in me.vertices:
        x, s, z = v.co.x, v.co.y, v.co.z
        cy = 0.62 * s
        cz = 0.3 * math.sin(math.pi * min(1.0, max(0.0, s))) ** 1.3
        dy = 0.62
        dz = 0.3 * 1.3 * math.pi * math.cos(math.pi * s) * max(1e-4, math.sin(math.pi * s)) ** 0.3
        t = Vector((0, dy, dz)).normalized()
        up = Vector((0, -t.z, t.y))
        p = Vector((x, cy, cz)) + up * z
        v.co = p
    me.update()


def setup_render(scene):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_object_outline = True
    scene.display.shading.object_outline_color = (0.18, 0.12, 0.1)
    scene.display.shading.show_specular_highlight = True
    scene.render.film_transparent = True
    scene.render.resolution_x = 192
    scene.render.resolution_y = 192
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"


def render_icons(icon_dir):
    """きせかえの絵：いろは Ω の字の全身を横から、小物は頭のアップ（ななめ前から）"""
    os.makedirs(icon_dir, exist_ok=True)
    scene = bpy.context.scene
    setup_render(scene)
    cam = bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 60
    for cid, params in COLORS.items():
        for o in list(scene.objects):
            if o.type == "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
        worm = kit.make_inchworm()
        recolor_worm(worm, params)
        bend_omega(worm)
        c = Vector((0.0, 0.31, 0.13))
        cam.location = c + Vector((1.25, 0.25, 0.32))
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.clip_start = 0.01
        scene.render.filepath = os.path.join(icon_dir, f"skin_{cid}.png")
        bpy.ops.render.render(write_still=True)
        print("icon", cid)
    for cid, mesh_name in WEAR_ICONS.items():
        for o in list(scene.objects):
            if o.type == "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
        worm = kit.make_inchworm()
        maker = next(f for f in MAKERS if f.__name__ == MAKER_OF[mesh_name])
        acc = maker()
        acc.location = HEAD
        c = HEAD + Vector((0.0, 0.0, 0.03))
        cam.location = c + Vector((0.22, 0.27, 0.12))
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(icon_dir, f"skin_{cid}.png")
        bpy.ops.render.render(write_still=True)
        print("icon", cid)


MAKER_OF = {
    "Wear_Hair": "make_hair", "Wear_Cap": "make_cap", "Wear_Straw": "make_straw", "Wear_Acorn": "make_acorn", "Wear_Ribbon": "make_ribbon",
    "Wear_Knit": "make_knit", "Wear_Flower": "make_flower_crown", "Wear_Beret": "make_beret", "Wear_Silk": "make_silk",
    "Wear_Crown": "make_crown", "Wear_RoundGlasses": "make_round_glasses", "Wear_Sunglasses": "make_sunglasses",
    "Wear_HeartGlasses": "make_heart_glasses", "Wear_StarGlasses": "make_star_glasses",
}


def main():
    fbx_dir, icon_dir = parse_args()
    os.makedirs(fbx_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.VC_MATERIAL = None
    for mk in MAKERS:
        ob = mk()
        p = kit.export_fbx(ob, fbx_dir)
        print("exported", ob.name, len(ob.data.vertices), "verts ->", os.path.basename(p))
    render_icons(icon_dir)


if __name__ == "__main__":
    main()
