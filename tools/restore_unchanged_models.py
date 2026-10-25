"""Blender のキットを作りなおすと、すべての FBX・図鑑の絵が書きかわる（中の ID や時刻だけのちがい）。
中身が変わらなかった FBX（Blender で読みこんで、頂点・面・色・法線・UV をくらべる）と PNG を、HEAD にもどす。
色だけを変えたときも、バイト数のちがいは小さいので、バイトでくらべてはいけない。
  python tools/restore_unchanged_models.py   （リポジトリのいちばん上で実行）"""
import io, os, subprocess, sys, tempfile
from PIL import Image, ImageChops
S = os.path.dirname(os.path.abspath(__file__))
BLENDER = "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
out = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True).stdout.splitlines()
tmp = os.path.join(tempfile.gettempdir(), "shaku_fbxcmp"); os.makedirs(tmp, exist_ok=True)
fbx_list, kept, restored = [], [], []
for line in out:
    if not line.startswith(" M "):
        continue
    path = line[3:].strip()
    if path.endswith(".png") and "/UI/" in path:
        old = subprocess.run(["git", "show", "HEAD:" + path], capture_output=True).stdout
        a = Image.open(io.BytesIO(old)).convert("RGBA"); b = Image.open(path).convert("RGBA")
        if a.size == b.size and max(e[1] for e in ImageChops.difference(a, b).getextrema()) <= 3:
            subprocess.run(["git", "checkout", "--", path]); restored.append(path)
        else:
            kept.append(path)
    elif path.endswith(".fbx"):
        old = subprocess.run(["git", "show", "HEAD:" + path], capture_output=True).stdout
        op = os.path.join(tmp, os.path.basename(path))
        open(op, "wb").write(old)
        fbx_list.append(f"{op}|{os.path.abspath(path)}|{path}")
if fbx_list:
    lf = os.path.join(tmp, "list.txt"); of = os.path.join(tmp, "out.txt")
    open(lf, "w", encoding="utf-8").write("\n".join(fbx_list))
    subprocess.run([r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe", "-b", "--factory-startup", "--python",
                    os.path.join(S, "fbx_same.py"), "--", lf, of], capture_output=True)
    for l in open(of, encoding="utf-8"):
        st, p = l.strip().split(" ", 1)
        if st == "SAME":
            subprocess.run(["git", "checkout", "--", p]); restored.append(p)
        else:
            kept.append(p)
print("restored", len(restored)); print("changed", len(kept))
for k in kept: print("  ", k)
