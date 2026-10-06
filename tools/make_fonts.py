"""
日本語 UI フォントを用意する。
Noto Sans JP（SIL Open Font License 1.1）の可変フォントから太字(700)と極太(900)を取り出し、
ゲーム内で使う文字だけにサブセット化して unity/Assets/UI/Fonts に置く。

  python tools/make_fonts.py [path/to/NotoSansJP-VF.ttf]

ゲーム内のテキストを増やしたら、もう一度実行してください（.cs / .uxml / .uss から文字を集めます）。
"""
import glob
import io
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "UI", "Fonts")

CANDIDATES = [
    r"C:\Windows\Fonts\NotoSansJP-VF.ttf",
    os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\Windows\Fonts\NotoSansJP-VF.ttf"),
    "/usr/share/fonts/truetype/noto/NotoSansJP-VF.ttf",
]


def collect_text():
    chars = set()
    for i in range(0x20, 0x7F):
        chars.add(chr(i))
    for start, end in ((0x3000, 0x303F), (0x3040, 0x309F), (0x30A0, 0x30FF), (0xFF00, 0xFFEF)):
        for c in range(start, end + 1):
            chars.add(chr(c))
    chars.update("◆◇○●■□▲△▼▽★☆♪→←↑↓・…—–「」『』（）【】〜ー！？、。")
    patterns = ["**/*.cs", "**/*.uxml", "**/*.uss"]
    for pat in patterns:
        for path in glob.glob(os.path.join(ROOT, "unity", "Assets", pat), recursive=True):
            with open(path, encoding="utf-8", errors="ignore") as f:
                chars.update(f.read())
    # index.html（読み込み画面）
    for path in glob.glob(os.path.join(ROOT, "unity", "Assets", "WebGLTemplates", "**", "*.html"), recursive=True):
        with open(path, encoding="utf-8", errors="ignore") as f:
            chars.update(f.read())
    return "".join(sorted(c for c in chars if ord(c) >= 0x20))


def make(src, weight, name, text):
    font = TTFont(src)
    inst = instancer.instantiateVariableFont(font, {"wght": weight}, inplace=False)
    buf = io.BytesIO()
    inst.save(buf)
    buf.seek(0)
    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.glyph_names = False
    f = subset.load_font(buf, opts)
    s = subset.Subsetter(opts)
    s.populate(text=text)
    s.subset(f)
    out = os.path.join(OUT, name + ".ttf")
    subset.save_font(f, out, opts)
    print("wrote", out, os.path.getsize(out) // 1024, "KB")


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else next((p for p in CANDIDATES if os.path.exists(p)), None)
    if not src:
        sys.exit("NotoSansJP-VF.ttf が見つかりません。https://fonts.google.com/noto/specimen/Noto+Sans+JP から入手して引数で指定してください。")
    os.makedirs(OUT, exist_ok=True)
    text = collect_text()
    print(len(text), "chars")
    make(src, 700, "ShakutoriSans-Bold", text)
    make(src, 900, "ShakutoriSans-Black", text)
    with open(os.path.join(OUT, "LICENSE-NotoSansJP.txt"), "w", encoding="utf-8") as f:
        f.write("ShakutoriSans-Bold.ttf / ShakutoriSans-Black.ttf are subsets of Noto Sans JP.\n"
                "Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'.\n"
                "Noto is a trademark of Google LLC.\n\n"
                "This Font Software is licensed under the SIL Open Font License, Version 1.1.\n"
                "https://openfontlicense.org\n")


if __name__ == "__main__":
    main()
