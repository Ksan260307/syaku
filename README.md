# しゃくとりの森 — Inchworm Forest

小さな **しゃくとりむし** になって、アニメ調の森をのんびり探検する 3D オープンワールドゲームです。
戦闘はありません。体を「Ω」の字に曲げては伸ばす、本物そっくりの歩き方で、森の名所と「森のしずく」を探します。

- **エンジン**: Unity 6 (6000.5.4f1) / URP / UI Toolkit / Input System
- **モデル**: Blender 5.2 の Python スクリプトですべてプロシージャル生成（`blender/scripts/build_forest_kit.py`）
- **音**: BGM・環境音・効果音も Python (numpy) で合成（`tools/make_audio.py`）
- **公開**: GitHub Actions → GitHub Pages（WebGL）

## あそびかた

| 操作 | キーボード・マウス | ゲームパッド | タッチ |
| --- | --- | --- | --- |
| すすむ | W A S D / 矢印 | 左スティック | 左下のスティック |
| 見まわす | ドラッグ（Q / R キーでも回転） | 右スティック | 画面をドラッグ |
| ズーム | ホイール | LB / RB | 2本指 |
| はやく | Shift | RT / B | 「はやく」 |
| 背伸び | E（長押し） | Y / LT | 「背伸び」 |
| 糸でおりる / のぼる | Space（長押しでのぼる） | A | 「糸」 |
| 地図 | M | View | 右上の地図ボタン |
| メニュー | Esc | Menu | 右上のボタン |

- **森のしずく**（45個）を集め、**9つの名所**を見つけるとクリアです。
- キノコの柄・切り株の壁・葉っぱの裏…しゃくとりむしは**どんな面でも這って登れます**。
- 高い所では **糸を出してぶら下がり**、ゆっくり下りられます（長押しで糸をのぼって戻れます）。
- 進行状況はブラウザに自動保存されます。

### 名所

目覚めの苔原 / 大樹の根元 / 光るキノコの洞 / 赤キノコの森 / 花の草原 / 鏡の水たまり / 古い切り株の頂 / どんぐり広場 / 朽ちた丸太のトンネル

## しくみ（実装メモ）

### しゃくとりむしの動き（`unity/Assets/Scripts/Player`）
シャクガの幼虫は胸脚（前の3対）と腹脚（後ろの2対）しかなく、中間の脚がありません。そのため

1. **引き寄せ**: 胸脚でつかまったまま、腹脚を胸のすぐ後ろまで引き寄せる → 体が Ω 字に持ち上がる
2. **伸び**: 腹脚でつかまったまま、前半身を持ち上げて前へ伸ばし、胸脚を次の場所へ下ろす

をくり返して進みます。ゲームでもこの2段階を交互に行い、体の中心線を
接線角 `θ(u) = a·sin(2πu)` の曲線（弾性体の座屈形に近い形）で作っています。
両端の距離 D と体長 L の比が第1種ベッセル関数 `J0(a)` になることを使い、表引きで `a` を解いています（`BodyCurve.cs`）。

Blender で作ったまっすぐなメッシュには、体のどの位置か・断面内の位置・法線を UV1〜UV3 に書き込んであり、
Unity 側で毎フレームこの曲線に沿って曲げています（`InchwormBody.cs`）。
次に手（脚）を置く場所は、表面に沿って少しずつ進むレイキャストで探すので、床→壁（凹んだ角）や崖のふち（出っ張った角）、裏側まで這って回り込めます（`SurfaceProbe.cs`）。

### 絵づくり（`unity/Assets/Shaders`）
- `ToonLit`: 2階調 + 柔らかい境界のトゥーン、青紫がかった影色、リムライト、アニメ風ハイライト、裏面法の輪郭線
- `ToonFoliage`: 草花用。風でそよぎ、しゃくとりむしが通ると草がかき分けられる。逆光で葉が透ける
- `ToonWater` / `ForestSky`（樹冠のすき間から見える空）/ `LightShaft`（木漏れ日の光の筋）/ `Dewdrop`
- 太陽光に木漏れ日のライトクッキー、ブルーム・色調整のポストエフェクト

### 森（`unity/Assets/Scripts/World`）
地形・小物の配置はシード値から決定的に生成します（`WorldGenerator.cs`, `ForestLayout.cs`）。
1 単位 = しゃくとりむしの体長（実寸で約 2.5cm）なので、どんぐりは岩、キノコは塔、切り株は山のような大きさです。
大量の草花・小物は GPU インスタンシング＋セル単位のカリングで描画しています（`InstancedRenderer.cs`）。

## フォルダ構成

```
blender/scripts/       Blender でモデルを生成するスクリプト（forest_kit.blend も出力）
tools/                 アイコン・フォント・音の生成、ローカルビルド用スクリプト
unity/                 Unity プロジェクト
  Assets/Art/Models    Blender から出力した FBX
  Assets/Scripts       ゲームのコード
  Assets/Shaders       トゥーンシェーダーなど
  Assets/UI            UI Toolkit (UXML/USS)・アイコン・フォント
  Assets/Editor        プロジェクトの自動セットアップとビルド
docs/                  ビルド済み WebGL（GitHub Pages の公開用）
.github/workflows/     GitHub Pages への自動デプロイ
```

## 開発

### アセットを作り直す
```bash
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup --python blender/scripts/build_forest_kit.py
python tools/make_icons.py
python tools/make_fonts.py
python tools/make_audio.py
```

### Unity で開く
Unity Hub で `unity/` フォルダを開き、`Assets/Scenes/Forest.unity` を再生します。
メニュー **Shakutori > Setup Project** でマテリアル・シーンを作り直せます。
**Shakutori > Preview World In Scene** で、再生せずに森をシーンビューで確認できます。

### ローカルでビルドして docs/ を更新
```powershell
powershell -ExecutionPolicy Bypass -File tools/build_webgl.ps1
```
`-Assets` を付けるとモデル・音などの生成から、`-Setup` を付けると Unity のセットアップからやり直します。

## テスト

```powershell
powershell -ExecutionPolicy Bypass -File tools/run_tests.ps1            # すべて
powershell -ExecutionPolicy Bypass -File tools/run_tests.ps1 -Only PlayMode
```

| 種類 | 場所 | 内容 |
| --- | --- | --- |
| 単体（Unity EditMode, 127件） | `unity/Assets/Tests/EditMode` | 数学関数、地形の高さ・色・名所・小道、体の曲線（Ω ループ・背伸び・ぶら下がり・J0 の解）、表面探索（床・壁・崖のふち・境界・水）、メッシュ変形（元の形の再現・左右反転判定）、インスタンス描画、保存と読み込み、しずく・名所の判定、入力、URP/プレイヤー設定、シーンの配線、マテリアルとシェーダーのコンパイル、UI 要素と USS、フォントの文字カバー、森の生成（しずく45個の配置・決定性・大樹のアーチ・丸太の空洞・葉の舟） |
| 総合（Unity PlayMode, 30件） | `unity/Assets/Tests/PlayMode` | 実際のシーンを読み込み、画面スティックと同じ経路で操作：読み込み→タイトル→開始、尺取り歩行（Ω 字に持ち上がるか）、ダッシュ、旋回、後ろ向き、背伸び、アイドルの見回し、切り株の壁登り、糸で下りる／のぼって戻る、森の外に出られない、水に入れない、カメラ、しずく収集（触れる／歩いて取る）、名所の発見（頂上・トンネル）、ミニマップ、ポーズ、地図、セーブと再開、最初からやり直す、画質切替、全部集めてクリア |
| 単体（Python, 20件） | `tools/tests` | 音の合成、アイコン、フォントのサブセット、ビルド検査、ワークフロー |
| 単体（Blender, 8件） | `blender/tests` | しゃくとりむしの変形用 UV、各モデルの生成・頂点カラー・法線の向き・大きさ |

GitHub Actions では Python のテストを毎回、Unity のテスト（EditMode + PlayMode）はライセンスの Secrets がある場合にビルド前に実行します。

## 公開（CI/CD）

`main` に push すると `.github/workflows/deploy-pages.yml` が動き、GitHub Pages に公開されます。

1. GitHub のリポジトリ設定 **Settings → Pages → Build and deployment → Source** を **GitHub Actions** にする（初回のみ）
2. そのままでも、リポジトリに入っているビルド済みの `docs/` が公開されます
3. GitHub Actions 上で Unity からビルドしたい場合は、**Settings → Secrets and variables → Actions** に次を登録します
   - `UNITY_LICENSE` … Unity Hub でライセンスを有効化したときにできる `Unity_lic.ulf` の中身
     （Windows: `C:\ProgramData\Unity\Unity_lic.ulf`）
   - `UNITY_EMAIL` / `UNITY_PASSWORD` … Unity アカウント
   - （Pro ライセンスの場合は `UNITY_SERIAL`）

   登録すると、push のたびに [game-ci/unity-builder](https://game.ci/) で WebGL をビルドして公開します。

## クレジット

- フォント: Noto Sans JP（SIL Open Font License 1.1）をサブセット化して使用 — `unity/Assets/UI/Fonts/LICENSE-NotoSansJP.txt`
- モデル・テクスチャ・音楽・効果音: すべてこのリポジトリのスクリプトで生成
