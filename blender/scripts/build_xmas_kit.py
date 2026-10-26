"""
しゃくとりの森 — クリスマス村のアセット生成スクリプト

夜の森の星見の丘の「光の輪」をくぐった先の、雪のふるクリスマスの夜の村（9 つ目のエリア）を、しゃくとりむしから見た大きさで作る。
大きなクリスマスツリー（段になった枝・オーナメント・モール・てっぺんの星）・プレゼントの箱・雪だるま・あかりのともる家・
サンタへのポスト・そり・手ぶくろ・ガーデンランプ・電球のさく・キャンディケイン・雪のモミの木・ヒイラギ・雪の吹きだまり・
こおった池の氷・光の輪。FBX を書き出します。1 単位 = しゃくとりむしの体長（2.5cm）。

光る所（ツリーの電球と星・ランプのガラス・家の窓・さくの電球・光の輪の電球）は、別のメッシュ（Unity で光る材質にする）。
座標は Unity の向き（x 東・y 上・z 北）で書く（build_shrine_kit の U などをつかう）。物の床のまん中が原点。

使い方:
  blender -b --factory-startup --python blender/scripts/build_xmas_kit.py -- [fbx_out_dir] [blend_out]
"""
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_forest_kit as kit  # noqa: E402
from build_forest_kit import MB, build, connect_rings, hexc, lathe, lerp, mixc, polar_sheet, ribbon, scalec, sstep, uv_sphere  # noqa: E402
from build_shrine_kit import UV, beam, prism, ubox, ucyl, ulathe, usphere, utube  # noqa: E402


def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    fbx = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "xmas_kit.blend")
    return fbx, blend


SNOW = hexc("#eef2f7")
SNOW_SHADE = hexc("#c4d0e2")       # 雪のかげ（白くとびすぎない、青み）
FIR = hexc("#2f6040")
FIR_LIGHT = hexc("#4f8452")
FIR_DARK = hexc("#1d3f2a")
TRUNK = hexc("#6b4a32")
WOOD = hexc("#a8794e")
WOOD_LIGHT = hexc("#c69a68")
WOOD_DARK = hexc("#6e4a2e")
IRON = hexc("#3a3d44")
GOLD = hexc("#e2b64a")
GOLD_LIGHT = hexc("#f6dc8a")
RED = hexc("#c8323a")
RED_DARK = hexc("#8e1f28")
GREEN = hexc("#2f7a45")
BLUE = hexc("#3a64b8")
SILVER = hexc("#c9cfd9")
WHITE = hexc("#f4f2ec")
CARROT = hexc("#e8812a")
COAL = hexc("#2a2a2e")
BUCKET = hexc("#6e7a86")
LOG = hexc("#9b6a44")
LOG_DARK = hexc("#6a442a")
STONE = hexc("#8f8c86")
ICE = hexc("#a9cbe0")
ICE_DEEP = hexc("#6f9dbd")
WARM = hexc("#ffd27a")             # あかり（光る材質）
WARM_WHITE = hexc("#fff0c8")
AMBER = hexc("#ffa53c")             # 窓とランプの、あたたかい琥珀色のあかり（雪の夜の寒色と対比）
BULBS = [hexc("#ffd060"), hexc("#ff6a5a"), hexc("#7ee08a"), hexc("#7ab4ff"), hexc("#ffa8e0")]


def snow_col(n):
    """雪の色（Unity の法線 n）：上は白く、下と横は、青いかげ"""
    return mixc(SNOW_SHADE, SNOW, sstep(-0.4, 0.8, n.y))


def bn(nb):
    """Blender の法線を Unity の法線に"""
    return Vector((-nb.x, nb.z, -nb.y))


def gbox(mb, c, s, colfn, n=6):
    """Unity の座標の直方体（まん中 c・大きさ s）。面をこまかく分けて、色のもよう（colfn(Unity の点, Unity の法線)）をつける"""
    cx, cy, cz = c
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    faces = [((1, 0, 0), (0, 1, 0), (0, 0, 1)), ((-1, 0, 0), (0, 0, 1), (0, 1, 0)), ((0, 1, 0), (0, 0, 1), (1, 0, 0)),
             ((0, -1, 0), (1, 0, 0), (0, 0, 1)), ((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (0, 1, 0), (1, 0, 0))]
    # 面ごとに格子を作り、ふちの点は同じ番号にする（すき間のない、とじた箱）
    index = {}

    def vid(p):
        key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
        if key not in index:
            nrm = Vector((0, 0, 0))
            if abs(abs(p.x - cx) - hx) < 1e-6:
                nrm.x = math.copysign(1, p.x - cx)
            if abs(abs(p.y - cy) - hy) < 1e-6:
                nrm.y = math.copysign(1, p.y - cy)
            if abs(abs(p.z - cz) - hz) < 1e-6:
                nrm.z = math.copysign(1, p.z - cz)
            index[key] = mb.v(UV(p), colfn(p, nrm.normalized()))
        return index[key]
    half = Vector((hx, hy, hz))
    for nrm, u, v in faces:
        N, Uu, Vv = Vector(nrm), Vector(u), Vector(v)
        grid = []
        for i in range(n + 1):
            row = []
            for j in range(n + 1):
                a = -1 + 2 * i / n
                b = -1 + 2 * j / n
                p = Vector((cx, cy, cz)) + Vector((N.x * half.x, N.y * half.y, N.z * half.z)) \
                    + Vector((Uu.x * half.x, Uu.y * half.y, Uu.z * half.z)) * a + Vector((Vv.x * half.x, Vv.y * half.y, Vv.z * half.z)) * b
                row.append(vid(p))
            grid.append(row)
        for i in range(n):
            for j in range(n):
                q = [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
                # 外向き：面の法線と、まわる向きをそろえる
                a0, a1, a2 = mb.V[q[0]], mb.V[q[1]], mb.V[q[2]]
                fn = (a1 - a0).cross(a2 - a0)
                if bn(fn).dot(N) < 0:
                    q.reverse()
                mb.f(*q)


def finer(prof, k=3):
    """回転体の輪郭の点のあいだに、点を足す（形は同じ折れ線のまま、色やでこぼこを細かく）"""
    out = []
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        for i in range(k):
            out.append((lerp(r0, r1, i / k), lerp(z0, z1, i / k)))
    out.append(prof[-1])
    return out


def needle_tint(c, a, t, k):
    """モミの葉の、こまかなすじ（枝の先は明るく、すきまは、こい）"""
    return scalec(c, 0.86 + 0.2 * (0.5 + 0.5 * math.sin(a * 64.0 + t * 30.0 + k)))


def torus(mb, c, axis, R, r, colfn, nu=40, nv=12):
    """Unity の座標の、とじた輪（まん中 c・軸 axis・輪の半径 R・太さ r）。colfn(Unity の点, Unity の外向き, u)"""
    c, axis = Vector(c), Vector(axis).normalized()
    ref = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))
    e1 = axis.cross(ref).normalized()
    e2 = axis.cross(e1).normalized()
    rings = []
    for i in range(nu):
        u = i / nu
        a = u * math.tau
        radial = e1 * math.cos(a) + e2 * math.sin(a)
        center = c + radial * R
        ring = []
        for j in range(nv):
            b = j / nv * math.tau
            d = radial * math.cos(b) + axis * math.sin(b)
            p = center + d * r
            ring.append(mb.v(UV(p), colfn(p, d, u)))
        rings.append(ring)
    connect_rings(mb, rings, closed=True)


def shiny(base, n, k=0.45):
    """ぴかぴかの玉（左上から光が当たって見える、明るい所）"""
    hl = sstep(0.5, 0.95, n.dot(Vector((-0.35, 0.75, -0.55)).normalized()))
    return mixc(scalec(base, 0.75 + 0.25 * (n.y * 0.5 + 0.5)), WHITE, hl * k)


# ---------------------------------------------------------------------------
# 大きなクリスマスツリー
# ---------------------------------------------------------------------------
TREE_TIERS = [7.0, 14.0, 20.5, 26.5, 32.0, 37.0, 41.5, 48.0]   # 枝の段のさかい（下から）
TREE_R0 = 15.0
TREE_TOP = 48.0
TREE_SKIRT = 2.2       # 段のすそが、下の段の上へ出る長さ
TUB_H = 6.0
STAR_C = Vector((0.0, 51.5, 0.0))
STAR_R = 3.0


def tree_master(z):
    """ツリーの、もとになる円すいの半径（高さ z）"""
    return TREE_R0 * max(0.0, 1.0 - (z - TREE_TIERS[0]) / (TREE_TOP - TREE_TIERS[0]))


def tier_rim(k):
    """k 段目のすその半径と高さ"""
    z0 = TREE_TIERS[k]
    return tree_master(z0) + TREE_SKIRT * lerp(1.0, 0.55, k / 6), z0 + 0.5


def make_xmas_tree(look=True):
    """大きなクリスマスツリー（高さ 1.3m ほど）：木のおけに立ち、7 段の枝のすそ（まわりがぎざぎざ）。
    すそには、赤・金・青・銀のオーナメントと、金のモール。段の上は、しゃくとりむしが登れる坂。
    look=False は当たり判定（_Col）：前の、なめらかな段のまま（登る道・しずくの場所を変えない）"""
    mb = MB()
    # 木のおけ（たてのいたと、2 本の鉄のたが）
    ulathe(mb, (0, -1.0, 0), [(0.0, 0.0), (6.0, 0.0), (6.6, TUB_H + 1.0), (6.1, TUB_H + 1.2), (0.0, TUB_H + 0.9)], 36,
           lambda t, a, p: IRON if abs(p.z - 1.6) < 0.45 or abs(p.z - 5.3) < 0.45 else mixc(WOOD, WOOD_LIGHT, 0.5 + 0.5 * math.sin(a * 18.0)))
    ucyl(mb, (0, TUB_H - 0.02, 0), 1.4, 3.15, lambda t, a, p: TRUNK, seg=12)   # 幹は、おけのふたの上から（おけの中に物を入れない）
    for k in range(7):
        z0, z1 = TREE_TIERS[k], TREE_TIERS[k + 1]
        rim_r, rim_z = tier_rim(k)
        top_r = tree_master(z1) * 0.85 if k < 6 else 0.0
        prof = [(0.0, z0 - 0.3), (rim_r * 0.5, z0 - 0.2), (rim_r, rim_z), (rim_r - 0.7, rim_z + 0.8),
                (lerp(rim_r - 0.7, top_r, 0.5), lerp(rim_z + 0.8, z1 + 0.8, 0.5))]
        prof += [(top_r, z1 + 0.8), (0.0, z1 + 1.0)] if k < 6 else [(0.0, z1 + 0.8)]

        def rfn(t, a, k=k):
            jag = 0.07 if 0.25 < t < 0.6 else 0.02
            return 1.0 + jag * (0.5 + 0.5 * math.cos(a * (11 - k)))

        def col(t, a, p, k=k):
            if t < 0.3:
                return FIR_DARK
            c = mixc(FIR, FIR_LIGHT, 0.4 * (0.5 + 0.5 * math.cos(a * (11 - k))))
            # すそのふちに、雪がうっすら
            if 0.3 < t < 0.55 and noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, k * 1.7))) > 0.15:
                c = mixc(c, SNOW, 0.75)
            return c
        if not look:
            lathe(mb, prof, 44, col, center=UV((0, 0, 0)), rfn=rfn)
            # すそのふちの、金のモール（輪。半分は、すその中）
            torus(mb, (0, rim_z + 0.45, 0), (0, 1, 0), rim_r - 0.55, 0.38,
                  lambda p, d, u: mixc(GOLD, GOLD_LIGHT, 0.5 + 0.5 * math.sin(u * 300.0)), nu=48, nv=8)
        else:
            # 見た目：すそは、枝の先が何重にもかさなる（大きなぎざぎざに、小さなぎざぎざ）。葉のすじと、枝先の明るい色
            def rfn2(t, a, k=k):
                jag = 0.09 if 0.25 < t < 0.6 else 0.025
                big = 0.5 + 0.5 * math.cos(a * (11 - k))
                small = 0.5 + 0.5 * math.cos(a * (33 - 3 * k) + 0.7)
                return 1.0 + jag * (0.65 * big + 0.35 * small)

            def col2(t, a, p, k=k):
                c = col(t, a, p)
                if t >= 0.3:
                    c = needle_tint(c, a, t, k)
                    c = mixc(c, FIR_LIGHT, 0.35 * sstep(0.7, 1.0, 0.5 + 0.5 * math.cos(a * (11 - k))) * sstep(0.25, 0.45, t))
                return c
            lathe(mb, finer(prof, 3), 110, col2, center=UV((0, 0, 0)), rfn=rfn2)
            # 金のモール：すそのふちに、ゆるく弧をえがいて、たれさがる（つなぎ目は、すその中）
            m = 8 - k // 2
            for i in range(m):
                a0 = (i + 0.15 * k) / m * math.tau
                a1 = (i + 1 + 0.15 * k) / m * math.tau
                pts = []
                for s in range(13):
                    u = s / 12
                    a = lerp(a0, a1, u)
                    sag = math.sin(math.pi * u)
                    rr = rim_r - 0.75 + 0.75 * sag ** 0.5
                    pts.append(Vector((math.cos(a) * rr, rim_z + 0.5 - 1.4 * sag, math.sin(a) * rr)))
                utube(mb, pts, [0.0] + [0.36] * 11 + [0.0], 8,
                      lambda t, a, p, d: mixc(GOLD, GOLD_LIGHT, sstep(0.3, 1.0, 0.5 + 0.5 * math.sin(a * 3.0 + t * 90.0))))
        # オーナメント（すそのふちに、ぶらさがる。上は、すその中）
        n = 9 - k
        for i in range(n):
            a = (i + 0.5 * (k % 2)) / n * math.tau + k * 0.37
            col_b = [RED, GOLD, BLUE, SILVER][(i + k) % 4]
            c = Vector((math.cos(a) * (rim_r - 0.25), rim_z - 0.45, math.sin(a) * (rim_r - 0.25)))
            rr = 1.05 - 0.06 * k
            usphere(mb, c, rr, lambda nn, col_b=col_b: shiny(col_b, bn(nn)), seg=14, rings=9)
            ucyl(mb, c + Vector((0, rr * 0.75, 0)), rr * 0.32, rr * 0.55, lambda t, a, p: GOLD, seg=8)
    return build(mb, "Xmas_Tree" + ("" if look else "_Col"), sharp_deg=60)   # おけの角は、かどのまま（なめらかにすると、しゃくとりむしには、ひさしに見える）


def make_xmas_tree_lights():
    """ツリーの電球（光る材質）：段のすそのふちに、いろいろな色の小さな電球"""
    mb = MB()
    for k in range(7):
        rim_r, rim_z = tier_rim(k)
        n = 16 - k
        for i in range(n):
            a = (i + 0.5) / n * math.tau + k * 0.21
            c = Vector((math.cos(a) * (rim_r - 0.15), rim_z + 0.85, math.sin(a) * (rim_r - 0.15)))
            col = BULBS[(i * 3 + k) % len(BULBS)]
            usphere(mb, c, 0.36, lambda nn, col=col: mixc(col, WARM_WHITE, 0.35 * sstep(0.2, 0.9, bn(nn).y)), seg=8, rings=5)
    return build(mb, "Xmas_TreeLights")


def make_xmas_tree_star():
    """ツリーのてっぺんの、金の星（光る材質）：ふくらんだ 5 つのとがり（前とうしろへ、まん中がもり上がる）と、ささえの柄"""
    mb = MB()
    pts = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5
        r = STAR_R if i % 2 == 0 else STAR_R * 0.45
        pts.append(STAR_C + Vector((math.cos(a) * r, math.sin(a) * r, 0.0)))
    for sz in (1, -1):
        tip = mb.v(UV(STAR_C + Vector((0, 0, 0.9 * sz))), GOLD_LIGHT)
        ids = [mb.v(UV(p), mixc(GOLD, WARM, 0.3)) for p in pts]
        for i in range(10):
            j = (i + 1) % 10
            if sz > 0:
                mb.f(tip, ids[j], ids[i])
            else:
                mb.f(tip, ids[i], ids[j])
    # 星のまわりの点は、前と後ろで同じ所（ふちで、とじている）
    ucyl(mb, (0, TREE_TOP - 0.5, 0), 0.45, STAR_C.y - 0.6 - (TREE_TOP - 0.5), lambda t, a, p: GOLD, seg=10)
    return build(mb, "Xmas_TreeStar", smooth=False)


# ---------------------------------------------------------------------------
# プレゼントの箱
# ---------------------------------------------------------------------------
PRESENTS = {
    "A": dict(size=(10.0, 8.0, 10.0), box=RED, box2=RED_DARK, ribbon=GOLD, dots=None),
    "B": dict(size=(7.0, 11.0, 7.0), box=GREEN, box2=hexc("#256237"), ribbon=RED, dots=WHITE),
    "C": dict(size=(13.0, 6.0, 9.0), box=BLUE, box2=hexc("#2c4f94"), ribbon=SILVER, dots=GOLD),
}


def make_present(key):
    """プレゼントの箱：リボンが十字にかかり、上にちょう結び。上の面は、ちょう結びのまわりを歩ける"""
    d = PRESENTS[key]
    w, h, dd = d["size"]
    mb = MB()
    rib_w = 1.4 if key != "B" else 1.2

    def box_col(p, n):
        c = mixc(d["box2"], d["box"], 0.6 + 0.4 * max(0.0, n.y))
        u = p.x + p.z if abs(n.y) < 0.5 else p.x
        v = p.y if abs(n.y) < 0.5 else p.z
        if key == "B":
            # 白い水玉（ならびを、1 列ずつずらす）
            fx, fy = u * 0.32, v * 0.32
            fx += 0.5 if int(math.floor(fy)) % 2 else 0.0
            dist = math.hypot(fx - math.floor(fx) - 0.5, fy - math.floor(fy) - 0.5)
            c = mixc(c, d["dots"], sstep(0.3, 0.16, dist))
        elif key == "C":
            # ななめの、金のしま
            s = (u + v) * 0.35
            c = mixc(c, d["dots"], 0.9 * sstep(0.34, 0.42, abs(s - math.floor(s) - 0.5)))
        else:
            # こい赤の、ひし形のもよう
            s1, s2 = (u + v) * 0.3, (u - v) * 0.3
            line = min(abs(s1 - round(s1)), abs(s2 - round(s2)))
            c = mixc(c, d["box2"], 0.6 * sstep(0.06, 0.02, line))
        return c
    gbox(mb, (0, h / 2, 0), (w, h, dd), box_col, n=34)
    rc = lambda p, n: mixc(scalec(d["ribbon"], 0.8), d["ribbon"], 0.6 + 0.4 * max(0.0, n.y))
    gbox(mb, (0, h / 2, 0), (w + 0.14, h + 0.12, rib_w), rc, n=4)
    gbox(mb, (0, h / 2, 0), (rib_w, h + 0.12, dd + 0.14), rc, n=4)
    # ちょう結び：まん中の結び目と、2 つの輪（両はしは結び目の中）と、たれ
    knot = Vector((0, h + 0.45, 0))
    usphere(mb, knot, 0.75, lambda n: d["ribbon"], scale=(1.0, 0.7, 1.0), seg=12, rings=8)
    for sx in (-1, 1):
        loop = [knot, knot + Vector((1.3 * sx, 1.0, 0.25)), knot + Vector((2.6 * sx, 0.8, 0.0)), knot + Vector((1.8 * sx, 0.15, -0.2)),
                knot + Vector((0.2 * sx, 0.0, 0.0))]
        utube(mb, loop, [0.0, 0.42, 0.48, 0.42, 0.0], 10, lambda t, a, p, dd_: mixc(d["ribbon"], WHITE, 0.15 * math.sin(t * math.pi)))
        beam(mb, knot + Vector((0.2 * sx, -0.3, 0.2)), Vector((1.9 * sx, h + 0.08, 1.8)), 0.9, 0.12, lambda n: d["ribbon"])
    return build(mb, f"Xmas_Present_{key}", sharp_deg=40)   # 箱の角は、かどのまま（かべが、ひさしに見えない）


# ---------------------------------------------------------------------------
# 雪だるま
# ---------------------------------------------------------------------------
SNOWMAN_BALLS = [(Vector((0, 5.0, 0)), 7.0), (Vector((0, 14.2, 0)), 5.0), (Vector((0, 21.3, 0)), 3.6)]


def make_snowman():
    """雪だるま（高さ 70cm ほど）：3 つの雪玉・バケツのぼうし・にんじんの鼻・炭の目と口とボタン・赤いマフラー・小枝のうで。
    根もとは、雪がもりあがっている（下の雪玉の下へ、もぐりこまない）"""
    mb = MB()
    for c, r in SNOWMAN_BALLS:
        usphere(mb, c, r, lambda n: snow_col(bn(n)), seg=28, rings=18)
    ulathe(mb, (0, -0.6, 0), [(0.0, 0.0), (9.0, 0.0), (8.0, 1.0), (6.4, 2.6), (0.0, 3.0)], 32, lambda t, a, p: mixc(SNOW, SNOW_SHADE, 0.3 * (1 - t)))
    head, hr = SNOWMAN_BALLS[2]
    mid, mr = SNOWMAN_BALLS[1]
    # バケツのぼうし（少しななめ。下は頭の中）
    hat_base = head + Vector((0.3, hr * 0.62, 0))
    ulathe(mb, hat_base, [(0.0, 0.0), (2.9, 0.0), (3.0, 0.25), (2.35, 3.8), (0.0, 3.85)], 20,
           lambda t, a, p: mixc(BUCKET, scalec(BUCKET, 1.3), 0.5 + 0.5 * math.sin(a * 2.0)) if t < 0.9 else scalec(BUCKET, 0.85))
    # 炭の目・口
    for sx in (-1, 1):
        usphere(mb, head + Vector((1.2 * sx, 0.8, -3.32)), 0.45, lambda n: COAL, seg=8, rings=6)
    for i in range(5):
        a = math.radians(-50 + 25 * i)
        usphere(mb, head + Vector((math.sin(a) * 1.6, -0.9 - math.cos(a) * 0.45, -3.4 + abs(math.sin(a)) * 0.35)), 0.28, lambda n: COAL, seg=6, rings=5)
    # にんじんの鼻（前の -z。つけ根は頭の中）
    utube(mb, [head + Vector((0, 0.1, -2.7)), head + Vector((0, 0.0, -4.6)), head + Vector((0, -0.25, -6.6))], [0.7, 0.45, 0.0], 12,
          lambda t, a, p, d: mixc(CARROT, hexc("#c86418"), 0.4 * sstep(0.7, 1.0, abs(math.sin(t * 18.0)))))
    # ボタン（炭）
    for i in range(3):
        y = mid.y + 2.0 - 2.0 * i
        z = -math.sqrt(max(0.0, mr * mr - (y - mid.y) ** 2)) + 0.1
        usphere(mb, Vector((0, y, z)), 0.5, lambda n: COAL, seg=8, rings=6)
    # 赤いマフラー（首の輪と、前にたれるはし）
    torus(mb, (0, mid.y + mr - 0.55, 0), (0, 1, 0), 3.05, 0.95,
          lambda p, d, u: RED if (int(u * 24) % 4) else WHITE, nu=36, nv=10)
    beam(mb, Vector((1.6, mid.y + mr - 0.6, -2.6)), Vector((2.6, mid.y + 0.6, -mr + 0.25)), 1.7, 0.45,
         lambda n: RED, up=(0.0, 0.0, -1.0))
    # 小枝のうで（つけ根は胴の中）
    for sx in (-1, 1):
        a0 = mid + Vector((3.6 * sx, 0.6, 0))
        tip = mid + Vector((10.0 * sx, 4.2, 0.6))
        utube(mb, [a0, a0.lerp(tip, 0.5) + Vector((0, 0.3, 0)), tip], [0.45, 0.3, 0.0], 8, lambda t, a, p, d: WOOD_DARK)
        f0 = a0.lerp(tip, 0.65)
        utube(mb, [f0 - Vector((0.3 * sx, 0, 0)), f0 + Vector((1.2 * sx, 1.6, -0.2))], [0.2, 0.0], 6, lambda t, a, p, d: WOOD_DARK)
    return build(mb, "Xmas_Snowman", sharp_deg=60)


def snowman_col_profile():
    """雪だるまの当たり判定の輪郭 [(半径, 高さ)]（下から上へ）。見た目（土台の雪の山・3 つの雪玉・バケツのぼうし）を、すべてつつむ。
    ある高さの半径は、それより上のどの高さの見た目の半径より小さくならない（下から上へ、太くならない：ひさしがない）。
    てっぺんは、ぼうしの上の平らな面と同じ高さ（そこに、しずくとコマドリがのる）"""
    head, hr = SNOWMAN_BALLS[2]
    hat_y = head.y + hr * 0.62
    hat = [(0.0, 0.0), (2.9, 0.0), (3.0, 0.25), (2.35, 3.8), (0.0, 3.85)]
    mound = [(9.0, -0.6), (8.0, 0.4), (6.4, 2.0), (0.0, 2.4)]

    def poly_r(pts, y):
        for (r0, y0), (r1, y1) in zip(pts, pts[1:]):
            if y0 <= y <= y1 and y1 > y0:
                return r0 + (r1 - r0) * (y - y0) / (y1 - y0)
        return 0.0

    def visual_r(y):
        r = poly_r(mound, y)
        for c, rr in SNOWMAN_BALLS:
            d = y - c.y
            if abs(d) < rr:
                r = max(r, math.sqrt(rr * rr - d * d))
        if hat_y <= y <= hat_y + 3.8:
            r = max(r, poly_r([(2.9, hat_y), (3.0, hat_y + 0.25), (2.35, hat_y + 3.8)], y) + 0.35)   # ぼうしは少し横にずれている
        return r
    top = hat_y + 3.85
    ys = [-1.6 + 0.25 * i for i in range(int((top - 0.1 + 1.6) / 0.25) + 1)] + [top - 0.05]
    rs = [visual_r(y) for y in ys]
    for i in range(len(rs) - 2, -1, -1):   # 上から下へ、それより上の、いちばん大きな半径
        rs[i] = max(rs[i], rs[i + 1])
    rs[0] = max(rs[0], 9.3)   # 根もとは、雪の中へ広く（下の雪の山のすそ）
    prof = [(0.0, -1.6)] + [(r + 0.05, y) for r, y in zip(rs, ys)] + [(0.0, top)]
    # 同じ半径がつづく所は、まとめる（縦のかべは 1 まい）
    out = [prof[0]]
    for i in range(1, len(prof) - 1):
        if abs(prof[i][0] - out[-1][0]) < 1e-3 and abs(prof[i][0] - prof[i + 1][0]) < 1e-3:
            continue
        out.append(prof[i])
    out.append(prof[-1])
    return out


def make_snowman_col():
    """雪だるまの当たり判定（見えない）：ひさしのない、1 まいのとじた形。下の雪玉のふくらみの下にもぐりこんだり、玉と玉の重なりの、
    中の面の上に乗ったりしない（見た目の形のままでは、土台の雪の山から登れず、雪だるまの中へ入ってしまった）"""
    mb = MB()
    ulathe(mb, (0, 0, 0), snowman_col_profile(), 32, lambda t, a, p: SNOW)
    return build(mb, "Xmas_Snowman_Col", sharp_deg=40)   # 縦のかべの下のかどは、かどのまま（ひさしに見えない）


# ---------------------------------------------------------------------------
# あかりのともる家（村のはしの、丸太の家の前の面）
# ---------------------------------------------------------------------------
HOUSE_W = 64.0
HOUSE_FRONT = -15.0      # 前の面（南）の z
HOUSE_EAVE = 40.0
SILL_Y = 14.0
WINDOW_X = -12.0
DOOR_X = 16.0


def eave_wire_y(x):
    """のきの電球のコードの高さ（4 つの電球ごとに、のきにとめて、たるむ）"""
    u = ((x + HOUSE_W / 2) / (HOUSE_W / 22.0) / 4.0) % 1.0
    return HOUSE_EAVE - 0.9 - 1.4 * math.sin(u * math.pi)


def make_cottage(look=True):
    """丸太の家（前の面のはばは 1.6m）：横に積んだ丸太のかべ・石の土台・白いわくの窓と、登れる窓台・赤いとびらとリース・
    雪の積もった屋根と、つらら・えんとつ。うしろ半分は、村の外（見えない）。
    look=False は当たり判定（_Col）：屋根の雪の、でこぼこのない、前の形"""
    mb = MB()
    hw = HOUSE_W / 2
    # 家の体（箱）と石の土台。見た目では、横のかべにも、丸太のすじ（形は同じ箱）

    def wall_col(p, n):
        c = mixc(LOG_DARK, LOG, 0.5)
        if look and abs(n.x) > 0.5:
            f = (p.y - 3.4) / 3.3
            band = abs(f - round(f))
            c = mixc(mixc(LOG, WOOD_LIGHT, 0.3), LOG_DARK, sstep(0.3, 0.5, band))
            c = scalec(c, 0.92 + 0.12 * noise.noise(Vector((p.z * 0.3, p.y * 2.0, 1.0))))
        return c
    gbox(mb, (0, HOUSE_EAVE / 2, 0), (HOUSE_W, HOUSE_EAVE, 30.0), wall_col, n=24 if look else 4)
    gbox(mb, (0, 0.5, 0), (HOUSE_W + 2.0, 4.0, 32.0), lambda p, n: mixc(STONE, scalec(STONE, 1.15), 0.5 + 0.5 * noise.noise(p * 0.4)), n=8)
    # 前の面の、横に積んだ丸太（半分は、かべの中）
    wx0, wx1, wy0, wy1 = WINDOW_X - 8.0, WINDOW_X + 8.0, SILL_Y + 0.5, SILL_Y + 15.5
    dx0, dx1 = DOOR_X - 7.0, DOOR_X + 7.0
    y = 3.4
    k = 0
    while y < HOUSE_EAVE - 1.0:
        cuts = []
        if wy0 - 2.2 < y < wy1 + 1.0:
            cuts.append((wx0 + 0.2, wx1 - 0.2))
        if y < 29.5 + 1.0:
            cuts.append((dx0 + 0.3, dx1 - 0.3))
        xs = [-hw - 1.2]
        for a_, b_ in sorted(cuts):
            xs += [a_, b_]
        xs.append(hw + 1.2)
        for i in range(0, len(xs), 2):
            utube(mb, [Vector((xs[i], y, HOUSE_FRONT + 0.6)), Vector((xs[i + 1], y, HOUSE_FRONT + 0.6))], [1.75, 1.75], 14,
                  lambda t, a, p, d, k=k: mixc(LOG, WOOD_LIGHT, 0.25 + 0.2 * math.sin(t * 40.0 + k)) if abs(d.y) < 0.85 else LOG_DARK)
        # 丸太の切り口（はしと、窓・とびらでとぎれた所。年輪の色）
        for x in xs:
            usphere(mb, Vector((x, y, HOUSE_FRONT + 0.6)), 1.75, lambda n: WOOD_LIGHT, scale=(0.25, 1.0, 1.0), seg=12, rings=8)
        y += 3.3
        k += 1
    # 窓：白いわくと十字の桟。窓台（ここまで登れる）
    fz = HOUSE_FRONT - 1.4
    for (a, b) in (((wx0, wy0), (wx0, wy1)), ((wx1, wy0), (wx1, wy1)), ((wx0, wy1), (wx1, wy1)),
                   (((wx0 + wx1) / 2, wy0), ((wx0 + wx1) / 2, wy1)), ((wx0, (wy0 + wy1) / 2), (wx1, (wy0 + wy1) / 2))):
        beam(mb, Vector((a[0], a[1], fz)), Vector((b[0], b[1], fz)), 1.0, 2.2, lambda n: WHITE, up=(0.0, 0.0, -1.0))
    gbox(mb, (WINDOW_X, SILL_Y - 0.5, HOUSE_FRONT - 2.0), (19.5, 1.2, 4.2), lambda p, n: WHITE if n.y < 0.5 else mixc(WHITE, SNOW, 0.5), n=4)
    usphere(mb, Vector((WINDOW_X, SILL_Y + 0.05, HOUSE_FRONT - 2.4)), 1.0, lambda n: snow_col(bn(n)), scale=(8.5, 0.6, 1.4), seg=20, rings=8)
    # とびら：赤いいたと、白いわく・ドアノブ・ふみ石
    gbox(mb, (DOOR_X, 15.5, HOUSE_FRONT - 1.0), (14.0, 27.0, 1.2),
         lambda p, n: mixc(RED_DARK, RED, 0.7) if abs((p.x - DOOR_X) % 3.5 - 1.75) > 0.15 else RED_DARK, n=8)
    for (a, b) in (((dx0, 2.0), (dx0, 29.5)), ((dx1, 2.0), (dx1, 29.5)), ((dx0, 29.5), (dx1, 29.5))):
        beam(mb, Vector((a[0], a[1], HOUSE_FRONT - 1.4)), Vector((b[0], b[1], HOUSE_FRONT - 1.4)), 1.2, 1.4, lambda n: WHITE, up=(0.0, 0.0, -1.0))
    usphere(mb, Vector((DOOR_X - 4.8, 14.0, HOUSE_FRONT - 1.9)), 0.6, lambda n: shiny(GOLD, bn(n)), seg=8, rings=6)
    gbox(mb, (DOOR_X, 1.0, HOUSE_FRONT - 3.0), (16.0, 2.6, 5.0), lambda p, n: mixc(STONE, SNOW, 0.6 if n.y > 0.5 else 0.0), n=4)
    # リース（とびらの上の方。緑の輪に、赤いリボンと実）
    wc = Vector((DOOR_X, 22.5, HOUSE_FRONT - 2.2))
    torus(mb, wc, (0, 0, 1), 3.0, 1.0, lambda p, d, u: mixc(FIR, FIR_LIGHT, 0.5 + 0.5 * math.sin(u * 60.0)), nu=36, nv=10)
    for i in range(6):
        a = i / 6 * math.tau + 0.3
        usphere(mb, wc + Vector((math.cos(a) * 3.0, math.sin(a) * 3.0, -0.75)), 0.4, lambda n: shiny(RED, bn(n)), seg=6, rings=5)
    usphere(mb, wc + Vector((0, -3.0, -0.7)), 0.7, lambda n: RED, scale=(1.0, 0.8, 0.8), seg=8, rings=6)
    for sx in (-1, 1):
        utube(mb, [wc + Vector((0, -3.0, -0.8)), wc + Vector((1.3 * sx, -2.3, -1.2)), wc + Vector((2.0 * sx, -3.2, -1.0)), wc + Vector((0.2 * sx, -3.1, -0.8))],
              [0.0, 0.32, 0.32, 0.0], 8, lambda t, a, p, d: RED)
    # 屋根：前へ 6 出た、のき。雪が厚く積もり、のきの下に、つらら
    eave_z = HOUSE_FRONT - 6.0
    ridge_y = 58.0
    prism(mb, [Vector((-hw - 3, HOUSE_EAVE - 1.0, eave_z)), Vector((-hw - 3, ridge_y, 0.0)), Vector((-hw - 3, HOUSE_EAVE - 1.0, -eave_z))],
          (HOUSE_W + 6, 0, 0), lambda n: WOOD_DARK if n.y < 0.2 else hexc("#7a3a2e"))
    for ez in (eave_z, -eave_z):
        e = Vector((-hw - 3.5, HOUSE_EAVE - 1.0, ez))
        r = Vector((-hw - 3.5, ridge_y, 0.0))
        d = (r - e).normalized()
        up = Vector((0.0, d.z, -d.y))
        if up.y < 0:
            up = -up
        if not look:
            prism(mb, [e - up * 0.3, r - up * 0.3, r + up * 1.8, e + up * 1.8], (HOUSE_W + 7, 0, 0), lambda n: snow_col(n))
        else:
            snow_sheet(mb, e, r, up, HOUSE_W + 7, 1.8, seed=3 if ez < 0 else 7)
        usphere(mb, Vector((0.0, e.y, e.z)) + up * 0.75, 1.0, lambda n: snow_col(bn(n)), scale=(hw + 3.5, 1.15, 1.15), seg=28, rings=8)
    usphere(mb, Vector((0, ridge_y + 1.2, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(hw + 3.5, 1.3, 2.0), seg=28, rings=8)
    if look:
        # のきのふちの、波うつ雪のふち
        lr = random.Random(41)
        for ez in (eave_z, -eave_z):
            e = Vector((0.0, HOUSE_EAVE - 1.0, ez))
            r = Vector((0.0, ridge_y, 0.0))
            d = (r - e).normalized()
            up = Vector((0.0, d.z, -d.y))
            if up.y < 0:
                up = -up
            for i in range(22):
                x = lerp(-hw - 3.0, hw + 3.0, i / 21)
                c = Vector((x, e.y, e.z)) + up * 0.6 - d * 0.4
                rr = lr.uniform(1.2, 1.7)
                usphere(mb, c, 1.0, lambda n: snow_col(bn(n)), scale=(2.2, rr, rr), seg=12, rings=7)
        # 切り妻のはしの、雪のふち
        for sx in (-1, 1):
            for s in (0.15, 0.4, 0.65, 0.9):
                for ez in (eave_z, -eave_z):
                    e = Vector((sx * (hw + 3.3), HOUSE_EAVE - 1.0, ez))
                    r = Vector((sx * (hw + 3.3), ridge_y, 0.0))
                    usphere(mb, e.lerp(r, s) + Vector((0, 1.0, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(1.3, 1.3, 2.6), seg=10, rings=6)
    rnd = random.Random(12)
    x = -hw - 1.5
    while x < hw + 1.5:
        L = rnd.uniform(1.5, 4.2)
        top = Vector((x, HOUSE_EAVE - 0.4, eave_z + 0.8))
        utube(mb, [top + Vector((0, 0.8, 0.3)), top, top + Vector((0, -L, 0))], [0.35, 0.32, 0.0], 6,
              lambda t, a, p, d: mixc(hexc("#dfeaf4"), ICE, 0.4 * t))
        x += rnd.uniform(2.4, 4.0)
    # のきの電球のコード（のきの下に、ところどころでとめて、たるむ。はしは屋根の中）
    wire = []
    for i in range(89):
        x = -hw + i * HOUSE_W / 88
        wire.append(Vector((x, eave_wire_y(x), HOUSE_FRONT - 5.2)))
    wire[0] = Vector((-hw, HOUSE_EAVE - 0.4, HOUSE_FRONT - 5.2))
    wire[-1] = Vector((hw, HOUSE_EAVE - 0.4, HOUSE_FRONT - 5.2))
    utube(mb, wire, [0.11] * len(wire), 5, lambda t, a, p, d: hexc("#2a3a2a"))
    # えんとつ（屋根の上。雪のぼうし）
    gbox(mb, (hw - 14.0, ridge_y - 2.0, -4.0), (6.0, 16.0, 6.0), lambda p, n: mixc(hexc("#9a4a3a"), hexc("#b8604a"), 0.5 + 0.5 * math.sin(p.y * 1.6)), n=6)
    usphere(mb, Vector((hw - 14.0, ridge_y + 6.2, -4.0)), 1.0, lambda n: snow_col(bn(n)), scale=(3.6, 1.0, 3.6), seg=14, rings=8)
    return build(mb, "Xmas_Cottage" + ("" if look else "_Col"), sharp_deg=50)


def snow_sheet(mb, e, r, up, length, thick, seed=0):
    """屋根の雪（見た目）：のき e から、むね r まで、x へ length。上の面は、ふきよせた雪のように、やわらかくうねる。
    下の面と、まわりの面もはる（とじた形）"""
    nx, nv = 48, 12
    off = Vector((seed * 2.1, seed * 0.7, 0.0))
    top, bot = [], []
    for i in range(nx + 1):
        x = e.x + length * i / nx
        tr, br = [], []
        for j in range(nv + 1):
            v = j / nv
            base = Vector((x, 0, 0)) + Vector((0, e.y, e.z)).lerp(Vector((0, r.y, r.z)), v)
            edge = min(i, nx - i, j, nv - j)
            hump = 0.55 * noise.noise(Vector((x * 0.09, v * 2.2, 0)) + off) + 0.25 * noise.noise(Vector((x * 0.3, v * 6.0, 1.0)) + off)
            h = thick + max(-0.5, hump) * min(1.0, edge / 2.0)
            p_top = base + up * h
            n_top = up.copy()
            tr.append(mb.v(UV(p_top), snow_col(n_top) if hump < 0.25 else mixc(snow_col(n_top), hexc("#ffffff"), 0.4)))
            br.append(mb.v(UV(base - up * 0.3), SNOW_SHADE))
        top.append(tr)
        bot.append(br)
    for i in range(nx):
        for j in range(nv):
            q = [top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]]
            a0, a1, a2 = mb.V[q[0]], mb.V[q[1]], mb.V[q[2]]
            if (a1 - a0).cross(a2 - a0).dot(UV(up) - UV(Vector((0, 0, 0)))) < 0:
                q.reverse()
            mb.f(*q)
            mb.f(*reversed([bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1]]) if (mb.V[bot[i + 1][j]] - mb.V[bot[i][j]]).cross(
                mb.V[bot[i + 1][j + 1]] - mb.V[bot[i][j]]).dot(UV(up) - UV(Vector((0, 0, 0)))) > 0 else
                [bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1]])
    # まわりの面（はしの、あつみ）
    ring = [(i, 0) for i in range(nx)] + [(nx, j) for j in range(nv)] + [(i, nv) for i in range(nx, 0, -1)] + [(0, j) for j in range(nv, 0, -1)]
    center = UV(Vector((e.x + length / 2, 0, 0)) + Vector((0, e.y, e.z)).lerp(Vector((0, r.y, r.z)), 0.5))
    for k in range(len(ring)):
        a, b = ring[k], ring[(k + 1) % len(ring)]
        q = [bot[a[0]][a[1]], bot[b[0]][b[1]], top[b[0]][b[1]], top[a[0]][a[1]]]
        a0, a1, a2 = mb.V[q[0]], mb.V[q[1]], mb.V[q[2]]
        fc = (a0 + a1 + a2) / 3
        if (a1 - a0).cross(a2 - a0).dot(fc - center) < 0:
            q.reverse()
        mb.f(*q)


def make_cottage_window():
    """家の窓のあかり（光る材質）：窓ガラスと、のきの下の電球"""
    mb = MB()
    # 窓のあかり：下は琥珀色、上ほど明るく（部屋の中の、だんろの火の色）
    gbox(mb, (WINDOW_X, SILL_Y + 8.0, HOUSE_FRONT - 0.4), (15.6, 14.6, 0.6),
         lambda p, n: mixc(AMBER, WARM, 0.25 + 0.5 * sstep(0.0, 1.0, (p.y - SILL_Y) / 15.0)), n=8)
    gbox(mb, (DOOR_X, 27.0, HOUSE_FRONT - 1.75), (10.0, 2.2, 0.3), lambda p, n: AMBER, n=2)   # とびらの上の、小さなあかりまど
    hw = HOUSE_W / 2
    for i in range(22):
        x = -hw + (i + 0.5) * HOUSE_W / 22
        usphere(mb, Vector((x, eave_wire_y(x) - 0.45, HOUSE_FRONT - 5.2)), 0.42, lambda n, i=i: BULBS[i % len(BULBS)], seg=8, rings=5)
    return build(mb, "Xmas_CottageWindow")


# ---------------------------------------------------------------------------
# 村の小物
# ---------------------------------------------------------------------------
def make_mailbox():
    """サンタへのポスト：木のはしらの上の、赤いポスト（かまぼこ形の屋根・手紙を入れる口・金の星・横の旗）と、雪のぼうし"""
    mb = MB()
    ubox(mb, (0, 6.0, 0), (1.8, 14.0, 1.8), lambda n: mixc(WOOD_DARK, WOOD, 0.5))
    gbox(mb, (0, 15.0, 0), (8.0, 4.5, 6.0), lambda p, n: mixc(RED_DARK, RED, 0.7 + 0.3 * max(0.0, n.y)), n=6)
    poly = [Vector((-4.0, 17.0, 3.0 * math.cos(math.pi * i / 10))) + Vector((0, 3.0 * math.sin(math.pi * i / 10), 0)) for i in range(11)]
    prism(mb, poly, (8.0, 0, 0), lambda n: mixc(RED_DARK, RED, 0.6 + 0.4 * max(0.0, n.y)))
    ubox(mb, (0, 15.8, -3.05), (4.6, 0.6, 0.3), lambda n: COAL)                     # 手紙を入れる口
    star = [Vector((0, 13.6, -2.95)) + Vector((math.cos(math.pi / 2 + i * math.pi / 5) * (1.1 if i % 2 == 0 else 0.45),
                                               math.sin(math.pi / 2 + i * math.pi / 5) * (1.1 if i % 2 == 0 else 0.45), 0)) for i in range(10)]
    for i in range(10):
        prism(mb, [star[i], star[(i + 1) % 10], Vector((0, 13.6, -2.95))], (0, 0, -0.32), lambda n: GOLD)
    beam(mb, Vector((3.8, 15.5, 1.0)), Vector((4.8, 15.5, 1.0)), 0.5, 0.5, lambda n: IRON)
    ubox(mb, (4.75, 17.2, 1.0), (0.3, 3.6, 0.4), lambda n: IRON)                    # 旗のさお
    ubox(mb, (4.75, 18.3, -0.3), (0.25, 1.6, 2.6), lambda n: hexc("#e8c040"))       # 旗
    usphere(mb, Vector((0, 20.1, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(4.0, 0.9, 2.8), seg=16, rings=8)
    return build(mb, "Xmas_Mailbox", sharp_deg=50)


SLED_DECK = (-12.0, 8.4)    # すのこの、うしろと前のはし（z）
SLED_DECK_TOP = 4.25        # すのこの上の面の高さ


def make_sled():
    """木のそり：赤い 2 本のすべり木（前がくるりと上へまがる）・木のすのこ・ひも。
    すのこの板は、すき間なくならぶ（しゃくとりむしが板のあいだに落ちない）。板のさかいめは、色のこい線だけ"""
    mb = MB()
    for sx in (-1, 1):
        x = 5.0 * sx
        pts = [Vector((x, 0.5, -12.5)), Vector((x, 0.5, -10.0)), Vector((x, 0.5, 6.0)), Vector((x, 1.2, 10.5)), Vector((x, 3.6, 12.5)),
               Vector((x, 5.4, 11.0)), Vector((x, 4.6, 9.4))]
        utube(mb, pts, [0.0, 0.5, 0.5, 0.5, 0.5, 0.45, 0.0], 10, lambda t, a, p, d: mixc(RED_DARK, RED, 0.7))
        for z in (-8.0, 0.0, 7.0):
            beam(mb, Vector((x, 0.6, z)), Vector((x, 3.4, z)), 0.8, 0.8, lambda n: mixc(RED_DARK, RED, 0.5))
        # すのこをささえる、たての木（すのこの下にくっつく）
        beam(mb, Vector((x, 3.3, -11.2)), Vector((x, 3.3, 8.6)), 0.9, 0.6, lambda n: mixc(RED_DARK, RED, 0.6))
    for i in range(7):
        z = -10.5 + i * 2.9

        def plank(p, n, z=z, i=i):
            c = mixc(WOOD, WOOD_LIGHT, 0.5 + 0.3 * math.sin(p.x * 2.0 + i * 1.7))
            return mixc(c, WOOD_DARK, 0.75 * sstep(1.1, 1.47, abs(p.z - z)))   # 板のさかいめ
        gbox(mb, (0, SLED_DECK_TOP - 0.35, z), (11.0, 0.7, 2.94), plank, n=6)   # はばは、すべり木の外がわまで（横から見て、ひさしにならない）
    utube(mb, [Vector((-4.8, 3.9, 7.0)), Vector((-4.8, 4.1, 8.0)), Vector((-3.0, 2.0, 14.0)), Vector((0.0, 1.0, 16.5)), Vector((3.0, 2.0, 14.0)),
               Vector((4.8, 4.1, 8.0)), Vector((4.8, 3.9, 7.0))],
          [0.0, 0.28, 0.25, 0.25, 0.25, 0.28, 0.0], 6, lambda t, a, p, d: hexc("#d8c8a0"))
    return build(mb, "Xmas_Sled", sharp_deg=40)


MITTEN_CUFF = Vector((0.0, 1.9, 4.2))     # 手ぶくろの口（ヤマネがねむる所）


def make_mitten():
    """赤い毛糸の手ぶくろ（片手。雪の上に落ちている）：ふっくらした手と親指・白いゴムあみの口（中は暗い）・雪の結晶のもよう"""
    mb = MB()

    def knit(nb):
        n = bn(nb)
        c = mixc(RED_DARK, RED, 0.7 + 0.3 * max(0.0, n.y))
        return c
    usphere(mb, Vector((0, 1.9, -0.8)), 1.0, knit, scale=(3.4, 1.95, 5.2), seg=24, rings=14)
    utube(mb, [Vector((1.8, 1.7, 0.8)), Vector((3.3, 1.65, -0.6)), Vector((3.8, 1.55, -2.4)), Vector((3.5, 1.5, -3.3))], [0.0, 1.05, 0.95, 0.0], 12,
          lambda t, a, p, d: mixc(RED_DARK, RED, 0.8))
    # 雪の結晶のもよう（白い点を、手のこうに。半分は手の中）
    def back_y(x, z):
        return 1.9 + 1.95 * math.sqrt(max(0.0, 1.0 - (x / 3.4) ** 2 - ((z + 0.8) / 5.2) ** 2)) - 0.12
    for (x, z) in ((0.0, -1.0), (-1.6, 0.6), (1.4, -2.6)):
        for i in range(6):
            a = i / 6 * math.tau
            px, pz = x + math.cos(a) * 0.55, z + math.sin(a) * 0.55
            usphere(mb, Vector((px, back_y(px, pz), pz)), 0.22, lambda n: WHITE, seg=6, rings=4)
        usphere(mb, Vector((x, back_y(x, z) + 0.05, z)), 0.26, lambda n: WHITE, seg=6, rings=4)
    # 白いゴムあみの口（輪）と、中の暗い所
    usphere(mb, Vector((0, 1.9, 2.3)), 1.0, knit, scale=(2.3, 1.75, 2.4), seg=20, rings=12)   # 手首
    torus(mb, MITTEN_CUFF, (0, 0, 1), 1.9, 0.65, lambda p, d, u: mixc(WHITE, hexc("#d8d4cc"), 0.5 + 0.5 * math.sin(u * 60.0)), nu=36, nv=10)
    usphere(mb, MITTEN_CUFF + Vector((0, 0, 0.1)), 1.0, lambda n: hexc("#3a2026"), scale=(1.65, 1.25, 0.45), seg=16, rings=8)
    return build(mb, "Xmas_Mitten")


LAMP_GLOW_C = Vector((0.0, 15.3, 0.0))


def make_lamp():
    """ガーデンランプ（高さ 50cm ほど）：黒い鉄のはしら・四すみの柱のランタン・とがった屋根と、てっぺんの玉"""
    mb = MB()
    ucyl(mb, (0, -2.0, 0), 0.42, 15.5, lambda t, a, p: IRON, seg=10)
    ucyl(mb, (0, -0.2, 0), 1.1, 1.0, lambda t, a, p: IRON, seg=12)
    ubox(mb, (0, 13.3, 0), (3.6, 0.5, 3.6), lambda n: IRON)
    for sx in (-1, 1):
        for sz in (-1, 1):
            ubox(mb, (1.55 * sx, 15.4, 1.55 * sz), (0.35, 4.2, 0.35), lambda n: IRON)
    prism(mb, [Vector((-2.2, 17.4, -2.2)), Vector((2.2, 17.4, -2.2)), Vector((2.2, 17.4, 2.2)), Vector((-2.2, 17.4, 2.2))], (0, 0.5, 0), lambda n: IRON)
    ulathe(mb, (0, 17.7, 0), [(0.0, 0.0), (2.0, 0.0), (0.3, 2.2), (0.0, 2.3)], 4, lambda t, a, p: IRON, phase=math.pi / 4)
    usphere(mb, Vector((0, 20.2, 0)), 0.45, lambda n: shiny(GOLD, bn(n)), seg=8, rings=6)
    usphere(mb, Vector((0, 17.95, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(2.1, 0.45, 2.1), seg=12, rings=6)
    return build(mb, "Xmas_Lamp", sharp_deg=50)


def make_lamp_glow():
    """ランプのガラスのあかり（光る材質）"""
    mb = MB()
    gbox(mb, tuple(LAMP_GLOW_C), (2.8, 3.8, 2.8), lambda p, n: mixc(AMBER, WARM, 0.4 + 0.3 * sstep(-1.9, 1.9, p.y - LAMP_GLOW_C.y)), n=4)
    return build(mb, "Xmas_LampGlow")


FENCE_HALF = 10.0
FENCE_WIRE_Z = -0.55


def make_fence():
    """木のさく（はば 50cm）：2 本の柱・2 本の横木・先のとがった 7 枚のいた。横木と、いたの先に雪。柱から柱へ、電球のコード"""
    mb = MB()
    wc = lambda n: mixc(WOOD, WOOD_LIGHT, 0.5)
    for sx in (-1, 1):
        ubox(mb, (sx * (FENCE_HALF - 0.7), 4.5, 0.25), (1.4, 13.0, 1.9), wc)
        usphere(mb, Vector((sx * (FENCE_HALF - 0.7), 11.05, 0.3)), 1.0, lambda n: snow_col(bn(n)), scale=(0.95, 0.4, 0.95), seg=10, rings=6)
    for y in (3.5, 8.0):
        ubox(mb, (0, y, 0.55), (FENCE_HALF * 2 - 0.4, 1.0, 0.7), wc)
        usphere(mb, Vector((0, y + 0.5, 0.55)), 1.0, lambda n: snow_col(bn(n)), scale=(FENCE_HALF - 1.2, 0.3, 0.42), seg=20, rings=6)
    for i in range(7):
        x = -FENCE_HALF + 2.6 + i * (2 * FENCE_HALF - 5.2) / 6
        ubox(mb, (x, 4.0, 0.0), (1.6, 10.0, 0.6), lambda n: mixc(WHITE, hexc("#e4ddd0"), 0.4))
        prism(mb, [Vector((x - 0.8, 9.0, -0.3)), Vector((x + 0.8, 9.0, -0.3)), Vector((x, 10.4, -0.3))], (0, 0, 0.6), lambda n: WHITE)
        usphere(mb, Vector((x, 10.2, 0.0)), 0.42, lambda n: snow_col(bn(n)), seg=8, rings=5)
    # 電球のコード（柱の頭から頭へ、たるむ。はしは柱の中）
    pts = []
    for i in range(13):
        u = i / 12
        x = lerp(-(FENCE_HALF - 0.7), FENCE_HALF - 0.7, u)
        pts.append(Vector((x, 10.3 - 1.6 * math.sin(math.pi * u), FENCE_WIRE_Z)))
    utube(mb, pts, [0.12] * 13, 5, lambda t, a, p, d: hexc("#2a3a2a"))
    return build(mb, "Xmas_Fence", sharp_deg=40)


def make_fence_lights():
    """さくの電球（光る材質）：コードにそって、いろいろな色"""
    mb = MB()
    for i in range(1, 12):
        u = i / 12
        x = lerp(-(FENCE_HALF - 0.7), FENCE_HALF - 0.7, u)
        y = 10.3 - 1.6 * math.sin(math.pi * u) - 0.35
        usphere(mb, Vector((x, y, FENCE_WIRE_Z)), 0.32, lambda n, i=i: BULBS[i % len(BULBS)], scale=(1.0, 1.35, 1.0), seg=8, rings=5)
    return build(mb, "Xmas_FenceLights")


def make_candy_cane():
    """キャンディケイン（赤と白のしま。雪にさしてある。高さ 25cm ほど）"""
    mb = MB()
    pts = [Vector((0, -2.0 + 10.2 * i / 20, 0)) for i in range(20)]
    for i in range(12):
        th = 1.15 * math.pi * i / 11
        pts.append(Vector((0, 8.2 + math.sin(th) * 1.6, 1.6 - math.cos(th) * 1.6)))

    def col(t, a, p, d):
        return RED if math.sin(t * 30.0 + a) > 0.15 else WHITE    # ななめに、ぐるぐるまく赤いしま
    utube(mb, pts, [0.6] * len(pts), 14, col)
    usphere(mb, pts[-1], 0.6, lambda n: WHITE, seg=10, rings=6)
    return build(mb, "Xmas_CandyCane")


def fir_tiers(mb, tiers, top, trunk_r, snow=0.75, seed=0, look=False):
    """雪のつもったモミ：段の円すい（上の面に雪）。look=True で、枝先のかさなりと、葉のすじ、雪のかたまり（見た目）"""
    ucyl(mb, (0, -2.0, 0), trunk_r, tiers[0][0] + 3.0, lambda t, a, p: TRUNK, seg=10)
    for k, (z0, r0) in enumerate(tiers):
        z1 = tiers[k + 1][0] if k + 1 < len(tiers) else top
        r1 = tiers[k + 1][1] * 0.6 if k + 1 < len(tiers) else 0.0
        prof = [(0.0, z0 - 0.2), (r0 * 0.5, z0 - 0.1), (r0, z0 + 0.3), (r0 - 0.5, z0 + 1.1)]
        prof += [(r1, z1 + 0.8), (0.0, z1 + 1.0)] if k + 1 < len(tiers) else [(0.0, z1 + 0.8)]

        def col(t, a, p, k=k):
            if t < 0.3:
                return FIR_DARK
            c = mixc(FIR, FIR_LIGHT, 0.3 * (0.5 + 0.5 * math.cos(a * 9)))
            nv = noise.noise(Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, k * 2.3 + seed)))
            return mixc(c, SNOW, snow * sstep(-0.3, 0.2, nv)) if t > 0.4 else c
        if not look:
            lathe(mb, prof, 28, col, center=UV((0, 0, 0)),
                  rfn=lambda t, a, k=k: 1.0 + (0.09 if 0.25 < t < 0.6 else 0.02) * (0.5 + 0.5 * math.cos(a * (9 + k))))
            continue

        def rfn2(t, a, k=k):
            jag = 0.11 if 0.25 < t < 0.6 else 0.025
            return 1.0 + jag * (0.6 * (0.5 + 0.5 * math.cos(a * (9 + k))) + 0.4 * (0.5 + 0.5 * math.cos(a * (27 + 2 * k) + 1.3)))

        def col2(t, a, p, k=k):
            c = col(t, a, p)
            if t >= 0.3 and c != SNOW:
                c = needle_tint(c, a, t, k)
            return c
        lathe(mb, finer(prof, 3), 72, col2, center=UV((0, 0, 0)), rfn=rfn2)



def make_fir(look=True):
    """雪のモミの木（高さ 85cm ほど）：5 段の枝に、雪がたっぷり。look=False は当たり判定（_Col：前の、なめらかな段）"""
    mb = MB()
    fir_tiers(mb, [(4.0, 9.0), (10.0, 7.4), (16.0, 5.8), (21.5, 4.2), (26.5, 2.8)], 34.0, 1.0, seed=3, look=look)
    return build(mb, "Xmas_Fir" + ("" if look else "_Col"), sharp_deg=60)


def make_holly():
    """ヒイラギ（高さ 15cm ほど）：とげとげの、つやのある葉と、赤い実"""
    rnd = random.Random(5)
    mb = MB()
    usphere(mb, Vector((0, 2.0, 0)), 1.0, lambda n: hexc("#16301c"), scale=(2.3, 2.0, 2.3), seg=12, rings=8)   # 中の、こいかげ
    # 葉：ふちに、とがったとげが並ぶ、波うつ、つやのある葉。表は、こい緑で、光るすじ
    def holly_r(th):
        base = 1.0 / math.sqrt((math.sin(th) / 0.5) ** 2 + (math.cos(th) / 1.0) ** 2)
        spike = max(0.0, math.cos(th * 8.0)) ** 6 * sstep(0.95, 0.75, abs(math.cos(th)))
        return base * (0.86 + 0.28 * spike)
    lr = random.Random(55)
    for i in range(72):
        z = lr.uniform(-0.75, 0.85)
        a = lr.uniform(0, math.tau)
        out_u = Vector((math.cos(a) * math.sqrt(1 - z * z), z, math.sin(a) * math.sqrt(1 - z * z)))
        c = Vector((0, 2.0, 0)) + Vector((out_u.x * 2.3, out_u.y * 2.0, out_u.z * 2.3)) * lr.uniform(0.86, 1.0)   # 葉の付け根は、しげみの中
        tang = out_u.cross(Vector((0, 1, 0)))
        tang = tang.normalized() if tang.length > 1e-3 else Vector((1, 0, 0))
        tw = lr.uniform(0, math.tau)
        tang = (tang * math.cos(tw) + out_u.cross(tang) * math.sin(tw)).normalized()
        dvec = (tang * 0.85 + out_u * 0.35).normalized()         # 葉の先の向き（Unity）
        nvec = out_u - dvec * out_u.dot(dvec)
        nvec = nvec.normalized()
        L = lr.uniform(0.9, 1.25)
        # うすい、とじた葉（平たい玉を、とげのあるふちの形に広げる）
        leaf = MB()
        uv_sphere(leaf, Vector((0, 0, 0)), 1.0, lambda n: FIR_DARK, seg=36, rings=6)
        for vi, v in enumerate(leaf.V):
            rxy = math.hypot(v.x, v.y)
            th = math.atan2(v.x, v.y)
            k = holly_r(th) * L
            px, py = (v.x / rxy * rxy * k, v.y / rxy * rxy * k) if rxy > 1e-6 else (0.0, 0.0)
            rho = rxy
            pz = v.z * 0.06 + 0.12 * L * math.sin(th * 4.0) * rho + 0.1 * L * rho * rho
            leaf.V[vi] = Vector((px, py, pz))
            midrib = abs(math.sin(th)) < 0.07 and rho > 0.1
            leaf.C[vi] = hexc("#7aa86a") if midrib and v.z > 0 else mixc(hexc("#1e4a26"), hexc("#3f7a3e"), 0.3 + 0.5 * rho) if v.z > 0                 else hexc("#2e5a30")
        bx, by, bz = UV(Vector((0, 0, 0))), UV(dvec), UV(nvec)
        side_b = by.cross(bz).normalized()
        m = Matrix((
            (side_b.x, by.x, bz.x, 0.0),
            (side_b.y, by.y, bz.y, 0.0),
            (side_b.z, by.z, bz.z, 0.0),
            (0.0, 0.0, 0.0, 1.0)))
        mb.add(leaf, Matrix.Translation(UV(c + dvec * L * 0.45)) @ m)
    # 赤い実（3 つずつ。しげみの面に、半分うまる）
    for (a0, y0) in ((-0.7, 3.6), (2.8, 3.1), (1.35, 3.8)):
        for k in range(3):
            a = a0 + (k - 1) * 0.2
            y = y0 + 0.25 * (k % 2)
            r = 3.0 * math.sqrt(max(0.0, 1.0 - ((y - 2.0) / 2.4) ** 2)) + 0.25
            usphere(mb, Vector((math.cos(a) * r, y, math.sin(a) * r)), 0.42, lambda n: shiny(RED, bn(n)), seg=8, rings=5)
            usphere(mb, Vector((math.cos(a) * (r + 0.4), y + 0.05, math.sin(a) * (r + 0.4))), 0.08, lambda n: hexc("#2a1a14"), seg=5, rings=3)   # 実の先の、黒い点
    return build(mb, "Xmas_Holly")


HOLLY_COL = [(0.0, -1.6), (3.7, -1.6), (3.7, 0.15), (3.3, 1.7), (2.5, 3.3), (1.3, 4.4), (0.0, 4.7)]   # ヒイラギの当たり判定のドーム（半径, 高さ。下は、地面の深くまで）


def make_holly_col():
    """ヒイラギの当たり判定（見えない）：しげみをつつむ、なめらかなドーム。とげとげの葉のすき間や、下のくびれにはさまらず、登ってこえられる"""
    mb = MB()
    ulathe(mb, (0, 0, 0), HOLLY_COL, 20, lambda t, a, p: FIR_DARK)
    return build(mb, "Xmas_Holly_Col", sharp_deg=40)   # 下のかどは、かどのまま（ひさしに見えない）


def make_snow_drift(look=True):
    """雪の吹きだまり（なだらかな山。下は雪の中）。見た目は、風下にせり出す雪のへり（雪庇）と、風のつけた、さざなみのすじ。
    look=False は当たり判定（_Col：前の、2 つのまるい山）"""
    mb = MB()
    if not look:
        usphere(mb, Vector((0, -1.2, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(7.5, 3.4, 5.2), seg=24, rings=12)
        usphere(mb, Vector((3.2, -0.8, 1.6)), 1.0, lambda n: snow_col(bn(n)), scale=(4.0, 2.4, 3.2), seg=18, rings=10)
        return build(mb, "Xmas_SnowDrift_Col")

    def drift_col(n, pb):
        c = snow_col(n)
        ripple = 0.5 + 0.5 * math.sin(pb.x * 2.4 + pb.y * 1.1 + 0.8 * noise.noise(pb * 0.4))
        c = mixc(c, SNOW_SHADE, 0.3 * sstep(0.7, 1.0, ripple) * sstep(0.2, 0.7, n.y))   # 風の、さざなみ
        return mixc(c, hexc("#ffffff"), 0.4 * sstep(0.6, 0.75, noise.noise(pb * 3.0)))   # きらきらする、こまかな雪
    for c, sc, seg, rings in ((Vector((0, -1.2, 0)), (7.5, 3.4, 5.2), 40, 18), (Vector((3.2, -0.8, 1.6)), (4.0, 2.4, 3.2), 28, 14),
                              (Vector((1.8, -0.6, 0.9)), (4.6, 2.3, 3.4), 28, 12)):   # 3 つめは、2 つの山の、つなぎ
        n0 = len(mb.V)
        usphere(mb, c, 1.0, lambda n: SNOW, scale=sc, seg=seg, rings=rings)
        cb = UV(c)
        sb = Vector((sc[0], sc[2], sc[1]))
        for i in range(n0, len(mb.V)):
            q = mb.V[i] - cb
            nb = Vector((q.x / sb.x ** 2, q.y / sb.y ** 2, q.z / sb.z ** 2)).normalized()
            mb.C[i] = drift_col(bn(nb), mb.V[i])
            # 風下（Unity の -z = Blender の +y）の上の方は、少し外へせり出す（雪庇）
            if q.y > 0:
                h = sstep(0.25, 0.85, (q.z + sb.z) / (2 * sb.z))
                mb.V[i] = mb.V[i] + Vector((0, 0.7 * h * (q.y / sb.y), 0.12 * h))
    return build(mb, "Xmas_SnowDrift")


ICE_RX, ICE_RZ = 16.0, 11.0


def make_ice(look=True):
    """こおった池の氷（だ円。上はつるつるで、白いひびと、ふちの雪）。水ではないので、歩ける。
    look=False は当たり判定（_Col：前の、あらい形）"""
    mb = MB()
    if look:
        return make_ice_look()

    def col(t, a, p):
        n = noise.noise(Vector((p.x * 0.12, p.y * 0.12, 3.0)))
        c = mixc(ICE_DEEP, ICE, 0.5 + 0.5 * n)
        crack = abs(noise.noise(Vector((p.x * 0.35, p.y * 0.35, 7.0))))
        if crack < 0.04:
            c = mixc(c, hexc("#e6f2fa"), 0.7)
        if t > 0.55:
            c = mixc(c, SNOW, sstep(0.94, 1.0, math.hypot(p.x / ICE_RX, p.y / ICE_RZ)))
        return c
    prof = [(0.0, -0.8), (1.0, -0.8), (1.0, -0.05)] + [(1.0 - 0.1 * i, 0.0) for i in range(1, 10)] + [(0.0, 0.0)]
    lathe(mb, prof, 48, lambda t, a, p: hexc("#8fb4cc"), center=Vector((0, 0, 0)))
    mb.xform(Matrix.Diagonal((ICE_RX, ICE_RZ, 1.0, 1.0)))
    mb.recolor(lambda p, c: col(1.0, 0.0, p) if p.z > -0.01 else c)
    return build(mb, "Xmas_Ice_Col", sharp_deg=60)


def make_ice_look():
    """氷の見た目：上の面は細かく分けて、深さの色のむらと、白い泡。ひびは、上の面にそった細い白い帯（中心から枝分かれ）"""
    mb = MB()

    def col(p):
        n = noise.noise(Vector((p.x * 0.12, p.y * 0.12, 3.0)))
        c = mixc(ICE_DEEP, ICE, 0.5 + 0.5 * n)
        c = mixc(c, hexc("#d8ecf8"), 0.35 * sstep(0.1, 0.5, noise.noise(Vector((p.x * 0.05, p.y * 0.05, 11.0)))))   # 白っぽくくもった所
        if noise.noise(Vector((p.x * 1.6, p.y * 1.6, 5.0))) > 0.55:
            c = mixc(c, hexc("#eef6fc"), 0.7)                                                                        # とじこめられた泡
        return mixc(c, SNOW, sstep(0.93, 1.0, math.hypot(p.x / ICE_RX, p.y / ICE_RZ)))
    prof = [(0.0, -0.8), (1.0, -0.8), (1.0, -0.05)] + [(1.0 - 0.025 * i, 0.0) for i in range(1, 40)] + [(0.0, 0.0)]
    lathe(mb, prof, 96, lambda t, a, p: hexc("#8fb4cc"), center=Vector((0, 0, 0)))
    mb.xform(Matrix.Diagonal((ICE_RX, ICE_RZ, 1.0, 1.0)))
    mb.recolor(lambda p, c: col(p) if p.z > -0.01 else c)
    # ひび：氷の上に、うすい白い帯（上の面から 0.01 だけ上）
    rnd = random.Random(23)
    for root in (Vector((-4.0, 2.0, 0)), Vector((6.0, -3.0, 0))):
        for k in range(7):
            a = k / 7 * math.tau + rnd.uniform(-0.3, 0.3)
            p = root.copy()
            d = Vector((math.cos(a), math.sin(a), 0))
            pts = [p.copy()]
            for s in range(rnd.randint(4, 7)):
                d = (d + Vector((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), 0))).normalized()
                p = p + d * rnd.uniform(1.2, 2.2)
                if (p.x / (ICE_RX * 0.9)) ** 2 + (p.y / (ICE_RZ * 0.9)) ** 2 > 1.0:
                    break
                pts.append(p.copy())
            if len(pts) < 2:
                continue
            ws = [lerp(0.16, 0.05, i / (len(pts) - 1)) for i in range(len(pts))]
            sides = []
            for i in range(len(pts)):
                dd = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
                sides.append(Vector((-dd.y, dd.x, 0)).normalized())
            ribbon(mb, [q + Vector((0, 0, 0.012)) for q in pts], ws, sides, lambda t, v, q: mixc(hexc("#f2f8fc"), hexc("#cfe4f2"), t),
                   normal_vecs=[Vector((0, 0, 1))] * len(pts))
    return build(mb, "Xmas_Ice", sharp_deg=60)


RING_C = Vector((0.0, 4.4, 0.0))
RING_R = 3.6


def make_warp_ring():
    """光の輪（エリアをつなぐワープ）：たてに立つ金の輪と、根もとの 2 つの雪の台"""
    mb = MB()
    torus(mb, RING_C, (0, 0, 1), RING_R, 0.42, lambda p, d, u: mixc(GOLD, GOLD_LIGHT, 0.5 + 0.5 * math.sin(u * 140.0)), nu=56, nv=10)
    for sx in (-1, 1):
        usphere(mb, Vector((1.3 * sx, 0.1, 0)), 1.0, lambda n: snow_col(bn(n)), scale=(1.8, 1.0, 1.3), seg=14, rings=8)
    return build(mb, "Xmas_WarpRing")


def make_warp_lights():
    """光の輪の電球（光る材質）：輪の外がわに、ぐるりと"""
    mb = MB()
    for i in range(16):
        a = (i + 0.5) / 16 * math.tau
        c = RING_C + Vector((math.cos(a) * (RING_R + 0.38), math.sin(a) * (RING_R + 0.38), 0))
        usphere(mb, c, 0.3, lambda n, i=i: BULBS[i % len(BULBS)], seg=8, rings=5)
    return build(mb, "Xmas_WarpLights")


MAKERS = [make_xmas_tree, make_xmas_tree_lights, make_xmas_tree_star,
          lambda: make_present("A"), lambda: make_present("B"), lambda: make_present("C"),
          make_snowman, make_cottage, make_cottage_window, make_mailbox, make_sled, make_mitten, make_lamp, make_lamp_glow,
          make_fence, make_fence_lights, make_candy_cane, make_fir, make_holly, make_holly_col, make_snow_drift, make_ice, make_warp_ring, make_warp_lights,
          make_snowman_col,
          # 当たり判定（見えない）：見た目を細かくした物は、前の形のまま（登る道・しずくの場所を変えない）
          lambda: make_xmas_tree(False), lambda: make_cottage(False), lambda: make_fir(False), lambda: make_snow_drift(False), lambda: make_ice(False)]
# 光る所（Unity で光る材質にする。部品のつながりは、のせる物の方で見る）
GLOW = ("Xmas_TreeLights", "Xmas_TreeStar", "Xmas_CottageWindow", "Xmas_LampGlow", "Xmas_FenceLights", "Xmas_WarpLights")


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
