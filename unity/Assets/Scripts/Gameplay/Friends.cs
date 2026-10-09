using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>図鑑の、いきものごとの記録：なかよし・観察したしぐさ・見た中でいちばん大きい／小さい大きさ。</summary>
    [Serializable]
    public class CreatureNote
    {
        public string id;
        public int friend;                                  // なかよし（0〜5）
        public float friendAt = -999f;                      // 最後になかよしが上がった時刻（遊んだ時間）
        public List<string> seen = new List<string>();      // 観察したしぐさ（Behavior.key）
        public float bigMm, smallMm;                        // 見た中でいちばん大きい・小さい（mm。0 = まだ）
    }

    /// <summary>観察できるしぐさ。key は、いきものがいまそれをしているかを Creatures が調べるときの名前。</summary>
    public class Behavior
    {
        public readonly string key, label;
        public Behavior(string key, string label)
        {
            this.key = key;
            this.label = label;
        }
    }

    /// <summary>
    /// 見つけたいきものに、もう一度会うと：
    /// あいさつしてくれて、なかよしになる（すきなもの・ひみつがわかり、★5 でとっておきのしぐさを見せてくれる）。
    /// しぐさを観察すると図鑑にスタンプがたまり、会うたびに大きさをはかって、いちばん大きい・小さいを記録する。
    /// なかよしになった飛ぶいきものは、まだ取っていないしずくの方へ、あんないしてくれる。
    /// </summary>
    public static class Friends
    {
        public const int MaxFriend = 5;
        /// <summary>同じ種のなかよしが上がるのは、この時間（遊んだ時間・秒）に 1 回まで。</summary>
        public const float FriendCooldown = 25f;
        public const int LikesAt = 1, GuideAt = 2, SecretAt = 3, SignatureAt = 5;
        /// <summary>1 体長（1 単位）＝ 2.5 cm。</summary>
        public const float MmPerUnit = 25f;

        static List<CreatureNote> Notes => SaveSystem.Data.notes;

        public static CreatureNote Find(string id)
        {
            foreach (var n in Notes)
                if (n.id == id) return n;
            return null;
        }

        public static CreatureNote Note(string id)
        {
            var n = Find(id);
            if (n != null) return n;
            n = new CreatureNote { id = id };
            Notes.Add(n);
            return n;
        }

        public static int Level(string id) => Find(id)?.friend ?? 0;

        /// <summary>なかよしを 1 上げる（同じ種は FriendCooldown 秒に 1 回まで）。上がったら true。</summary>
        public static bool Befriend(string id, float now)
        {
            var n = Note(id);
            if (n.friend >= MaxFriend || now - n.friendAt < FriendCooldown) return false;
            n.friend++;
            n.friendAt = now;
            return true;
        }

        /// <summary>しぐさを観察した（はじめてなら true）。</summary>
        public static bool Observe(string id, string key)
        {
            var n = Note(id);
            if (n.seen.Contains(key)) return false;
            n.seen.Add(key);
            return true;
        }

        public static bool HasSeen(string id, string key)
        {
            var n = Find(id);
            return n != null && n.seen.Contains(key);
        }

        /// <summary>その種で観察したしぐさの数（0〜3）。</summary>
        public static int SeenCount(string id)
        {
            var n = Find(id);
            if (n == null) return 0;
            int c = 0;
            foreach (var b in BehaviorsOf(id))
                if (n.seen.Contains(b.key)) c++;
            return c;
        }

        public static bool FullyObserved(string id) => SeenCount(id) >= BehaviorsOf(id).Length;

        /// <summary>観察したしぐさの数（ぜんぶの種の合計）。</summary>
        public static int TotalObserved
        {
            get
            {
                int c = 0;
                foreach (var n in Notes) c += SeenCount(n.id);
                return c;
            }
        }

        /// <summary>なかよし ★5 の種の数。</summary>
        public static int BestFriendCount
        {
            get
            {
                int c = 0;
                foreach (var n in Notes)
                    if (n.friend >= MaxFriend) c++;
                return c;
            }
        }

        /// <summary>
        /// 大きさを記録する。いままでの記録を更新したら +1（いちばん大きい）か -1（いちばん小さい）。
        /// はじめての記録と、更新しなかったときは 0。
        /// </summary>
        public static int RecordSize(string id, float mm)
        {
            if (mm <= 0f) return 0;
            var n = Note(id);
            if (n.bigMm <= 0f)
            {
                n.bigMm = n.smallMm = mm;
                return 0;
            }
            // 同じくらい（1% 未満のちがい）は、更新にしない
            if (mm > n.bigMm * 1.01f) { n.bigMm = mm; return 1; }
            if (mm < n.smallMm * 0.99f) { n.smallMm = mm; return -1; }
            return 0;
        }

        public static string Stars(int level) => new string('★', Mathf.Clamp(level, 0, MaxFriend)) + new string('☆', MaxFriend - Mathf.Clamp(level, 0, MaxFriend));

        /// <summary>大きさの書き方（10 cm 以上は cm で）。</summary>
        public static string FormatSize(float mm) => mm >= 100f ? $"{mm / 10f:0.#} cm" : $"{mm:0.#} mm";

        // ------------------------------------------------------------------
        // 種ごとの、しぐさ・すきなもの・ひみつ
        // ------------------------------------------------------------------
        static Behavior B(string key, string label) => new Behavior(key, label);

        class Entry
        {
            public Behavior[] behaviors;
            public string likes, secret;
        }

        static readonly Behavior[] None = new Behavior[0];

        public static Behavior[] BehaviorsOf(string id) => Book.TryGetValue(id ?? "", out var e) ? e.behaviors : None;
        public static string Likes(string id) => Book.TryGetValue(id ?? "", out var e) ? e.likes : "";
        public static string Secret(string id) => Book.TryGetValue(id ?? "", out var e) ? e.secret : "";

        /// <summary>
        /// 3 つめのしぐさが「とっておき」（なかよし ★5 になると、会ったときに見せてくれる）。
        /// key は Creatures.Doing で調べる名前（Creatures.BehaviorKeys にあるもの）。
        /// </summary>
        static readonly Dictionary<string, Entry> Book = new Dictionary<string, Entry>
        {
            ["ant"] = new Entry { behaviors = new[] { B("groom", "前脚で触角の手入れ"), B("carry", "食べものを運ぶ"), B("antgreet", "なかまとあいさつ") },
                likes = "あまいもの（花のみつや、アブラムシの出すみつ）",
                secret = "歩いた道に、においのしるしをつけて、食べもののある場所をなかまに知らせる。" },
            ["snail"] = new Entry { behaviors = new[] { B("climb", "かべを登る"), B("retreat", "殻にひっこむ"), B("stretch", "首をのばす") },
                likes = "しめった葉っぱと、雨の日",
                secret = "殻の材料はカルシウム。コンクリートをかじって、カルシウムをとることもある。" },
            ["butterfly"] = new Entry { behaviors = new[] { B("rest", "花にとまって休む"), B("chase", "なかまと追いかけっこ"), B("wings", "羽を大きくひらく") },
                likes = "花のみつ",
                secret = "足の先で、味がわかる。花にとまると、まず足で味見をしている。" },
            ["otoshibumi"] = new Entry { behaviors = new[] { B("walk", "長い首で歩く"), B("climb", "葉や茎を登る"), B("display", "うなずくように首をふる") },
                likes = "クヌギやコナラの、やわらかい若い葉",
                secret = "葉っぱをくるくる巻いて「ゆりかご」を作り、中に卵をうむ。落とした手紙（落とし文）に見えるので、この名前。" },
            ["grasshopper"] = new Entry { behaviors = new[] { B("walk", "少し歩く"), B("hop", "ぴょんと跳ぶ"), B("groom", "後ろ足で体をかく") },
                likes = "イネのなかまの草",
                secret = "耳は、おなかの横（はねの付け根の近く）にある。" },
            ["frog"] = new Entry { behaviors = new[] { B("hop", "ぴょんと跳ぶ"), B("call", "のどをふくらませて鳴く"), B("special", "跳ぶかまえをする") },
                likes = "雨がふる前の、しめった空気",
                secret = "まわりに合わせて、からだの色を緑から茶色っぽく変えられる。" },
            ["sparrow"] = new Entry { behaviors = new[] { B("turnhop", "ぴょんと跳ねて向きを変える"), B("shake", "砂あび"), B("special", "尾をぴっと上げる") },
                likes = "草のたねと、小さな虫",
                secret = "水あびだけでなく、砂あびもして、羽をきれいにしている。" },
            ["crow"] = new Entry { behaviors = new[] { B("fly", "大きな羽で飛び立つ"), B("sidehop", "横っとび"), B("call", "首をのばして鳴く") },
                likes = "なんでも食べる（とてもかしこい）",
                secret = "人の顔を見分けて、覚えていられる。" },
            ["ladybug"] = new Entry { behaviors = new[] { B("groom", "体の手入れ"), B("dead", "死んだふり"), B("spin", "高い所でくるくる回る") },
                likes = "アブラムシ",
                secret = "おどろくと、脚の関節から、にがい黄色いしるを出して身を守る。" },
            ["pillbug"] = new Entry { behaviors = new[] { B("walk", "たくさんの脚で歩く"), B("roll", "まるいまま、ころがる"), B("curl", "まるくなる") },
                likes = "落ち葉と、くさりかけた木",
                secret = "虫ではなく、エビやカニのなかま（甲殻類）。" },
            ["beetle"] = new Entry { behaviors = new[] { B("ride", "せなかに乗せてくれる"), B("display", "角を持ち上げて見せる"), B("special", "角で地面をすくう") },
                likes = "クヌギの樹液",
                secret = "角があるのはオスだけ。角で、ほかのオスを投げとばして、えさ場をとりあう。" },
            ["waterstrider"] = new Entry { behaviors = new[] { B("row", "水面をすいすいこぐ"), B("dart", "さっとすべって逃げる"), B("special", "前脚で水面をたたく") },
                likes = "水に落ちた、小さな虫",
                secret = "足の先の細かい毛が水をはじくので、しずまない。あめのようなにおいがするので「アメンボ」。" },
            ["dragonfly"] = new Entry { behaviors = new[] { B("perch", "とまって休む"), B("hunt", "虫を追いかける"), B("special", "おしりを持ち上げる") },
                likes = "とまりやすい、棒の先",
                secret = "夏はすずしい山ですごし、秋に赤くなって、里へ下りてくる。" },
            ["crab"] = new Entry { behaviors = new[] { B("sideways", "横歩き"), B("display", "はさみを上げて、おどす"), B("groom", "口もとをつつく") },
                likes = "きれいな水の川と、石の下のすきま",
                secret = "一生を川ですごすカニ。卵から、小さなカニの形で生まれてくる。" },
            ["riversnail"] = new Entry { behaviors = new[] { B("climb", "石を登る"), B("retreat", "殻にひっこむ"), B("stretch", "首をのばす") },
                likes = "石についた、コケのような藻",
                secret = "ゲンジボタルの幼虫の、だいじな食べもの。" },
            ["firefly"] = new Entry { behaviors = new[] { B("glow", "光る"), B("hover", "光りながら、ふわふわ飛ぶ"), B("perch", "草にとまって休む") },
                likes = "きれいな水辺の、夜",
                secret = "光は、オスとメスのあいずの言葉。地いきによって、光る間かく（リズム）がちがう。" },
            ["spider"] = new Entry { behaviors = new[] { B("watch", "じっと見つめる"), B("hop", "ぴょんと跳ぶ"), B("raise", "前脚を上げてかまえる") },
                likes = "小さなハエやカ",
                secret = "巣を張らずに、大きな目で見て、ぴょんと跳んでつかまえる。" },
            ["mantis"] = new Entry { behaviors = new[] { B("groom", "かまの手入れ"), B("raise", "かまをかまえる"), B("strike", "かまをふる") },
                likes = "動いている虫",
                secret = "首を自由に回して、うしろの方まで見られる。" },
            ["kamikiri"] = new Entry { behaviors = new[] { B("walk", "長い触角をゆらして歩く"), B("climb", "木を登る"), B("alarm", "キイキイ鳴く") },
                likes = "ミカンやヤナギの木",
                secret = "つかまえると「キイキイ」鳴く。首のつけ根をこすって、音を出している。" },
            ["kuwagata"] = new Entry { behaviors = new[] { B("ride", "せなかに乗せてくれる"), B("display", "あごを持ち上げて見せる"), B("special", "あごで地面をすくう") },
                likes = "クヌギやコナラの樹液",
                secret = "大きなオスほど、あごが大きく曲がる。小さなオスのあごは、まっすぐに近い。" },
            ["kamemushi"] = new Entry { behaviors = new[] { B("walk", "葉の上を歩く"), B("climb", "茎を登る"), B("alarm", "くさいにおいを出す") },
                likes = "豆や実のしる",
                secret = "ストローのような口を、くきや実にさして、しるを吸う。" },
            ["tokage"] = new Entry { behaviors = new[] { B("run", "さっと走って逃げる"), B("ride", "せなかに乗せてくれる"), B("special", "腕立てふせ") },
                likes = "日なたぼっこ",
                secret = "子どものときは、しっぽが青く光って見える。おそわれると、しっぽを切って逃げる。" },
            ["monshiro"] = new Entry { behaviors = new[] { B("rest", "花にとまって休む"), B("chase", "なかまと追いかけっこ"), B("wings", "羽を大きくひらく") },
                likes = "キャベツやアブラナの花",
                secret = "卵は、キャベツなどアブラナのなかまの葉にうむ。幼虫は、あおむし。" },
            ["koumori"] = new Entry { behaviors = new[] { B("hover", "ひらひら飛ぶ"), B("zigzag", "急に向きを変える"), B("swoop", "すーっと急降下") },
                likes = "夕方に飛ぶ、小さな虫",
                secret = "超音波の声のはね返りで、暗やみでも虫の場所がわかる。" },
            ["mogura"] = new Entry { behaviors = new[] { B("popup", "顔を出す"), B("dig", "土にもぐる"), B("sniff", "鼻をくんくん") },
                likes = "ミミズ",
                secret = "目はほとんど見えないが、鼻先のするどい感覚で、土の中のえものをさがす。" },
            ["okera"] = new Entry { behaviors = new[] { B("walk", "前脚で土をかき分けて歩く"), B("dig", "土にもぐる"), B("popup", "土から出てくる") },
                likes = "しめった土と、植物の根",
                secret = "土をほるのが得意なうえに、泳いだり、飛んだりもできる。" },
            ["nanafushi"] = new Entry { behaviors = new[] { B("walk", "前脚でさぐりながら歩く"), B("climb", "茎を登る"), B("mimic", "小枝のまねをする") },
                likes = "サクラやクヌギの葉",
                secret = "小枝にそっくりな体で、鳥から身をかくす（擬態）。" },
            ["gengorou"] = new Entry { behaviors = new[] { B("row", "すいすい泳ぐ"), B("dive", "水にもぐる"), B("surface", "息つぎに上がる") },
                likes = "水の中の、小さな生きもの",
                secret = "おしりの先から空気をとりこみ、はねの下に空気をためて、もぐる。" },
            ["hanakamakiri"] = new Entry { behaviors = new[] { B("groom", "かまの手入れ"), B("raise", "かまをかまえる"), B("strike", "かまをふる") },
                likes = "花のそばに来る虫",
                secret = "花びらのような形と色で、花にまぎれて、虫を待ちぶせする。" },
            ["hato"] = new Entry { behaviors = new[] { B("groom", "羽づくろい"), B("fly", "群れで飛び立つ"), B("special", "胸をふくらませて回る") },
                likes = "ひろばに落ちている、たね",
                secret = "首をふって歩くのは、景色をぶれずに見るため。頭を止めては、体を前へ進めている。" },
            // ---- 山 ----
            ["raichou"] = new Entry { behaviors = new[] { B("groom", "羽づくろい"), B("look", "首をかしげて、足もとを見る"), B("special", "胸をはって、くるりと回る") },
                likes = "高山の草の芽と、花のつぼみ",
                secret = "足の先まで、白い羽毛でおおわれている。雪の上でも、しずまずに歩ける、かんじきのよう。" },
            ["risu"] = new Entry { behaviors = new[] { B("run", "さっと走る"), B("ride", "せなかに乗せてくれる"), B("groom", "前足で顔を洗う") },
                likes = "松ぼっくりのたね",
                secret = "秋になると、木の実を地面にうめて、冬のためにかくしておく。わすれた実から、新しい木が育つ。" },
            ["okojo"] = new Entry { behaviors = new[] { B("popup", "顔を出す"), B("dig", "岩のすきまにかくれる"), B("sniff", "あたりを見まわす") },
                likes = "岩のすきまの、ネズミのなかま",
                secret = "しっぽの先だけは、冬になっても黒いまま。雪の上で、目くらましになる。" },
            ["nakiusagi"] = new Entry { behaviors = new[] { B("hop", "ぴょんと跳ねる"), B("call", "「ピチッ」と鳴く"), B("groom", "前足で顔をこする") },
                likes = "夏のあいだに集めて、ほした草",
                secret = "氷河期からの生きのこり。冬眠をせず、岩の下にためた草を食べて、冬をこす。" },
            ["sanshouuo"] = new Entry { behaviors = new[] { B("walk", "体をくねらせて歩く"), B("look", "首をもたげる"), B("stretch", "体をのばす") },
                likes = "水べの小さな虫",
                secret = "子どもは、谷川の流れの中で、爪のある指で石にしがみついてくらす。" },
            ["asagimadara"] = new Entry { behaviors = new[] { B("rest", "花にとまって休む"), B("chase", "なかまと追いかけっこ"), B("wings", "羽を大きくひらく") },
                likes = "フジバカマやヒヨドリバナの花のみつ",
                secret = "秋になると、海をこえて南の島まで、2000 キロ以上も旅をするものもいる。" },
            ["maruhanabachi"] = new Entry { behaviors = new[] { B("rest", "花にもぐって、みつをすう"), B("chase", "なかまと追いかけっこ"), B("wings", "羽をふるわせる") },
                likes = "コマクサやチングルマの花粉",
                secret = "寒い朝は、羽を動かさずに胸の筋肉をふるわせて、体をあたためてから飛び立つ。" },
            ["oniyanma"] = new Entry { behaviors = new[] { B("hover", "道の上を、行ったり来たり"), B("hunt", "虫を追いかける"), B("perch", "とまって休む") },
                likes = "道の上を飛ぶ、ハエやアブ",
                secret = "子ども（ヤゴ）のまま、きれいな水の中で 3〜4 年もくらしてから、おとなになる。" },
            ["higurashi"] = new Entry { behaviors = new[] { B("climb", "木の幹を登る"), B("call", "「カナカナカナ」と鳴く"), B("alarm", "「ジジッ」と鳴いて、あばれる") },
                likes = "松の木のしる",
                secret = "おなかの中は、ほとんど空っぽ。鳴き声をひびかせる、太鼓の胴のようになっている。" },
            ["maimaikaburi"] = new Entry { behaviors = new[] { B("walk", "長い首をのばして歩く"), B("climb", "石や木を登る"), B("display", "首をふって、においをさがす") },
                likes = "かたつむり",
                secret = "うしろの羽がなく、飛べない。そのかわり、長い脚で、夜の山道をすばやく走りまわる。" },
            // ---- めったに会えないいきもの ----
            ["ant_helmet"] = new Entry { behaviors = new[] { B("groom", "前脚で触角の手入れ"), B("carry", "食べものを運ぶ"), B("special", "ヘルメットをかぶりなおす") },
                likes = "かたい木の実",
                secret = "ヘルメットは、どんぐりのぼうし…？ なかまどうしで、ゆずりあってかぶっているらしい。" },
            ["spider_sneaker"] = new Entry { behaviors = new[] { B("watch", "じっと見つめる"), B("hop", "ぴょんと跳ぶ"), B("special", "スニーカーで足ぶみ") },
                likes = "走りやすい、かわいた道",
                secret = "8 本の足のスニーカーは、雨の日もすべらない、とくべつせい…？" },
            ["kameleon"] = new Entry { behaviors = new[] { B("climb", "木を登る"), B("ride", "せなかに乗せてくれる"), B("run", "さっと逃げる") },
                likes = "ゆっくり近づいて、さっとつかまえる虫",
                secret = "左右の目を、べつべつに動かせる。2 か所をいっしょに見られる。" },
            ["herakuresu"] = new Entry { behaviors = new[] { B("ride", "せなかに乗せてくれる"), B("display", "角を持ち上げて見せる"), B("special", "角で地面をすくう") },
                likes = "よく熟した、くだもの",
                secret = "世界でいちばん長いカブトムシ（17 センチをこえることも）。南アメリカの森にすむ。" },
            ["flamingo"] = new Entry { behaviors = new[] { B("groom", "羽づくろい"), B("stretch", "羽をのばす"), B("shake", "羽を広げて見せる") },
                likes = "水の中の、小さなエビのなかま",
                secret = "ピンク色は、食べものにふくまれる色（カロテノイド）のおかげ。" },
            ["harinezumi"] = new Entry { behaviors = new[] { B("walk", "鼻をひくひくさせて歩く"), B("roll", "まるいまま、ころがる"), B("curl", "はりの玉になる") },
                likes = "虫やミミズ",
                secret = "せなかのはりは、毛がかたくなったもの。まるくなると、はりの玉になって身を守る。" },
        };

        /// <summary>しぐさの名前を持っている種の一覧（テスト用）。</summary>
        public static IEnumerable<string> BookIds => Book.Keys;
    }
}
