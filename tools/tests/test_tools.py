"""
tools/ のスクリプトの単体テスト（音の合成・アイコン・フォント・ビルド検査）。
  python -m unittest discover -s tools/tests -v
"""
import importlib.util
import io
import os
import shutil
import sys
import tempfile
import unittest
import wave
from contextlib import redirect_stdout

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.abspath(os.path.join(HERE, ".."))
ROOT = os.path.abspath(os.path.join(TOOLS, ".."))


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(TOOLS, name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


audio = load("make_audio")
icons = load("make_icons")
check = load("check_build")


class AudioTests(unittest.TestCase):
    def test_midi_a4_is_440(self):
        self.assertAlmostEqual(audio.midi(69), 440.0)
        self.assertAlmostEqual(audio.midi(81), 880.0)

    def test_shaped_noise_is_normalized_and_loops(self):
        x = audio.shaped_noise(44100, 1.0, 100, 2000)
        self.assertEqual(len(x), 44100)
        self.assertAlmostEqual(float(np.max(np.abs(x))), 1.0, places=5)
        # FFT 合成なので端と端がつながる（つなぎ目の段差が小さい）
        self.assertLess(abs(x[0] - x[-1]), 0.2)

    def test_envelope_shape(self):
        e = audio.env_adsr(44100, 0.1, 0.1, 0.5, 0.2)
        self.assertAlmostEqual(e[0], 0.0, places=3)
        self.assertAlmostEqual(float(np.max(e)), 1.0, places=2)
        self.assertAlmostEqual(e[-1], 0.0, places=2)
        self.assertTrue(np.all(e >= 0))

    def test_instruments_decay(self):
        for sig in (audio.kalimba(440.0), audio.celesta(880.0)):
            head = np.max(np.abs(sig[:4410]))
            tail = np.max(np.abs(sig[-4410:]))
            self.assertGreater(head, tail * 5)

    def test_reverb_keeps_length_and_channels(self):
        x = np.zeros((44100, 2))
        x[100, :] = 1.0
        y = audio.circular_reverb(x, seconds=0.5, mix=0.4)
        self.assertEqual(y.shape, x.shape)
        self.assertGreater(np.abs(y[2000:20000]).sum(), 0.0, "残響が残る")

    def test_add_wraps_for_loops(self):
        buf = np.zeros((100, 2))
        audio.add(buf, 95, np.ones(10), pan=0.0, gain=1.0, wrap=True)
        self.assertGreater(buf[2, 0], 0.0, "末尾からはみ出た音は先頭へ回り込む")
        buf2 = np.zeros((100, 2))
        audio.add(buf2, 95, np.ones(10), wrap=False)
        self.assertEqual(buf2[2, 0], 0.0)

    def test_write_produces_stereo_wav(self):
        tmp = tempfile.mkdtemp()
        old = audio.OUT
        try:
            audio.OUT = tmp
            with redirect_stdout(io.StringIO()):
                audio.write("t.wav", np.sin(np.linspace(0, 100, 4410)))
            with wave.open(os.path.join(tmp, "t.wav")) as w:
                self.assertEqual(w.getnchannels(), 2)
                self.assertEqual(w.getframerate(), 44100)
                self.assertEqual(w.getnframes(), 4410)
        finally:
            audio.OUT = old
            shutil.rmtree(tmp)

    def test_chord_notes(self):
        self.assertEqual(audio.chord_notes("F"), (5, [5, 9, 12]))
        self.assertEqual(audio.chord_notes("Dm"), (2, [2, 5, 9]))

    def test_generated_audio_assets_exist(self):
        d = os.path.join(ROOT, "unity", "Assets", "Audio")
        for n in ["music_forest", "music_river", "music_park", "ambience_forest", "step_1", "step_2", "step_3", "collect", "discover", "click", "silk", "land", "complete",
                  "ambience_river", "creature", "travel", "unlock", "caw", "fall", "splash", "rare", "chirp", "croak"]:
            p = os.path.join(d, n + ".wav")
            self.assertTrue(os.path.isfile(p), p)
            with wave.open(p) as w:
                self.assertGreater(w.getnframes(), 1000)


    def test_bubble_rises_in_pitch(self):
        b = audio.bubble(500.0, 0.04)
        self.assertEqual(len(b), int(0.04 * audio.SR))
        half = len(b) // 2
        # 後半のほうがゼロ交差が多い（音程が上がる）
        zc = lambda x: int(np.sum(np.abs(np.diff(np.sign(x))) > 0))
        self.assertGreater(zc(b[half:]), zc(b[:half]))
        self.assertLess(abs(b[-1]), 0.2, "減衰して終わる")

    def test_crow_caw_is_a_harsh_mid_voice(self):
        c = audio.crow_caw(0.36, 560.0)
        spec = np.abs(np.fft.rfft(c))
        f = np.fft.rfftfreq(len(c), 1 / audio.SR)
        centroid = float(np.sum(f * spec) / np.sum(spec))
        self.assertGreater(centroid, 700.0)
        self.assertLess(centroid, 3500.0)
        self.assertLess(abs(c[0]), 0.05, "立ち上がりはなめらか")

    def test_river_ambience_loops_seamlessly(self):
        p = os.path.join(ROOT, "unity", "Assets", "Audio", "ambience_river.wav")
        with wave.open(p) as w:
            self.assertEqual(w.getnchannels(), 2)
            n = w.getnframes()
            self.assertGreater(n / w.getframerate(), 30.0)
            x = np.frombuffer(w.readframes(n), dtype=np.int16).reshape(-1, 2).astype(np.float64) / 32767
        jump = np.abs(x[0] - x[-1]).max()
        typical = np.abs(np.diff(x[:, 0])).mean() * 6 + 0.05
        self.assertLess(jump, typical + 0.15, "ループのつなぎ目で音がとばない")


class IconTests(unittest.TestCase):
    def test_drop_shape_is_closed_polygon(self):
        pts = icons.drop_shape(100, 100, 40)
        self.assertEqual(len(pts), 120)
        ys = [p[1] for p in pts]
        self.assertLess(min(ys), 100 - 40, "上がとがっている")

    def test_icons_are_written(self):
        tmp = tempfile.mkdtemp()
        old = icons.OUT
        try:
            icons.OUT = tmp
            with redirect_stdout(io.StringIO()):
                for fn in (icons.icon_drop, icons.icon_place, icons.icon_unknown, icons.icon_player, icons.icon_menu, icons.icon_map, icons.banner_deco,
                           icons.icon_bug, icons.icon_book, icons.icon_fullscreen, icons.icon_lock, icons.icon_gate):
                    fn()
            from PIL import Image
            for n in ["icon_drop.png", "icon_place.png", "icon_unknown.png", "icon_player.png", "icon_menu.png", "icon_map.png", "banner_deco.png",
                      "icon_bug.png", "icon_book.png", "icon_fullscreen.png", "icon_lock.png", "icon_gate.png"]:
                im = Image.open(os.path.join(tmp, n))
                self.assertEqual(im.mode, "RGBA")
                alpha = np.array(im)[:, :, 3]
                self.assertGreater(alpha.max(), 200, n)
                self.assertEqual(alpha[0, 0], 0, f"{n} の角は透明")
        finally:
            icons.OUT = old
            shutil.rmtree(tmp)

    def test_ui_icons_referenced_by_uss_exist(self):
        import re
        uss = read(os.path.join(ROOT, "unity", "Assets", "UI", "GameUI.uss"))
        for rel in re.findall(r'url\("([^"]+)"\)', uss):
            self.assertTrue(os.path.isfile(os.path.join(ROOT, "unity", "Assets", "UI", rel)), rel)


class FontTests(unittest.TestCase):
    def test_collect_text_includes_game_text(self):
        fonts = load("make_fonts")
        text = fonts.collect_text()
        for ch in "しゃくとりの森赤キノコ切り株名所糸背伸びＡ":
            self.assertIn(ch, text)

    def test_font_subsets_cover_ui(self):
        from fontTools.ttLib import TTFont
        p = os.path.join(ROOT, "unity", "Assets", "UI", "Fonts", "ShakutoriSans-Bold.ttf")
        cmap = TTFont(p).getBestCmap()
        uxml = read(os.path.join(ROOT, "unity", "Assets", "UI", "GameUI.uxml"))
        missing = sorted({c for c in uxml if ord(c) >= 0x3000 and ord(c) not in cmap})
        self.assertEqual(missing, [], "フォントにない文字")


class CheckBuildTests(unittest.TestCase):
    def make_site(self, loader="Game.loader.js", template_ok=True, extra_dir=None, jslib=True):
        site = tempfile.mkdtemp()
        os.makedirs(os.path.join(site, "Build"))
        for n in (loader, "Game.data.unityweb", "Game.framework.js.unityweb", "Game.wasm.unityweb"):
            with open(os.path.join(site, "Build", n), "wb") as f:
                f.write(b"function _ShakuRegisterPageEvents(){}" if "framework" in n and jslib else b"x" * 10)
        html = 'const buildUrl = "Build"; const loaderUrl = buildUrl + "/Game.loader.js";'
        if not template_ok:
            html += " {{{ DATA_FILENAME }}}"
        with open(os.path.join(site, "index.html"), "w", encoding="utf-8") as f:
            f.write(html)
        if extra_dir:
            os.makedirs(os.path.join(site, extra_dir))
        return site

    def run_check(self, site, cleanup=True):
        # cleanup: テスト用に作った一時フォルダを消す（本物の docs/ は消さない）
        old = sys.argv
        sys.argv = ["check_build.py", site]
        try:
            with redirect_stdout(io.StringIO()):
                check.main()
            return 0
        except SystemExit as e:
            return e.code
        finally:
            sys.argv = old
            if cleanup:
                shutil.rmtree(site, ignore_errors=True)

    def test_valid_site_passes(self):
        self.assertEqual(self.run_check(self.make_site()), 0)

    def test_missing_loader_fails(self):
        self.assertEqual(self.run_check(self.make_site(loader="Other.loader.js")), 1)

    def test_unexpanded_template_fails(self):
        self.assertEqual(self.run_check(self.make_site(template_ok=False)), 1)

    def test_debug_info_fails(self):
        self.assertEqual(self.run_check(self.make_site(extra_dir="Game_BurstDebugInformation_DoNotShip")), 1)

    def test_missing_jslib_fails(self):
        self.assertEqual(self.run_check(self.make_site(jslib=False)), 1)

    def test_real_build_has_jslib_and_manifest(self):
        docs = os.path.join(ROOT, "docs")
        if os.path.isdir(os.path.join(docs, "Build")):
            self.assertEqual(self.run_check(docs, cleanup=False), 0)
            self.assertTrue(os.path.isdir(os.path.join(docs, "Build")), "検査で docs/ を消さない")

    def test_missing_index_fails(self):
        site = tempfile.mkdtemp()
        self.assertEqual(self.run_check(site), 1)


class WorkflowTests(unittest.TestCase):
    def test_workflow_deploys_pages(self):
        p = os.path.join(ROOT, ".github", "workflows", "deploy-pages.yml")
        s = read(p)
        for key in ("actions/deploy-pages", "actions/upload-pages-artifact", "game-ci/unity-builder", "game-ci/unity-test-runner",
                    "Shakutori.EditorTools.BuildScript.BuildWebGL", "tools/check_build.py", "pages: write", "id-token: write"):
            self.assertIn(key, s)


if __name__ == "__main__":
    unittest.main()
