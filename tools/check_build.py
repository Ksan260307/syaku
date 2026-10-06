"""
WebGL ビルド（公開用フォルダ）の検査。CI のデプロイ前に実行する。
  python tools/check_build.py <site_dir>
確認すること:
 - index.html と Build/ の必須ファイル（loader / data / framework / wasm）がそろっている
 - index.html が実在するローダーを参照している
 - GitHub の 100MB 制限を超えるファイルがない
 - 配布してはいけないデバッグ情報（*_DoNotShip）が含まれていない
"""
import os
import re
import sys


def fail(msg):
    print(f"::error::{msg}")
    sys.exit(1)


def main():
    site = sys.argv[1] if len(sys.argv) > 1 else "docs"
    index = os.path.join(site, "index.html")
    if not os.path.isfile(index):
        fail(f"{index} がありません")
    build = os.path.join(site, "Build")
    if not os.path.isdir(build):
        fail(f"{build} がありません")
    files = os.listdir(build)
    need = {
        "loader": [f for f in files if f.endswith(".loader.js")],
        "data": [f for f in files if ".data" in f],
        "framework": [f for f in files if ".framework.js" in f],
        "wasm": [f for f in files if ".wasm" in f],
    }
    for k, v in need.items():
        if not v:
            fail(f"Build/ に {k} ファイルがありません: {files}")
    with open(index, encoding="utf-8") as f:
        html = f.read()
    m = re.search(r'buildUrl \+ "/([^"]+\.loader\.js)"', html)
    if not m:
        fail("index.html にローダーの参照がありません（テンプレートが展開されていない？）")
    if m.group(1) not in files:
        fail(f"index.html が参照する {m.group(1)} がありません")
    if "{{{" in html:
        fail("index.html に未展開のテンプレート変数が残っています")
    total = 0
    for root, dirs, fs in os.walk(site):
        for d in dirs:
            if d.endswith("DoNotShip"):
                fail(f"配布しないフォルダが含まれています: {os.path.join(root, d)}")
        for fn in fs:
            size = os.path.getsize(os.path.join(root, fn))
            total += size
            if size > 95 * 1024 * 1024:
                fail(f"{fn} が大きすぎます ({size / 1024 / 1024:.1f} MB)")
    print(f"OK: {site} ({total / 1024 / 1024:.1f} MB) loader={m.group(1)}")


if __name__ == "__main__":
    main()
