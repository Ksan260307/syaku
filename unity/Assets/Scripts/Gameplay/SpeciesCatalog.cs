using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    public enum MobKind
    {
        Crawler,   // 地面や葉の上をうろうろ歩く
        Marcher,   // 列をつくって道をたどる（アリ）
        Flutter,   // ひらひら舞う（チョウ）
        Hopper,    // ときどき跳ねる（バッタ・カエル）
        Bird,      // 地面をちょんちょん歩き、近づくと飛び立つ（スズメ・カラス）
        Skater,    // 水面をすいすい（アメンボ）
        Hover,     // 空中でホバリング（トンボ・ホタル）
        Pouncer,   // ちょこちょこ歩いて、ときどきぴょんと跳ぶ（ハエトリグモ）
        Stalker,   // じっと待ち、近づくと向きを変えてかまを上げる（カマキリ）
    }

    /// <summary>羽などの動く部品の付け方（いきもののローカル座標、前は -Z）。</summary>
    public class PartMount
    {
        public string mesh;
        public Vector3 offset;
        public float restYaw;     // たたんだときの向き（度）
        public float restRoll;
        public float flapAmp;     // はばたきの振れ幅（度）
        public float flapHz;
        public bool pair = true;  // 左右一対
        public bool glow;         // 光る部品
        public bool fixedPart;    // 動かない飾り（ヘルメットなど）。体と同じ座標で作ってある
        public bool onlyCarrying; // アリが食べものを運んでいるときだけ見せる
    }

    public class SpeciesDef
    {
        public string id;
        public string name;
        public string areaLabel;
        public string description;
        public string hint;
        public string body;
        public PartMount[] parts = new PartMount[0];
        public float scale = 1f;
        public float speed = 0.5f;
        public float discoverRadius = 3f;
        public float fleeRadius;
        public MobKind kind;
        public bool sideways;      // カニの横歩き
        // 動き
        public bool climbs;        // 面にそって体をかたむける（壁や葉の上も歩く）。false なら体はいつもまっすぐ上向き
        public bool rideable;      // 当たり判定があり、しゃくとりむしが登って乗れる
        public string rig;         // 脚の付け方（CreatureRig）。null なら脚は体のメッシュの一部
        public string legSuffix = "";   // 脚のメッシュの種類（スニーカーなど）
        public GaitKind gait = GaitKind.Alternate;
        public float stride = 0.15f;    // ひと足で進む長さ
        public float legSwing = 22f;
        public float legLift = 16f;
        public float birdSize;     // 鳥の羽の姿勢を Blender の作りから求めるときの大きさ
        public float birdShoulder = 0.38f;   // 鳥の羽の付け根の高さ（birdSize に対する割合。フラミンゴは脚が長いので高い）
        /// <summary>動き方をまねるいきもの（null なら自分の動き方）。山のいきものは、似たくらしのなかまの動きを使う。</summary>
        public string behavesLike;
        // レア
        public string rareOf;      // 元になるいきもの（その個体がまれにレアになる）
        public float rareChance;
        public bool IsRare => rareOf != null;
        /// <summary>動き方を決める種（レアは元のいきもの、まねる種があればその種）。</summary>
        public string BehaviorId => IsRare ? rareOf : behavesLike ?? id;
    }

    /// <summary>いきもの図鑑にのる 40 しゅと、めったに会えないレア 6 しゅ。</summary>
    public static class SpeciesCatalog
    {
        static List<SpeciesDef> _all;
        static List<SpeciesDef> _regular;
        static List<SpeciesDef> _rares;
        static Dictionary<string, SpeciesDef> _byId;

        /// <summary>レアもふくむ、すべて。</summary>
        public static List<SpeciesDef> All
        {
            get
            {
                if (_all == null) Build();
                return _all;
            }
        }

        /// <summary>ふつうのいきもの（図鑑のコンプリートに必要なもの）。</summary>
        public static List<SpeciesDef> Regular
        {
            get
            {
                if (_all == null) Build();
                return _regular;
            }
        }

        public static List<SpeciesDef> Rares
        {
            get
            {
                if (_all == null) Build();
                return _rares;
            }
        }

        public static SpeciesDef Get(string id)
        {
            if (_all == null) Build();
            return id != null && _byId.TryGetValue(id, out var s) ? s : null;
        }

        /// <summary>図鑑のコンプリートに必要な数（レアはふくまない）。</summary>
        public static int Count => Regular.Count;
        public static int RareCount => Rares.Count;

        /// <summary>このいきもののレアな仲間（なければ null）。</summary>
        public static SpeciesDef RareVariantOf(string id)
        {
            foreach (var r in Rares)
                if (r.rareOf == id) return r;
            return null;
        }

        static void Build()
        {
            _all = new List<SpeciesDef>
            {
                new SpeciesDef { id = "ant", name = "アリ", areaLabel = "森・川辺・公園・山", kind = MobKind.Marcher, body = "Ant", speed = 0.9f, discoverRadius = 2.6f,
                    climbs = true, rig = "Ant", stride = 0.11f, legSwing = 26f, legLift = 18f,
                    parts = new[] { new PartMount { mesh = "Crumb", offset = new Vector3(0f, 0.105f, -0.19f), pair = false, onlyCarrying = true } },
                    description = "いつも列をつくって、せっせと食べものを運んでいる。においの道をたどって、迷わず巣へ帰る。",
                    hint = "はじまりの苔原やどんぐり広場で、行列を見かけるかも。" },
                new SpeciesDef { id = "snail", name = "かたつむり", areaLabel = "森・山", kind = MobKind.Crawler, body = "Snail", scale = 1.2f, speed = 0.12f, discoverRadius = 2.6f,
                    climbs = true, rideable = true, gait = GaitKind.None,
                    description = "ゆっくり、ゆっくり。通ったあとには、きらきら光る道がのこる。",
                    hint = "しめった場所が好き。赤キノコの根もとをさがしてみよう。" },
                new SpeciesDef { id = "butterfly", name = "ちょうちょ", areaLabel = "森・川辺・公園・山", kind = MobKind.Flutter, body = "Butterfly_Body", speed = 1.4f, discoverRadius = 5.5f,
                    parts = new[] { new PartMount { mesh = "Butterfly_Wing", restRoll = 25f, flapAmp = 65f, flapHz = 7f } },
                    description = "空色の小さなチョウ、ルリシジミ。花から花へ、ひらひら舞う。",
                    hint = "花の草原や、川辺の花のまわり。" },
                new SpeciesDef { id = "otoshibumi", name = "オトシブミ", areaLabel = "森", kind = MobKind.Crawler, body = "Otoshibumi", scale = 1.3f, speed = 0.22f, discoverRadius = 2.6f,
                    climbs = true, rig = "Otoshibumi", stride = 0.09f,
                    description = "葉っぱをくるくる巻いて「ゆりかご」を作り、中に卵を産む。落ちている巻物は、まるで手紙のよう。",
                    hint = "葉っぱの巻物がころがっている場所の近く。" },
                new SpeciesDef { id = "grasshopper", name = "バッタ", areaLabel = "森・川辺・公園・山", kind = MobKind.Hopper, body = "Grasshopper", speed = 0.3f, discoverRadius = 4f,
                    rig = "Grasshopper", stride = 0.2f, legSwing = 14f,
                    parts = new[] { new PartMount { mesh = "Grasshopper_Hind", offset = CreatureRig.GrasshopperHip } },
                    description = "大きな後ろ足で、体の何十倍も遠くまでジャンプする。",
                    hint = "草のしげった明るい場所で、ぴょんと跳ねている。" },
                new SpeciesDef { id = "frog", name = "アマガエル", areaLabel = "森・川辺", kind = MobKind.Hopper, body = "Frog", speed = 0.2f, discoverRadius = 3.5f,
                    rideable = true, gait = GaitKind.None,
                    description = "雨がふりそうになると鳴きだす。指先の吸盤で、つるつるの葉っぱにもぴたり。",
                    hint = "水たまりのふちや、川のほとり。" },
                new SpeciesDef { id = "sparrow", name = "スズメ", areaLabel = "森・川辺・公園", kind = MobKind.Bird, body = "Sparrow_Body", speed = 1.2f, discoverRadius = 10f, fleeRadius = 4.5f,
                    birdSize = 5.5f, gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Sparrow_Wing", flapAmp = 60f, flapHz = 9f } },
                    description = "人の近くでくらす、おなじみの小鳥。ちょんちょん跳ねて、地面の草の実をついばむ。",
                    hint = "ひらけた明るい場所に下りてくる。近づきすぎると飛んでいってしまう。" },
                new SpeciesDef { id = "crow", name = "カラス", areaLabel = "森・川辺・公園・山", kind = MobKind.Bird, body = "Crow_Body", speed = 2.5f, discoverRadius = 16f, fleeRadius = 8f,
                    birdSize = 18f, gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Crow_Wing", flapAmp = 50f, flapHz = 3.2f } },
                    description = "とても頭のいい大きな鳥。しゃくとりむしから見ると、まるで黒い山のよう。",
                    hint = "高い場所に下りて、あたりを見張っている。" },
                new SpeciesDef { id = "ladybug", name = "てんとうむし", areaLabel = "森・川辺・公園・山", kind = MobKind.Crawler, body = "Ladybug", speed = 0.35f, discoverRadius = 2.6f,
                    climbs = true, rig = "Ladybug", stride = 0.12f, legSwing = 20f,
                    description = "七つの黒い星のナナホシテントウ。アブラムシを食べてくれる、植物の味方。",
                    hint = "葉っぱや花のまわりを歩いている。" },
                new SpeciesDef { id = "pillbug", name = "だんごむし", areaLabel = "森・公園", kind = MobKind.Crawler, body = "PillBug", speed = 0.28f, discoverRadius = 2.6f,
                    climbs = true, gait = GaitKind.None,
                    description = "さわると、くるんとまるくなる。じめじめした落ち葉の下が大好き。",
                    hint = "丸太のトンネルの中や、落ち葉のたまり場。" },
                new SpeciesDef { id = "beetle", name = "カブトムシ", areaLabel = "森", kind = MobKind.Crawler, body = "Beetle", speed = 0.1f, discoverRadius = 3.6f,
                    climbs = true, rideable = true, rig = "Beetle", stride = 0.3f, legSwing = 16f, legLift = 12f,
                    description = "森の力持ち。りっぱな角で、木の上の場所とりをする。",
                    hint = "大樹の根っこの上で見かけたという話。" },
                new SpeciesDef { id = "waterstrider", name = "アメンボ", areaLabel = "森・川辺", kind = MobKind.Skater, body = "WaterStrider", speed = 1.6f, discoverRadius = 4.5f,
                    rig = "WaterStrider", gait = GaitKind.Row, stride = 0.6f, legSwing = 18f, legLift = 4f,
                    description = "細い足の毛で水をはじき、水面をすいすい歩く。",
                    hint = "流れのゆるやかな水面。" },
                new SpeciesDef { id = "dragonfly", name = "アキアカネ", areaLabel = "森・川辺・公園・山", kind = MobKind.Hover, body = "Dragonfly_Body", speed = 3f, discoverRadius = 5.5f,
                    gait = GaitKind.None,
                    parts = new[]
                    {
                        new PartMount { mesh = "Dragonfly_Wing", offset = new Vector3(0.03f, 0.05f, -0.1f), restYaw = 8f, flapAmp = 18f, flapHz = 22f },
                        new PartMount { mesh = "Dragonfly_Wing", offset = new Vector3(0.03f, 0.05f, 0.02f), restYaw = -14f, flapAmp = 18f, flapHz = 22f },
                    },
                    description = "秋になると真っ赤になるトンボ。大きな目で、ぐるりと見わたす。",
                    hint = "水の上を、すーっと飛んでいる。" },
                new SpeciesDef { id = "crab", name = "サワガニ", areaLabel = "川辺", kind = MobKind.Crawler, body = "Crab", speed = 0.45f, discoverRadius = 2.8f, sideways = true,
                    rideable = true, rig = "Crab", stride = 0.3f, legSwing = 14f, legLift = 14f,
                    description = "きれいな川にだけすむ小さなカニ。横歩きで、石のすきまにかくれる。",
                    hint = "川のまんなかの中州で見かけたという…。" },
                new SpeciesDef { id = "riversnail", name = "カワニナ", areaLabel = "川辺", kind = MobKind.Crawler, body = "RiverSnail", speed = 0.08f, discoverRadius = 2.6f,
                    climbs = true, gait = GaitKind.None,
                    description = "とがった巻き貝。ゲンジボタルの幼虫のごちそうでもある。",
                    hint = "とびいしのあたりの水ぎわ。" },
                new SpeciesDef { id = "firefly", name = "ゲンジボタル", areaLabel = "川辺", kind = MobKind.Hover, body = "Firefly_Body", speed = 0.5f, discoverRadius = 4.5f,
                    gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Firefly_Glow", pair = false, glow = true } },
                    description = "おしりの光でおしゃべりする。ホタルがいるのは、きれいな川のしるし。",
                    hint = "水辺の草のかげで、ぽうっと光っている。" },
                new SpeciesDef { id = "spider", name = "ハエトリグモ", areaLabel = "森・川辺・公園・山", kind = MobKind.Pouncer, body = "Spider", speed = 0.5f, discoverRadius = 2.8f,
                    climbs = true, rig = "Spider", stride = 0.14f, legSwing = 20f, legLift = 22f,
                    description = "大きな前の目でえものをねらい、ぴょんと跳びかかる小さなクモ。巣は張らずに歩きまわる。",
                    hint = "切り株の上や丸太のまわり、川辺の石の上をうろうろしている。" },
                new SpeciesDef { id = "mantis", name = "オオカマキリ", areaLabel = "森・川辺", kind = MobKind.Stalker, body = "Mantis", speed = 0.25f, discoverRadius = 4.5f,
                    rideable = true, rig = "Mantis", stride = 0.45f, legSwing = 14f, legLift = 14f,
                    parts = new[] { new PartMount { mesh = "Mantis_Arm", offset = CreatureRig.MantisShoulder } },
                    description = "かまのような前足で、じっとえものを待つ。近づくと、かまを持ち上げてこちらをにらむ。",
                    hint = "花の草原の、草がたくさん生えているところ。" },
                // ---- 公園 ----
                new SpeciesDef { id = "kamikiri", name = "ゴマダラカミキリ", areaLabel = "公園", kind = MobKind.Crawler, body = "Kamikiri", scale = 1.4f, speed = 0.18f, discoverRadius = 3f,
                    climbs = true, rig = "Kamikiri", stride = 0.16f, legSwing = 18f, legLift = 14f,
                    description = "黒い体に白い水玉。体より長い、しまもようの触角をゆらす。つかまえると「キイキイ」と鳴く。",
                    hint = "公園の大きなクヌギの木のまわりや、ベンチの上。" },
                new SpeciesDef { id = "kuwagata", name = "ノコギリクワガタ", areaLabel = "公園", kind = MobKind.Crawler, body = "Kuwagata", scale = 1.6f, speed = 0.12f, discoverRadius = 3.6f,
                    climbs = true, rideable = true, rig = "Kuwagata", stride = 0.26f, legSwing = 16f, legLift = 12f,
                    description = "のこぎりのような大あごで、樹液の場所をまもる。カブトムシのライバル。",
                    hint = "あまい樹液のにおいがする、大きな木の根もと。" },
                new SpeciesDef { id = "kamemushi", name = "アオクサカメムシ", areaLabel = "公園", kind = MobKind.Crawler, body = "Kamemushi", scale = 1.3f, speed = 0.3f, discoverRadius = 2.6f,
                    climbs = true, rig = "Kamemushi", stride = 0.12f, legSwing = 20f,
                    description = "たての形をした緑の虫。おどろくと、くさいにおいを出して身をまもる。",
                    hint = "花だんの草花や、さくのまわり。" },
                new SpeciesDef { id = "tokage", name = "ニホントカゲ", areaLabel = "公園", kind = MobKind.Crawler, body = "Tokage", scale = 1.4f, speed = 2.2f, discoverRadius = 5f,
                    climbs = true, rideable = true, rig = "Tokage", stride = 0.5f, legSwing = 28f, legLift = 10f,
                    description = "日なたぼっこが大好き。子どものうちは、しっぽがつやつやの青色。すばやく走って、ぴたりと止まる。",
                    hint = "日当たりのいい砂場のふちや、タイヤのあたり。" },
                new SpeciesDef { id = "monshiro", name = "モンシロチョウ", areaLabel = "公園", kind = MobKind.Flutter, body = "Monshiro_Body", speed = 1.3f, discoverRadius = 5.5f,
                    parts = new[] { new PartMount { mesh = "Monshiro_Wing", restRoll = 25f, flapAmp = 65f, flapHz = 6.5f } },
                    description = "白いはねの、いちばん身近なチョウ。キャベツの葉に卵をうむ。",
                    hint = "公園の花だん。チューリップのまわりをひらひら。" },
                // ---- 森・川辺・公園の、新しいなかま ----
                new SpeciesDef { id = "koumori", name = "アブラコウモリ", areaLabel = "森", kind = MobKind.Flutter, body = "Koumori_Body", speed = 2.4f, discoverRadius = 7f,
                    parts = new[] { new PartMount { mesh = "Koumori_Wing", restRoll = 8f, flapAmp = 70f, flapHz = 7.5f } },
                    description = "家のまわりでもくらす、いちばん身近なコウモリ。自分の声のはね返りを耳できいて、くらがりでも虫をつかまえる。",
                    hint = "光るキノコの洞のあたりを、ひらひら飛びまわっている。" },
                new SpeciesDef { id = "mogura", name = "アズマモグラ", areaLabel = "森・公園", kind = MobKind.Crawler, body = "Mogura", speed = 0f, discoverRadius = 5f,
                    description = "土の中にトンネルをほってくらす。ほり出した土の山（モグラ塚）から、ときどき顔を出す。目はとても小さい。",
                    hint = "土がもり上がった山のそば。はなれて、じっと待ってみよう。" },
                new SpeciesDef { id = "okera", name = "ケラ", areaLabel = "川辺・公園", kind = MobKind.Crawler, body = "Okera", speed = 0.35f, discoverRadius = 2.6f,
                    climbs = true, rig = "Okera", stride = 0.1f, legSwing = 22f, legLift = 12f,
                    description = "シャベルのような前足で、土をほって進む。夜は「ジー」と鳴く。泳ぐことも、飛ぶこともできる、なんでも屋。",
                    hint = "しめった土の上。水ぎわや、水たまりのまわり。" },
                new SpeciesDef { id = "nanafushi", name = "ナナフシ", areaLabel = "森", kind = MobKind.Crawler, body = "Nanafushi", speed = 0.07f, discoverRadius = 3f,
                    climbs = true, rig = "Nanafushi", stride = 0.25f, legSwing = 14f, legLift = 14f,
                    description = "小枝そっくりの虫。風にゆれる枝のまねをして、ゆらゆらしながら、ゆっくり歩く。近づくと、ぴたりと止まる。",
                    hint = "草むらのなか。動かない小枝を、よく見てみよう。" },
                new SpeciesDef { id = "gengorou", name = "ゲンゴロウ", areaLabel = "森・川辺", kind = MobKind.Skater, body = "Gengorou", speed = 1.1f, discoverRadius = 4.5f,
                    description = "水の中を、オールのようなうしろ足で泳ぐ甲虫。おしりに空気をためて、ときどき水面で息つぎする。",
                    hint = "鏡の水たまりや、流れのゆるい水の中。" },
                new SpeciesDef { id = "hanakamakiri", name = "ハナカマキリ", areaLabel = "森・公園", kind = MobKind.Stalker, body = "Hanakamakiri", speed = 0.2f, discoverRadius = 3.5f,
                    rig = "Hanakamakiri", stride = 0.3f, legSwing = 14f, legLift = 14f,
                    parts = new[] { new PartMount { mesh = "Hanakamakiri_Arm", offset = CreatureRig.HanaShoulder } },
                    description = "ランの花そっくりの、ピンクのカマキリ。花にまぎれて、みつをすいにくる虫を待ちぶせする。",
                    hint = "花の草原や、チューリップの花だん。" },
                new SpeciesDef { id = "hato", name = "ドバト", areaLabel = "公園", kind = MobKind.Bird, body = "Hato_Body", speed = 1.6f, discoverRadius = 13f, fleeRadius = 5f,
                    birdSize = 13f, gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Hato_Wing", flapAmp = 55f, flapHz = 6f } },
                    description = "首を前後にふりながら歩く、公園でおなじみの鳥。首は見る向きで、緑や紫に光る。",
                    hint = "公園のひろば。みんなで地面をつついている。" },
                // ---- 山 ----
                new SpeciesDef { id = "raichou", name = "ライチョウ", areaLabel = "山", kind = MobKind.Bird, body = "Raichou_Body", speed = 1.1f, discoverRadius = 12f, fleeRadius = 2.6f,
                    birdSize = 15f, gait = GaitKind.None, behavesLike = "hato",
                    parts = new[] { new PartMount { mesh = "Raichou_Wing", flapAmp = 55f, flapHz = 7f } },
                    description = "高い山にだけすむ鳥。夏は茶色と黒のまだら、冬はまっ白に衣がえする。人をあまりこわがらず、のんびり歩く。",
                    hint = "山小屋のまわりや、はい松のしげみのそば。" },
                new SpeciesDef { id = "risu", name = "ニホンリス", areaLabel = "山", kind = MobKind.Crawler, body = "Risu", speed = 2f, discoverRadius = 7f, fleeRadius = 6f,
                    climbs = true, rideable = true, rig = "Risu", stride = 0.55f, legSwing = 30f, legLift = 14f, behavesLike = "tokage",
                    description = "ふさふさのしっぽの、木の上でくらすリス。松ぼっくりを両手で持って、くるくる回しながら食べる。",
                    hint = "大きな松の木の根もと。松ぼっくりが落ちている所。" },
                new SpeciesDef { id = "okojo", name = "オコジョ", areaLabel = "山", kind = MobKind.Crawler, body = "Okojo", speed = 0f, discoverRadius = 5.5f,
                    behavesLike = "mogura",
                    description = "岩場にすむ、小さなイタチのなかま。夏は茶色、冬はまっ白。岩のすきまから、ひょっこり顔を出す。",
                    hint = "石が積み重なった岩のすみか。はなれて、じっと待ってみよう。" },
                new SpeciesDef { id = "nakiusagi", name = "エゾナキウサギ", areaLabel = "山", kind = MobKind.Hopper, body = "Nakiusagi", speed = 0.25f, discoverRadius = 4.5f,
                    rideable = true, gait = GaitKind.None,
                    description = "岩のすきまでくらす、耳のまるい小さなウサギのなかま。「ピチッ」と高い声で鳴く。夏のあいだに草を集めて、冬にそなえる。",
                    hint = "雪渓のそばの、岩がごろごろした所。高い声をたよりにさがそう。" },
                new SpeciesDef { id = "sanshouuo", name = "ハコネサンショウウオ", areaLabel = "山", kind = MobKind.Crawler, body = "Sanshouuo", speed = 0.18f, discoverRadius = 3.2f,
                    climbs = true, rig = "Sanshouuo", stride = 0.3f, legSwing = 24f, legLift = 10f,
                    description = "つめたい谷川の、きれいな水でくらすサンショウウオ。肺がなく、ぬれた皮ふで息をする。",
                    hint = "湧き水の泉のほとり。しめった石の上。" },
                new SpeciesDef { id = "asagimadara", name = "アサギマダラ", areaLabel = "山", kind = MobKind.Flutter, body = "Asagimadara_Body", speed = 1.1f, discoverRadius = 6f,
                    behavesLike = "butterfly",
                    parts = new[] { new PartMount { mesh = "Asagimadara_Wing", restRoll = 25f, flapAmp = 50f, flapHz = 4f } },
                    description = "海をこえて、何千キロも旅をするチョウ。すきとおった浅葱色（うすい青緑）の羽で、ふわりふわりと高く舞う。",
                    hint = "高山の花畑。ゆっくり、ふわりと飛んでいる。" },
                new SpeciesDef { id = "maruhanabachi", name = "マルハナバチ", areaLabel = "山", kind = MobKind.Flutter, body = "Maruhanabachi", speed = 1.3f, discoverRadius = 4f,
                    behavesLike = "butterfly",
                    parts = new[] { new PartMount { mesh = "Maruhanabachi_Wing", offset = new Vector3(0f, 0.55f, -0.15f), restYaw = -35f, restRoll = 10f, flapAmp = 35f, flapHz = 30f } },
                    description = "まるくて、ふわふわの毛につつまれたハチ。花から花へ、ブーンと低い羽音で飛びまわる。おとなしくて、めったにささない。",
                    hint = "高山の花畑の、コマクサやチングルマの花。" },
                new SpeciesDef { id = "oniyanma", name = "オニヤンマ", areaLabel = "山", kind = MobKind.Hover, body = "Oniyanma_Body", speed = 3.4f, discoverRadius = 7f,
                    gait = GaitKind.None, behavesLike = "dragonfly",
                    parts = new[]
                    {
                        new PartMount { mesh = "Oniyanma_Wing", offset = new Vector3(0.1f, 0.2f, -0.4f), restYaw = 8f, flapAmp = 16f, flapHz = 18f },
                        new PartMount { mesh = "Oniyanma_Wing", offset = new Vector3(0.1f, 0.2f, -0.1f), restYaw = -14f, flapAmp = 16f, flapHz = 18f },
                    },
                    description = "日本でいちばん大きなトンボ。黒と黄色のしまもようと、エメラルド色の大きな目。山道の上を、まっすぐ行ったり来たりする。",
                    hint = "湧き水の泉のまわりや、山道の上。" },
                new SpeciesDef { id = "higurashi", name = "ヒグラシ", areaLabel = "山", kind = MobKind.Crawler, body = "Higurashi", speed = 0.12f, discoverRadius = 4f,
                    climbs = true, rig = "Higurashi", stride = 0.12f, legSwing = 18f, legLift = 12f, behavesLike = "kamikiri",
                    description = "夏の夕ぐれ、「カナカナカナ…」とすずしい声で鳴くセミ。すきとおった羽に、緑と茶色のもようの体。",
                    hint = "大きな松の幹。夕方になると、声が聞こえてくる。" },
                new SpeciesDef { id = "maimaikaburi", name = "マイマイカブリ", areaLabel = "山", kind = MobKind.Crawler, body = "Maimaikaburi", scale = 1.2f, speed = 0.45f, discoverRadius = 3f,
                    climbs = true, rig = "Maimaikaburi", stride = 0.2f, legSwing = 24f, legLift = 14f, behavesLike = "otoshibumi",
                    description = "かたつむりが大好物の、細長い首のオサムシのなかま。首を殻の中へさしこんで食べる。おどろくと、くさい液を出す。",
                    hint = "しめった落ち葉や、石のまわり。山小屋のうらの日かげ。" },
                // ---- レア ----
                new SpeciesDef { id = "ant_helmet", name = "ヘルメットアリ", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Marcher, body = "Ant", speed = 0.9f, discoverRadius = 2.8f,
                    climbs = true, rig = "Ant", stride = 0.11f, legSwing = 26f, legLift = 18f, rareOf = "ant", rareChance = 0.05f,
                    parts = new[]
                    {
                        new PartMount { mesh = "Ant_Helmet", pair = false, fixedPart = true },
                        new PartMount { mesh = "Crumb", offset = new Vector3(0f, 0.105f, -0.19f), pair = false, onlyCarrying = true },
                    },
                    description = "工事現場のヘルメットをかぶった、めったに会えないアリ。今日も安全第一。",
                    hint = "アリの行列のなかに、ときどき…。" },
                new SpeciesDef { id = "spider_sneaker", name = "スニーカーグモ", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Pouncer, body = "Spider", speed = 0.55f, discoverRadius = 3f,
                    climbs = true, rig = "Spider", legSuffix = "_Sneaker", stride = 0.14f, legSwing = 20f, legLift = 22f, rareOf = "spider", rareChance = 0.05f,
                    description = "8本の足ぜんぶにスニーカーをはいたクモ。足音がちょっとだけかわいい。",
                    hint = "ハエトリグモのなかに、ときどき…。" },
                new SpeciesDef { id = "kameleon", name = "カメレオン", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Crawler, body = "Kameleon", speed = 0.5f, discoverRadius = 5f,
                    climbs = true, rideable = true, rig = "Kameleon", stride = 0.4f, legSwing = 20f, legLift = 12f, rareOf = "tokage", rareChance = 0.05f,
                    description = "ぐるぐる動く大きな目と、くるんと巻いたしっぽ。トカゲのなかまにまじって、ゆっくり歩く。",
                    hint = "ニホントカゲのなかに、ときどき…。" },
                new SpeciesDef { id = "herakuresu", name = "ヘラクレスオオカブト", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Crawler, body = "Herakuresu", speed = 0.1f, discoverRadius = 6f,
                    climbs = true, rideable = true, rig = "Herakuresu", stride = 0.45f, legSwing = 16f, legLift = 12f, rareOf = "beetle", rareChance = 0.05f,
                    description = "世界でいちばん大きなカブトムシ。黒くて長い角と、黒い点のあるオリーブ色のはね。",
                    hint = "カブトムシのなかに、ときどき…。" },
                new SpeciesDef { id = "flamingo", name = "フラミンゴ", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Bird, body = "Flamingo_Body", speed = 2.5f, discoverRadius = 24f, fleeRadius = 9f,
                    birdSize = 20f, birdShoulder = 1.02f, gait = GaitKind.None, rareOf = "crow", rareChance = 0.05f,
                    parts = new[] { new PartMount { mesh = "Flamingo_Wing", flapAmp = 45f, flapHz = 2.6f } },
                    description = "ピンクの羽の、背の高い鳥。しゃくとりむしから見ると、空までとどく塔のよう。",
                    hint = "カラスのなかに、ときどき…。" },
                new SpeciesDef { id = "harinezumi", name = "ハリネズミ", areaLabel = "？？？（とてもめずらしい）", kind = MobKind.Crawler, body = "Harinezumi", speed = 0.25f, discoverRadius = 4.5f,
                    rareOf = "pillbug", rareChance = 0.05f,
                    description = "せなかいっぱいの、とがったはり。おどろくと、だんごむしのように、くるんとまるくなる。",
                    hint = "だんごむしのなかに、ときどき…。" },
            };
            _byId = new Dictionary<string, SpeciesDef>();
            _regular = new List<SpeciesDef>();
            _rares = new List<SpeciesDef>();
            foreach (var s in _all)
            {
                _byId[s.id] = s;
                if (s.IsRare) _rares.Add(s); else _regular.Add(s);
            }
        }
    }
}
