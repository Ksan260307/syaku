"""
しゃくとりの森 — 神社のアセット生成スクリプト

鎮守の森の、小さなお稲荷さんの神社（7 つ目のエリア）を、しゃくとりむしから見た大きさで作る。
大鳥居・千本鳥居の小さな鳥居・お社（流造の屋根・格子戸・鈴と鈴緒・しめ縄）・さい銭箱・石灯籠・キツネの石像（玉と鍵）・
手水舎（水盤・竹の筧・ひしゃく）・ご神木（しめ縄と紙垂・うろ・太い枝）・奥の宮の祠・太鼓橋・玉垣・絵馬かけ・おみくじかけ・
参道の石畳と石段と、草花（ヒガンバナ・ササ）・落ちたイチョウの葉。FBX を書き出します。
1 単位 = しゃくとりむしの体長（2.5cm）。大鳥居は 1m ほどの、村の小さなお稲荷さんの大きさ。

座標は Unity の向きで書く（U() で Blender にかえる）：x = 東、y = 上、z = 北。物の床のまん中が原点で、前は +z。

使い方:
  blender -b --factory-startup --python blender/scripts/build_shrine_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import (MB, TAU, bark_color, build, hexc, lathe, lerp, mixc, polar_sheet, scalec, sstep,  # noqa: E402
                              trunk_mesh, tube, uv_sphere)


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "shrine_kit.blend")
    return fbx, blend


SHU = hexc("#d9452b")            # 朱色（鳥居・欄干）
SHU_DARK = hexc("#a9321f")
LACQ = hexc("#2b2522")           # 黒うるし（笠木・根巻き）
PLASTER = hexc("#eee8da")        # しっくい（亀腹・かべ）
STONE = hexc("#a7a398")
STONE_LIGHT = hexc("#c4c0b4")
STONE_DARK = hexc("#7c786e")
MOSS = hexc("#6f8a3a")
HINOKI = hexc("#c9a27a")         # ひのきの白木
HINOKI_DARK = hexc("#8e6a48")
COPPER = hexc("#5f9c88")         # 緑青の銅板屋根
COPPER_DARK = hexc("#3f7363")
BARK_ROOF = hexc("#6b4a35")      # 檜皮ぶき
GOLD = hexc("#d8b04c")
PAPER = hexc("#f7f4ec")
STRAW = hexc("#d6bf7a")
STRAW_DARK = hexc("#a88f4e")
BAMBOO = hexc("#86a64e")
BAMBOO_DARK = hexc("#5f7d34")
FOX_STONE = hexc("#e3ded2")
BIB = hexc("#d3332a")


def U(x, y, z):
    """Unity の座標（x 東・y 上・z 北）を Blender の座標にする（FBX で Blender (x, y, z) は Unity (-x, z, -y) になる）"""
    return Vector((-x, -z, y))


def UV(v):
    return U(v[0], v[1], v[2])


def to_unity_n(nb):
    return Vector((-nb.x, nb.z, -nb.y))


def _newell(ps):
    n = Vector((0, 0, 0))
    for i in range(len(ps)):
        a, b = ps[i], ps[(i + 1) % len(ps)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def _face(mb, ps_b, center_b, col):
    """面を 1 枚（外向きになるよう、まん中から見て向きをそろえる）。col は色、または col(Unity の法線) -> 色"""
    n = _newell(ps_b)
    fc = sum(ps_b, Vector((0, 0, 0))) / len(ps_b)
    if n.dot(fc - center_b) < 0:
        ps_b = list(reversed(ps_b))
        n = -n
    nu = to_unity_n(n.normalized() if n.length > 1e-9 else Vector((0, 0, 1)))
    c = col(nu) if callable(col) else col
    mb.f(*[mb.v(p, c) for p in ps_b])


def hexa(mb, P, col):
    """8 つの角（Unity の座標。下の 4 つ、上の 4 つの順で、同じ回り方）の六面体"""
    B = [UV(p) for p in P]
    center = sum(B, Vector((0, 0, 0))) / 8
    b, t = B[:4], B[4:]
    _face(mb, b, center, col)
    _face(mb, t, center, col)
    for i in range(4):
        j = (i + 1) % 4
        _face(mb, [b[i], b[j], t[j], t[i]], center, col)


def prism(mb, poly, ext, col):
    """平らな凸の多角形 poly（Unity の座標）を、ext（Unity のベクトル）の向きへのばした柱"""
    ext = Vector(ext)
    bot = [UV(p) for p in poly]
    top = [UV(Vector(p) + ext) for p in poly]
    center = (sum(bot, Vector((0, 0, 0))) + sum(top, Vector((0, 0, 0)))) / (2 * len(bot))
    _face(mb, bot, center, col)
    _face(mb, top, center, col)
    n = len(bot)
    for i in range(n):
        j = (i + 1) % n
        _face(mb, [bot[i], bot[j], top[j], top[i]], center, col)


def ubox(mb, c, s, col):
    """Unity の座標で、まん中 c・大きさ s（x, y, z）の直方体"""
    cx, cy, cz = c
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    P = [Vector((cx - hx, cy - hy, cz - hz)), Vector((cx + hx, cy - hy, cz - hz)), Vector((cx + hx, cy - hy, cz + hz)), Vector((cx - hx, cy - hy, cz + hz))]
    P += [p + Vector((0, 2 * hy, 0)) for p in P]
    hexa(mb, P, col)


def beam(mb, a, b, w, h, col, up=(0.0, 1.0, 0.0)):
    """a から b へわたす角材（はば w は横、高さ h は up の向き）"""
    a, b, up = Vector(a), Vector(b), Vector(up)
    d = (b - a).normalized()
    side = up.cross(d)
    if side.length < 1e-6:
        side = Vector((1, 0, 0)).cross(d)
    side.normalize()
    upv = d.cross(side).normalized()
    sw, sh = side * (w / 2), upv * (h / 2)
    P = [a - sw - sh, a + sw - sh, a + sw + sh, a - sw + sh]
    Q = [p + (b - a) for p in P]
    hexa(mb, [P[0], P[1], Q[1], Q[0], P[3], P[2], Q[2], Q[3]], col)


def ucyl(mb, c, r, h, col, seg=16, top=True, r_top=None):
    """Unity の座標で、床の点 c から上へ高さ h の円柱（上をふさぐ）"""
    base = UV(c)
    rt = r if r_top is None else r_top
    prof = [(0.0, 0.0), (r, 0.0), (rt, h)] + ([(0.0, h)] if top else [])
    lathe(mb, prof, seg, lambda t, a, p: col(t, a, p) if callable(col) else col, center=base)


def ulathe(mb, c, prof, seg, col, phase=0.0):
    """Unity の座標の点 c を中心に、たての軸のまわりの回転体（prof = [(半径, 高さ), ...] を下から上へ）"""
    lathe(mb, prof, seg, lambda t, a, p: col(t, a, p) if callable(col) else col, center=UV(c), phase=phase)


def utube(mb, pts, radii, seg, col, oval=(1.0, 1.0), rfn=None):
    """Unity の座標の点をつなぐチューブ。col(t, a, p, d) または色"""
    tube(mb, [UV(p) for p in pts], radii, seg, col if callable(col) else (lambda t, a, p, d: col), oval=oval, rfn=rfn)


def usphere(mb, c, r, col, scale=(1.0, 1.0, 1.0), seg=16, rings=10):
    """Unity の座標の楕円体（scale は Unity の x, y, z）"""
    uv_sphere(mb, UV(c), r, col if callable(col) else (lambda n: col), seg=seg, rings=rings,
              scale=Vector((scale[0], scale[2], scale[1])))


def stone_col(seed=0.0, moss=0.25, base=STONE):
    def f(n):
        k = 0.5 + 0.5 * math.sin(seed * 12.9 + n.x * 3.1 + n.z * 2.3)
        c = mixc(base, STONE_LIGHT, 0.25 * k)
        if n.y > 0.6:
            c = mixc(c, MOSS, moss * (0.4 + 0.6 * k))
        elif n.y < -0.5:
            c = scalec(c, 0.8)
        return c
    return f


def by_normal(top, side, bottom=None):
    bottom = bottom if bottom is not None else scalec(side, 0.8)
    return lambda n: top if n.y > 0.5 else bottom if n.y < -0.5 else side


# ---------------------------------------------------------------------------
# 鳥居
# ---------------------------------------------------------------------------
def torii(mb, half, pr, ph, nuki_y, nuki_ext, shimaki_y, kasagi_half, kasagi_y, rise, kw, kh, base_r, plaque=True):
    """明神鳥居：2 本の柱（根もとは黒）・貫・島木・反りのある黒い笠木・額束と額・柱の下の亀腹"""
    for sx in (-1, 1):
        x = half * sx
        ucyl(mb, (x, 0.0, 0.0), pr, ph, SHU, seg=18)
        ucyl(mb, (x, -0.2, 0.0), pr * 1.1, ph * 0.08 + 0.2, LACQ, seg=18)                    # 根巻き
        ulathe(mb, (x, -0.3, 0.0), [(0.0, 0.0), (base_r, 0.0), (base_r * 0.92, base_r * 0.25), (base_r * 0.7, base_r * 0.45),
                                    (pr * 1.15, base_r * 0.52), (0.0, base_r * 0.52)], 20, PLASTER)   # 亀腹
    sh = by_normal(SHU, SHU, SHU_DARK)
    beam(mb, (-half - nuki_ext, nuki_y, 0.0), (half + nuki_ext, nuki_y, 0.0), pr * 0.95, pr * 1.4, sh)       # 貫
    beam(mb, (-kasagi_half + 1.0, shimaki_y, 0.0), (kasagi_half - 1.0, shimaki_y, 0.0), pr * 1.5, pr * 1.15, sh)  # 島木
    # 笠木：はしへ行くほど、上へ反る
    n = 14
    for i in range(n):
        x0 = -kasagi_half + 2 * kasagi_half * i / n
        x1 = -kasagi_half + 2 * kasagi_half * (i + 1) / n
        y0 = kasagi_y + rise * (abs(x0) / kasagi_half) ** 2.6
        y1 = kasagi_y + rise * (abs(x1) / kasagi_half) ** 2.6
        beam(mb, (x0, y0, 0.0), (x1, y1, 0.0), kw, kh, by_normal(LACQ, LACQ, scalec(LACQ, 1.3)))
    if plaque:
        mid = (nuki_y + shimaki_y) / 2
        beam(mb, (0.0, nuki_y + pr * 0.6, 0.0), (0.0, shimaki_y - pr * 0.5, 0.0), pr * 0.9, pr * 0.9, SHU, up=(0, 0, 1))   # 額束
        ph2 = (shimaki_y - nuki_y) * 0.78
        ubox(mb, (0.0, mid, pr * 0.55), (ph2 * 0.72, ph2, pr * 0.5), GOLD)              # 額のふち
        ubox(mb, (0.0, mid, pr * 0.85), (ph2 * 0.58, ph2 * 0.84, 0.2), LACQ)             # 額


def make_torii():
    mb = MB()
    torii(mb, half=11.0, pr=1.7, ph=36.0, nuki_y=26.0, nuki_ext=4.2, shimaki_y=33.2, kasagi_half=21.0, kasagi_y=35.4,
          rise=1.6, kw=3.6, kh=2.3, base_r=3.0)
    return build(mb, "Shrine_Torii", smooth=False)


def make_torii_small():
    mb = MB()
    torii(mb, half=4.6, pr=0.85, ph=20.0, nuki_y=14.0, nuki_ext=1.6, shimaki_y=18.8, kasagi_half=7.8, kasagi_y=19.9,
          rise=0.7, kw=1.9, kh=1.2, base_r=1.4, plaque=False)
    return build(mb, "Shrine_ToriiSmall", smooth=False)


# ---------------------------------------------------------------------------
# 石灯籠（春日灯籠）：火袋の窓に、あかりがともる
# ---------------------------------------------------------------------------
LANTERN_LIGHT = (0.0, 11.1, 0.0)   # 火袋のまん中（Unity でほんのり光らせる）


def make_lantern():
    mb = MB()
    st = stone_col(1.0, 0.3)
    ulathe(mb, (0, 0, 0), [(0.0, 0.0), (3.1, 0.0), (3.0, 0.9), (2.6, 1.3), (0.0, 1.3)], 6, lambda t, a, p: st(Vector((0, 1 if t > 0.6 else 0, 0))))   # 基礎
    ulathe(mb, (0, 1.3, 0), [(0.0, 0.0), (1.5, 0.0), (1.15, 0.5), (1.05, 3.2), (1.3, 3.4), (1.3, 3.8), (1.05, 4.0), (1.0, 6.9), (0.0, 6.9)], 18,
           lambda t, a, p: mixc(STONE, STONE_DARK, 0.2 + 0.2 * math.sin(a * 3 + t * 9)))                                        # 竿と節
    ulathe(mb, (0, 8.0, 0), [(0.0, 0.0), (1.2, 0.0), (2.3, 0.7), (2.7, 1.3), (2.6, 1.6), (0.0, 1.6)], 6, lambda t, a, p: STONE_LIGHT if t > 0.6 else STONE)   # 中台
    # 火袋：六角の柱。前と後ろに、あかりのついた窓
    ulathe(mb, (0, 9.6, 0), [(0.0, 0.0), (1.75, 0.0), (1.75, 3.0), (0.0, 3.0)], 6, STONE)
    for sz in (-1, 1):
        ubox(mb, (0.0, 11.1, sz * 1.52), (1.5, 2.1, 0.12), hexc("#ffd27a"))
        ubox(mb, (0.0, 11.1, sz * 1.56), (0.12, 2.1, 0.1), STONE_DARK)                  # 窓のさん
    # 笠：大きな六角の屋根（ふちは少し反る）と、上の宝珠
    ulathe(mb, (0, 12.6, 0), [(0.0, 0.0), (2.2, 0.0), (3.5, 0.35), (3.6, 0.75), (2.6, 1.3), (1.0, 2.2), (0.75, 2.5), (0.0, 2.5)], 6,
           lambda t, a, p: mixc(STONE, MOSS, 0.35 * sstep(0.45, 0.8, t)) if t > 0.4 else STONE_DARK)
    ulathe(mb, (0, 15.0, 0), [(0.0, 0.0), (0.75, 0.0), (0.95, 0.4), (0.9, 0.9), (0.55, 1.4), (0.2, 1.8), (0.0, 2.1)], 14, STONE_LIGHT)   # 宝珠
    return build(mb, "Shrine_Lantern", smooth=False)


# ---------------------------------------------------------------------------
# キツネの石像（お稲荷さんのお使い）：台座の上にすわり、赤い前かけ。A は玉、B は鍵をくわえる
# ---------------------------------------------------------------------------
FOX_TOP = 7.4   # 台座の上の面


def make_fox_statue(name, holds):
    mb = MB()
    base = stone_col(2.0, 0.35, STONE_DARK)
    ubox(mb, (0, 0.6, 0), (7.6, 1.6, 6.8), base)
    ubox(mb, (0, 3.9, 0), (6.2, 5.0, 5.4), stone_col(3.0, 0.15))
    ubox(mb, (0, FOX_TOP - 0.4, 0), (7.0, 0.8, 6.2), stone_col(4.0, 0.25, STONE_LIGHT))
    y0 = FOX_TOP
    fur = lambda t, a, p, d: mixc(FOX_STONE, STONE, 0.12 + 0.12 * noise.noise(p * 0.8))
    # すわった体：うしろのこし・むね・首
    usphere(mb, (0, y0 + 1.7, -0.9), 1.0, lambda n: mixc(FOX_STONE, STONE, 0.15), scale=(2.1, 1.8, 2.3), seg=18, rings=12)
    utube(mb, [(0, y0 + 1.6, -0.6), (0, y0 + 3.4, -0.1), (0, y0 + 5.2, 0.5), (0, y0 + 6.6, 0.9)], [1.75, 1.6, 1.25, 0.95], 18, fur)
    # 頭と、細長い鼻先・とがった耳
    hc = (0.0, y0 + 7.6, 1.15)
    usphere(mb, hc, 1.15, lambda n: mixc(FOX_STONE, STONE, 0.1), scale=(1.0, 0.95, 1.1), seg=18, rings=12)
    utube(mb, [(0, y0 + 7.45, 1.8), (0, y0 + 7.2, 2.9), (0, y0 + 7.0, 3.5)], [0.7, 0.42, 0.0], 14, fur)
    for sx in (-1, 1):
        utube(mb, [(0.55 * sx, y0 + 8.4, 0.95), (0.75 * sx, y0 + 9.4, 0.85), (0.85 * sx, y0 + 10.3, 0.75)], [0.48, 0.3, 0.0], 10,
              lambda t, a, p, d: FOX_STONE if t < 0.7 else STONE)
        # 朱の目のふち
        usphere(mb, (0.62 * sx, y0 + 7.85, 1.95), 0.16, BIB, scale=(1.0, 0.45, 1.6), seg=8, rings=5)
        # 前足（まっすぐ立てて、台座につく）
        utube(mb, [(0.7 * sx, y0 + 4.4, 0.9), (0.75 * sx, y0 + 2.2, 1.25), (0.75 * sx, y0 + 0.25, 1.45)], [0.5, 0.42, 0.38], 12, fur)
        usphere(mb, (0.75 * sx, y0 + 0.25, 1.75), 0.42, FOX_STONE, scale=(1.0, 0.6, 1.3), seg=10, rings=6)
        # うしろ足
        usphere(mb, (1.35 * sx, y0 + 0.5, -0.1), 0.6, FOX_STONE, scale=(0.9, 0.7, 1.6), seg=10, rings=6)
    # 赤い前かけ
    usphere(mb, (0.0, y0 + 5.1, 1.0), 1.0, BIB, scale=(1.55, 1.25, 0.45), seg=16, rings=10)
    # ふさふさのしっぽ（うしろで、上へはね上がる）
    utube(mb, [(0.0, y0 + 0.7, -2.6), (0.0, y0 + 2.2, -3.7), (0.0, y0 + 4.8, -3.6), (0.0, y0 + 7.2, -2.7), (0.0, y0 + 8.4, -1.9)],
          [0.7, 1.15, 1.2, 0.95, 0.0], 14, fur)
    # くわえている物
    mouth = (0.0, y0 + 7.0, 3.55)
    if holds == "jewel":
        usphere(mb, (mouth[0], mouth[1] - 0.1, mouth[2] + 0.2), 0.62, lambda n: mixc(GOLD, FOX_STONE, 0.4), seg=14, rings=9)
        utube(mb, [(0.0, mouth[1] + 0.5, mouth[2] + 0.2), (0.0, mouth[1] + 1.0, mouth[2] + 0.15)], [0.25, 0.0], 8, mixc(GOLD, FOX_STONE, 0.4))   # 宝珠のとがり
    else:
        utube(mb, [(-1.2, mouth[1], mouth[2]), (1.4, mouth[1], mouth[2])], [0.22, 0.22], 8, mixc(GOLD, STONE_DARK, 0.3))   # 鍵のじく
        utube(mb, [(-1.2, mouth[1] - 0.6, mouth[2]), (-1.9, mouth[1], mouth[2]), (-1.2, mouth[1] + 0.6, mouth[2]), (-0.5, mouth[1], mouth[2]),
                   (-1.2, mouth[1] - 0.6, mouth[2])], [0.2] * 5, 8, mixc(GOLD, STONE_DARK, 0.3))                                 # 鍵の輪
        for k in range(2):
            ubox(mb, (1.1 - 0.5 * k, mouth[1] - 0.4, mouth[2]), (0.22, 0.6, 0.22), mixc(GOLD, STONE_DARK, 0.3))              # 鍵の歯
    return build(mb, name, smooth=True, sharp_deg=60)


# ---------------------------------------------------------------------------
# さい銭箱
# ---------------------------------------------------------------------------
def make_saisen():
    mb = MB()
    wood = by_normal(HINOKI, HINOKI_DARK)
    ubox(mb, (0, 2.0, 0), (9.0, 4.0, 4.6), wood)
    for sx in (-1, 1):
        ubox(mb, (sx * 4.2, 0.3, 0), (0.9, 0.6, 4.9), HINOKI_DARK)                  # 足
        ubox(mb, (sx * 4.45, 2.2, 0), (0.2, 4.2, 4.8), GOLD)                         # 金具
    # 上の、ななめの板（お金が落ちる、すきま）
    for k in range(6):
        z = -1.9 + k * 0.76
        beam(mb, (-4.3, 4.35, z), (4.3, 4.35, z), 0.42, 0.55, HINOKI, up=(0, 1, 0.0))
    ubox(mb, (0, 4.0, 2.25), (9.0, 1.0, 0.25), HINOKI_DARK)
    ubox(mb, (0, 4.0, -2.25), (9.0, 1.0, 0.25), HINOKI_DARK)
    ubox(mb, (0, 2.0, 2.35), (2.6, 1.4, 0.12), GOLD)                                # 前の金の紋
    return build(mb, "Shrine_Saisen", smooth=False)


# ---------------------------------------------------------------------------
# お社（流造：前の屋根が長くのびる）：高床・縁・格子戸・しっくいのかべ・銅板の屋根・千木と鰹木・
# 正面の木の階段・鈴と鈴緒・しめ縄と紙垂
# ---------------------------------------------------------------------------
HALL_FLOOR = 6.5
HALL_BELL = (0.0, 20.4, 12.6)      # 鈴のまん中（Unity で、鈴緒を引くと鳴る）


def shide(mb, top, k=1.0, facing=(0.0, 0.0, 1.0)):
    """紙垂：白い紙を、いなずまの形に折って下げる"""
    f = Vector(facing)
    f.y = 0.0
    f.normalize()
    side = f.cross(Vector((0, 1, 0))).normalized()
    t = Vector(top)
    pts = [(0.0, 0.0), (0.45, -0.8), (-0.2, -1.5), (0.55, -2.4), (-0.1, -3.1)]
    for i in range(len(pts) - 1):
        a = t + side * (pts[i][0] * k) + Vector((0, pts[i][1] * k, 0))
        b = t + side * (pts[i + 1][0] * k) + Vector((0, pts[i + 1][1] * k, 0))
        beam(mb, tuple(a), tuple(b), 0.8 * k, 0.07, PAPER, up=tuple(f))


def shimenawa(mb, a, b, sag, r, n_shide=4, facing=(0.0, 0.0, 1.0)):
    """しめ縄：たるんだ太いわらの縄（よじれのもよう）と、紙垂"""
    a, b = Vector(a), Vector(b)
    pts = []
    for i in range(17):
        t = i / 16
        p = a.lerp(b, t)
        p.y -= sag * math.sin(math.pi * t)
        pts.append(p)
    utube(mb, pts, [r * (0.55 + 0.45 * math.sin(math.pi * (i / 16)) ** 0.5) for i in range(17)], 12,
          lambda t, ang, p, d: mixc(STRAW, STRAW_DARK, 0.5 + 0.5 * math.sin(ang * 2 + t * 70)))
    for k in range(n_shide):
        t = (k + 1) / (n_shide + 1)
        p = a.lerp(b, t)
        p.y -= sag * math.sin(math.pi * t) + r * 0.7
        shide(mb, (p.x, p.y, p.z), 0.9, facing)


def roof_slope(mb, x0, x1, ridge, eave, sag, thick, top_col, under_col, n=7):
    """屋根の片側：むね（y, z）から軒（y, z）へ、少したわんだ板（銅板）"""
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n

        def at(t):
            y = lerp(ridge[0], eave[0], t) - sag * math.sin(math.pi * t)
            z = lerp(ridge[1], eave[1], t)
            return y, z
        ya, za = at(t0)
        yb, zb = at(t1)
        # 板の厚みは、上へ
        P = [Vector((x0, ya, za)), Vector((x1, ya, za)), Vector((x1, yb, zb)), Vector((x0, yb, zb))]
        Q = [p + Vector((0, thick, 0)) for p in P]
        hexa(mb, P + Q, lambda nn: top_col if nn.y > 0.3 else under_col if nn.y < -0.3 else scalec(top_col, 0.75))


def make_hall():
    mb = MB()
    wood = by_normal(HINOKI, HINOKI, HINOKI_DARK)
    F = HALL_FLOOR
    # 柱（床下から、軒のけたまで）
    for x in (-9.0, 0.0, 9.0):
        for z in (-7.0, 7.0):
            ucyl(mb, (x, 0.0, z), 0.85, 23.0, SHU, seg=12)
            ubox(mb, (x, 0.35, z), (2.4, 0.7, 2.4), STONE_DARK)                         # 礎石
    # 床と縁（前と横に、ぐるりと）
    ubox(mb, (0.0, F - 0.5, 1.5), (22.0, 1.0, 19.0), wood)
    for x in (-10.5, 10.5):
        for z in (-7.5, 10.5):
            ucyl(mb, (x, 0.0, z), 0.5, F - 1.0, HINOKI_DARK, seg=10)                     # 縁の束
    # 縁の高欄（朱。前のまん中は、階段のためにあける）
    for sx in (-1, 1):
        for z in (-7.5, -2.0, 3.5, 10.5):
            beam(mb, (10.7 * sx, F, z), (10.7 * sx, F + 2.6, z), 0.45, 0.45, SHU, up=(0, 0, 1))
        beam(mb, (10.7 * sx, F + 2.4, -7.5), (10.7 * sx, F + 2.4, 10.7), 0.5, 0.35, SHU)
        beam(mb, (10.7 * sx, F + 1.2, -7.5), (10.7 * sx, F + 1.2, 10.7), 0.3, 0.25, SHU)
        beam(mb, (10.7 * sx, F + 2.4, 10.7), (4.4 * sx, F + 2.4, 10.7), 0.5, 0.35, SHU)
        beam(mb, (4.4 * sx, F, 10.7), (4.4 * sx, F + 2.8, 10.7), 0.5, 0.5, SHU, up=(0, 0, 1))
        usphere(mb, (4.4 * sx, F + 3.2, 10.7), 0.45, GOLD, scale=(1, 1.3, 1), seg=10, rings=6)   # 擬宝珠
    # かべ：うしろと横はしっくい、前は格子戸（中はうす暗い）
    wall_top = 21.5
    hh = wall_top - F
    ubox(mb, (0.0, F + hh / 2, -7.0), (18.0, hh, 0.7), PLASTER)
    for sx in (-1, 1):
        ubox(mb, (9.0 * sx, F + hh / 2, 0.0), (0.7, hh, 14.0), PLASTER)
    ubox(mb, (0.0, F + hh / 2, 6.9), (18.0, hh, 0.4), hexc("#3a2a20"))
    for k in range(15):
        x = -8.4 + k * 1.2
        beam(mb, (x, F + 0.2, 7.25), (x, wall_top - 0.3, 7.25), 0.28, 0.28, SHU, up=(0, 0, 1))
    for k in range(9):
        y = F + 1.0 + k * 1.6
        beam(mb, (-8.6, y, 7.3), (8.6, y, 7.3), 0.28, 0.28, SHU)
    # 長押とけた（朱）
    for z in (-7.0, 7.0):
        beam(mb, (-10.5, wall_top + 0.6, z), (10.5, wall_top + 0.6, z), 1.0, 1.2, SHU)
    for sx in (-1, 1):
        beam(mb, (9.0 * sx, wall_top + 0.6, -7.8), (9.0 * sx, wall_top + 0.6, 7.8), 1.0, 1.2, SHU)
    # 妻かべ（三角）：しっくいと、白木のわく
    ridge_y, ridge_z = 35.0, -1.0
    for sx in (-1, 1):
        prism(mb, [(9.0 * sx - 0.3, wall_top + 1.2, -7.0), (9.0 * sx - 0.3, wall_top + 1.2, 7.0), (9.0 * sx - 0.3, ridge_y - 0.6, ridge_z)],
              (0.6 * sx, 0.0, 0.0), PLASTER)
        beam(mb, (9.0 * sx, wall_top + 1.2, -7.0), (9.0 * sx, ridge_y - 0.6, ridge_z), 0.7, 0.7, HINOKI, up=(1, 0, 0))
        beam(mb, (9.0 * sx, wall_top + 1.2, 7.0), (9.0 * sx, ridge_y - 0.6, ridge_z), 0.7, 0.7, HINOKI, up=(1, 0, 0))
    # 屋根（流造：前へ長くのびる）と、むね・千木・鰹木
    x0, x1 = -14.5, 14.5
    roof_slope(mb, x0, x1, (ridge_y, ridge_z), (18.6, 17.5), 1.6, 1.3, COPPER, HINOKI_DARK, n=9)
    roof_slope(mb, x0, x1, (ridge_y, ridge_z), (24.0, -12.5), 0.9, 1.3, COPPER, HINOKI_DARK, n=6)
    beam(mb, (x0 - 0.4, ridge_y + 1.6, ridge_z), (x1 + 0.4, ridge_y + 1.6, ridge_z), 2.4, 1.8, by_normal(COPPER_DARK, COPPER_DARK))
    for sx in (-1, 1):
        x = (x1 + 0.2) * sx
        beam(mb, (x, ridge_y - 1.0, ridge_z - 3.2), (x, ridge_y + 6.2, ridge_z + 2.8), 0.5, 1.3, HINOKI_DARK, up=(1, 0, 0))   # 千木
        beam(mb, (x, ridge_y - 1.0, ridge_z + 3.2), (x, ridge_y + 6.2, ridge_z - 2.8), 0.5, 1.3, HINOKI_DARK, up=(1, 0, 0))
    for x in (-7.5, 0.0, 7.5):
        utube(mb, [(x, ridge_y + 3.1, ridge_z - 2.4), (x, ridge_y + 3.1, ridge_z + 2.4)], [0.9, 0.9], 12,
              lambda t, a, p, d: GOLD if t < 0.06 or t > 0.94 else HINOKI_DARK)                                                   # 鰹木
    # 正面の木の階段（高床へ）と、朱の手すり
    for k in range(6):
        top = (k + 1) * F / 6
        zc = 11.6 + (5 - k) * 1.25
        ubox(mb, (0.0, top / 2, zc), (8.0, top, 1.25), by_normal(HINOKI, HINOKI_DARK))
    for sx in (-1, 1):
        beam(mb, (4.2 * sx, 1.6, 19.2), (4.2 * sx, F + 1.6, 11.2), 0.45, 0.4, SHU)
        beam(mb, (4.2 * sx, 0.0, 19.2), (4.2 * sx, 2.2, 19.2), 0.5, 0.5, SHU, up=(0, 0, 1))
    # 鈴：軒からつるした金の鈴と、紅白の鈴緒（階段の上にたれる）
    bx, by, bz = HALL_BELL
    ubox(mb, (bx, by + 2.2, bz), (0.6, 2.2, 0.6), HINOKI_DARK)
    usphere(mb, (bx, by, bz), 1.25, lambda n: mixc(GOLD, hexc("#fff1b0"), 0.4 * max(0.0, n.y)), seg=18, rings=12)
    ubox(mb, (bx, by - 0.55, bz), (1.6, 0.18, 0.18), LACQ)                             # 鈴の口
    rope = [(bx, by - 1.2, bz), (bx + 0.1, by - 5.0, bz + 0.1), (bx - 0.05, by - 9.5, bz), (bx, 7.6, bz + 0.1)]
    utube(mb, rope, [0.42, 0.42, 0.42, 0.42], 10,
          lambda t, a, p, d: [hexc("#d23a2a"), PAPER, hexc("#6b3f8f")][int((t * 26 + a * 0.6) % 3)])
    usphere(mb, (bx, 7.2, bz + 0.1), 0.65, hexc("#d23a2a"), scale=(1, 1.4, 1), seg=10, rings=6)
    # 正面のしめ縄
    shimenawa(mb, (-9.0, wall_top - 0.4, 7.9), (9.0, wall_top - 0.4, 7.9), 1.3, 0.75, n_shide=4)
    return build(mb, "Shrine_Hall", smooth=False)


# ---------------------------------------------------------------------------
# 手水舎：四本の柱と切妻屋根・石の水盤（中に水。Unity で水面をはる）・竹の筧・ひしゃく
# ---------------------------------------------------------------------------
BASIN_IN = (4.9, 2.15)     # 水盤の内がわの半分の大きさ（x, z）
BASIN_FLOOR = 2.4          # 水盤の中の底
BASIN_RIM = 5.0            # ふちの上
SPOUT_TIP = (1.8, 7.9, -0.6)


def make_chozuya():
    mb = MB()
    st = stone_col(5.0, 0.2)
    # 水盤
    ubox(mb, (0.0, BASIN_FLOOR / 2, 0.0), (12.0, BASIN_FLOOR, 6.5), st)
    for sz in (-1, 1):
        ubox(mb, (0.0, (BASIN_FLOOR + BASIN_RIM) / 2, sz * 2.7), (12.0, BASIN_RIM - BASIN_FLOOR, 1.1), st)
    for sx in (-1, 1):
        ubox(mb, (sx * 5.45, (BASIN_FLOOR + BASIN_RIM) / 2, 0.0), (1.1, BASIN_RIM - BASIN_FLOOR, 4.3), st)
    ubox(mb, (0.0, BASIN_FLOOR + 0.02, 0.0), (9.8, 0.06, 4.3), hexc("#7d8a7e"))         # 水の底（こけ色）
    # 柱と屋根（檜皮ぶき）
    for sx in (-1, 1):
        for sz in (-1, 1):
            ucyl(mb, (sx * 7.0, 0.0, sz * 4.5), 0.7, 17.0, HINOKI, seg=10)
            ubox(mb, (sx * 7.0, 0.3, sz * 4.5), (1.8, 0.6, 1.8), STONE_DARK)
        beam(mb, (sx * 7.0, 16.6, -5.2), (sx * 7.0, 16.6, 5.2), 0.8, 0.9, HINOKI_DARK)
    for sz in (-1, 1):
        beam(mb, (-8.0, 16.8, sz * 4.5), (8.0, 16.8, sz * 4.5), 0.8, 0.9, HINOKI_DARK)
    for sz in (-1, 1):
        roof_slope(mb, -10.0, 10.0, (21.6, 0.0), (16.2, 8.2 * sz), 0.5, 0.9, BARK_ROOF, HINOKI_DARK, n=5)
    beam(mb, (-10.3, 22.6, 0.0), (10.3, 22.6, 0.0), 1.4, 1.2, by_normal(scalec(BARK_ROOF, 0.8), scalec(BARK_ROOF, 0.8)))
    for sx in (-1, 1):
        prism(mb, [(sx * 7.0 - 0.25, 17.3, -4.6), (sx * 7.0 - 0.25, 17.3, 4.6), (sx * 7.0 - 0.25, 21.2, 0.0)], (0.5 * sx, 0, 0), HINOKI)
    # 竹の筧（うしろの竹から、水盤の上へ）
    bb = lambda t, a, p, d: mixc(BAMBOO, BAMBOO_DARK, 0.3 + 0.3 * math.sin(a * 2)) if (t * 6) % 1 > 0.08 else BAMBOO_DARK
    utube(mb, [(4.6, 0.0, -3.9), (4.6, 9.0, -3.9)], [0.42, 0.42], 10, bb)
    utube(mb, [(4.6, 8.4, -3.9), (3.2, 8.2, -2.2), (SPOUT_TIP[0], SPOUT_TIP[1], SPOUT_TIP[2])], [0.32, 0.32, 0.3], 10, bb)
    # ひしゃく：うしろのふちにお椀、前のふちに柄をかける
    for k, x in enumerate((-3.2, -1.2, 0.8)):
        cup = (x, BASIN_RIM + 0.05, -2.75)
        ulathe(mb, cup, [(0.0, 0.0), (0.62, 0.0), (0.66, 0.9), (0.58, 0.9), (0.54, 0.12), (0.0, 0.12)], 14, HINOKI)
        utube(mb, [(x, BASIN_RIM + 0.55, -2.15), (x + 0.15 * (k - 1), BASIN_RIM + 0.35, 2.9)], [0.13, 0.11], 6, HINOKI_DARK)
    return build(mb, "Shrine_Chozuya", smooth=False)


# ---------------------------------------------------------------------------
# ご神木：太い楠の幹と根・しめ縄と紙垂・フクロウのすむうろ・高い所の太い枝
# ---------------------------------------------------------------------------
TREE_R = 8.5
TREE_ROPE_Y = 12.0
TREE_PERCH = (21.0, 49.4, 6.0)    # 枝の上の、フクロウがとまる所（Unity の座標）
TREE_HOLLOW = (1.5, 17.0, 8.6)    # うろの口（前の面。たてに長い、ゆがんだ穴）


def make_tree():
    rnd = random.Random(31)
    mb = MB()
    trunk_mesh(mb, TREE_R, 200.0, 72, 34, 5, 0.45, 5.0, 2.3, moss=0.3)
    # 地をはう根（参道の方はひかえめ）
    for k in range(6):
        a = TAU * k / 6 + 0.4 + rnd.uniform(-0.12, 0.12)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        L = rnd.uniform(12, 18)
        n = 16
        pts = []
        for i in range(n):
            t = i / (n - 1)
            r = TREE_R * 0.9 + L * t
            z = lerp(3.0, -0.9, t ** 0.6)
            pts.append(d * r + side * (math.sin(t * 4 + k) * 1.4) + Vector((0, 0, z)))
        radii = [lerp(2.6, 0.3, t ** 0.7) for t in (i / (n - 1) for i in range(n))]
        radii[-1] = 0.0
        tube(mb, pts, radii, 14, lambda t, a2, p, dd: bark_color(p * 0.5, a2, 0.3 * sstep(0.7, 1.0, abs(math.sin(a2 * 4))), 5.0 + k, 0.5, 0.2))
    # しめ縄（幹をぐるりと）と紙垂（前の方）
    ring = []
    for i in range(41):
        a = TAU * i / 40
        rr = TREE_R + 1.1 + 0.25 * math.sin(a * 5)
        ring.append(Vector((math.cos(a) * rr, math.sin(a) * rr, TREE_ROPE_Y + 0.35 * math.sin(a * 2))))
    tube(mb, ring, [1.0] * 41, 12, lambda t, ang, p, d: mixc(STRAW, STRAW_DARK, 0.5 + 0.5 * math.sin(ang * 2 + t * 160)))
    for k in range(5):
        a = math.radians(-120 + k * 18)   # Unity の南の面（参道から見える方）
        rr = TREE_R + 1.6
        bpos = Vector((math.cos(a) * rr, math.sin(a) * rr, TREE_ROPE_Y - 0.8))
        u = Vector((-bpos.x, bpos.z, -bpos.y))
        facing = (math.cos(a) * -1.0, 0.0, -math.sin(a))
        shide(mb, (u.x, u.y, u.z), 1.1, facing)
    # フクロウのうろ（南東の面の、黒いくぼみと、ふちのもりあがり）
    hx, hy, hz = TREE_HOLLOW
    usphere(mb, (hx, hy, hz), 1.0, hexc("#241b14"), scale=(1.1, 2.9, 0.7), seg=14, rings=10)
    # 穴のふちの、もりあがった樹皮（左右と下だけ。目玉のように見えない）
    for side in (-1, 1):
        utube(mb, [(hx + 1.0 * side, hy + 2.6, hz - 0.3), (hx + 1.35 * side, hy + 0.4, hz - 0.1), (hx + 0.9 * side, hy - 2.4, hz - 0.2)],
              [0.25, 0.55, 0.3], 8, lambda t, a, p, d: bark_color(p * 0.5, a, 0.4, 9.0, 0.3, 0.1))
    utube(mb, [(hx - 0.9, hy - 2.4, hz - 0.2), (hx, hy - 2.9, hz - 0.05), (hx + 0.9, hy - 2.4, hz - 0.2)], [0.3, 0.5, 0.3], 8,
          lambda t, a, p, d: bark_color(p * 0.5, a, 0.4, 9.0, 0.3, 0.1))
    # 高い所の太い枝（上はゆるい坂で、歩ける）
    branches = [((0.92, 0.0, 0.38), 46.0, 26.0), ((-0.7, 0.0, 0.7), 52.0, 22.0), ((-0.4, 0.0, -0.9), 58.0, 24.0), ((0.6, 0.0, -0.8), 41.0, 18.0)]
    for (dx, _, dz), y, L in branches:
        d = Vector((dx, 0.0, dz)).normalized()
        pts = []
        n = 10
        for i in range(n):
            t = i / (n - 1)
            p = d * (TREE_R * 0.5 + L * t) + Vector((0, y + 4.5 * t - 2.0 * t * t, 0))
            pts.append(UV(p))
        radii = [lerp(3.4, 1.0, t) for t in (i / (n - 1) for i in range(n))]
        radii[-1] = 0.0
        tube(mb, pts, radii, 14, lambda t, a2, p, dd: bark_color(p * 0.5, a2, 0.3, 21.0, 0.5, 0.3))
    return build(mb, "Shrine_Tree")


# ---------------------------------------------------------------------------
# 絵馬かけ・おみくじかけ
# ---------------------------------------------------------------------------
EMA_COLORS = [hexc("#d23a2a"), hexc("#2f6db0"), hexc("#3e8a4a"), hexc("#e2a92c"), hexc("#f2f0ea"), hexc("#7a4fa0")]


def make_ema():
    rnd = random.Random(41)
    mb = MB()
    for sx in (-1, 1):
        ucyl(mb, (sx * 6.5, 0.0, 0.0), 0.45, 13.0, HINOKI, seg=8)
    beam(mb, (-7.5, 12.8, 0.0), (7.5, 12.8, 0.0), 0.9, 0.8, HINOKI_DARK)
    for sz in (-1, 1):
        roof_slope(mb, -8.5, 8.5, (15.0, 0.0), (13.2, 2.4 * sz), 0.2, 0.5, BARK_ROOF, HINOKI_DARK, n=3)
    for y in (6.8, 10.3):
        beam(mb, (-6.5, y, 0.0), (6.5, y, 0.0), 0.35, 0.35, HINOKI_DARK)
        x = -5.6
        while x < 5.8:
            # 五角形の絵馬（前と後ろに 1 まいずつ）
            for sz in (-1, 1):
                w, h = 1.5, 1.15
                z = sz * 0.3
                yy = y - 0.35 - rnd.uniform(0.0, 0.15)
                poly = [(x - w / 2, yy - h, z), (x + w / 2, yy - h, z), (x + w / 2, yy - 0.35, z), (x, yy, z), (x - w / 2, yy - 0.35, z)]
                prism(mb, poly, (0, 0, 0.14 * sz), HINOKI)
                ubox(mb, (x, yy - 0.65, z + 0.16 * sz), (0.95, 0.5, 0.04), rnd.choice(EMA_COLORS))
            x += rnd.uniform(1.6, 1.9)
    return build(mb, "Shrine_Ema", smooth=False)


def make_omikuji():
    rnd = random.Random(43)
    mb = MB()
    for sx in (-1, 1):
        ucyl(mb, (sx * 5.0, 0.0, 0.0), 0.4, 9.5, HINOKI_DARK, seg=8)
    for y in (4.2, 6.2, 8.2):
        pts = [(-5.0 + 10.0 * i / 8, y - 0.35 * math.sin(math.pi * i / 8), 0.0) for i in range(9)]
        utube(mb, pts, [0.12] * 9, 6, STRAW)
        for k in range(14):
            t = rnd.uniform(0.06, 0.94)
            x = -5.0 + 10.0 * t
            yy = y - 0.35 * math.sin(math.pi * t)
            ubox(mb, (x, yy, rnd.uniform(-0.05, 0.05)), (0.42, 0.34, 0.3), PAPER)
            ubox(mb, (x + rnd.uniform(-0.1, 0.1), yy - 0.5, 0.0), (0.22, 0.7, 0.06), PAPER)
    return build(mb, "Shrine_Omikuji", smooth=False)


# ---------------------------------------------------------------------------
# 奥の宮の祠（千本鳥居をぬけた先）
# ---------------------------------------------------------------------------
def make_hokora():
    mb = MB()
    ubox(mb, (0.0, 1.0, 0.0), (9.0, 2.0, 8.0), stone_col(6.0, 0.4, STONE_DARK))
    ubox(mb, (0.0, 2.4, 0.0), (7.6, 0.8, 6.8), stone_col(7.0, 0.3))
    ubox(mb, (0.0, 5.4, -0.3), (5.2, 5.2, 4.4), by_normal(HINOKI, HINOKI))
    ubox(mb, (0.0, 5.2, 1.95), (3.6, 3.8, 0.2), hexc("#3a2a20"))
    ubox(mb, (0.0, 5.2, 2.07), (0.2, 3.8, 0.1), GOLD)
    for sz in (-1, 1):
        roof_slope(mb, -3.8, 3.8, (10.4, -0.3), (7.6, -0.3 + 3.6 * sz), 0.25, 0.6, COPPER, HINOKI_DARK, n=3)
    beam(mb, (-4.0, 11.0, -0.3), (4.0, 11.0, -0.3), 1.0, 0.8, COPPER_DARK)
    for sx in (-1, 1):
        prism(mb, [(sx * 2.6 - 0.2, 8.0, -2.5), (sx * 2.6 - 0.2, 8.0, 1.9), (sx * 2.6 - 0.2, 10.3, -0.3)], (0.4 * sx, 0, 0), HINOKI)
    shimenawa(mb, (-2.7, 7.9, 2.25), (2.7, 7.9, 2.25), 0.4, 0.3, n_shide=2)
    # 小さなキツネの像が 2 ひき、祠のわきに
    for sx in (-1, 1):
        x = sx * 3.3
        usphere(mb, (x, 3.6, 2.2), 0.55, FOX_STONE, scale=(1.0, 1.0, 1.2), seg=10, rings=7)
        utube(mb, [(x, 3.6, 2.3), (x, 4.6, 2.6)], [0.42, 0.32], 8, FOX_STONE)
        usphere(mb, (x, 5.0, 2.75), 0.38, FOX_STONE, seg=10, rings=7)
        for ex in (-1, 1):
            utube(mb, [(x + 0.18 * ex, 5.25, 2.7), (x + 0.24 * ex, 5.75, 2.65)], [0.13, 0.0], 6, FOX_STONE)
        usphere(mb, (x, 4.3, 2.75), 0.35, BIB, scale=(1.0, 0.8, 0.4), seg=8, rings=5)
    return build(mb, "Shrine_Hokora", smooth=False)


# ---------------------------------------------------------------------------
# 太鼓橋：池の上の、まるくそった朱の橋（東西にわたす。床の上は歩ける）
# ---------------------------------------------------------------------------
BRIDGE_HALF = 13.0
BRIDGE_RISE = 5.0
BRIDGE_END_Y = 0.7


def bridge_y(x):
    return BRIDGE_END_Y + BRIDGE_RISE * (1.0 - (x / BRIDGE_HALF) ** 2)


def make_bridge():
    mb = MB()
    n = 18
    deck = by_normal(hexc("#8a6646"), SHU, SHU_DARK)
    for i in range(n):
        x0 = -BRIDGE_HALF + 2 * BRIDGE_HALF * i / n
        x1 = -BRIDGE_HALF + 2 * BRIDGE_HALF * (i + 1) / n
        y0, y1 = bridge_y(x0), bridge_y(x1)
        P = [Vector((x0, y0 - 0.9, -3.0)), Vector((x1, y1 - 0.9, -3.0)), Vector((x1, y1 - 0.9, 3.0)), Vector((x0, y0 - 0.9, 3.0))]
        hexa(mb, P + [p + Vector((0, 0.9, 0)) for p in P], deck)
        for sz in (-1, 1):
            beam(mb, (x0, y0 - 1.6, 2.6 * sz), (x1, y1 - 1.6, 2.6 * sz), 0.7, 1.5, SHU)                  # 桁（朱）
            beam(mb, (x0, y0 + 2.7, 2.75 * sz), (x1, y1 + 2.7, 2.75 * sz), 0.5, 0.4, SHU)                # 手すり
            beam(mb, (x0, y0 + 1.3, 2.75 * sz), (x1, y1 + 1.3, 2.75 * sz), 0.3, 0.25, SHU)
    for k in range(9):
        x = -BRIDGE_HALF + 0.3 + (2 * BRIDGE_HALF - 0.6) * k / 8
        y = bridge_y(x)
        for sz in (-1, 1):
            beam(mb, (x, y - 0.1, 2.75 * sz), (x, y + 3.0, 2.75 * sz), 0.5, 0.5, SHU, up=(0, 0, 1))
            if k in (0, 8):
                usphere(mb, (x, y + 3.5, 2.75 * sz), 0.45, GOLD, scale=(1, 1.3, 1), seg=10, rings=6)
    # 橋脚（池の底まで）
    for x in (-6.0, 6.0):
        for sz in (-1, 1):
            ucyl(mb, (x, -3.5, 2.2 * sz), 0.55, bridge_y(x) - 1.2 + 3.5, SHU_DARK, seg=10)
        beam(mb, (x, bridge_y(x) - 2.6, -2.6), (x, bridge_y(x) - 2.6, 2.6), 0.6, 0.7, SHU_DARK)
    return build(mb, "Shrine_Bridge", smooth=False)


# ---------------------------------------------------------------------------
# 玉垣（朱の木のさく）・石畳・石段
# ---------------------------------------------------------------------------
def make_fence():
    mb = MB()
    for k in range(5):
        x = -5.0 + k * 2.5
        beam(mb, (x, 0.0, 0.0), (x, 5.6, 0.0), 0.6, 0.6, SHU, up=(0, 0, 1))
        ulathe(mb, (x, 5.6, 0.0), [(0.0, 0.0), (0.45, 0.0), (0.35, 0.35), (0.0, 0.55)], 4, SHU_DARK, phase=math.pi / 4)
    beam(mb, (-5.3, 4.9, 0.0), (5.3, 4.9, 0.0), 0.7, 0.5, SHU)
    beam(mb, (-5.3, 2.0, 0.0), (5.3, 2.0, 0.0), 0.5, 0.4, SHU)
    return build(mb, "Shrine_Fence", smooth=False)


def make_slab():
    mb = MB()
    top = 0.18
    st = stone_col(8.0, 0.08, STONE_LIGHT)
    ubox(mb, (0.0, -0.2, 0.0), (3.0, 0.6, 2.0), st)
    P = [Vector((-1.5, top - 0.1, -1.0)), Vector((1.5, top - 0.1, -1.0)), Vector((1.5, top - 0.1, 1.0)), Vector((-1.5, top - 0.1, 1.0))]
    Q = [Vector((-1.38, top, -0.88)), Vector((1.38, top, -0.88)), Vector((1.38, top, 0.88)), Vector((-1.38, top, 0.88))]
    hexa(mb, P + Q, st)
    return build(mb, "Shrine_Slab", smooth=False)


def make_step():
    mb = MB()
    st = stone_col(9.0, 0.12)
    ubox(mb, (0.0, -0.5, 0.0), (16.0, 1.3, 1.9), st)
    ubox(mb, (0.0, 0.2, -0.05), (15.6, 0.2, 1.8), stone_col(10.0, 0.18, STONE_LIGHT))
    return build(mb, "Shrine_Step", smooth=False)


# ---------------------------------------------------------------------------
# 草花と落ち葉
# ---------------------------------------------------------------------------
def make_higanbana(name, seed):
    """ヒガンバナ：細いくきの先に、そり返った赤い花びらと、長くのびたしべ（3 本）"""
    rnd = random.Random(seed)
    mb = MB()
    red = hexc("#e0312a")
    for s in range(3):
        a0 = TAU * s / 3 + rnd.uniform(-0.3, 0.3)
        base = Vector((math.cos(a0) * 0.6, 0.0, math.sin(a0) * 0.6))
        h = rnd.uniform(3.0, 4.2)
        lean = Vector((rnd.uniform(-0.3, 0.3), 0.0, rnd.uniform(-0.3, 0.3)))
        top = base + Vector((0, h, 0)) + lean
        utube(mb, [tuple(base), tuple(base + Vector((0, h * 0.5, 0)) + lean * 0.4), tuple(top)], [0.09, 0.08, 0.07], 6,
              lambda t, a, p, d: mixc(hexc("#4f7a2a"), hexc("#7da244"), t))
        for k in range(6):
            a = TAU * k / 6 + rnd.uniform(-0.2, 0.2)
            d = Vector((math.cos(a), 0.0, math.sin(a)))
            p0 = top
            p1 = top + d * 0.55 + Vector((0, 0.15, 0))
            p2 = top + d * 0.95 - Vector((0, 0.15, 0))
            p3 = top + d * 1.05 - Vector((0, 0.45, 0))
            utube(mb, [tuple(p0), tuple(p1), tuple(p2), tuple(p3)], [0.08, 0.1, 0.07, 0.0], 5, red)          # 花びら
            q1 = top + d * 0.7 + Vector((0, 0.55, 0))
            q2 = top + d * 1.35 + Vector((0, 0.95, 0))
            utube(mb, [tuple(top), tuple(q1), tuple(q2)], [0.03, 0.025, 0.0], 4, red)                       # しべ
            usphere(mb, tuple(q2), 0.06, hexc("#f2c94c"), seg=5, rings=3)
    return build(mb, name)


def make_sasa(name, seed, stems=6):
    """ササ：細いくきに、細長い葉が何まいも"""
    rnd = random.Random(seed)
    mb = MB()
    for s in range(stems):
        a = rnd.uniform(0, TAU)
        base = Vector((math.cos(a) * rnd.uniform(0, 0.8), 0.0, math.sin(a) * rnd.uniform(0, 0.8)))
        h = rnd.uniform(3.5, 6.0)
        lean = Vector((rnd.uniform(-0.6, 0.6), 0.0, rnd.uniform(-0.6, 0.6)))
        pts = [base + Vector((0, h * t, 0)) + lean * t * t for t in (0.0, 0.5, 1.0)]
        utube(mb, [tuple(p) for p in pts], [0.07, 0.06, 0.04], 5, BAMBOO_DARK)
        for k in range(4):
            t = 0.45 + 0.17 * k
            at = base + Vector((0, h * t, 0)) + lean * t * t
            la = rnd.uniform(0, TAU)
            d = Vector((math.cos(la), -0.25, math.sin(la))).normalized()
            L = rnd.uniform(1.6, 2.4)
            side = Vector((0, 1, 0)).cross(d).normalized()
            leaf = [at, at + d * L * 0.5 + Vector((0, 0.15, 0)), at + d * L]
            # 葉は、うすい箱を 2 つ（どちらから見ても見える）
            for i in range(2):
                a3, b3 = leaf[i], leaf[i + 1]
                w = 0.42 if i == 0 else 0.3
                beam(mb, tuple(a3), tuple(b3), w, 0.04, lambda n: mixc(hexc("#5d8f36"), hexc("#a7c766"), 0.4 + 0.3 * n.y), up=tuple(side.cross(d)))
    return build(mb, name, smooth=False)


def make_ginkgo_leaf():
    """落ちたイチョウの葉：黄色い扇の形"""
    mb = MB()
    tmp = MB()
    polar_sheet(tmp, lambda th: 0.0 if abs(th) > 1.25 else 1.0 * (1.0 - 0.12 * (1 if abs(th) < 0.08 else 0)) * (0.9 + 0.1 * math.cos(th * 3)),
                lambda rho, th, p: mixc(hexc("#f2c63a"), hexc("#e6a224"), 0.3 * rho + 0.2 * math.sin(th * 9)),
                n_ang=40, n_rad=5, a0=-1.25, a1=1.25, zfn=lambda rho, th: 0.06 * rho * rho, full=False)
    mb.add(tmp)
    tube(mb, [Vector((0, 0, 0.0)), Vector((0, -0.8, 0.05))], [0.04, 0.03], 5, lambda t, a, p, d: hexc("#c99a2a"))
    return build(mb, "Shrine_Ginkgo")


# ---------------------------------------------------------------------------
MAKERS = [
    make_torii, make_torii_small, make_lantern,
    lambda: make_fox_statue("Shrine_Fox_A", "jewel"),
    lambda: make_fox_statue("Shrine_Fox_B", "key"),
    make_saisen, make_hall, make_chozuya, make_tree, make_ema, make_omikuji, make_hokora, make_bridge,
    make_fence, make_slab, make_step,
    lambda: make_higanbana("Shrine_Higanbana", 901),
    lambda: make_sasa("Shrine_Sasa", 911),
    make_ginkgo_leaf,
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
