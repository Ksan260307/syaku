using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：PC・スマホ・タブレットの、いろいろな画面の大きさ（文字を大きくしたときも）で、
    /// どの画面の文字も、はみ出したり、切れたり、わくの外に出たりしない。
    /// </summary>
    public class UiFitTests
    {
        public struct ScreenCase
        {
            public string name;
            public int w, h;
            public bool touch, big;
            public ScreenCase(string name, int w, int h, bool touch, bool big = false)
            {
                this.name = name;
                this.w = w;
                this.h = h;
                this.touch = touch;
                this.big = big;
            }
            public override string ToString() => name;
        }

        public static readonly ScreenCase[] Cases =
        {
            new ScreenCase("PC 16:9", 1280, 720, false),
            new ScreenCase("PC 16:9 文字大", 1280, 720, false, true),
            new ScreenCase("PC 4:3", 1024, 768, false),
            new ScreenCase("PC 4:3 文字大", 1024, 768, false, true),
            new ScreenCase("PC 21:9", 2560, 1080, false),
            new ScreenCase("PC 縦長ウィンドウ", 900, 1100, false),
            new ScreenCase("スマホ横", 844, 390, true),
            new ScreenCase("スマホ横 文字大", 844, 390, true, true),
            new ScreenCase("小さいスマホ横", 667, 375, true),
            new ScreenCase("スマホ縦", 390, 844, true),
            new ScreenCase("スマホ縦 文字大", 390, 844, true, true),
            new ScreenCase("タブレット横", 1024, 768, true),
            new ScreenCase("タブレット横 文字大", 1024, 768, true, true),
            new ScreenCase("タブレット縦", 768, 1024, true),
        };

        readonly List<string> _problems = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _problems.Clear();
            yield return Boot();
        }

        [TearDown]
        public void TearDown()
        {
            if (UI != null)
            {
                UI.EmulateScreen(null);
                UI.ForceTouch = null;
                UI.SetPhotoMode(false);
            }
            SaveSystem.Settings.textSize = 0;
            ResetInput();
            Time.timeScale = 1f;
        }

        IEnumerator Use(ScreenCase c)
        {
            SaveSystem.Settings.textSize = c.big ? 1 : 0;
            UI.ForceTouch = c.touch;
            UI.EmulateScreen(new Vector2Int(c.w, c.h));
            yield return Settle();
        }

        static IEnumerator Settle(float seconds = 0.7f)
        {
            // 出し入れのアニメーション（transition）が終わるまで待つ。メニュー中は時間が止まるので、実時間で待つ
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        void Check(ScreenCase c, string screen)
        {
            _problems.AddRange(Overflows(UI.Root).Select(p => $"[{c.name}] {screen}: {p}"));
            Capture(c, screen);
        }

        /// <summary>SHAKU_UIFIT_SHOTS に書き出し先があれば、UI だけの絵を PNG で残す（目で見て確かめる用）。</summary>
        static void Capture(ScreenCase c, string screen)
        {
            string dir = Environment.GetEnvironmentVariable("SHAKU_UIFIT_SHOTS");
            var rt = UI.EmulatedTarget;
            if (string.IsNullOrEmpty(dir) || rt == null) return;
            System.IO.Directory.CreateDirectory(dir);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                // すきとおった所は、うすい灰色の上にかさねる
                var p = px[i];
                int a = p.a;
                px[i] = new Color32((byte)((p.r * a + 96 * (255 - a)) / 255), (byte)((p.g * a + 110 * (255 - a)) / 255), (byte)((p.b * a + 96 * (255 - a)) / 255), 255);
            }
            tex.SetPixels32(px);
            string file = $"{c.name}_{screen}".Replace(" ", "_").Replace(":", "-").Replace("/", "-");
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        void Report()
        {
            foreach (var p in _problems) Debug.Log("UIFIT " + p);
            Assert.IsEmpty(_problems, "はみ出し・切れ：\n" + string.Join("\n", _problems.Take(60)));
        }

        // ------------------------------------------------------------------
        // テスト
        // ------------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator TitleAndLoading_FitEveryScreen()
        {
            Assert.IsNotNull(s_ComputedStyle, "字の自動の大きさを調べられる");
            var tab = UI.Root.Q<Button>("tab-zukan");
            Assert.Greater(AutoSizeMin(tab), 0f, "タブの字は、入らなければ小さくなる");
            Assert.AreEqual(GameManager.GameState.Title, GM.State);
            string longestTip = GameUI.Tips.Concat(GameUI.AreaTips.Values.SelectMany(t => t)).OrderByDescending(t => t.Length).First();
            foreach (var c in Cases)
            {
                yield return Use(c);
                Check(c, "タイトル");
                UI.ShowCredits(true);
                yield return Settle();
                Check(c, "クレジット");
                UI.ShowCredits(false);
                UI.ShowHowto(true);
                yield return Settle();
                Check(c, "あそびかた");
                UI.ShowHowto(false);
                UI.ShowTutorialChoice(true);
                yield return Settle();
                Check(c, "チュートリアルをうけるか");
                UI.ShowTutorialChoice(false);
                UI.ShowLoading();
                UI.Root.Q<Label>("loading-tip").text = longestTip;
                yield return Settle();
                Check(c, "読み込み");
                UI.HideLoading();
                yield return Settle(0.2f);
            }
            Report();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator GameScreens_FitEveryScreen_AtTheStart()
        {
            // はじめ：何も集めていない（きせかえは、ほとんど「？？？」）
            yield return StartNewGame();
            foreach (var c in Cases)
            {
                yield return Use(c);
                yield return CheckGameScreens(c, "はじめ");
            }
            Report();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator GameScreens_FitEveryScreen_WithEverythingCollected()
        {
            // ぜんぶ集めた：きせかえの名前・図鑑の説明・きろくが、いちばん長くなる
            yield return StartNewGame();
            FillProgress();
            foreach (var c in Cases)
            {
                yield return Use(c);
                yield return CheckGameScreens(c, "ぜんぶ");
            }
            Report();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TutorialScreens_FitEveryScreen()
        {
            // チュートリアル：カード（いちばん長い説明と、ひとこと）・メニュー（「チュートリアルをおわって、森へ」）・地図（ほかのエリアは出さない）
            GM.StartGame(false, tutorial: true);
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && GM.InTutorial && Worm.InputEnabled, 180f, "練習の森");
            GM.TutorialGuide.Stop();
            string title = "", text = "";
            foreach (var step in Tutorial.Steps)
                foreach (var device in new[] { "kb", "pad", "touch" })
                {
                    var (t, x) = Tutorial.Texts(step, device);
                    if (t.Length > title.Length) title = t;
                    if (x.Length > text.Length) text = x;
                }
            foreach (var c in Cases)
            {
                yield return Use(c);
                UI.ShowTutorialCard(true);
                UI.SetTutorialStep(12, 12, title, text, "メニューから、いつでもチュートリアルをおえて、森へ行けます");
                typeof(GameUI).GetMethod("SetHelpVisible", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(UI, new object[] { true });
                yield return Settle();
                Check(c, "チュートリアル");
                _problems.AddRange(HudOverlaps(UI.Root).Select(p => $"[{c.name}] チュートリアル: {p}"));
                GM.Pause();
                yield return Settle();
                Check(c, "チュートリアルのメニュー");
                GM.Resume();
                UI.ShowMap(true);
                yield return Settle();
                Check(c, "チュートリアルの地図");
                UI.ShowMap(false);
                UI.ShowTutorialCard(false);
                yield return Settle(0.2f);
            }
            Report();
        }

        IEnumerator CheckGameScreens(ScreenCase c, string stage)
        {
            UI.ShowComplete(false, "");   // ぜんぶ集めると、ゲームが「めぐり終えた」を出すので閉じておく
            UI.RefreshCounts(true);
            // 操作説明は、しばらくすると消えるので、出しておく
            typeof(GameUI).GetMethod("SetHelpVisible", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(UI, new object[] { true });
            yield return Settle();
            Check(c, $"{stage} あそび中");
            _problems.AddRange(HudOverlaps(UI.Root).Select(p => $"[{c.name}] {stage} あそび中: {p}"));

            // おしらせ・発見の見出し・いきもののカード・手元の案内（どれも、いちばん長い文で）
            var lm = Areas.All.SelectMany(a => a.Landmarks).OrderByDescending(l => l.name.Length * 4 + l.description.Length).First();
            UI.ShowBanner(lm);
            var sp = SpeciesCatalog.All.OrderByDescending(s => s.name.Length * 4 + s.description.Length).First();
            UI.ShowCreature(sp);
            UI.Toast("しぐさを観察した：「いちばん長いおしらせの文を、ここに出してみる」");
            SetPrompt("Space", "長押しで糸をのぼる　／　Shift ではやく　／　E で糸を切る");
            yield return Settle(1.2f);
            Check(c, $"{stage} 見出し・カード・おしらせ");
            SetPrompt("", "");

            GM.Pause();
            yield return Settle();
            Check(c, $"{stage} メニュー");
            var settings = UI.Root.Q<ScrollView>("settings-scroll");
            settings.scrollOffset = new Vector2(0f, 99999f);
            yield return Settle(0.2f);
            Check(c, $"{stage} メニュー（下まで）");
            GM.Resume();
            yield return Settle(0.2f);

            UI.ShowMap(true);
            yield return Settle();
            Check(c, $"{stage} 地図");
            UI.ShowMap(false);

            foreach (var tab in new[] { "zukan", "skin", "record" })
            {
                UI.ShowComplete(false, "");
                UI.ShowCollection(true, tab);
                if (tab == "zukan") UI.SelectSpecies(sp.id);
                yield return Settle();
                Check(c, $"{stage} {tab}");
                var scroll = tab == "skin" ? UI.Root.Q<ScrollView>("skin-scroll") : tab == "record" ? UI.Root.Q<ScrollView>("record-list") : UI.Root.Q<ScrollView>("zukan-detail");
                scroll.scrollOffset = new Vector2(0f, 99999f);
                yield return Settle(0.2f);
                Check(c, $"{stage} {tab}（下まで）");
            }
            UI.ShowCollection(false);

            // 確認（いちばん長いのは「最初からやり直す」）
            GM.Pause();
            yield return Settle(0.2f);
            var reset = UI.Root.Q<Button>("reset");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = reset;
                reset.SendEvent(e);
            }
            yield return Settle();
            Assert.IsTrue(UI.IsConfirmOpen, "確認が出る");
            Check(c, $"{stage} 確認");
            UI.CloseAllOverlays();
            GM.Resume();

            UI.ShowComplete(true, "すべてをめぐり終えました！",
                $"しずく {Areas.TotalDrops} 個、名所 {Areas.TotalLandmarks} か所、いきもの {SpeciesCatalog.Count} 種を、すべて見つけました。\n" +
                "探検した時間：約 9999 分\n\nごほうびに、きせかえ「にじいろ」が使えるようになりました。");
            yield return Settle();
            Check(c, $"{stage} めぐり終えた");
            UI.ShowComplete(false, "");

            UI.SetPhotoMode(true);
            yield return Settle();
            Check(c, $"{stage} 写真モード");
            UI.SetPhotoMode(false);
            UI.CloseAllOverlays();
            yield return Settle(0.3f);
        }

        static void SetPrompt(string key, string text)
        {
            var m = typeof(GameUI).GetMethod("SetPrompt", BindingFlags.Instance | BindingFlags.NonPublic);
            m.Invoke(UI, new object[] { key, text });
        }

        /// <summary>しずく・名所・いきもの・なかよし・観察を、ぜんぶ集めたことにする。</summary>
        static void FillProgress()
        {
            var d = SaveSystem.Data;
            for (int i = 0; i < Areas.TotalDrops; i++) if (!d.drops.Contains(i)) d.drops.Add(i);
            for (int i = 0; i < Areas.TotalLandmarks; i++) if (!d.places.Contains(i)) d.places.Add(i);
            foreach (var s in SpeciesCatalog.All)
            {
                if (!d.creatures.Contains(s.id)) d.creatures.Add(s.id);
                var n = Friends.Note(s.id);
                n.friend = Friends.MaxFriend;
                foreach (var b in Friends.BehaviorsOf(s.id)) if (!n.seen.Contains(b.key)) n.seen.Add(b.key);
                n.bigMm = 123.4f;
                n.smallMm = 12.3f;
            }
            foreach (var a in Areas.All)
            {
                if (!d.visited.Contains(a.Id)) d.visited.Add(a.Id);
                if (!d.completedAreas.Contains(a.Id)) d.completedAreas.Add(a.Id);
            }
            d.playTime = 99f * 3600f;
            d.steps = 9999999;
            d.silkUses = 99999;
            d.falls = 99999;
            d.highest = 999f;
            d.skin = "rainbow";
            d.hat = "straw";
            d.glasses = "heart";
        }

        // ------------------------------------------------------------------
        // はみ出しを見つける
        // ------------------------------------------------------------------

        const float Tol = 1.5f;
        static readonly HashSet<string> ClipNames = new HashSet<string> { "minimap-mask", "minimap" };

        static bool Shown(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
            {
                var rs = p.resolvedStyle;
                if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden || rs.opacity < 0.05f) return false;
                // すべりこみ・すべり出しの途中（おしらせなど）は、まだ止まっていないので見ない
                var tr = rs.translate;
                if (Mathf.Abs(tr.x) > 0.5f || Mathf.Abs(tr.y) > 0.5f) return false;
            }
            return true;
        }

        /// <summary>背景・わく・絵がある（見た目の「箱」）。</summary>
        static bool IsBox(VisualElement e)
        {
            var rs = e.resolvedStyle;
            if (rs.backgroundColor.a > 0.15f) return true;
            var bg = rs.backgroundImage;
            if (bg.texture != null || bg.sprite != null || bg.vectorImage != null || bg.renderTexture != null) return true;
            return (rs.borderLeftWidth > 0.5f && rs.borderLeftColor.a > 0.15f) || (rs.borderTopWidth > 0.5f && rs.borderTopColor.a > 0.15f);
        }

        static string Name(VisualElement e)
        {
            string n = !string.IsNullOrEmpty(e.name) ? "#" + e.name : "";
            string cls = string.Join(".", e.GetClasses().Where(k => !k.StartsWith("unity-")).Take(3));
            if (cls.Length > 0) n += "." + cls;
            if (n.Length == 0) n = e.GetType().Name;
            return n;
        }

        static string Short(string t)
        {
            t = t.Replace("\n", " ");
            return t.Length > 18 ? t.Substring(0, 18) + "…" : t;
        }

        // USS の -unity-text-auto-size（入らなければ字を小さくする）は resolvedStyle に出ないので、計算ずみのスタイル
        // （VisualElement.computedStyle。ref で返す内部のプロパティなので、IL で値を取り出す）を見る
        static readonly Func<VisualElement, object> s_ComputedStyle = MakeComputedStyleGetter();

        static Func<VisualElement, object> MakeComputedStyleGetter()
        {
            var getter = typeof(VisualElement).GetProperty("computedStyle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            if (getter == null || !getter.ReturnType.IsByRef) return null;
            var type = getter.ReturnType.GetElementType();
            var dm = new System.Reflection.Emit.DynamicMethod("ShakuComputedStyle", typeof(object), new[] { typeof(VisualElement) }, typeof(VisualElement).Module, true);
            var il = dm.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Call, getter);
            il.Emit(System.Reflection.Emit.OpCodes.Ldobj, type);
            il.Emit(System.Reflection.Emit.OpCodes.Box, type);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return (Func<VisualElement, object>)dm.CreateDelegate(typeof(Func<VisualElement, object>));
        }

        /// <summary>字の大きさを自動で合わせるなら、いちばん小さい字の大きさ（合わせないなら -1）。</summary>
        public static float AutoSizeMin(TextElement te)
        {
            if (s_ComputedStyle == null) return -1f;
            object cs = s_ComputedStyle(te);
            var p = cs.GetType().GetProperty("unityTextAutoSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return -1f;
            var a = (TextAutoSize)p.GetValue(cs);
            return a.mode == TextAutoSizeMode.BestFit ? a.minSize.value : -1f;
        }

        /// <summary>文字が実際にかかれる四角（よせ方を考える）。</summary>
        static Rect InkRect(TextElement te, out Vector2 need, out bool wraps)
        {
            Rect cr = te.contentRect;
            var ws = te.resolvedStyle.whiteSpace;
            wraps = ws == WhiteSpace.Normal || ws == WhiteSpace.PreWrap;
            Vector2 free = te.MeasureTextSize(te.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            // 字の大きさを自動で合わせるなら、入りきらないときは字が小さくなる（いちばん小さくしても入らなければ、はみ出し）
            float min = AutoSizeMin(te);
            if (min > 0f && !wraps && free.x > cr.width)
                free *= Mathf.Max(min / Mathf.Max(1f, te.resolvedStyle.fontSize), cr.width / free.x);
            // 字の幅ぴったりの大きさになっているときは、折り返さない（はかり方の小さな誤差で 2 行にしない）
            need = wraps && free.x > cr.width + Tol
                ? te.MeasureTextSize(te.text, cr.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined)
                : free;
            float w = wraps ? Mathf.Min(free.x, cr.width) : need.x;
            float h = need.y;
            var a = te.resolvedStyle.unityTextAlign;
            int col = (int)a % 3, row = (int)a / 3;
            float x = col == 0 ? cr.xMin : col == 1 ? cr.center.x - w * 0.5f : cr.xMax - w;
            float y = row == 0 ? cr.yMin : row == 1 ? cr.center.y - h * 0.5f : cr.yMax - h;
            return te.LocalToWorld(new Rect(x, y, w, h));
        }

        /// <summary>あそび中にいつも出ている物（目標・操作説明・ミニマップ・丸ボタン・タッチのボタン・場所の名前）どうしが重ならないか。</summary>
        public static List<string> HudOverlaps(VisualElement root)
        {
            var items = new List<VisualElement>();
            foreach (var n in new[] { "objectives", "help", "minimap", "area-name", "tutorial-card" }) items.Add(root.Q(n));
            items.AddRange(root.Query(className: "round-button").ToList());
            items.AddRange(root.Query(className: "touch-button").ToList());
            items = items.Where(e => e != null && Shown(e) && e.worldBound.width > 1f && !(e is TextElement t && string.IsNullOrEmpty(t.text))).ToList();
            var bad = new List<string>();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (items[i].Contains(items[j]) || items[j].Contains(items[i])) continue;
                    Rect o = items[i].worldBound;
                    if (Clip(ref o, items[j].worldBound) && o.width > 2f && o.height > 2f)
                        bad.Add($"{Name(items[i])} と {Name(items[j])} が重なる（{items[i].worldBound} / {items[j].worldBound}）");
                }
            return bad;
        }

        static bool Clip(ref Rect r, Rect by)
        {
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, by.xMin), Mathf.Max(r.yMin, by.yMin), Mathf.Min(r.xMax, by.xMax), Mathf.Min(r.yMax, by.yMax));
            return r.width > 0.5f && r.height > 0.5f;
        }

        /// <summary>
        /// 見えている文字と、文字の入った箱が、まわりの箱（と画面）からはみ出していないか。
        /// 同じウィンドウ（とタイトル・読み込みの画面）の中で、文字どうしが重なっていないか（スクロールで見えない所は数えない）。
        /// </summary>
        public static List<string> Overflows(VisualElement root)
        {
            var bad = new List<string>();
            var screen = root.panel.visualTree.worldBound;
            var inks = new List<(TextElement te, Rect r, VisualElement win)>();
            root.Query<VisualElement>().ForEach(e =>
            {
                var te = e as TextElement;
                bool text = te != null && !string.IsNullOrEmpty(te.text) && te.text.Trim().Length > 0;
                bool box = !text && IsBox(e) && e.Query<TextElement>().Where(t => !string.IsNullOrEmpty(t.text)).ToList().Count > 0;
                if (!text && !box) return;
                if (e.GetClasses().Any(k => k.StartsWith("unity-scroller") || k.StartsWith("unity-base-slider") || k.StartsWith("unity-toggle__")) && !text) return;
                if (!Shown(e)) return;
                // まるく切りぬいたミニマップの中の絵は、はみ出してよい
                for (var p = e.parent; p != null; p = p.parent) if (ClipNames.Contains(p.name)) return;
                if (float.IsNaN(e.layout.width) || e.layout.width < 0.5f) return;

                Rect r = e.worldBound;
                string what = text ? $"「{Short(te.text)}」({Name(e)})" : Name(e);
                if (text)
                {
                    r = InkRect(te, out var need, out bool wraps);
                    // 字が内側のすき間（padding）にかかるのはよい。わくの内がわ（padding の外べり）までに入っているか
                    var rs = te.resolvedStyle;
                    float pw = te.layout.width - rs.borderLeftWidth - rs.borderRightWidth;
                    float ph = te.layout.height - rs.borderTopWidth - rs.borderBottomWidth;
                    if (!wraps && need.x > pw + Tol) { bad.Add($"{what} が横に入りきらない（{need.x:0} > {pw:0}）"); return; }
                    // 行の高さには上下のすき間が入るので、字の 1/3 ほどまでは、はみ出しても字は切れない
                    if (need.y > ph + Mathf.Max(Tol, rs.fontSize * 0.35f)) { bad.Add($"{what} が下で切れる（{need.y:0} > {ph:0}、幅 {te.contentRect.width:0}）"); return; }
                }
                bool vertical = true;
                for (var p = e.parent; p != null; p = p.parent)
                {
                    if (p is ScrollView sv)
                    {
                        // スクロールの中は、横だけ（たてはスクロールして見る）
                        Rect v = sv.contentViewport.worldBound;
                        if (r.xMin < v.xMin - Tol || r.xMax > v.xMax + Tol)
                        {
                            bad.Add($"{what} がスクロールの横からはみ出す（{Name(sv)}）");
                            return;
                        }
                        vertical = false;
                        continue;
                    }
                    if (!IsBox(p)) continue;
                    Rect b = p.worldBound;
                    bool outX = r.xMin < b.xMin - Tol || r.xMax > b.xMax + Tol;
                    bool outY = vertical && (r.yMin < b.yMin - Tol || r.yMax > b.yMax + Tol);
                    if (outX || outY)
                    {
                        bad.Add($"{what} が {Name(p)} からはみ出す（{(outX ? "横" : "たて")}：{r} / {b}）");
                        return;
                    }
                }
                if (vertical && (r.xMin < screen.xMin - Tol || r.xMax > screen.xMax + Tol || r.yMin < screen.yMin - Tol || r.yMax > screen.yMax + Tol))
                    bad.Add($"{what} が画面の外に出る（{r} / 画面 {screen}）");

                if (!text) return;
                VisualElement win = null;
                Rect seen = r;
                for (var p = e.parent; p != null; p = p.parent)
                {
                    if (p is ScrollView sv && !Clip(ref seen, sv.contentViewport.worldBound)) return;   // スクロールして見えない
                    if (win == null && (p.ClassListContains("window") || p.name == "title-overlay" || p.name == "loading")) win = p;
                }
                if (win != null) inks.Add((te, seen, win));
            });

            // 同じウィンドウの文字どうしの重なり
            for (int i = 0; i < inks.Count; i++)
                for (int j = i + 1; j < inks.Count; j++)
                {
                    var a = inks[i];
                    var b = inks[j];
                    if (a.win != b.win || a.te.Contains(b.te) || b.te.Contains(a.te)) continue;
                    Rect o = a.r;
                    if (Clip(ref o, b.r) && o.width > 2f && o.height > 2f)
                        bad.Add($"「{Short(a.te.text)}」({Name(a.te)}) と「{Short(b.te.text)}」({Name(b.te)}) が重なる");
                }
            return bad;
        }
    }
}
