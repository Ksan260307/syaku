"""
しゃくとりの森 — Blender アセットキット生成スクリプト

すべてのモデル（しゃくとりむし本体・キノコ・岩・葉っぱ・切り株・大樹など）を
プロシージャルに生成し、Unity 用の FBX と編集用の .blend を書き出します。
色は頂点カラー(Col)で持たせ、Unity 側のトゥーンシェーダーで描画します。

使い方:
  blender -b --factory-startup --python blender/scripts/build_forest_kit.py -- [fbx_out_dir] [blend_out]

単位: 1 = しゃくとりむしの体長（実寸で約 2.5cm）
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Vector, Matrix, Quaternion, noise

TAU = math.tau


# ---------------------------------------------------------------------------
# 引数
# ---------------------------------------------------------------------------
def parse_args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(os.path.join(here, "..", ".."))
    out = a[0] if len(a) > 0 else os.path.join(repo, "unity", "Assets", "Art", "Models")
    blend = a[1] if len(a) > 1 else os.path.join(repo, "blender", "forest_kit.blend")
    return out, blend


# ---------------------------------------------------------------------------
# 色・数学ユーティリティ
# ---------------------------------------------------------------------------
def hexc(h, a=1.0):
    h = h.lstrip("#")
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0, a)


def clamp01(x):
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


def mixc(c1, c2, t):
    t = clamp01(t)
    return tuple(c1[i] + (c2[i] - c1[i]) * t for i in range(4))


def scalec(c, k):
    return (clamp01(c[0] * k), clamp01(c[1] * k), clamp01(c[2] * k), c[3])


def with_alpha(c, a):
    return (c[0], c[1], c[2], clamp01(a))


def sstep(e0, e1, x):
    if e0 == e1:
        return 0.0 if x < e0 else 1.0
    t = clamp01((x - e0) / (e1 - e0))
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def smax(a, b, k):
    return 0.5 * (a + b + math.sqrt((a - b) * (a - b) + k * k))


def s2l(c):
    def f(x):
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    return (f(c[0]), f(c[1]), f(c[2]), c[3])


def n3(p, scale=1.0, seed=0.0):
    return noise.noise(Vector((p[0] * scale + seed * 17.13, p[1] * scale + seed * 3.71, p[2] * scale - seed * 9.37)))


def fbm(p, scale=1.0, octaves=4, seed=0.0):
    amp, tot, norm = 1.0, 0.0, 0.0
    f = scale
    for _ in range(octaves):
        tot += n3(p, f, seed) * amp
        norm += amp
        amp *= 0.5
        f *= 2.03
    return tot / norm


def wrap_angle(a):
    while a > math.pi:
        a -= TAU
    while a < -math.pi:
        a += TAU
    return a


# ---------------------------------------------------------------------------
# メッシュビルダー
# ---------------------------------------------------------------------------
class MB:
    """頂点・頂点カラー・面を蓄積する簡易メッシュビルダー"""

    def __init__(self):
        self.V = []
        self.C = []
        self.F = []

    def v(self, p, c):
        self.V.append(Vector(p))
        self.C.append(tuple(c))
        return len(self.V) - 1

    def f(self, *ids):
        if len(set(ids)) < 3:
            return
        self.F.append(tuple(ids))

    def add(self, other, m=None):
        base = len(self.V)
        for p in other.V:
            self.V.append((m @ p) if m is not None else p.copy())
        self.C.extend(other.C)
        for fc in other.F:
            self.F.append(tuple(i + base for i in fc))
        return self

    def xform(self, m):
        self.V = [m @ p for p in self.V]
        return self

    def recolor(self, fn):
        self.C = [fn(p, c) for p, c in zip(self.V, self.C)]
        return self


def connect_rings(mb, rings, closed=False):
    """リング列を四角形でつなぐ。長さ1のリングは極（扇形）として扱う。"""
    n = len(rings)
    count = n if closed else n - 1
    for i in range(count):
        A = rings[i]
        B = rings[(i + 1) % n]
        if len(A) == 1 and len(B) == 1:
            continue
        if len(A) == 1:
            seg = len(B)
            for j in range(seg):
                mb.f(A[0], B[(j + 1) % seg], B[j])
        elif len(B) == 1:
            seg = len(A)
            for j in range(seg):
                mb.f(A[j], A[(j + 1) % seg], B[0])
        else:
            seg = len(A)
            for j in range(seg):
                mb.f(A[j], A[(j + 1) % seg], B[(j + 1) % seg], B[j])


def lathe(mb, prof, seg, colfn, center=Vector((0, 0, 0)), rfn=None, closed=False, phase=0.0):
    """Z軸まわりの回転体。prof=[(r,z),...] は「下の極→外側を上へ→上の極」の順で並べると法線が外向きになる。
    colfn(t, ang, p) -> rgba / rfn(t, ang) -> 半径倍率"""
    n = len(prof)
    rings = []
    for i, (r, z) in enumerate(prof):
        t = i / max(1, n - 1)
        if r <= 1e-7:
            p = center + Vector((0, 0, z))
            rings.append([mb.v(p, colfn(t, 0.0, p))])
            continue
        ring = []
        for j in range(seg):
            a = phase + TAU * j / seg
            rr = r * (rfn(t, a) if rfn else 1.0)
            p = center + Vector((math.cos(a) * rr, math.sin(a) * rr, z))
            ring.append(mb.v(p, colfn(t, a, p)))
        rings.append(ring)
    connect_rings(mb, rings, closed)
    return rings


def path_frames(pts, up_hint=Vector((0, 0, 1))):
    n = len(pts)
    T = []
    for i in range(n):
        d = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        T.append(d.normalized() if d.length > 1e-9 else Vector((0, 0, 1)))
    t0 = T[0]
    ref = up_hint if abs(t0.dot(up_hint)) < 0.95 else Vector((1, 0, 0))
    N0 = (ref - t0 * ref.dot(t0)).normalized()
    Ns = [N0]
    for i in range(1, n):
        q = T[i - 1].rotation_difference(T[i])
        Ni = q @ Ns[-1]
        Ni = (Ni - T[i] * Ni.dot(T[i]))
        Ns.append(Ni.normalized() if Ni.length > 1e-9 else Ns[-1])
    return T, Ns


def tube(mb, pts, radii, seg, colfn, up_hint=Vector((0, 0, 1)), oval=(1.0, 1.0), rfn=None):
    """パスに沿ったチューブ。radii の 0 は極（閉じた先端）。colfn(t, ang, p, dir)"""
    T, Ns = path_frames(pts, up_hint)
    n = len(pts)
    rings = []
    for i in range(n):
        r = radii[i]
        t = i / max(1, n - 1)
        if r <= 1e-7:
            rings.append([mb.v(pts[i], colfn(t, 0.0, pts[i], T[i]))])
            continue
        B = T[i].cross(Ns[i])
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            d = Ns[i] * math.cos(a) * oval[0] + B * math.sin(a) * oval[1]
            rr = r * (rfn(t, a) if rfn else 1.0)
            p = pts[i] + d * rr
            ring.append(mb.v(p, colfn(t, a, p, d.normalized())))
        rings.append(ring)
    connect_rings(mb, rings)
    return rings


def bezier(p0, p1, p2, p3, n):
    out = []
    for i in range(n):
        t = i / (n - 1)
        u = 1 - t
        out.append(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t))
    return out


def quad_bezier(p0, p1, p2, n):
    out = []
    for i in range(n):
        t = i / (n - 1)
        u = 1 - t
        out.append(p0 * (u * u) + p1 * (2 * u * t) + p2 * (t * t))
    return out


def ribbon(mb, pts, widths, side_vecs, colfn, fold=0.0, normal_vecs=None):
    """平たい帯（草の葉など）。fold>0 で中央を盛り上げたV字断面。colfn(t, v, p)"""
    n = len(pts)
    rows = []
    for i in range(n):
        t = i / max(1, n - 1)
        w = widths[i]
        if w <= 1e-7:
            rows.append([mb.v(pts[i], colfn(t, 0.0, pts[i]))])
            continue
        sv = side_vecs[i]
        nv = normal_vecs[i] if normal_vecs else Vector((0, 0, 0))
        row = []
        for v in (-1.0, 0.0, 1.0):
            p = pts[i] + sv * (v * w * 0.5) + nv * (fold * w * (1.0 - abs(v)))
            row.append(mb.v(p, colfn(t, v, p)))
        rows.append(row)
    for i in range(n - 1):
        A, B = rows[i], rows[i + 1]
        if len(A) == 3 and len(B) == 3:
            mb.f(A[0], A[1], B[1], B[0])
            mb.f(A[1], A[2], B[2], B[1])
        elif len(A) == 3 and len(B) == 1:
            mb.f(A[0], A[1], B[0])
            mb.f(A[1], A[2], B[0])
        elif len(A) == 1 and len(B) == 3:
            mb.f(A[0], B[1], B[0])
            mb.f(A[0], B[2], B[1])
    return rows


def uv_sphere(mb, center, radius, colfn, seg=16, rings=10, scale=Vector((1, 1, 1)), rot=None):
    prof = []
    for i in range(rings + 1):
        th = math.pi * i / rings  # 0 = 下極
        prof.append((math.sin(th), -math.cos(th)))
    tmp = MB()

    def cf(t, a, p):
        return colfn(p.normalized() if p.length > 1e-9 else Vector((0, 0, -1)))
    lathe(tmp, prof, seg, cf)
    m = Matrix.Translation(center)
    if rot is not None:
        m = m @ rot.to_matrix().to_4x4()
    m = m @ Matrix.Diagonal((scale.x * radius, scale.y * radius, scale.z * radius, 1.0))
    mb.add(tmp, m)


def polar_sheet(mb, rfn, colfn, n_ang=64, n_rad=6, a0=-math.pi, a1=math.pi, zfn=None, full=True):
    """原点中心の極座標グリッドで平たい形（葉など）を作る。rfn(theta)->外周半径。
    colfn(rho, theta, p)。full=True なら一周、False なら a0..a1 の扇形"""
    center = mb.v((0, 0, zfn(0.0, 0.0) if zfn else 0.0), colfn(0.0, 0.0, Vector((0, 0, 0))))
    rings = []
    count = n_ang if full else n_ang + 1
    for k in range(1, n_rad + 1):
        rho = k / n_rad
        ring = []
        for j in range(count):
            th = (a0 + (a1 - a0) * j / n_ang)
            r = rfn(th) * rho
            x = math.sin(th) * r
            y = math.cos(th) * r
            z = zfn(rho, th) if zfn else 0.0
            p = Vector((x, y, z))
            ring.append(mb.v(p, colfn(rho, th, p)))
        rings.append(ring)
    first = rings[0]
    rng = range(n_ang) if full else range(n_ang)
    for j in rng:
        j2 = (j + 1) % len(first) if full else j + 1
        mb.f(center, first[j2], first[j])
    for k in range(len(rings) - 1):
        A, B = rings[k], rings[k + 1]
        for j in rng:
            j2 = (j + 1) % len(A) if full else j + 1
            mb.f(A[j], A[j2], B[j2], B[j])
    return rings


# ---------------------------------------------------------------------------
# Blender オブジェクト化
# ---------------------------------------------------------------------------
VC_MATERIAL = None


def vc_material():
    global VC_MATERIAL
    if VC_MATERIAL is not None:
        return VC_MATERIAL
    mat = bpy.data.materials.new("VertexColor")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = None
    for nd in nt.nodes:
        if nd.type == "BSDF_PRINCIPLED":
            bsdf = nd
    try:
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Col"
        if bsdf is not None:
            nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
            if "Roughness" in bsdf.inputs:
                bsdf.inputs["Roughness"].default_value = 0.8
    except Exception as e:  # noqa
        print("material setup warning:", e)
    VC_MATERIAL = mat
    return mat


def build(mb, name, smooth=True, sharp_deg=None, solidify=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in mb.V], [], mb.F)
    me.validate(clean_customdata=False)
    me.update(calc_edges=True)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    vidx = [0] * len(me.loops)
    me.loops.foreach_get("vertex_index", vidx)
    lin = [s2l(c) for c in mb.C]
    flat = []
    for vi in vidx:
        flat.extend(lin[vi] if vi < len(lin) else (1, 1, 1, 1))
    attr.data.foreach_set("color", flat)
    try:
        me.color_attributes.active_color = attr
        me.color_attributes.render_color_index = me.color_attributes.active_color_index
    except Exception:
        pass
    me.polygons.foreach_set("use_smooth", [smooth] * len(me.polygons))
    if sharp_deg is not None:
        me.set_sharp_from_angle(angle=math.radians(sharp_deg))
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(vc_material())
    if solidify:
        mod = ob.modifiers.new("Solidify", "SOLIDIFY")
        mod.thickness = solidify
        mod.offset = -1.0
        mod.use_even_offset = True
    return ob


# ---------------------------------------------------------------------------
# しゃくとりむし（シャクガの幼虫）
# ---------------------------------------------------------------------------
WORM_BODY = hexc("#8fd14f")
WORM_DORSAL = hexc("#5c9b30")
WORM_SUBDORSAL = hexc("#d6f19a")
WORM_LATERAL = hexc("#f4f9c0")
WORM_BELLY = hexc("#c9eb8c")
WORM_HEAD = hexc("#a8db5e")
WORM_MOUTH = hexc("#7a6232")
WORM_BLUSH = hexc("#ff9cb0")
WORM_LEG = hexc("#7b8a3a")
WORM_LEG_TIP = hexc("#3b3a22")
WORM_PROLEG = hexc("#9ccc56")
WORM_PROLEG_PAD = hexc("#566e2a")

S0, S1, NSEG = 0.04, 0.88, 13
SEGLEN = (S1 - S0) / NSEG
HEAD_C, HEAD_LEN, RH = 0.935, 0.066, 0.064
RB = 0.052


def worm_groove(s):
    if s < S0 or s > S1:
        return 0.0
    f = (s - S0) / SEGLEN
    return math.cos(math.pi * f) ** 20


def worm_radius(s):
    # 胴体
    rb = RB * (0.84 + 0.16 * sstep(0.02, 0.25, s)) * (1.0 - 0.07 * sstep(0.6, 0.86, s))
    if s < 0.05:
        k = (0.05 - s) / 0.05
        rb *= math.sqrt(max(0.0, 1.0 - k * k))
    if s > 0.86:
        k = (s - 0.86) / 0.05
        rb *= math.sqrt(max(0.0, 1.0 - k * k))
    rb *= 1.0 - 0.055 * worm_groove(s)
    # 頭
    k = (s - HEAD_C) / HEAD_LEN
    rh = RH * math.sqrt(max(0.0, 1.0 - k * k))
    if rb <= 0 and rh <= 0:
        return 0.0
    return smax(rb, rh, 0.012) - 0.006


def worm_head_w(s):
    return sstep(0.865, 0.905, s)


def worm_color(s, a):
    up = math.sin(a)
    side = math.cos(a)
    c = WORM_BODY
    da = abs(wrap_angle(a - math.pi / 2))
    # 背中の濃いライン
    c = mixc(c, WORM_DORSAL, 0.75 * sstep(0.28, 0.08, da))
    # 背中寄りの細いライン
    c = mixc(c, WORM_SUBDORSAL, 0.8 * sstep(0.09, 0.0, abs(da - 0.62)))
    # お腹
    c = mixc(c, WORM_BELLY, sstep(-0.35, -0.85, up))
    # 体側の淡いストライプ
    c = mixc(c, WORM_LATERAL, 0.9 * sstep(0.13, 0.03, abs(up + 0.12)))
    # 節のくびれは少し暗く
    c = scalec(c, 1.0 - 0.12 * worm_groove(s))
    # 尾端は少し黄色っぽく
    c = mixc(c, hexc("#bfe36a"), 0.5 * sstep(0.08, 0.0, s))
    # 頭
    hw = worm_head_w(s)
    if hw > 0:
        hc = WORM_HEAD
        hc = scalec(hc, 1.0 - 0.06 * sstep(0.2, 0.9, up))
        hc = mixc(hc, WORM_MOUTH, sstep(0.975, 0.995, s) * sstep(-0.1, -0.55, up))
        # ほっぺ
        bl = sstep(0.04, 0.0, abs(s - 0.972)) * sstep(0.45, 0.85, abs(side)) * sstep(0.35, 0.0, abs(up + 0.05))
        hc = mixc(hc, WORM_BLUSH, 0.85 * bl)
        c = mixc(c, hc, hw)
    return c


def make_inchworm():
    mb = MB()
    N = 150
    seg = 22
    rings = []
    for i in range(N):
        s = i / (N - 1)
        r = worm_radius(s)
        if i == 0 or i == N - 1 or r <= 1e-5:
            p = Vector((0, s, 0.007 * worm_head_w(s)))
            rings.append([mb.v(p, worm_color(s, -math.pi / 2))])
            continue
        hw = worm_head_w(s)
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            ca, sa = math.cos(a), math.sin(a)
            rx = r * lerp(1.0, 1.04, hw)
            rz = r * (lerp(0.93, 1.0, hw) if sa > 0 else lerp(0.8, 0.97, hw))
            p = Vector((ca * rx, s, sa * rz + 0.007 * hw))
            ring.append(mb.v(p, worm_color(s, a)))
        rings.append(ring)
    # 回転体の向き: ここでは軸が +Y。lathe と同じ巻き順にするため X,Z 平面で角度を取る → 法線は内向きになるので反転
    tmp_faces_start = len(mb.F)
    connect_rings(mb, rings)
    for k in range(tmp_faces_start, len(mb.F)):
        mb.F[k] = tuple(reversed(mb.F[k]))

    head_c = Vector((0, HEAD_C, 0.007))

    # 目（アニメ調のつぶらな瞳）
    for sx in (-1, 1):
        d = Vector((0.62 * sx, 0.66, 0.42)).normalized()
        ec = head_c + d * (RH * 0.86)
        rot = Vector((0, 0, 1)).rotation_difference(d)

        def eye_col(n):
            up = n.dot(Vector((0, 0, 1)))
            base = mixc(hexc("#10121c"), hexc("#2b3f7a"), sstep(0.1, -0.8, up))
            return base
        uv_sphere(mb, ec, 0.021, eye_col, seg=16, rings=10, scale=Vector((1.0, 1.0, 0.6)), rot=rot)
        # ハイライト
        hl = ec + d * 0.011 + Vector((0, 0.002, 0.009))
        uv_sphere(mb, hl, 0.0068, lambda n: hexc("#ffffff"), seg=8, rings=5)
        hl2 = ec + d * 0.011 + Vector((0.004 * sx, 0.003, -0.006))
        uv_sphere(mb, hl2, 0.0033, lambda n: hexc("#e8f0ff"), seg=6, rings=4)

    # 小さな触角
    for sx in (-1, 1):
        base = head_c + Vector((0.032 * sx, 0.045, -0.018))
        tip = base + Vector((0.012 * sx, 0.016, -0.004))
        pts = [base.lerp(tip, t) for t in (0, 0.5, 1)]
        tube(mb, pts, [0.004, 0.003, 0.0], 6, lambda t, a, p, d: mixc(hexc("#c8e07a"), hexc("#6b5a2a"), t))

    # 胸脚（3対）
    for k in (10, 11, 12):
        s = S0 + SEGLEN * (k + 0.5)
        r = worm_radius(s)
        for sx in (-1, 1):
            a = -math.pi / 2 + sx * 0.72
            base = Vector((math.cos(a) * r * 0.85, s, math.sin(a) * r * 0.75))
            dirv = Vector((0.32 * sx, 0.22, -1.0)).normalized()
            mid = base + dirv * 0.017 + Vector((0, 0.004, 0))
            tip = mid + Vector((0.003 * sx, 0.009, -0.011))
            pts = [base - dirv * 0.004, base, mid, tip, tip + Vector((0, 0.002, -0.002))]
            tube(mb, pts, [0.0, 0.0105, 0.0085, 0.0045, 0.0], 7,
                 lambda t, a, p, d: mixc(WORM_LEG, WORM_LEG_TIP, sstep(0.55, 0.95, t)))

    # 腹脚（A6）と尾脚（A10）
    for k, back in ((4, 0.15), (0, 0.55)):
        s = S0 + SEGLEN * (k + 0.5)
        r = worm_radius(s)
        for sx in (-1, 1):
            a = -math.pi / 2 + sx * 0.62
            base = Vector((math.cos(a) * r * 0.8, s, math.sin(a) * r * 0.7))
            dirv = Vector((0.28 * sx, -back, -1.0)).normalized()
            p1 = base + dirv * 0.016
            p2 = base + dirv * 0.028
            pts = [base - dirv * 0.006, base, p1, p2, p2 + dirv * 0.0015]
            tube(mb, pts, [0.0, 0.0175, 0.0155, 0.0135, 0.0], 9,
                 lambda t, a, p, d: mixc(WORM_PROLEG, WORM_PROLEG_PAD, sstep(0.6, 0.85, t)))

    ob = build(mb, "Inchworm", smooth=True)
    me = ob.data
    # しゃくとり変形用データを UV に格納（軸の変換に依存しないように）
    #  UV1 = (s, n_fwd)  UV2 = (ox, oy)  UV3 = (n_side, n_up)
    uv0 = me.uv_layers.new(name="UVMap")
    uv1 = me.uv_layers.new(name="Deform1")
    uv2 = me.uv_layers.new(name="Deform2")
    uv3 = me.uv_layers.new(name="Deform3")
    nl = len(me.loops)
    vidx = [0] * nl
    me.loops.foreach_get("vertex_index", vidx)
    cn = [0.0] * (nl * 3)
    me.corner_normals.foreach_get("vector", cn)
    d0, d1, d2, d3 = [], [], [], []
    for li in range(nl):
        co = me.vertices[vidx[li]].co
        nx, ny, nz = cn[li * 3], cn[li * 3 + 1], cn[li * 3 + 2]
        ang = math.atan2(co.z, co.x) / TAU + 0.5
        d0 += [co.y, ang]
        d1 += [co.y, ny]
        d2 += [co.x, co.z]
        d3 += [nx, nz]
    uv0.uv.foreach_set("vector", d0)
    uv1.uv.foreach_set("vector", d1)
    uv2.uv.foreach_set("vector", d2)
    uv3.uv.foreach_set("vector", d3)
    me.uv_layers.active = uv0
    return ob


# ---------------------------------------------------------------------------
# 岩（小石 = しゃくとりむしにとっては巨岩）
# ---------------------------------------------------------------------------
def make_rock(name, seed, size=(1.6, 1.25, 1.0), cuts=7, moss=0.55):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
    off = Vector((rnd.uniform(-50, 50), rnd.uniform(-50, 50), rnd.uniform(-50, 50)))
    planes = []
    for _ in range(cuts):
        nrm = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 1.0))).normalized()
        planes.append((nrm, rnd.uniform(0.62, 0.85)))
    for v in bm.verts:
        p = v.co.copy()
        p *= 1.0 + 0.16 * noise.fractal(p * 1.2 + off, 0.6, 2.0, 4) + 0.04 * noise.noise(p * 5.0 + off)
        for nrm, d in planes:
            k = p.dot(nrm)
            if k > d:
                p -= nrm * (k - d) * 0.92
        p = Vector((p.x * size[0], p.y * size[1], p.z * size[2]))
        floor = -0.25 * size[2]
        if p.z < floor:
            p.z = floor + (p.z - floor) * 0.25
        v.co = p
    bm.normal_update()
    mb = MB()
    idx = {}
    base_a = hexc("#8e98a8")
    base_b = hexc("#b0a493")
    mossc = hexc("#77ad46")
    moss2 = hexc("#a7d05c")
    for v in bm.verts:
        p = v.co
        nz = v.normal.z
        t = 0.5 + 0.5 * noise.noise(p * 0.9 + off * 1.3)
        c = mixc(base_a, base_b, t)
        c = scalec(c, 0.82 + 0.25 * sstep(-0.3 * size[2], 0.7 * size[2], p.z))
        c = mixc(c, hexc("#c9c2b4"), 0.35 * sstep(0.2, 0.6, noise.noise(p * 3.0 + off)))
        m = sstep(moss - 0.12, moss + 0.12, nz + 0.25 * noise.noise(p * 1.6 + off * 0.7))
        mc = mixc(mossc, moss2, 0.5 + 0.5 * noise.noise(p * 4.0 + off))
        c = mixc(c, mc, m)
        idx[v.index] = mb.v(p, c)
    for fc in bm.faces:
        mb.f(*[idx[v.index] for v in fc.verts])
    bm.free()
    return build(mb, name, smooth=True, sharp_deg=38)


# ---------------------------------------------------------------------------
# キノコ
# ---------------------------------------------------------------------------
def cap_point(prof, t, a):
    """回転体プロファイル上の点と法線（近似）"""
    n = len(prof)
    x = t * (n - 1)
    i = min(int(x), n - 2)
    f = x - i
    r0, z0 = prof[i]
    r1, z1 = prof[i + 1]
    r = lerp(r0, r1, f)
    z = lerp(z0, z1, f)
    dr, dz = r1 - r0, z1 - z0
    # 断面内法線（プロファイル方向を右回転）
    nr, nz = dz, -dr
    ln = math.hypot(nr, nz) or 1.0
    nr, nz = nr / ln, nz / ln
    p = Vector((math.cos(a) * r, math.sin(a) * r, z))
    nrm = Vector((math.cos(a) * nr, math.sin(a) * nr, nz))
    return p, nrm


def add_spot(mb, p, nrm, radius, col, rnd, height=0.05):
    rot = Vector((0, 0, 1)).rotation_difference(nrm)
    k = 9
    cen = mb.v(p + nrm * height * 1.2, col)
    ring = []
    for j in range(k):
        a = TAU * j / k
        rr = radius * (0.85 + 0.3 * rnd.random())
        q = rot @ Vector((math.cos(a) * rr, math.sin(a) * rr, 0.0))
        ring.append(mb.v(p + q + nrm * height * 0.15, scalec(col, 0.96)))
    for j in range(k):
        mb.f(cen, ring[j], ring[(j + 1) % k])
    # 縁を少し下へ
    ring2 = []
    for j in range(k):
        q = mb.V[ring[j]] - nrm * height * 0.6
        ring2.append(mb.v(q, scalec(col, 0.9)))
    for j in range(k):
        mb.f(ring[j], ring2[j], ring2[(j + 1) % k], ring[(j + 1) % k])


def make_mushroom_red():
    rnd = random.Random(11)
    mb = MB()
    stem_prof = [(0.0, -0.3), (0.7, -0.3), (0.86, 0.12), (0.78, 0.55), (0.56, 1.0), (0.46, 2.0),
                 (0.42, 3.4), (0.40, 4.9), (0.0, 4.95)]
    stem_white = hexc("#f6f2e6")
    stem_base = hexc("#d9ccb0")

    def stem_col(t, a, p):
        c = mixc(stem_base, stem_white, sstep(-0.2, 1.4, p.z))
        c = scalec(c, 0.94 + 0.06 * math.sin(a * 9 + p.z * 3))
        return c
    lathe(mb, stem_prof, 28, stem_col, rfn=lambda t, a: 1.0 + 0.03 * math.sin(a * 5 + t * 7))
    # つば（リング）
    ring_prof = [(0.40, 4.25), (0.62, 4.05), (0.78, 3.82), (0.74, 3.74), (0.56, 3.93), (0.40, 4.05)]
    lathe(mb, ring_prof, 28, lambda t, a, p: scalec(stem_white, 0.97 - 0.08 * t), closed=True,
          rfn=lambda t, a: 1.0 + 0.06 * math.sin(a * 7) * sstep(0.2, 0.5, t))
    # かさ
    cap_prof = [(0.0, 4.85), (0.45, 4.8), (1.2, 4.74), (2.2, 4.66), (2.85, 4.62), (3.1, 4.72),
                (3.12, 4.88), (2.95, 5.2), (2.55, 5.65), (1.9, 6.08), (1.0, 6.38), (0.0, 6.48)]
    red = hexc("#e2402f")
    red_dark = hexc("#b92a2a")
    orange = hexc("#f2763a")
    gill = hexc("#f1e6c8")

    def cap_col(t, a, p):
        rr = math.hypot(p.x, p.y)
        if t < 0.38:
            g = 0.9 + 0.1 * math.cos(a * 64)
            return scalec(mixc(gill, hexc("#e2d2a8"), sstep(2.6, 1.0, rr)), g)
        c = mixc(red_dark, red, sstep(0.0, 1.6, rr))
        c = mixc(c, orange, sstep(2.2, 3.1, rr))
        return c
    lathe(mb, cap_prof, 56, cap_col, rfn=lambda t, a: 1.0 + 0.025 * math.sin(a * 5 + 1.0) + 0.015 * math.sin(a * 11))
    # 白い斑点
    top = cap_prof[6:]
    for i in range(22):
        t = rnd.uniform(0.08, 0.92)
        a = rnd.uniform(0, TAU)
        p, nrm = cap_point(top, t, a)
        rad = rnd.uniform(0.2, 0.42) * (0.7 + 0.5 * t)
        add_spot(mb, p, nrm, rad, hexc("#fffaf0"), rnd, height=0.06)
    return build(mb, "Mushroom_Red")


def make_mushroom_brown():
    mb = MB()
    stem_prof = [(0.0, -0.3), (0.95, -0.3), (1.12, 0.3), (1.05, 1.0), (0.82, 1.9), (0.7, 2.7), (0.0, 2.75)]

    def stem_col(t, a, p):
        c = mixc(hexc("#cdb592"), hexc("#efe2c4"), sstep(0.0, 2.2, p.z))
        # 網目模様
        net = 0.5 + 0.5 * math.sin(a * 14 + p.z * 6) * math.sin(a * 14 - p.z * 6)
        return scalec(c, 0.94 + 0.08 * net)
    lathe(mb, stem_prof, 28, stem_col)
    cap_prof = [(0.0, 2.55), (0.8, 2.5), (1.7, 2.45), (2.2, 2.5), (2.35, 2.7), (2.25, 3.05), (1.85, 3.45),
                (1.1, 3.75), (0.0, 3.85)]

    def cap_col(t, a, p):
        rr = math.hypot(p.x, p.y)
        if t < 0.42:
            return mixc(hexc("#e6cf73"), hexc("#d4b65a"), sstep(0.6, 2.0, rr))
        c = mixc(hexc("#7f4f2a"), hexc("#a8703f"), sstep(0.0, 2.2, rr))
        return scalec(c, 0.95 + 0.07 * noise.noise(p * 2.0))
    lathe(mb, cap_prof, 48, cap_col, rfn=lambda t, a: 1.0 + 0.03 * math.sin(a * 4 + 2.0))
    return build(mb, "Mushroom_Brown")


def make_mushroom_cluster():
    rnd = random.Random(5)
    mb = MB()
    tan = hexc("#d8b98a")
    tan_dark = hexc("#9c7650")
    stem_c = hexc("#f2e8d4")
    for i in range(6):
        h = rnd.uniform(1.2, 2.9)
        ang = rnd.uniform(0, TAU)
        dist = rnd.uniform(0.0, 0.55) if i else 0.0
        base = Vector((math.cos(ang) * dist, math.sin(ang) * dist, -0.1))
        lean = Vector((math.cos(ang), math.sin(ang), 0)) * rnd.uniform(0.15, 0.5) * h
        top = base + Vector((0, 0, h)) + lean
        pts = quad_bezier(base, base + Vector((0, 0, h * 0.6)), top, 8)
        rr = rnd.uniform(0.07, 0.11)
        tube(mb, pts, [rr * 1.4] + [rr * (1 - 0.2 * k / 7) for k in range(1, 7)] + [rr * 0.7], 10,
             lambda t, a, p, d: mixc(hexc("#cbb594"), stem_c, t))
        cr = rnd.uniform(0.32, 0.55) * (h / 2.5) + 0.15
        prof = [(0.0, -0.02), (cr * 0.3, -0.04), (cr * 0.85, -0.18), (cr, -0.22), (cr * 0.95, -0.05),
                (cr * 0.72, cr * 0.42), (cr * 0.35, cr * 0.72), (0.0, cr * 0.8)]
        tmp = MB()

        def cc(t, a, p, cr=cr):
            if t < 0.3:
                return scalec(hexc("#efe2c8"), 0.9 + 0.1 * math.cos(a * 30))
            return mixc(tan, tan_dark, sstep(cr * 0.5, cr * 0.85, p.z))
        lathe(tmp, prof, 22, cc)
        d = (pts[-1] - pts[-2]).normalized()
        rot = Vector((0, 0, 1)).rotation_difference(d)
        mb.add(tmp, Matrix.Translation(top) @ rot.to_matrix().to_4x4())
    return build(mb, "Mushroom_Cluster")


def make_mushroom_glow():
    mb = MB()
    stem_prof = [(0.0, -0.2), (0.42, -0.2), (0.45, 0.3), (0.32, 1.0), (0.26, 1.9), (0.0, 1.95)]
    lathe(mb, stem_prof, 18, lambda t, a, p: mixc(hexc("#bfe7f2"), hexc("#e8fbff"), t))
    prof = [(0.0, 1.8), (0.6, 1.75), (1.15, 1.72), (1.25, 1.85), (1.05, 2.15), (0.6, 2.42), (0.0, 2.52)]

    def cc(t, a, p):
        rr = math.hypot(p.x, p.y)
        if t < 0.35:
            return scalec(hexc("#9fefff"), 0.85 + 0.15 * math.cos(a * 40))
        return mixc(hexc("#3fb6ff"), hexc("#a6fbff"), sstep(0.3, 1.2, rr))
    lathe(mb, prof, 32, cc)
    return build(mb, "Mushroom_Glow")


# ---------------------------------------------------------------------------
# 切り株
# ---------------------------------------------------------------------------
BARK_A = hexc("#6a4a34")
BARK_B = hexc("#8c6849")
BARK_GROOVE = hexc("#43301f")
MOSS_A = hexc("#6fa543")
MOSS_B = hexc("#9ccc58")


def bark_color(p, a, groove, seed=0.0, moss_amt=0.0, moss_h=0.0):
    t = 0.5 + 0.5 * noise.noise(Vector((p.x * 0.3, p.y * 0.3, p.z * 0.08 + seed)))
    c = mixc(BARK_A, BARK_B, t)
    c = mixc(c, BARK_GROOVE, groove)
    lich = sstep(0.55, 0.7, noise.noise(p * 0.45 + Vector((seed, 0, 0))))
    c = mixc(c, hexc("#a9b49a"), 0.45 * lich)
    if moss_amt > 0:
        m = sstep(0.0, 0.4, noise.noise(p * 0.35 + Vector((0, seed, 0))) + moss_amt - p.z * moss_h)
        c = mixc(c, mixc(MOSS_A, MOSS_B, 0.5 + 0.5 * noise.noise(p * 2.0)), m)
    return c


def make_stump():
    rnd = random.Random(21)
    mb = MB()
    R = 7.5
    H = 9.0
    seg = 96
    zs = [-1.5 + (H + 1.5) * (k / 34) for k in range(35)]
    top_h = []
    for j in range(seg):
        a = TAU * j / seg
        top_h.append(H + 0.5 * noise.noise(Vector((math.cos(a) * 1.5, math.sin(a) * 1.5, 3.3))) +
                     0.25 * math.sin(a * 3 + 1))
    rings = []
    for i, z0 in enumerate(zs):
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            z = z0 if i < len(zs) - 1 else top_h[j]
            if i == len(zs) - 2:
                z = min(z0, top_h[j] - 0.3)
            flare = 2.2 * math.exp(-(z + 1.5) / 1.6)
            fins = 1.0 + 0.35 * max(0.0, math.cos(a * 5 + 0.4)) ** 3 * math.exp(-(z + 1.5) / 2.0)
            groove = abs(math.sin(a * 21 + 1.3 * noise.noise(Vector((a, z * 0.15, 0)))))
            g = sstep(0.75, 1.0, groove)
            r = (R + flare) * fins * (1.0 - 0.035 * g) * (1.0 + 0.03 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, z * 0.3))))
            p = Vector((math.cos(a) * r, math.sin(a) * r, z))
            ring.append(mb.v(p, bark_color(p, a, g * 0.7, 1.0, moss_amt=0.25, moss_h=0.12)))
        rings.append(ring)
    connect_rings(mb, rings)
    # 上面（年輪）
    last = rings[-1]
    inner_rings = [last]
    nr = 10
    for k in range(1, nr + 1):
        f = 1.0 - k / (nr + 0.5)
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            pl = mb.V[last[j]]
            rr = math.hypot(pl.x, pl.y) * f * 0.97
            z = lerp(top_h[j] - 0.15, H - 0.1, 1 - f) + 0.08 * noise.noise(Vector((math.cos(a) * rr * 0.4, math.sin(a) * rr * 0.4, 7.7)))
            p = Vector((math.cos(a) * rr, math.sin(a) * rr, z))
            # 年輪
            ringv = 0.5 + 0.5 * math.sin(rr * 4.2 + noise.noise(p * 0.3) * 2.0)
            c = mixc(hexc("#e8c896"), hexc("#c99a62"), sstep(0.55, 0.95, ringv))
            if k == 1:
                c = hexc("#b98a58")
            c = mixc(c, hexc("#a8713f"), sstep(2.0, 0.3, rr))
            # ひび
            crack = sstep(0.03, 0.0, abs(wrap_angle(a - 0.8)) - rr * 0.0) * sstep(0.5, 4.0, rr)
            c = mixc(c, hexc("#6f4a2c"), crack * 0.8)
            ring.append(mb.v(p, c))
        inner_rings.append(ring)
    center = mb.v(Vector((0, 0, H - 0.05)), hexc("#9a6a3c"))
    inner_rings.append([center])
    connect_rings(mb, inner_rings)
    # 根
    for k in range(6):
        a = TAU * k / 6 + 0.4 + rnd.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(7, 11)
        pts = []
        for i in range(14):
            t = i / 13
            r = R * 0.8 + L * t
            z = lerp(1.6, -0.9, t ** 0.7) + 0.35 * math.sin(t * 5 + k)
            side = Vector((-d.y, d.x, 0)) * math.sin(t * 3 + k) * 1.2
            pts.append(d * r + side + Vector((0, 0, z)))
        radii = [lerp(1.8, 0.35, t ** 0.8) for t in (i / 13 for i in range(14))]
        radii[-1] = 0.0
        tube(mb, pts, radii, 14, lambda t, a2, p, dd: bark_color(p, a2, 0.25 * sstep(0.6, 1.0, abs(math.sin(a2 * 3))), 2.0, 0.35, 0.3))
    return build(mb, "Stump")


# ---------------------------------------------------------------------------
# 中が空洞の倒木（トンネル）
# ---------------------------------------------------------------------------
def make_hollow_log():
    mb = MB()
    L = 48.0
    Ro, Ri = 5.0, 3.6
    seg = 64
    nx = 48
    ends0 = []
    ends1 = []
    for j in range(seg):
        a = TAU * j / seg
        ends0.append(1.2 * noise.noise(Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, 0.5))) + 0.6 * math.sin(a * 3))
        ends1.append(L + 1.4 * noise.noise(Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, 9.5))) + 0.8 * math.sin(a * 2 + 1))

    def xpos(i, j):
        t = i / nx
        return lerp(ends0[j], ends1[j], t)

    def outer_r(x, a):
        g = sstep(0.8, 1.0, abs(math.sin(a * 17 + 0.6 * noise.noise(Vector((x * 0.08, a, 0))))))
        r = Ro * (1.0 + 0.03 * noise.noise(Vector((x * 0.1, math.cos(a), math.sin(a)))) - 0.03 * g)
        r *= 1.0 - 0.08 * sstep(0.85, 1.0, abs(x - L / 2) / (L / 2))
        return r, g

    outer = []
    inner = []
    for i in range(nx + 1):
        ro = []
        ri = []
        for j in range(seg):
            a = TAU * j / seg
            x = xpos(i, j)
            r, g = outer_r(x, a)
            p = Vector((x, math.cos(a) * r, math.sin(a) * r))
            up = math.sin(a)
            c = bark_color(Vector((x, p.y, p.z)), a, g * 0.75, 3.0)
            m = sstep(0.1, 0.55, up + 0.35 * noise.noise(Vector((x * 0.15, a * 2, 1.0))))
            c = mixc(c, mixc(MOSS_A, MOSS_B, 0.5 + 0.5 * noise.noise(p * 0.8)), m)
            ro.append(mb.v(p, c))
            rin = Ri * (1.0 + 0.06 * noise.noise(Vector((x * 0.2, math.cos(a) * 2, math.sin(a) * 2))))
            q = Vector((x, math.cos(a) * rin, math.sin(a) * rin))
            depth = sstep(0.0, 0.3, min(i, nx - i) / nx)
            ci = mixc(hexc("#b9804d"), hexc("#7a4e2e"), depth)
            ci = scalec(ci, 0.92 + 0.1 * math.sin(x * 1.3 + a * 5))
            ri.append(mb.v(q, ci))
        outer.append(ro)
        inner.append(ri)
    # 外側: X 方向が軸。リング角度の向きは (Y,Z) で反時計回り → X 軸に対して右手系
    for i in range(nx):
        A, B = outer[i], outer[i + 1]
        for j in range(seg):
            j2 = (j + 1) % seg
            mb.f(A[j], A[j2], B[j2], B[j])
        A, B = inner[i], inner[i + 1]
        for j in range(seg):
            j2 = (j + 1) % seg
            mb.f(A[j], B[j], B[j2], A[j2])
    # 両端の断面
    wood_a = hexc("#e3be8a")
    wood_b = hexc("#b98754")
    for end, sgn in ((0, -1), (nx, 1)):
        mids = []
        for j in range(seg):
            a = TAU * j / seg
            po = mb.V[outer[end][j]]
            pi = mb.V[inner[end][j]]
            m = po.lerp(pi, 0.5)
            m.x += sgn * 0.05
            rr = m.yz.length
            ringv = 0.5 + 0.5 * math.sin(rr * 5.0)
            mids.append(mb.v(m, mixc(wood_a, wood_b, ringv)))
        for j in range(seg):
            j2 = (j + 1) % seg
            o0, o1 = outer[end][j], outer[end][j2]
            i0, i1 = inner[end][j], inner[end][j2]
            m0, m1 = mids[j], mids[j2]
            if sgn > 0:
                mb.f(o0, o1, m1, m0)
                mb.f(m0, m1, i1, i0)
            else:
                mb.f(o0, m0, m1, o1)
                mb.f(m0, i0, i1, m1)
    return build(mb, "HollowLog")


# ---------------------------------------------------------------------------
# 大樹（根元）と背景の幹
# ---------------------------------------------------------------------------
def trunk_mesh(mb, R, H, seg, nz, fins_n, fin_amp, flare, seed, moss=0.4, z0=-3.0):
    rings = []
    for i in range(nz + 1):
        t = i / nz
        z = z0 + (H - z0) * (t ** 1.6)
        ring = []
        for j in range(seg):
            a = TAU * j / seg
            fl = flare * math.exp(-(z - z0) / (R * 0.45))
            fin = 1.0 + fin_amp * max(0.0, math.cos(a * fins_n + seed + 0.3 * math.sin(a * 3))) ** 2.5 * math.exp(-(z - z0) / (R * 0.7))
            gw = abs(math.sin(a * (fins_n * 5 + 3) + 0.25 * z * 0.05 + 1.2 * noise.noise(Vector((a * 2, z * 0.04, seed)))))
            g = sstep(0.7, 1.0, gw)
            wob = 1.0 + 0.04 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, z * 0.03 + seed)))
            r = (R + fl) * fin * wob * (1.0 - 0.03 * g)
            p = Vector((math.cos(a) * r, math.sin(a) * r, z))
            c = bark_color(p * 0.4, a, g * 0.8, seed, moss_amt=moss, moss_h=0.02)
            # 高いところは霧に溶けるよう少し明るく青みに
            c = mixc(c, hexc("#7f8f86"), sstep(40, 160, z) * 0.5)
            ring.append(mb.v(p, c))
        rings.append(ring)
    top = mb.v(Vector((0, 0, H + 1)), hexc("#7f8f86"))
    rings.append([top])
    connect_rings(mb, rings)


def make_great_tree():
    rnd = random.Random(77)
    mb = MB()
    R = 15.0
    trunk_mesh(mb, R, 220.0, 112, 44, 6, 0.55, 9.0, 0.7, moss=0.55)
    # 地を這う根
    for k in range(7):
        a = TAU * k / 7 + rnd.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        L = rnd.uniform(26, 40)
        pts = []
        n = 22
        for i in range(n):
            t = i / (n - 1)
            r = R * 0.9 + L * t
            z = lerp(5.0, -1.4, t ** 0.6) + 0.8 * math.sin(t * 7 + k * 1.7) * (1 - t)
            pts.append(d * r + side * (math.sin(t * 4 + k) * 3.0) + Vector((0, 0, z)))
        radii = [lerp(4.2, 0.5, t ** 0.7) for t in (i / (n - 1) for i in range(n))]
        radii[-1] = 0.0
        tube(mb, pts, radii, 18, lambda t, a2, p, dd: bark_color(p * 0.5, a2, 0.3 * sstep(0.7, 1.0, abs(math.sin(a2 * 4))), 5.0 + k, 0.45, 0.2))
    # アーチ状の根（下をくぐれる洞）
    a = math.radians(200)
    d = Vector((math.cos(a), math.sin(a), 0))
    side = Vector((-d.y, d.x, 0))
    pts = []
    n = 26
    for i in range(n):
        t = i / (n - 1)
        r = R * 0.95 + 30 * t
        z = 3.0 + 9.0 * math.sin(math.pi * min(1.0, t * 1.15)) - 4.0 * t
        pts.append(d * r + side * (math.sin(t * 2.2) * 4.0) + Vector((0, 0, z)))
    radii = [lerp(4.0, 1.4, t) for t in (i / (n - 1) for i in range(n))]
    radii[-1] = 0.0
    tube(mb, pts, radii, 20, lambda t, a2, p, dd: bark_color(p * 0.5, a2, 0.3, 13.0, 0.5, 0.05))
    return build(mb, "GreatTree")


def make_bg_trunk(name, seed):
    mb = MB()
    trunk_mesh(mb, 11.0, 240.0, 48, 18, 5, 0.6, 6.0, seed, moss=0.35)
    return build(mb, name)


# ---------------------------------------------------------------------------
# 小枝
# ---------------------------------------------------------------------------
def make_twig(name, seed, length=14.0):
    rnd = random.Random(seed)
    mb = MB()
    n = 22
    pts = []
    for i in range(n):
        t = i / (n - 1)
        x = (t - 0.5) * length
        y = 0.6 * math.sin(t * 3.3 + seed) + 0.3 * math.sin(t * 9 + seed * 2)
        z = 0.22 + 0.25 * math.sin(t * math.pi) + 0.08 * math.sin(t * 13)
        pts.append(Vector((x, y, z)))
    rad = [lerp(0.26, 0.14, t) for t in (i / (n - 1) for i in range(n))]

    def bc(t, a, p, d):
        g = sstep(0.75, 1.0, abs(math.sin(a * 5 + p.x * 0.8)))
        c = mixc(hexc("#7a5a40"), hexc("#9b7a5b"), 0.5 + 0.5 * noise.noise(p * 1.5))
        c = mixc(c, hexc("#4c3726"), g * 0.6)
        c = mixc(c, hexc("#b4bfa0"), 0.4 * sstep(0.5, 0.7, noise.noise(p * 2.2 + Vector((seed, 0, 0)))))
        return c
    rings = tube(mb, pts, rad, 12, bc, up_hint=Vector((0, 0, 1)))
    # 折れた端面
    for end in (0, len(rings) - 1):
        ring = rings[end]
        c = mb.v(pts[end] + (pts[end] - pts[1 if end == 0 else end - 1]).normalized() * 0.05, hexc("#d9bf94"))
        cnt = len(ring)
        for j in range(cnt):
            if end == 0:
                mb.f(c, ring[(j + 1) % cnt], ring[j])
            else:
                mb.f(c, ring[j], ring[(j + 1) % cnt])
    # 横枝
    for k in range(3):
        t0 = rnd.uniform(0.2, 0.85)
        i0 = int(t0 * (n - 1))
        base = pts[i0]
        dirv = Vector((rnd.uniform(-0.6, 0.6), rnd.choice((-1, 1)), rnd.uniform(0.0, 0.5))).normalized()
        L = rnd.uniform(2.0, 4.5)
        bp = [base + dirv * (L * s) + Vector((0, 0, 0.2 * math.sin(s * 3))) for s in (0, 0.25, 0.5, 0.75, 1.0)]
        tube(mb, bp, [0.12, 0.1, 0.08, 0.06, 0.0], 8, bc)
    return build(mb, name)


# ---------------------------------------------------------------------------
# 落ち葉（足場になる大きな葉っぱ）
# ---------------------------------------------------------------------------
def make_oak_leaf(name, base_col, edge_col, seed, length=5.0):
    rnd = random.Random(seed)
    mb = MB()
    nu, nv = 30, 8
    W = length * 0.36

    def hw(u):
        env = math.sin(math.pi * min(1.0, u ** 0.85)) ** 0.8
        lobes = 1.0 - 0.28 * (0.5 + 0.5 * math.cos(u * TAU * 4.5 + 0.6)) * sstep(0.08, 0.2, u)
        return W * env * lobes + 0.002

    rows = []
    for i in range(nu + 1):
        u = i / nu
        row = []
        for k in range(-nv, nv + 1):
            v = k / nv
            x = v * hw(u)
            y = u * length
            z = 0.12 * length * (v * v) * sstep(0.0, 0.4, u) + 0.06 * length * math.sin(math.pi * u)
            z -= 0.03 * length * (1 - abs(v)) ** 6
            z += 0.04 * length * noise.noise(Vector((x * 0.6, y * 0.6, seed)))
            p = Vector((x, y - length * 0.15, z))
            vein = sstep(0.07, 0.0, abs(v))
            lat = abs(((u - abs(v) * 0.32) * 7.0) % 1.0 - 0.5)
            vein = max(vein, 0.7 * sstep(0.06, 0.0, lat - 0.44) * sstep(0.95, 0.2, abs(v)))
            c = mixc(base_col, edge_col, sstep(0.55, 1.0, abs(v)) * 0.8)
            c = scalec(c, 0.92 + 0.12 * noise.noise(p * 0.8 + Vector((seed, 0, 0))))
            c = mixc(c, scalec(mixc(base_col, hexc("#fff4c8"), 0.35), 1.05), vein * 0.6)
            sp = sstep(0.62, 0.7, noise.noise(p * 1.4 + Vector((0, seed, 0))))
            c = mixc(c, scalec(edge_col, 0.7), sp * 0.6)
            row.append(mb.v(p, c))
        rows.append(row)
    for i in range(nu):
        A, B = rows[i], rows[i + 1]
        for k in range(2 * nv):
            mb.f(A[k], A[k + 1], B[k + 1], B[k])
    # 葉柄
    pts = [Vector((0, -length * 0.15 - 0.6 * t, 0.02 + 0.1 * t * t)) for t in (0, 0.3, 0.6, 1.0)]
    tube(mb, pts, [0.09, 0.08, 0.07, 0.0], 6, lambda t, a, p, d: scalec(base_col, 0.8))
    return build(mb, name, solidify=0.05)


def make_maple_leaf(name, base_col, edge_col, seed, size=2.6):
    mb = MB()
    lobes = [(-1.9, 0.55), (-1.05, 0.78), (0.0, 1.0), (1.05, 0.78), (1.9, 0.55)]

    def rfn(th):
        r = 0.32
        for c, s in lobes:
            d = wrap_angle(th - c)
            r = max(r, s * math.exp(-(d / 0.33) ** 2) + 0.3 * math.exp(-(d / 0.6) ** 2))
            # 鋸歯
        r *= 1.0 + 0.05 * math.sin(th * 26)
        return r * size

    def zfn(rho, th):
        return 0.12 * size * rho * rho + 0.05 * size * math.sin(th * 3 + seed) * rho

    def cf(rho, th, p):
        vein = 0.0
        for c, s in lobes:
            vein = max(vein, sstep(0.07, 0.0, abs(wrap_angle(th - c))) * sstep(0.95, 0.1, rho))
        col = mixc(base_col, edge_col, sstep(0.45, 1.0, rho))
        col = scalec(col, 0.93 + 0.12 * noise.noise(p * 1.1 + Vector((0, 0, seed))))
        col = mixc(col, mixc(base_col, hexc("#fff0c0"), 0.45), vein * 0.7)
        return col
    polar_sheet(mb, rfn, cf, n_ang=120, n_rad=7, zfn=zfn)
    pts = [Vector((0, -0.3 * size * t, 0.02 + 0.1 * t)) for t in (0, 0.4, 0.8, 1.0)]
    tube(mb, pts, [0.07, 0.06, 0.05, 0.0], 6, lambda t, a, p, d: scalec(base_col, 0.75))
    return build(mb, name, solidify=0.05)


# ---------------------------------------------------------------------------
# どんぐり・松ぼっくり
# ---------------------------------------------------------------------------
def make_acorn():
    mb = MB()
    nut = [(0.0, 0.0), (0.12, 0.03), (0.3, 0.16), (0.43, 0.4), (0.47, 0.62), (0.45, 0.85), (0.4, 1.0), (0.0, 1.02)]

    def nc(t, a, p):
        c = mixc(hexc("#8a5428"), hexc("#c58a4a"), sstep(0.05, 0.6, p.z))
        c = mixc(c, hexc("#e2c08a"), sstep(0.85, 0.98, p.z))
        c = mixc(c, hexc("#4d3018"), sstep(0.08, 0.0, p.z))
        stripe = 0.5 + 0.5 * math.sin(a * 8)
        return scalec(c, 0.95 + 0.06 * stripe)
    lathe(mb, nut, 28, nc)
    cap = [(0.0, 0.84), (0.44, 0.84), (0.5, 0.92), (0.49, 1.08), (0.4, 1.22), (0.18, 1.3), (0.08, 1.31),
           (0.06, 1.45), (0.0, 1.46)]

    def cc(t, a, p):
        u = a * 14 / TAU
        v = p.z * 7
        chk = (int(math.floor(u + v)) + int(math.floor(u - v))) % 2
        c = mixc(hexc("#7a5a3a"), hexc("#a3845c"), 0.25 + 0.5 * chk)
        if p.z > 1.3:
            c = hexc("#5e4630")
        return c
    lathe(mb, cap, 28, cc)
    return build(mb, "Acorn")


def make_acorn_cap():
    mb = MB()
    prof = [(0.0, 0.0), (0.12, 0.0), (0.3, 0.06), (0.46, 0.2), (0.56, 0.4), (0.6, 0.55), (0.56, 0.6),
            (0.48, 0.48), (0.34, 0.3), (0.16, 0.2), (0.0, 0.17)]

    def cc(t, a, p):
        if t > 0.6:
            return mixc(hexc("#c79a68"), hexc("#a77a4c"), sstep(0.5, 0.2, p.z))
        u = a * 16 / TAU
        v = p.z * 9
        chk = (int(math.floor(u + v)) + int(math.floor(u - v))) % 2
        return mixc(hexc("#755536"), hexc("#a3845c"), 0.25 + 0.5 * chk)
    lathe(mb, prof, 30, cc)
    # へた
    tube(mb, [Vector((0, 0, 0.02)), Vector((0, 0.03, -0.12)), Vector((0.02, 0.06, -0.22))],
         [0.07, 0.06, 0.0], 8, lambda t, a, p, d: hexc("#5e4630"))
    return build(mb, "AcornCap")


def make_pinecone():
    rnd = random.Random(3)
    mb = MB()
    H = 2.4
    core = [(0.0, 0.0), (0.25, 0.05), (0.42, 0.5), (0.45, 1.2), (0.36, 1.9), (0.15, 2.35), (0.0, 2.4)]
    lathe(mb, core, 16, lambda t, a, p: hexc("#5a3a20"))
    N = 110
    for i in range(N):
        t = (i + 0.5) / N
        z = 0.12 + t * (H - 0.25)
        th = i * math.radians(137.508)
        # 芯の半径
        rc = 0.42 * math.sin(math.pi * min(1.0, 0.15 + t * 0.9)) + 0.08
        size = 0.42 * math.sin(math.pi * (0.1 + 0.85 * t)) ** 0.7 + 0.1
        out = Vector((math.cos(th), math.sin(th), 0))
        side = Vector((-out.y, out.x, 0))
        base = out * rc + Vector((0, 0, z))
        dirv = (out * 1.0 + Vector((0, 0, -0.35 + 0.9 * t))).normalized()
        nrm = dirv.cross(side).normalized()
        tip = base + dirv * size
        w = size * 0.55
        thick = size * 0.15
        pts = [base + side * w * 0.5, base + dirv * size * 0.6 + side * w * 0.6, tip + side * w * 0.2,
               tip + side * (-w * 0.2), base + dirv * size * 0.6 - side * w * 0.6, base - side * w * 0.5]
        ct = mixc(hexc("#8a5a32"), hexc("#c08a54"), 0.6)
        cb = hexc("#6b4426")
        top_ids = [mb.v(p + nrm * thick, mixc(cb, ct, 0.3 + 0.7 * ((p - base).length / size))) for p in pts]
        bot_ids = [mb.v(p - nrm * thick * 0.3, scalec(cb, 0.85)) for p in pts]
        tc = mb.v(base + dirv * size * 0.5 + nrm * thick * 1.6, ct)
        for j in range(len(pts) - 1):
            mb.f(tc, top_ids[j], top_ids[j + 1])
        mb.f(tc, top_ids[-1], top_ids[0])
        bc = mb.v(base + dirv * size * 0.5 - nrm * thick * 0.4, cb)
        for j in range(len(pts) - 1):
            mb.f(bc, bot_ids[j + 1], bot_ids[j])
        for j in range(len(pts) - 1):
            mb.f(top_ids[j], bot_ids[j], bot_ids[j + 1], top_ids[j + 1])
    return build(mb, "Pinecone")


# ---------------------------------------------------------------------------
# 植物（風で揺れるもの: 頂点アルファ = 揺れの強さ）
# ---------------------------------------------------------------------------
GRASS_BASE = hexc("#3d7a2c")
GRASS_MID = hexc("#6db33f")
GRASS_TIP = hexc("#cfe678")


def blade(mb, rnd, base, h, w, lean_dir, lean, cols=(GRASS_BASE, GRASS_MID, GRASS_TIP), segs=6, twist=0.0):
    pts = []
    sides = []
    nrms = []
    widths = []
    side0 = Vector((-lean_dir.y, lean_dir.x, 0))
    for i in range(segs + 1):
        t = i / segs
        bend = lean * h * (t ** 1.8)
        p = base + Vector((0, 0, h * t * (1 - 0.15 * t * t * lean))) + lean_dir * bend
        pts.append(p)
        rot = Quaternion(Vector((0, 0, 1)), twist * t)
        sv = rot @ side0
        sides.append(sv)
        widths.append(w * (1.0 - t ** 1.6) if i < segs else 0.0)
    for i in range(segs + 1):
        tng = (pts[min(i + 1, segs)] - pts[max(i - 1, 0)]).normalized()
        nrms.append(sides[i].cross(tng).normalized())

    def cf(t, v, p):
        c = mixc(cols[0], cols[1], sstep(0.0, 0.45, t))
        c = mixc(c, cols[2], sstep(0.5, 1.0, t))
        c = scalec(c, 1.0 - 0.08 * abs(v))
        return with_alpha(c, t)
    ribbon(mb, pts, widths, sides, cf, fold=0.25, normal_vecs=nrms)


def make_grass(name, seed, count=7, hmin=2.4, hmax=5.0, cols=(GRASS_BASE, GRASS_MID, GRASS_TIP), wmin=0.22, wmax=0.34):
    rnd = random.Random(seed)
    mb = MB()
    for i in range(count):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0, 0.45)
        base = Vector((math.cos(a) * r, math.sin(a) * r, -0.05))
        la = rnd.uniform(0, TAU)
        blade(mb, rnd, base, rnd.uniform(hmin, hmax), rnd.uniform(wmin, wmax), Vector((math.cos(la), math.sin(la), 0)),
              rnd.uniform(0.15, 0.55), cols=cols, twist=rnd.uniform(-0.8, 0.8))
    return build(mb, name)


def make_clover(name, seed, count=4):
    rnd = random.Random(seed)
    mb = MB()
    dark = hexc("#3f8a35")
    light = hexc("#8fcf63")
    chev = hexc("#b8e48e")

    def leaflet(R):
        tmp = MB()

        def rfn(th):
            base = 0.62 + 0.38 * math.cos(th * 0.95) ** 0.6 if abs(th) < math.pi / 2 / 0.95 else 0.25
            notch = 1.0 - 0.22 * math.exp(-(th / 0.16) ** 2)
            return R * base * notch

        def zfn(rho, th):
            return 0.18 * R * rho * abs(math.sin(th)) + 0.05 * R * rho * rho

        def cf(rho, th, p):
            c = mixc(dark, light, 0.35 + 0.4 * rho)
            v = abs(rho - (0.45 + 0.25 * abs(th)))
            c = mixc(c, chev, sstep(0.07, 0.0, v) * 0.85)
            c = mixc(c, scalec(dark, 1.1), sstep(0.04, 0.0, abs(th)) * 0.5)
            return with_alpha(c, 0.6 + 0.4 * rho)
        polar_sheet(tmp, rfn, cf, n_ang=28, n_rad=4, a0=-1.55, a1=1.55, zfn=zfn, full=False)
        return tmp
    for i in range(count):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0.1, 0.9) if i else 0.0
        base = Vector((math.cos(a) * r, math.sin(a) * r, -0.05))
        h = rnd.uniform(0.9, 1.6)
        top = base + Vector((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), h))
        pts = quad_bezier(base, base + Vector((0, 0, h * 0.7)), top, 6)
        tube(mb, pts, [0.05, 0.045, 0.04, 0.035, 0.03, 0.0], 5,
             lambda t, a2, p, d: with_alpha(mixc(hexc("#5c8f3a"), hexc("#86c25a"), t), t * 0.6))
        R = rnd.uniform(0.38, 0.52)
        rot0 = rnd.uniform(0, TAU)
        for k in range(3):
            lf = leaflet(R)
            m = Matrix.Translation(top) @ Matrix.Rotation(rot0 + k * TAU / 3, 4, "Z") @ Matrix.Rotation(-0.25, 4, "X")
            mb.add(lf, m)
    return build(mb, name)


def make_fern(name, seed, fronds=7, L=13.0, H=6.0):
    rnd = random.Random(seed)
    mb = MB()
    rach = hexc("#4c7a2b")
    pin_a = hexc("#4f9333")
    pin_b = hexc("#93cc55")
    for f in range(fronds):
        a = TAU * f / fronds + rnd.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0))
        l = L * rnd.uniform(0.75, 1.1)
        h = H * rnd.uniform(0.75, 1.15)
        p0 = Vector((0, 0, 0))
        p1 = d * l * 0.3 + Vector((0, 0, h * 1.25))
        p2 = d * l + Vector((0, 0, h * 0.15))
        pts = quad_bezier(p0, p1, p2, 16)
        tube(mb, pts, [lerp(0.16, 0.03, t / 15) for t in range(15)] + [0.0], 5,
             lambda t, a2, p, dd: with_alpha(scalec(rach, 0.9 + 0.2 * t), t))
        shade = rnd.uniform(0.9, 1.08)
        npin = 22
        side = Vector((-d.y, d.x, 0))
        for k in range(npin):
            t = 0.12 + 0.86 * k / (npin - 1)
            i = t * 15
            i0 = int(i)
            fr = i - i0
            pos = pts[i0].lerp(pts[min(i0 + 1, 15)], fr)
            tng = (pts[min(i0 + 1, 15)] - pts[i0]).normalized()
            plen = l * 0.2 * math.sin(math.pi * min(1.0, t * 1.05)) ** 0.7 + 0.25
            for sgn in (-1, 1):
                sv = (side * sgn)
                dirv = (sv * 0.85 + tng * 0.5 + Vector((0, 0, -0.15))).normalized()
                nrm = dirv.cross(tng).normalized()
                if nrm.z < 0:
                    nrm = -nrm
                segs = 4
                ppts = [pos + dirv * plen * (s / segs) + Vector((0, 0, -0.25 * plen * (s / segs) ** 2)) for s in range(segs + 1)]
                widths = [plen * 0.28 * math.sin(math.pi * min(1.0, 0.25 + 0.75 * s / segs)) for s in range(segs + 1)]
                widths[-1] = 0.0
                svs = [tng for _ in range(segs + 1)]

                def cf(tt, v, p, t=t, shade=shade):
                    c = mixc(pin_a, pin_b, 0.3 + 0.6 * tt)
                    c = scalec(c, shade * (0.97 + 0.08 * (1 - abs(v))))
                    return with_alpha(c, t * (0.7 + 0.3 * tt))
                ribbon(mb, ppts, widths, svs, cf, fold=0.15, normal_vecs=[nrm] * (segs + 1))
    return build(mb, name)


def make_daisy(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(4.2, 5.4)
    lean = Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0))
    pts = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 0.6)), Vector((0, 0, h)) + lean, 10)
    tube(mb, pts, [0.075] * 9 + [0.06], 7, lambda t, a, p, d: with_alpha(mixc(hexc("#4e8a32"), hexc("#7fbf4f"), t), t))
    top = pts[-1]
    up = (pts[-1] - pts[-2]).normalized()
    rot = Vector((0, 0, 1)).rotation_difference(up)
    head = MB()
    disk = [(0.0, -0.05), (0.38, -0.02), (0.42, 0.05), (0.33, 0.16), (0.18, 0.22), (0.0, 0.24)]

    def dc(t, a, p):
        rr = math.hypot(p.x, p.y)
        c = mixc(hexc("#f0a81e"), hexc("#ffd84a"), sstep(0.0, 0.35, rr))
        return with_alpha(scalec(c, 0.92 + 0.1 * math.sin(a * 13 + rr * 40)), 1.0)
    lathe(head, disk, 20, dc)
    np_ = 21
    for k in range(np_):
        a = TAU * k / np_ + rnd.uniform(-0.05, 0.05)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        L = rnd.uniform(0.95, 1.15)
        ppts = [d * (0.32 + L * s / 4) + Vector((0, 0, 0.04 + 0.12 * (s / 4) - 0.18 * (s / 4) ** 2)) for s in range(5)]
        widths = [0.12, 0.2, 0.22, 0.2, 0.0]

        def pc(tt, v, p):
            c = mixc(hexc("#fffdf6"), hexc("#ffd3e0"), sstep(0.7, 1.0, tt) * 0.8)
            return with_alpha(c, 1.0)
        ribbon(head, ppts, widths, [sv] * 5, pc, fold=0.05, normal_vecs=[Vector((0, 0, 1))] * 5)
    mb.add(head, Matrix.Translation(top) @ rot.to_matrix().to_4x4())
    # 根生葉
    for k in range(5):
        a = TAU * k / 5 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        L = rnd.uniform(1.0, 1.5)
        ppts = [d * (L * s / 5) + Vector((0, 0, 0.3 * math.sin(math.pi * s / 5 * 0.8))) for s in range(6)]
        widths = [0.08, 0.2, 0.32, 0.38, 0.3, 0.0]
        ribbon(mb, ppts, widths, [sv] * 6,
               lambda tt, v, p: with_alpha(mixc(hexc("#3f7f30"), hexc("#74b84a"), tt), tt * 0.3),
               fold=0.2, normal_vecs=[Vector((0, 0, 1))] * 6)
    return build(mb, name)


def make_bellflower(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(3.6, 4.4)
    lean = Vector((rnd.uniform(0.8, 1.3), 0, 0))
    pts = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 1.1)), Vector((0, 0, h * 0.75)) + lean * 1.4, 14)
    tube(mb, pts, [0.06] * 13 + [0.0], 6, lambda t, a, p, d: with_alpha(mixc(hexc("#4b8030"), hexc("#7bb14d"), t), t))
    bell = [(0.0, 0.62), (0.3, 0.55), (0.36, 0.3), (0.38, 0.04), (0.47, -0.04), (0.44, 0.0), (0.4, 0.2),
            (0.33, 0.5), (0.2, 0.68), (0.0, 0.72)]

    def bc(t, a, p):
        if t < 0.45:
            c = mixc(hexc("#e9e6ff"), hexc("#a9a3f0"), sstep(0.6, 0.1, p.z))
        else:
            c = mixc(hexc("#6f6fe0"), hexc("#a7a4f6"), sstep(0.6, 0.0, p.z))
        return with_alpha(c, 1.0)
    for k, t in enumerate((0.62, 0.8, 0.97)):
        i = t * 13
        i0 = int(i)
        pos = pts[i0].lerp(pts[min(i0 + 1, 13)], i - i0)
        tmp = MB()
        lathe(tmp, bell, 20, bc, rfn=lambda tt, a: 1.0 + 0.12 * max(0.0, math.cos(a * 5)) * sstep(0.2, 0.45, tt) * sstep(0.62, 0.45, tt))
        tilt = Matrix.Rotation(rnd.uniform(-0.3, 0.3), 4, "X")
        s = 1.0 - 0.15 * k
        m = Matrix.Translation(pos + Vector((0, 0, -0.75 * s))) @ tilt @ Matrix.Diagonal((s, s, s, 1))
        mb.add(tmp, m)
    return build(mb, name)


def make_dandelion(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(3.8, 4.6)
    lean = Vector((rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), 0))
    pts = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 0.6)), Vector((0, 0, h)) + lean, 10)
    tube(mb, pts, [0.07] * 9 + [0.065], 7, lambda t, a, p, d: with_alpha(mixc(hexc("#6f9b3a"), hexc("#a3c862"), t), t))
    top = pts[-1]
    up = (pts[-1] - pts[-2]).normalized()
    rot = Vector((0, 0, 1)).rotation_difference(up)
    head = MB()
    lathe(head, [(0.0, -0.25), (0.2, -0.2), (0.28, 0.0), (0.0, 0.02)], 12, lambda t, a, p: hexc("#5f8f34"))
    for ring, (cnt, L, tilt) in enumerate(((30, 0.62, 0.15), (26, 0.5, 0.55), (20, 0.36, 0.95), (12, 0.2, 1.3))):
        for k in range(cnt):
            a = TAU * k / cnt + ring * 0.3
            d = Vector((math.cos(a), math.sin(a), 0))
            sv = Vector((-d.y, d.x, 0))
            dirv = (d * math.cos(tilt) + Vector((0, 0, math.sin(tilt)))).normalized()
            ppts = [d * 0.05 + dirv * (L * s / 3) for s in range(4)]
            widths = [0.05, 0.1, 0.1, 0.0]
            nrm = sv.cross(dirv).normalized()
            if nrm.z < 0:
                nrm = -nrm
            col = mixc(hexc("#ffd53a"), hexc("#ffb81f"), ring / 3)
            ribbon(head, ppts, widths, [sv] * 4, lambda tt, v, p, col=col: with_alpha(scalec(col, 0.95 + 0.08 * tt), 1.0),
                   fold=0.02, normal_vecs=[nrm] * 4)
    mb.add(head, Matrix.Translation(top) @ rot.to_matrix().to_4x4())
    # ギザギザの葉
    for k in range(4):
        a = TAU * k / 4 + rnd.uniform(-0.4, 0.4)
        d = Vector((math.cos(a), math.sin(a), 0))
        sv = Vector((-d.y, d.x, 0))
        L = rnd.uniform(1.6, 2.2)
        n = 9
        ppts = [d * (L * s / (n - 1)) + Vector((0, 0, 0.25 * math.sin(math.pi * s / (n - 1) * 0.7))) for s in range(n)]
        widths = [0.1 + 0.28 * math.sin(math.pi * s / (n - 1)) * (1.0 + 0.45 * (s % 2)) for s in range(n)]
        widths[-1] = 0.0
        ribbon(mb, ppts, widths, [sv] * n,
               lambda tt, v, p: with_alpha(mixc(hexc("#3c7a2c"), hexc("#6aaa40"), tt), tt * 0.3),
               fold=0.15, normal_vecs=[Vector((0, 0, 1))] * n)
    return build(mb, name)


def make_dandelion_puff(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(4.6, 5.4)
    pts = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 0.6)), Vector((rnd.uniform(-0.3, 0.3), 0.2, h)), 10)
    tube(mb, pts, [0.065] * 9 + [0.06], 7, lambda t, a, p, d: with_alpha(mixc(hexc("#7d9b4a"), hexc("#b5c98a"), t), t))
    top = pts[-1]
    uv_sphere(mb, top, 0.14, lambda n: with_alpha(hexc("#9b8a5e"), 1.0), seg=10, rings=6)
    N = 70
    white = hexc("#fbfbf4")
    for i in range(N):
        # フィボナッチ球（下側は茎があるので除く）
        y = 1 - (i + 0.5) / N * 1.7
        r = math.sqrt(max(0.0, 1 - y * y))
        th = i * math.radians(137.508)
        d = Vector((math.cos(th) * r, math.sin(th) * r, y)).normalized()
        L = 0.75
        a0 = top + d * 0.12
        a1 = top + d * L
        sv = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        nv = d.cross(sv).normalized()
        for s in (sv, nv):
            i0 = mb.v(a0 + s * 0.012, with_alpha(hexc("#d8d2b8"), 1.0))
            i1 = mb.v(a0 - s * 0.012, with_alpha(hexc("#d8d2b8"), 1.0))
            i2 = mb.v(a1 - s * 0.006, with_alpha(white, 1.0))
            i3 = mb.v(a1 + s * 0.006, with_alpha(white, 1.0))
            mb.f(i0, i1, i2, i3)
        # 冠毛（パラシュート）
        cen = mb.v(a1, with_alpha(white, 1.0))
        rim = []
        k = 10
        for j in range(k):
            a = TAU * j / k
            q = a1 + (sv * math.cos(a) + nv * math.sin(a)) * 0.24 + d * 0.06
            rim.append(mb.v(q, with_alpha(scalec(white, 0.97), 1.0)))
        for j in range(k):
            if j % 2 == 0:
                mb.f(cen, rim[j], rim[(j + 1) % k])
    return build(mb, name)


def make_moss(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    for i in range(7):
        a = rnd.uniform(0, TAU)
        r = rnd.uniform(0, 0.55) if i else 0
        c = Vector((math.cos(a) * r, math.sin(a) * r, -0.05))
        rad = rnd.uniform(0.25, 0.45)
        off = Vector((rnd.uniform(0, 50), rnd.uniform(0, 50), 0))

        def mc(n, off=off):
            k = 0.5 + 0.5 * noise.noise(n * 3 + off)
            return with_alpha(mixc(hexc("#4f8a36"), hexc("#9ccd5a"), k * sstep(-0.2, 0.8, n.z)), 0.3)
        uv_sphere(mb, c, rad, mc, seg=12, rings=7, scale=Vector((1, 1, 0.55)))
    return build(mb, name)


def make_sprout(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    h = rnd.uniform(1.0, 1.4)
    pts = quad_bezier(Vector((0, 0, -0.05)), Vector((0.1, 0, h * 0.6)), Vector((0, 0.05, h)), 8)
    tube(mb, pts, [0.06] * 7 + [0.05], 6, lambda t, a, p, d: with_alpha(mixc(hexc("#a8c070"), hexc("#d2e89a"), t), t))
    top = pts[-1]
    for sgn in (-1, 1):
        def rfn(th):
            return 0.5 * (0.55 + 0.45 * math.cos(th * 0.9) ** 0.5) if abs(th) < 1.6 else 0.1

        def zfn(rho, th):
            return 0.12 * rho * rho

        tmp = MB()
        polar_sheet(tmp, rfn, lambda rho, th, p: with_alpha(mixc(hexc("#79c24a"), hexc("#b9ec7a"), rho), 0.8 + 0.2 * rho),
                    n_ang=20, n_rad=4, a0=-1.5, a1=1.5, zfn=zfn, full=False)
        m = Matrix.Translation(top) @ Matrix.Rotation(sgn * math.pi / 2, 4, "Z") @ Matrix.Rotation(-0.35, 4, "X")
        mb.add(tmp, m)
    return build(mb, name)


def make_strawberry(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    # 三出複葉
    for k in range(3):
        a = TAU * k / 3 + rnd.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a), 0))
        h = rnd.uniform(1.6, 2.2)
        top = d * 0.6 + Vector((0, 0, h))
        pts = quad_bezier(Vector((0, 0, -0.05)), Vector((0, 0, h * 0.8)), top, 8)
        tube(mb, pts, [0.05] * 7 + [0.04], 5, lambda t, a2, p, dd: with_alpha(hexc("#6a9a3c"), t))
        for j in range(3):
            def rfn(th):
                env = 0.5 * (0.5 + 0.5 * math.cos(th * 0.85) ** 0.7) if abs(th) < 1.8 else 0.1
                return env * (1.0 + 0.08 * math.sin(th * 18))

            def zfn(rho, th):
                return 0.1 * rho * rho + 0.06 * abs(math.sin(th)) * rho

            tmp = MB()
            polar_sheet(tmp, rfn, lambda rho, th, p: with_alpha(mixc(hexc("#3d7d2e"), hexc("#6fb14a"), rho), 0.6 + 0.4 * rho),
                        n_ang=22, n_rad=4, a0=-1.6, a1=1.6, zfn=zfn, full=False)
            m = Matrix.Translation(top) @ Matrix.Rotation(a - math.pi / 2 + (j - 1) * 0.9, 4, "Z") @ Matrix.Rotation(-0.2, 4, "X") @ Matrix.Diagonal((1.6, 1.6, 1.6, 1))
            mb.add(tmp, m)
    # 実
    for k in range(2):
        a = rnd.uniform(0, TAU)
        d = Vector((math.cos(a), math.sin(a), 0))
        tip = d * 1.1 + Vector((0, 0, 0.6))
        pts = quad_bezier(Vector((0, 0, 0)), Vector((0, 0, 1.4)) + d * 0.4, tip + Vector((0, 0, 0.45)), 7)
        tube(mb, pts, [0.035] * 6 + [0.03], 5, lambda t, a2, p, dd: with_alpha(hexc("#7aa040"), t))
        berry = [(0.0, -0.5), (0.18, -0.42), (0.34, -0.2), (0.4, 0.0), (0.36, 0.12), (0.2, 0.18), (0.0, 0.18)]
        tmp = MB()

        def bc(t, a2, p):
            u = a2 * 8 / TAU
            v = p.z * 9
            dots = abs(math.sin(u * math.pi + v)) < 0.18 and abs(math.sin(v * 1.7)) < 0.4
            c = mixc(hexc("#c3162b"), hexc("#ff4a4a"), sstep(-0.5, 0.1, p.z))
            if dots:
                c = hexc("#ffe08a")
            return with_alpha(c, 1.0)
        lathe(tmp, berry, 18, bc)
        # がく
        for j in range(5):
            aa = TAU * j / 5
            dd = Vector((math.cos(aa), math.sin(aa), 0))
            sv = Vector((-dd.y, dd.x, 0))
            ribbon(tmp, [Vector((0, 0, 0.18)) + dd * 0.4 * s / 2 + Vector((0, 0, -0.08 * (s / 2) ** 2)) for s in range(3)],
                   [0.14, 0.12, 0.0], [sv] * 3, lambda tt, v, p: with_alpha(hexc("#4f9a35"), 1.0), normal_vecs=[Vector((0, 0, 1))] * 3)
        mb.add(tmp, Matrix.Translation(tip))
    return build(mb, name)


def make_lilypad(name, seed, R=3.0, notch=True):
    """睡蓮の葉。notch=False は、切れこみまでふさいだ当たり判定用（切れこみの上でも水に落ちない）"""
    rnd = random.Random(seed)
    mb = MB()
    gap = 0.22

    def rfn(th):
        return R * (1.0 + 0.03 * math.sin(th * 7 + seed))

    def zfn(rho, th):
        return 0.12 * sstep(0.85, 1.0, rho) + 0.05 * math.sin(th * 5 + seed) * rho

    def cf(rho, th, p):
        c = mixc(hexc("#3f8f45"), hexc("#6cbf5a"), 0.4 + 0.4 * rho)
        vein = sstep(0.06, 0.0, abs(((th + math.pi) * 24 / TAU) % 1.0 - 0.5) - 0.44)
        c = mixc(c, hexc("#a0dc7a"), vein * 0.5 * rho)
        c = mixc(c, hexc("#9a5a4a"), sstep(0.93, 1.0, rho) * 0.6)
        return c
    if notch:
        polar_sheet(mb, rfn, cf, n_ang=64, n_rad=6, a0=-math.pi + gap, a1=math.pi - gap, zfn=zfn, full=False)
    else:
        polar_sheet(mb, rfn, cf, n_ang=64, n_rad=6, zfn=zfn, full=True)
    return build(mb, name, solidify=0.08)


def make_water_lily(name):
    mb = MB()
    for ring, (cnt, L, tilt, col) in enumerate(((8, 1.3, 0.35, hexc("#fff2f6")), (8, 1.05, 0.75, hexc("#ffc6da")),
                                                 (6, 0.8, 1.05, hexc("#ff9fc0")))):
        for k in range(cnt):
            a = TAU * k / cnt + ring * 0.4
            d = Vector((math.cos(a), math.sin(a), 0))
            sv = Vector((-d.y, d.x, 0))
            dirv = (d * math.cos(tilt) + Vector((0, 0, math.sin(tilt)))).normalized()
            ppts = [dirv * (L * s / 4) + Vector((0, 0, 0.1)) for s in range(5)]
            widths = [0.12, 0.42, 0.5, 0.36, 0.0]
            nrm = sv.cross(dirv).normalized()
            if nrm.z < 0:
                nrm = -nrm
            ribbon(mb, ppts, widths, [sv] * 5,
                   lambda tt, v, p, col=col: with_alpha(mixc(col, hexc("#ffffff"), (1 - tt) * 0.4), 1.0),
                   fold=0.25, normal_vecs=[nrm] * 5)
    uv_sphere(mb, Vector((0, 0, 0.25)), 0.32, lambda n: hexc("#ffd23a"), seg=12, rings=6, scale=Vector((1, 1, 0.6)))
    return build(mb, name)


def make_reed(name, seed):
    rnd = random.Random(seed)
    mb = MB()
    for i in range(5):
        a = rnd.uniform(0, TAU)
        base = Vector((math.cos(a), math.sin(a), 0)) * rnd.uniform(0, 0.5)
        la = rnd.uniform(0, TAU)
        blade(mb, rnd, base + Vector((0, 0, -0.1)), rnd.uniform(7, 11), rnd.uniform(0.35, 0.5),
              Vector((math.cos(la), math.sin(la), 0)), rnd.uniform(0.1, 0.35),
              cols=(hexc("#3d6e35"), hexc("#5f9a48"), hexc("#a9c97a")), segs=8, twist=rnd.uniform(-0.5, 0.5))
    # ガマの穂
    h = 12.0
    pts = quad_bezier(Vector((0, 0, -0.1)), Vector((0, 0, h * 0.6)), Vector((0.5, 0.2, h)), 12)
    tube(mb, pts, [0.11] * 11 + [0.08], 7, lambda t, a, p, d: with_alpha(mixc(hexc("#4f7a3a"), hexc("#86a85a"), t), t))
    top = pts[-1]
    d = (pts[-1] - pts[-2]).normalized()
    rot = Vector((0, 0, 1)).rotation_difference(d)
    tmp = MB()
    lathe(tmp, [(0.0, 0.0), (0.3, 0.1), (0.36, 0.6), (0.36, 1.8), (0.3, 2.3), (0.0, 2.4)], 14,
          lambda t, a, p: with_alpha(mixc(hexc("#6b4428"), hexc("#8a5a34"), 0.5 + 0.5 * math.sin(p.z * 10 + a * 3)), 1.0))
    tube(tmp, [Vector((0, 0, 2.3)), Vector((0, 0, 3.4))], [0.04, 0.0], 5, lambda t, a, p, dd: with_alpha(hexc("#8a7a4a"), 1.0))
    mb.add(tmp, Matrix.Translation(top) @ rot.to_matrix().to_4x4())
    return build(mb, name)


def make_dewdrop():
    mb = MB()
    prof = []
    n = 16
    for i in range(n + 1):
        th = math.pi * i / n
        r = math.sin(th)
        z = -math.cos(th)
        # 少しつぶれた水滴（下が平たい）
        z = z * (0.85 if z < 0 else 1.0)
        r *= 1.0 + 0.08 * math.sin(th)
        prof.append((r * 0.5, z * 0.5 + 0.42))
    lathe(mb, prof, 24, lambda t, a, p: hexc("#ffffff"))
    return build(mb, "Dewdrop")


# ---------------------------------------------------------------------------
# エリアの小物（森：サルノコシカケ・クモの巣・ホコリタケ。川辺：流木・笹舟・水草）
# ---------------------------------------------------------------------------
def make_shelf_fungus(name="ShelfFungus", R=2.2, D=1.7):
    """木の幹にはえる、棚のようなキノコ（サルノコシカケ）。背中（y=0）を幹につけ、-Y（Unity の +Z）へ張り出す。
    上はほぼ平らで、しゃくとりむしが乗って休める"""
    mb = MB()
    n_ang, n_rad = 24, 6

    def rim(th):
        return 1.0 + 0.05 * math.sin(th * 5.0) + 0.03 * math.sin(th * 11.0 + 1.0)

    def pt(rho, th, top):
        r = rho * rim(th)
        x = math.cos(th) * R * r
        y = -math.sin(th) * D * r
        if top:
            z = 0.32 * (1.0 - rho * rho) + 0.06
        else:
            z = -0.08 - 0.22 * (1.0 - rho)
        return Vector((x, y, z))

    def top_col(rho, th):
        band = 0.5 + 0.5 * math.sin(rho * 22.0)
        c = mixc(hexc("#5e3c22"), hexc("#b07a45"), 0.35 + 0.45 * rho)
        c = mixc(c, hexc("#d6a46a"), band * 0.35 * rho)
        return mixc(c, hexc("#f1dfb8"), sstep(0.86, 1.0, rho) * 0.8)

    def bot_col(rho, th):
        return mixc(hexc("#efe2c6"), hexc("#d8c6a0"), 0.5 * (1.0 - rho))

    top_rings, bot_rings = [], []
    for k in range(n_rad + 1):
        rho = k / n_rad
        tr, br = [], []
        for j in range(n_ang + 1):
            th = math.pi * j / n_ang
            tr.append(mb.v(pt(rho, th, True), top_col(rho, th)))
            br.append(mb.v(pt(rho, th, False), bot_col(rho, th)))
        top_rings.append(tr)
        bot_rings.append(br)
    for k in range(n_rad):
        for j in range(n_ang):
            A, B = top_rings[k], top_rings[k + 1]
            mb.f(A[j], A[j + 1], B[j + 1], B[j])
            C, E = bot_rings[k], bot_rings[k + 1]
            mb.f(C[j], E[j], E[j + 1], C[j + 1])
    # 外のふち
    T, Bt = top_rings[-1], bot_rings[-1]
    for j in range(n_ang):
        mb.f(T[j], T[j + 1], Bt[j + 1], Bt[j])
    # 幹につく背中（y = 0 の面）
    for side in (0, n_ang):
        for k in range(n_rad):
            a, b = top_rings[k][side], top_rings[k + 1][side]
            c, d = bot_rings[k + 1][side], bot_rings[k][side]
            if side == 0:
                mb.f(a, d, c, b)
            else:
                mb.f(a, b, c, d)
    return build(mb, name, smooth=True)


def make_spider_web(name="SpiderWeb", R=2.3, spokes=10, seed=3):
    """朝つゆのついたクモの巣（Blender の XZ 面 = Unity のたての面）。糸は細い管、つゆは小さな玉"""
    rnd = random.Random(seed)
    mb = MB()
    thread = hexc("#eef4fa")
    angs = [TAU * k / spokes + rnd.uniform(-0.12, 0.12) for k in range(spokes)]
    lens = [R * rnd.uniform(0.82, 1.0) for _ in range(spokes)]

    def at(k, r):
        a = angs[k % spokes]
        return Vector((math.cos(a) * r, 0.0, math.sin(a) * r))
    cf = (lambda t, a, p, d: with_alpha(thread, 0.35))
    for k in range(spokes):
        tube(mb, [Vector((0, 0, 0)), at(k, lens[k])], [0.022, 0.018], 4, cf)
    # うずまき（スポークの間は、まっすぐな糸）
    r = 0.25
    k = 0
    while r < R * 0.8:
        a, b = at(k, min(r, lens[k % spokes] * 0.95)), at(k + 1, min(r + 0.06, lens[(k + 1) % spokes] * 0.95))
        tube(mb, [a, b], [0.014, 0.014], 4, cf)
        if rnd.random() < 0.35:
            uv_sphere(mb, a.lerp(b, rnd.uniform(0.2, 0.8)), rnd.uniform(0.045, 0.07),
                      lambda n: with_alpha(hexc("#dff4ff"), 0.35), seg=8, rings=5)
        r += 0.032
        k += 1
    return build(mb, name)


def make_puffball(name="Mushroom_Puffball"):
    """ホコリタケ（ころんと丸いキノコ。てっぺんに胞子の出る穴）"""
    mb = MB()
    prof = [(0.0, 0.0), (0.38, 0.0), (0.55, 0.12), (0.82, 0.45), (0.92, 0.85), (0.86, 1.2), (0.6, 1.48), (0.22, 1.6), (0.0, 1.58)]
    rnd = random.Random(7)

    def col(t, a, p):
        c = mixc(hexc("#d9c7a2"), hexc("#f4ead2"), sstep(0.1, 1.0, p.z))
        wart = sstep(0.62, 0.8, noise.noise(p * 6.0 + Vector((3, 1, 0))))
        c = mixc(c, hexc("#b39a70"), wart * 0.6)
        hole = sstep(0.3, 0.05, math.hypot(p.x, p.y)) * sstep(1.4, 1.58, p.z)
        return mixc(c, hexc("#6d5a3e"), hole)
    lathe(mb, prof, 24, col, rfn=lambda t, a: 1.0 + 0.04 * math.sin(a * 3 + 1.0))
    return build(mb, name)


def make_drift_log(name="DriftLog", length=9.0, seed=4):
    """川岸の流木（白っぽく、すべすべ。登れる）"""
    rnd = random.Random(seed)
    mb = MB()
    n = 18
    pts, rad = [], []
    for i in range(n):
        t = i / (n - 1)
        pts.append(Vector(((t - 0.5) * length, 0.35 * math.sin(t * 2.6 + seed), 0.45 + 0.08 * math.sin(t * math.pi))))
        rad.append(lerp(0.55, 0.36, t) * (1.0 + 0.06 * math.sin(t * 17.0)))

    def bc(t, a, p, d):
        grain = 0.5 + 0.5 * math.sin(a * 9.0 + p.x * 1.4)
        c = mixc(hexc("#bdb3a0"), hexc("#ddd5c4"), grain * 0.6 + 0.2 * noise.noise(p * 1.3))
        return mixc(c, hexc("#8f8574"), sstep(0.8, 1.0, grain) * 0.4)
    rings = tube(mb, pts, rad, 14, bc)
    for end in (0, len(rings) - 1):
        ring = rings[end]
        c = mb.v(pts[end] + (pts[end] - pts[1 if end == 0 else end - 1]).normalized() * 0.04, hexc("#e8dcc0"))
        cnt = len(ring)
        for j in range(cnt):
            if end == 0:
                mb.f(c, ring[(j + 1) % cnt], ring[j])
            else:
                mb.f(c, ring[j], ring[(j + 1) % cnt])
    # 折れた枝の根もと
    base = pts[n // 3]
    bp = [base + Vector((0.2 * s, -1.6 * s, 0.25 * s)) for s in (0.0, 0.33, 0.66, 1.0)]
    tube(mb, bp, [0.28, 0.22, 0.16, 0.0], 8, bc)
    return build(mb, name)


def make_sasa_bune(name="SasaBune", L=2.6, W=0.42):
    """笹の葉で折った小舟（川を流れていく）"""
    mb = MB()
    nu, nv = 16, 6
    rows = []
    for i in range(nu + 1):
        u = i / nu
        w = W * math.sin(math.pi * u) ** 0.55
        end = 0.55 * abs(2 * u - 1) ** 3
        row = []
        for j in range(nv + 1):
            v = -1.0 + 2.0 * j / nv
            p = Vector(((u - 0.5) * L, v * w, abs(v) * 0.32 * (0.4 + 0.6 * math.sin(math.pi * u)) + end))
            c = mixc(hexc("#3f7f30"), hexc("#7fb54a"), 0.5 + 0.5 * abs(v))
            c = mixc(c, hexc("#a8c870"), sstep(0.8, 1.0, abs(2 * u - 1)))
            row.append(mb.v(p, c))
        rows.append(row)
    for i in range(nu):
        for j in range(nv):
            mb.f(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])
    return build(mb, name, solidify=0.04)


def make_water_grass(name="WaterGrass", seed=5):
    """水面にうかぶ水草のしげみ（丸い小さな葉がたくさん）"""
    rnd = random.Random(seed)
    mb = MB()
    for k in range(22):
        a = rnd.uniform(0, TAU)
        d = math.sqrt(rnd.random()) * 1.9
        r = rnd.uniform(0.22, 0.42)
        tmp = MB()
        base = mixc(hexc("#3e8a3a"), hexc("#79b84e"), rnd.random())

        def cf(rho, th, p, base=base):
            return mixc(base, hexc("#a9d47a"), 0.3 * rho)
        polar_sheet(tmp, lambda th, r=r: r * (1.0 - 0.18 * math.exp(-(th / 0.25) ** 2)), cf, n_ang=14, n_rad=2,
                    zfn=lambda rho, th: 0.02 * rho)
        mb.add(tmp, Matrix.Translation(Vector((math.cos(a) * d, math.sin(a) * d, 0.02 + k * 0.002)))
               @ Matrix.Rotation(rnd.uniform(0, TAU), 4, "Z"))
    return build(mb, name, solidify=0.03)


# ---------------------------------------------------------------------------
# メイン
# ---------------------------------------------------------------------------
def export_fbx(ob, out_dir):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(out_dir, ob.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        colors_type="SRGB",
        prioritize_active_color=True,
        add_leaf_bones=False,
        use_tspace=False,
        embed_textures=False,
        path_mode="AUTO",
    )
    return path


def main():
    out_dir, blend_out = parse_args()
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(os.path.dirname(blend_out), exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    makers = [
        make_inchworm,
        lambda: make_rock("Rock_A", 1, (1.6, 1.3, 1.05)),
        lambda: make_rock("Rock_B", 2, (1.3, 1.1, 1.3), cuts=9),
        lambda: make_rock("Rock_C", 3, (2.0, 1.4, 0.75), cuts=6, moss=0.45),
        lambda: make_rock("Rock_D", 4, (1.0, 0.9, 0.85), cuts=10, moss=0.7),
        make_mushroom_red,
        make_mushroom_brown,
        make_mushroom_cluster,
        make_mushroom_glow,
        make_stump,
        make_hollow_log,
        make_great_tree,
        lambda: make_bg_trunk("BgTrunk_A", 0.3),
        lambda: make_bg_trunk("BgTrunk_B", 2.1),
        lambda: make_twig("Twig_A", 1),
        lambda: make_twig("Twig_B", 2, 10.0),
        lambda: make_oak_leaf("Leaf_Oak_Orange", hexc("#e8892e"), hexc("#b4501f"), 1),
        lambda: make_oak_leaf("Leaf_Oak_Brown", hexc("#b88a52"), hexc("#7a5532"), 2),
        lambda: make_oak_leaf("Leaf_Oak_Green", hexc("#7dbb4a"), hexc("#c4c24a"), 3),
        lambda: make_maple_leaf("Leaf_Maple_Red", hexc("#e2452f"), hexc("#a3262a"), 4),
        lambda: make_maple_leaf("Leaf_Maple_Yellow", hexc("#f3c23c"), hexc("#e0782a"), 5),
        make_acorn,
        make_acorn_cap,
        make_pinecone,
        lambda: make_grass("Grass_A", 1),
        lambda: make_grass("Grass_B", 2, count=9, hmin=1.8, hmax=3.6),
        lambda: make_grass("Grass_C", 3, count=5, hmin=3.5, hmax=6.0),
        lambda: make_clover("Clover_A", 1),
        lambda: make_clover("Clover_B", 2, count=6),
        lambda: make_fern("Fern_A", 1),
        lambda: make_fern("Fern_B", 2, fronds=9, L=10.0, H=5.0),
        lambda: make_daisy("Daisy", 1),
        lambda: make_bellflower("Bellflower", 2),
        lambda: make_dandelion("Dandelion", 3),
        lambda: make_dandelion_puff("DandelionPuff", 4),
        lambda: make_moss("Moss", 1),
        lambda: make_sprout("Sprout", 1),
        lambda: make_strawberry("Strawberry", 1),
        lambda: make_lilypad("LilyPad", 1),
        lambda: make_lilypad("LilyPad_Col", 1, notch=False),
        lambda: make_water_lily("WaterLily"),
        lambda: make_reed("Reed", 1),
        make_dewdrop,
        make_shelf_fungus,
        make_spider_web,
        make_puffball,
        make_drift_log,
        make_sasa_bune,
        make_water_grass,
    ]
    objs = []
    for mk in makers:
        ob = mk()
        p = export_fbx(ob, out_dir)
        print("exported", ob.name, len(ob.data.vertices), "verts ->", os.path.basename(p))
        objs.append(ob)

    # .blend ではアセットを並べて見やすく配置
    x = 0.0
    for ob in objs:
        dims = ob.dimensions
        w = max(dims.x, 1.0)
        ob.location = (x + w / 2, 0, 0)
        x += w + 2.0
    bpy.ops.wm.save_as_mainfile(filepath=blend_out, compress=True)
    print("saved", blend_out)


if __name__ == "__main__":
    main()
