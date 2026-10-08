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
        // レア
        public string rareOf;      // 元になるいきもの（その個体がまれにレアになる）
        public float rareChance;
        public bool IsRare => rareOf != null;
    }

    /// <summary>いきもの図鑑にのる 18 しゅと、めったに会えないレア 2 しゅ。</summary>
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
                new SpeciesDef { id = "ant", name = "アリ", areaLabel = "森・川辺", kind = MobKind.Marcher, body = "Ant", speed = 0.9f, discoverRadius = 2.6f,
                    climbs = true, rig = "Ant", stride = 0.11f, legSwing = 26f, legLift = 18f,
                    parts = new[] { new PartMount { mesh = "Crumb", offset = new Vector3(0f, 0.105f, -0.19f), pair = false, onlyCarrying = true } },
                    description = "いつも列をつくって、せっせと食べものを運んでいる。においの道をたどって、迷わず巣へ帰る。",
                    hint = "はじまりの苔原やどんぐり広場で、行列を見かけるかも。" },
                new SpeciesDef { id = "snail", name = "かたつむり", areaLabel = "森", kind = MobKind.Crawler, body = "Snail", scale = 1.2f, speed = 0.12f, discoverRadius = 2.6f,
                    climbs = true, rideable = true, gait = GaitKind.None,
                    description = "ゆっくり、ゆっくり。通ったあとには、きらきら光る道がのこる。",
                    hint = "しめった場所が好き。赤キノコの根もとをさがしてみよう。" },
                new SpeciesDef { id = "butterfly", name = "ちょうちょ", areaLabel = "森・川辺", kind = MobKind.Flutter, body = "Butterfly_Body", speed = 1.4f, discoverRadius = 5.5f,
                    parts = new[] { new PartMount { mesh = "Butterfly_Wing", restRoll = 25f, flapAmp = 65f, flapHz = 7f } },
                    description = "空色の小さなチョウ、ルリシジミ。花から花へ、ひらひら舞う。",
                    hint = "花の草原や、川辺の花のまわり。" },
                new SpeciesDef { id = "otoshibumi", name = "オトシブミ", areaLabel = "森", kind = MobKind.Crawler, body = "Otoshibumi", scale = 1.3f, speed = 0.22f, discoverRadius = 2.6f,
                    climbs = true, rig = "Otoshibumi", stride = 0.09f,
                    description = "葉っぱをくるくる巻いて「ゆりかご」を作り、中に卵を産む。落ちている巻物は、まるで手紙のよう。",
                    hint = "葉っぱの巻物がころがっている場所の近く。" },
                new SpeciesDef { id = "grasshopper", name = "バッタ", areaLabel = "森・川辺", kind = MobKind.Hopper, body = "Grasshopper", speed = 0.3f, discoverRadius = 4f,
                    rig = "Grasshopper", stride = 0.2f, legSwing = 14f,
                    parts = new[] { new PartMount { mesh = "Grasshopper_Hind", offset = CreatureRig.GrasshopperHip } },
                    description = "大きな後ろ足で、体の何十倍も遠くまでジャンプする。",
                    hint = "草のしげった明るい場所で、ぴょんと跳ねている。" },
                new SpeciesDef { id = "frog", name = "アマガエル", areaLabel = "森・川辺", kind = MobKind.Hopper, body = "Frog", speed = 0.2f, discoverRadius = 3.5f,
                    rideable = true, gait = GaitKind.None,
                    description = "雨がふりそうになると鳴きだす。指先の吸盤で、つるつるの葉っぱにもぴたり。",
                    hint = "水たまりのふちや、川のほとり。" },
                new SpeciesDef { id = "sparrow", name = "スズメ", areaLabel = "森・川辺", kind = MobKind.Bird, body = "Sparrow_Body", speed = 1.2f, discoverRadius = 10f, fleeRadius = 4.5f,
                    birdSize = 5.5f, gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Sparrow_Wing", flapAmp = 60f, flapHz = 9f } },
                    description = "人の近くでくらす、おなじみの小鳥。ちょんちょん跳ねて、地面の草の実をついばむ。",
                    hint = "ひらけた明るい場所に下りてくる。近づきすぎると飛んでいってしまう。" },
                new SpeciesDef { id = "crow", name = "カラス", areaLabel = "森・川辺", kind = MobKind.Bird, body = "Crow_Body", speed = 2.5f, discoverRadius = 16f, fleeRadius = 8f,
                    birdSize = 18f, gait = GaitKind.None,
                    parts = new[] { new PartMount { mesh = "Crow_Wing", flapAmp = 50f, flapHz = 3.2f } },
                    description = "とても頭のいい大きな鳥。しゃくとりむしから見ると、まるで黒い山のよう。",
                    hint = "高い場所に下りて、あたりを見張っている。" },
                new SpeciesDef { id = "ladybug", name = "てんとうむし", areaLabel = "森・川辺", kind = MobKind.Crawler, body = "Ladybug", speed = 0.35f, discoverRadius = 2.6f,
                    climbs = true, rig = "Ladybug", stride = 0.12f, legSwing = 20f,
                    description = "七つの黒い星のナナホシテントウ。アブラムシを食べてくれる、植物の味方。",
                    hint = "葉っぱや花のまわりを歩いている。" },
                new SpeciesDef { id = "pillbug", name = "だんごむし", areaLabel = "森", kind = MobKind.Crawler, body = "PillBug", speed = 0.28f, discoverRadius = 2.6f,
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
                new SpeciesDef { id = "dragonfly", name = "アキアカネ", areaLabel = "森・川辺", kind = MobKind.Hover, body = "Dragonfly_Body", speed = 3f, discoverRadius = 5.5f,
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
                new SpeciesDef { id = "spider", name = "ハエトリグモ", areaLabel = "森・川辺", kind = MobKind.Pouncer, body = "Spider", speed = 0.5f, discoverRadius = 2.8f,
                    climbs = true, rig = "Spider", stride = 0.14f, legSwing = 20f, legLift = 22f,
                    description = "大きな前の目でえものをねらい、ぴょんと跳びかかる小さなクモ。巣は張らずに歩きまわる。",
                    hint = "切り株の上や丸太のまわり、川辺の石の上をうろうろしている。" },
                new SpeciesDef { id = "mantis", name = "オオカマキリ", areaLabel = "森・川辺", kind = MobKind.Stalker, body = "Mantis", speed = 0.25f, discoverRadius = 4.5f,
                    rideable = true, rig = "Mantis", stride = 0.45f, legSwing = 14f, legLift = 14f,
                    parts = new[] { new PartMount { mesh = "Mantis_Arm", offset = CreatureRig.MantisShoulder } },
                    description = "かまのような前足で、じっとえものを待つ。近づくと、かまを持ち上げてこちらをにらむ。",
                    hint = "花の草原の、草がたくさん生えているところ。" },
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
