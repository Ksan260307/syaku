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
        public bool sideways;   // カニの横歩き
    }

    /// <summary>いきもの図鑑にのる 16 しゅ。</summary>
    public static class SpeciesCatalog
    {
        static List<SpeciesDef> _all;
        static Dictionary<string, SpeciesDef> _byId;

        public static List<SpeciesDef> All
        {
            get
            {
                if (_all == null) Build();
                return _all;
            }
        }

        public static SpeciesDef Get(string id)
        {
            if (_all == null) Build();
            return _byId.TryGetValue(id, out var s) ? s : null;
        }

        public static int Count => All.Count;

        static void Build()
        {
            _all = new List<SpeciesDef>
            {
                new SpeciesDef { id = "ant", name = "アリ", areaLabel = "森・川辺", kind = MobKind.Marcher, body = "Ant", speed = 0.9f, discoverRadius = 2.6f,
                    description = "いつも列をつくって、せっせと食べものを運んでいる。においの道をたどって、迷わず巣へ帰る。",
                    hint = "はじまりの苔原やどんぐり広場で、行列を見かけるかも。" },
                new SpeciesDef { id = "snail", name = "かたつむり", areaLabel = "森", kind = MobKind.Crawler, body = "Snail", scale = 1.2f, speed = 0.12f, discoverRadius = 2.6f,
                    description = "ゆっくり、ゆっくり。通ったあとには、きらきら光る道がのこる。",
                    hint = "しめった場所が好き。赤キノコの根もとをさがしてみよう。" },
                new SpeciesDef { id = "butterfly", name = "ちょうちょ", areaLabel = "森・川辺", kind = MobKind.Flutter, body = "Butterfly_Body", speed = 1.4f, discoverRadius = 5.5f,
                    parts = new[] { new PartMount { mesh = "Butterfly_Wing", restRoll = 25f, flapAmp = 65f, flapHz = 7f } },
                    description = "空色の小さなチョウ、ルリシジミ。花から花へ、ひらひら舞う。",
                    hint = "花の草原や、川辺の花のまわり。" },
                new SpeciesDef { id = "otoshibumi", name = "オトシブミ", areaLabel = "森", kind = MobKind.Crawler, body = "Otoshibumi", scale = 1.3f, speed = 0.22f, discoverRadius = 2.6f,
                    description = "葉っぱをくるくる巻いて「ゆりかご」を作り、中に卵を産む。落ちている巻物は、まるで手紙のよう。",
                    hint = "葉っぱの巻物がころがっている場所の近く。" },
                new SpeciesDef { id = "grasshopper", name = "バッタ", areaLabel = "森・川辺", kind = MobKind.Hopper, body = "Grasshopper", speed = 0.3f, discoverRadius = 4f,
                    description = "大きな後ろ足で、体の何十倍も遠くまでジャンプする。",
                    hint = "草のしげった明るい場所で、ぴょんと跳ねている。" },
                new SpeciesDef { id = "frog", name = "アマガエル", areaLabel = "森・川辺", kind = MobKind.Hopper, body = "Frog", speed = 0.2f, discoverRadius = 3.5f,
                    description = "雨がふりそうになると鳴きだす。指先の吸盤で、つるつるの葉っぱにもぴたり。",
                    hint = "水たまりのふちや、川のほとり。" },
                new SpeciesDef { id = "sparrow", name = "スズメ", areaLabel = "森・川辺", kind = MobKind.Bird, body = "Sparrow_Body", speed = 1.2f, discoverRadius = 10f, fleeRadius = 4.5f,
                    parts = new[] { new PartMount { mesh = "Sparrow_Wing", offset = new Vector3(0.66f, 2.1f, -0.1f), restYaw = -78f, restRoll = -14f, flapAmp = 60f, flapHz = 9f } },
                    description = "人の近くでくらす、おなじみの小鳥。ちょんちょん跳ねて、地面の草の実をついばむ。",
                    hint = "ひらけた明るい場所に下りてくる。近づきすぎると飛んでいってしまう。" },
                new SpeciesDef { id = "crow", name = "カラス", areaLabel = "森・川辺", kind = MobKind.Bird, body = "Crow_Body", speed = 2.5f, discoverRadius = 16f, fleeRadius = 8f,
                    parts = new[] { new PartMount { mesh = "Crow_Wing", offset = new Vector3(2.2f, 6.8f, -0.4f), restYaw = -80f, restRoll = -12f, flapAmp = 50f, flapHz = 3.2f } },
                    description = "とても頭のいい大きな鳥。しゃくとりむしから見ると、まるで黒い山のよう。",
                    hint = "高い場所に下りて、あたりを見張っている。" },
                new SpeciesDef { id = "ladybug", name = "てんとうむし", areaLabel = "森・川辺", kind = MobKind.Crawler, body = "Ladybug", speed = 0.35f, discoverRadius = 2.6f,
                    description = "七つの黒い星のナナホシテントウ。アブラムシを食べてくれる、植物の味方。",
                    hint = "葉っぱや花のまわりを歩いている。" },
                new SpeciesDef { id = "pillbug", name = "だんごむし", areaLabel = "森", kind = MobKind.Crawler, body = "PillBug", speed = 0.28f, discoverRadius = 2.6f,
                    description = "さわると、くるんとまるくなる。じめじめした落ち葉の下が大好き。",
                    hint = "丸太のトンネルの中や、落ち葉のたまり場。" },
                new SpeciesDef { id = "beetle", name = "カブトムシ", areaLabel = "森", kind = MobKind.Crawler, body = "Beetle", speed = 0.1f, discoverRadius = 3.6f,
                    description = "森の力持ち。りっぱな角で、木の上の場所とりをする。",
                    hint = "大樹の根っこの上で見かけたという話。" },
                new SpeciesDef { id = "waterstrider", name = "アメンボ", areaLabel = "森・川辺", kind = MobKind.Skater, body = "WaterStrider", speed = 1.6f, discoverRadius = 4.5f,
                    description = "細い足の毛で水をはじき、水面をすいすい歩く。",
                    hint = "流れのゆるやかな水面。" },
                new SpeciesDef { id = "dragonfly", name = "アキアカネ", areaLabel = "森・川辺", kind = MobKind.Hover, body = "Dragonfly_Body", speed = 3f, discoverRadius = 5.5f,
                    parts = new[]
                    {
                        new PartMount { mesh = "Dragonfly_Wing", offset = new Vector3(0.03f, 0.05f, -0.1f), restYaw = 8f, flapAmp = 18f, flapHz = 22f },
                        new PartMount { mesh = "Dragonfly_Wing", offset = new Vector3(0.03f, 0.05f, 0.02f), restYaw = -14f, flapAmp = 18f, flapHz = 22f },
                    },
                    description = "秋になると真っ赤になるトンボ。大きな目で、ぐるりと見わたす。",
                    hint = "水の上を、すーっと飛んでいる。" },
                new SpeciesDef { id = "crab", name = "サワガニ", areaLabel = "川辺", kind = MobKind.Crawler, body = "Crab", speed = 0.45f, discoverRadius = 2.8f, sideways = true,
                    description = "きれいな川にだけすむ小さなカニ。横歩きで、石のすきまにかくれる。",
                    hint = "川のまんなかの中州で見かけたという…。" },
                new SpeciesDef { id = "riversnail", name = "カワニナ", areaLabel = "川辺", kind = MobKind.Crawler, body = "RiverSnail", speed = 0.08f, discoverRadius = 2.6f,
                    description = "とがった巻き貝。ゲンジボタルの幼虫のごちそうでもある。",
                    hint = "とびいしのあたりの水ぎわ。" },
                new SpeciesDef { id = "firefly", name = "ゲンジボタル", areaLabel = "川辺", kind = MobKind.Hover, body = "Firefly_Body", speed = 0.5f, discoverRadius = 4.5f,
                    parts = new[] { new PartMount { mesh = "Firefly_Glow", pair = false, glow = true } },
                    description = "おしりの光でおしゃべりする。ホタルがいるのは、きれいな川のしるし。",
                    hint = "水辺の草のかげで、ぽうっと光っている。" },
            };
            _byId = new Dictionary<string, SpeciesDef>();
            foreach (var s in _all) _byId[s.id] = s;
        }
    }
}
