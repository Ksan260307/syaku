# Blender アセット

`scripts/build_forest_kit.py` が、ゲームで使うすべてのモデルをプロシージャルに作ります。

```bash
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup --python blender/scripts/build_forest_kit.py
```

- 出力: `unity/Assets/Art/Models/*.fbx`（Unity 用）と `blender/forest_kit.blend`（編集・確認用）
- 色はすべて頂点カラー `Col`（sRGB）。Unity のトゥーンシェーダーで描画します
- 単位: 1 = しゃくとりむしの体長（実寸で約 2.5cm）
- しゃくとりむし本体（`Inchworm`）は、まっすぐな状態で +Y 方向に長さ 1。
  変形用のデータを UV に書き込んでいます：
  - UV1 = (体のどこか s, 法線の前後成分)
  - UV2 = (断面内の横位置, 断面内の上下位置)
  - UV3 = (法線の横成分, 法線の上下成分)

`scripts/render_previews.py` で各アセットのプレビュー画像を作れます。
