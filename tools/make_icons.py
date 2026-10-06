"""
UI アイコンを生成する（PIL）。4 倍で描いて縮小し、なめらかにする。
  python tools/make_icons.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "unity", "Assets", "UI", "Icons")
S = 4


def canvas(size):
    return Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))


def save(img, name, size):
    img = img.resize((size, size), Image.LANCZOS)
    img.save(os.path.join(OUT, name))
    print("wrote", name)


def drop_shape(cx, cy, r, sharp=1.25):
    """しずく形のポリゴン（上がとがる）"""
    pts = []
    for i in range(120):
        t = i / 120 * math.tau
        x = math.sin(t)
        y = -math.cos(t)
        # 上半分をとがらせる
        if y < 0:
            k = (-y) ** sharp
            x *= 1 - 0.55 * k
            y *= 1 + 0.55 * k
        pts.append((cx + x * r, cy + y * r * 1.0 + r * 0.25))
    return pts


def icon_drop():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    c = n * S / 2
    r = n * S * 0.34
    d.polygon(drop_shape(c, c, r + 6 * S), fill=(255, 255, 255, 255))
    d.polygon(drop_shape(c, c, r), fill=(70, 190, 235, 255))
    d.polygon(drop_shape(c - r * 0.08, c + r * 0.05, r * 0.72), fill=(140, 225, 255, 255))
    d.ellipse([c - r * 0.42, c - r * 0.05, c - r * 0.12, c + r * 0.35], fill=(255, 255, 255, 230))
    save(im, "icon_drop.png", n)


def icon_place():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    c = n * S / 2
    # 葉っぱ形のピン
    def leaf(cx, cy, w, h):
        pts = []
        for i in range(100):
            t = i / 100 * math.tau
            x = math.sin(t) * w * (0.55 + 0.45 * math.cos(t) ** 2) ** 0.5
            y = -math.cos(t) * h
            pts.append((cx + x, cy + y))
        return pts
    d.polygon(leaf(c, c, n * S * 0.36, n * S * 0.44), fill=(255, 248, 225, 255))
    d.polygon(leaf(c, c, n * S * 0.29, n * S * 0.37), fill=(240, 180, 70, 255))
    d.line([(c, c - n * S * 0.3), (c, c + n * S * 0.3)], fill=(255, 240, 200, 255), width=3 * S)
    for k in (-1, 1):
        d.line([(c, c - n * S * 0.05), (c + k * n * S * 0.15, c - n * S * 0.18)], fill=(255, 240, 200, 255), width=2 * S)
        d.line([(c, c + n * S * 0.1), (c + k * n * S * 0.15, c - n * S * 0.02)], fill=(255, 240, 200, 255), width=2 * S)
    save(im, "icon_place.png", n)


def icon_unknown():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    c = n * S / 2
    r = n * S * 0.4
    d.ellipse([c - r, c - r, c + r, c + r], fill=(255, 248, 225, 235))
    r2 = r * 0.82
    d.ellipse([c - r2, c - r2, c + r2, c + r2], fill=(120, 105, 85, 235))
    # ？の形
    w = 5 * S
    d.arc([c - r * 0.38, c - r * 0.55, c + r * 0.38, c + r * 0.15], start=200, end=420, fill=(255, 248, 225, 255), width=w)
    d.line([(c + r * 0.0, c + r * 0.12), (c, c + r * 0.3)], fill=(255, 248, 225, 255), width=w)
    d.ellipse([c - w * 0.6, c + r * 0.45, c + w * 0.6, c + r * 0.45 + w * 1.2], fill=(255, 248, 225, 255))
    save(im, "icon_unknown.png", n)


def icon_player():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    c = n * S / 2
    r = n * S * 0.42
    pts = [(c, c - r), (c + r * 0.72, c + r * 0.75), (c, c + r * 0.38), (c - r * 0.72, c + r * 0.75)]
    d.polygon([(x, y + 2 * S) for x, y in pts], fill=(0, 0, 0, 110))
    d.polygon(pts, fill=(255, 255, 255, 255))
    pts2 = [(c, c - r * 0.72), (c + r * 0.5, c + r * 0.55), (c, c + r * 0.25), (c - r * 0.5, c + r * 0.55)]
    d.polygon(pts2, fill=(120, 205, 80, 255))
    save(im, "icon_player.png", n)


def icon_menu():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    for k in (-1, 0, 1):
        y = n * S / 2 + k * n * S * 0.18
        d.rounded_rectangle([n * S * 0.2, y - 3 * S, n * S * 0.8, y + 3 * S], radius=3 * S, fill=(255, 248, 225, 255))
    save(im, "icon_menu.png", n)


def icon_map():
    n = 64
    im = canvas(n)
    d = ImageDraw.Draw(im)
    u = n * S
    pts = [(0.16, 0.24), (0.38, 0.16), (0.62, 0.26), (0.84, 0.18), (0.84, 0.76), (0.62, 0.84), (0.38, 0.74), (0.16, 0.82)]
    d.polygon([(x * u, y * u) for x, y in pts], fill=(255, 248, 225, 255))
    d.line([(0.38 * u, 0.16 * u), (0.38 * u, 0.74 * u)], fill=(150, 125, 90, 255), width=3 * S)
    d.line([(0.62 * u, 0.26 * u), (0.62 * u, 0.84 * u)], fill=(150, 125, 90, 255), width=3 * S)
    d.ellipse([0.46 * u, 0.42 * u, 0.56 * u, 0.52 * u], fill=(235, 110, 80, 255))
    save(im, "icon_map.png", n)


def banner_deco():
    w, h = 520, 22
    im = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cy = h * S / 2
    col = (255, 236, 180, 235)
    for side in (-1, 1):
        x0 = w * S / 2 + side * 26 * S
        x1 = w * S / 2 + side * (w * S / 2 - 10 * S)
        for i in range(60):
            t = i / 60
            xa = x0 + (x1 - x0) * t
            xb = x0 + (x1 - x0) * (t + 1 / 60)
            a = int(235 * (1 - t) ** 0.8)
            d.line([(xa, cy), (xb, cy)], fill=(255, 236, 180, a), width=2 * S)
    c = w * S / 2
    r = 9 * S
    d.polygon([(c, cy - r), (c + r, cy), (c, cy + r), (c - r, cy)], fill=col)
    r2 = 4 * S
    d.polygon([(c, cy - r2), (c + r2, cy), (c, cy + r2), (c - r2, cy)], fill=(200, 160, 80, 255))
    for side in (-1, 1):
        cx = c + side * 18 * S
        r3 = 4 * S
        d.polygon([(cx, cy - r3), (cx + r3, cy), (cx, cy + r3), (cx - r3, cy)], fill=col)
    im = im.resize((w, h), Image.LANCZOS)
    im.save(os.path.join(OUT, "banner_deco.png"))
    print("wrote banner_deco.png")


def vignette():
    w, h = 512, 288
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    for y in range(h):
        for x in range(w):
            u = (x / (w - 1) - 0.5) * 2
            v = (y / (h - 1) - 0.5) * 2
            d = math.sqrt(u * u * 0.8 + v * v * 1.1)
            a = max(0.0, min(1.0, (d - 0.35) / 0.9)) ** 1.6
            # 中央の文字の後ろはほんのり暗く
            center = max(0.0, 1 - math.sqrt(u * u * 2.2 + (v + 0.15) ** 2 * 6)) ** 2 * 0.35
            alpha = min(1.0, a * 0.75 + center)
            px[x, y] = (12, 28, 16, int(alpha * 255))
    im = im.filter(ImageFilter.GaussianBlur(2))
    im.save(os.path.join(OUT, "vignette.png"))
    print("wrote vignette.png")


def app_icon():
    """WebGL テンプレート用のアイコン（しゃくとりむしのシルエット）"""
    n = 256
    im = canvas(n)
    d = ImageDraw.Draw(im)
    u = n * S
    d.rounded_rectangle([0, 0, u, u], radius=56 * S, fill=(120, 190, 90, 255))
    d.rounded_rectangle([10 * S, 10 * S, u - 10 * S, u - 10 * S], radius=48 * S, fill=(150, 210, 110, 255))
    # Ω 字のしゃくとりむし
    pts = []
    for i in range(100):
        t = i / 99
        th = 2.0 * math.sin(2 * math.pi * t)
        pts.append(th)
    x, y = 0.0, 0.0
    path = [(x, y)]
    for i in range(1, 100):
        th = pts[i]
        x += math.cos(th) / 99
        y += math.sin(th) / 99
        path.append((x, y))
    span = path[-1][0]
    scale = u * 0.62 / max(span, 1e-3)
    ox = u * 0.5 - span * scale / 2
    oy = u * 0.72
    body = [(ox + px_ * scale, oy - py_ * scale) for px_, py_ in path]
    for i, (bx, by) in enumerate(body):
        r = 15 * S if i < 95 else 19 * S
        d.ellipse([bx - r, by - r, bx + r, by + r], fill=(70, 120, 45, 255))
    for i, (bx, by) in enumerate(body):
        r = 11 * S if i < 95 else 15 * S
        d.ellipse([bx - r, by - r, bx + r, by + r], fill=(205, 240, 120, 255))
    hx, hy = body[-1]
    d.ellipse([hx - 6 * S, hy - 8 * S, hx + 4 * S, hy + 2 * S], fill=(30, 30, 40, 255))
    d.ellipse([hx - 3 * S, hy - 6 * S, hx, hy - 3 * S], fill=(255, 255, 255, 255))
    d.line([(u * 0.12, oy + 16 * S), (u * 0.88, oy + 16 * S)], fill=(90, 140, 60, 255), width=6 * S)
    im = im.resize((n, n), Image.LANCZOS)
    tpl = os.path.join(HERE, "..", "unity", "Assets", "WebGLTemplates", "Shakutori")
    os.makedirs(tpl, exist_ok=True)
    im.save(os.path.join(tpl, "icon.png"))
    im.resize((64, 64), Image.LANCZOS).save(os.path.join(tpl, "favicon.png"))
    print("wrote app icon")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    icon_drop()
    icon_place()
    icon_unknown()
    icon_player()
    icon_menu()
    icon_map()
    banner_deco()
    vignette()
    app_icon()
