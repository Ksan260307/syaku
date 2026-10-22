using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>集めた数（しずく・名所・いきもの）。</summary>
    public struct ProgressStats
    {
        public int drops;
        public int places;
        public int species;
        public int observed;      // 観察したしぐさ
        public int bestFriends;   // なかよし ★5 の種

        public static ProgressStats FromSave()
        {
            var d = SaveSystem.Data;
            return new ProgressStats { drops = Mathf.Max(d.drops.Count, d.legacyDrops), places = d.places.Count, species = Creatures.DiscoveredCount,
                observed = Friends.TotalObserved, bestFriends = Friends.BestFriendCount };
        }

        public bool IsComplete => drops >= Areas.TotalDrops && places >= Areas.TotalLandmarks && species >= SpeciesCatalog.Count;
    }

    /// <summary>きせかえの種類：体のいろ・ぼうし（頭の上）・めがね（目の前）。いろ・ぼうし・めがねは、1 つずつ同時に身につけられる。</summary>
    public enum WearKind { Color, Hat, Glasses }

    public class SkinDef
    {
        public string id;
        public string name;
        public WearKind kind = WearKind.Color;
        public float hue;        // 色相をずらす角度（度）
        public float sat = 1f;
        public float val = 1f;
        public float hueAlong;   // 体の尾から頭へ、色相をずらしていく角度（度。レインボーのしま）
        public float hueSpeed;   // 色相が時間で回る速さ（度/秒）
        public float stripes;    // 体の前後のしまの数（0 = なし）
        public float stripeDark; // しまの暗さ
        public float dots;       // 水玉の濃さ（0 = なし）
        public string mesh;      // ぼうし・めがねのメッシュ（しゃくとりむしの頭のまん中が原点）
        public bool bouncy;      // 動くと、ぴょこぴょこゆれる（ぴょこんげ）
        public Color swatch;
        public string condition;
        public int needDrops, needPlaces, needSpecies, needObserved, needFriends;
        public bool needAll;
        public bool rainbow;
        /// <summary>図鑑の絵の名前（きせかえの画面の絵）。</summary>
        public string Icon => "skin_" + id;
    }

    /// <summary>しゃくとりむしのきせかえ（集めた数でふえていく）。</summary>
    public static class Skins
    {
        static List<SkinDef> _all;

        public static List<SkinDef> All
        {
            get
            {
                if (_all == null)
                {
                    _all = new List<SkinDef>
                    {
                        new SkinDef { id = "wakaba", name = "わかば", swatch = Hex("#8fd14f"), condition = "はじめから" },
                        new SkinDef { id = "kimidori", name = "きみどり", hue = -22f, sat = 1.1f, val = 1.12f, swatch = Hex("#c2e04a"), needDrops = 10, condition = "しずくを 10 こ" },
                        new SkinDef { id = "sakura", name = "さくら", hue = -110f, sat = 0.55f, val = 1.18f, swatch = Hex("#f4a6c0"), needPlaces = 5, condition = "名所を 5 か所" },
                        new SkinDef { id = "sorairo", name = "そらいろ", hue = 115f, sat = 0.75f, val = 1.12f, swatch = Hex("#7cc4f2"), needSpecies = 4, condition = "いきものを 4 しゅ" },
                        new SkinDef { id = "momiji", name = "もみじ", hue = -75f, sat = 1.2f, val = 1.0f, swatch = Hex("#e8663a"), needDrops = 30, condition = "しずくを 30 こ" },
                        new SkinDef { id = "yozora", name = "よぞら", hue = 175f, sat = 0.85f, val = 0.8f, swatch = Hex("#6a5ac8"), needSpecies = 10, condition = "いきものを 10 しゅ" },
                        new SkinDef { id = "kogane", name = "こがね", hue = -45f, sat = 1.35f, val = 1.25f, swatch = Hex("#f2c53a"), needSpecies = SpeciesCatalog.Count, condition = "いきもの図鑑をコンプリート" },
                        new SkinDef { id = "ruri", name = "るりいろ", hue = 130f, sat = 1.05f, val = 0.85f, swatch = Hex("#3a62c8"), needObserved = 30, condition = "しぐさを 30 こ観察" },
                        new SkinDef { id = "fuji", name = "ふじいろ", hue = -170f, sat = 0.5f, val = 1.15f, swatch = Hex("#b9a2e6"), needFriends = 5, condition = "5 しゅと、なかよし ★5" },
                        new SkinDef { id = "niji", name = "にじいろ", rainbow = true, sat = 1.1f, val = 1.1f, swatch = Hex("#ff7ad0"), needAll = true, condition = "ぜんぶ集める" },
                        // ---- 新しいいろ（もようつき） ----
                        new SkinDef { id = "mint", name = "ミント", hue = 62f, sat = 0.62f, val = 1.18f, swatch = Hex("#86e3c6"), needDrops = 40, condition = "しずくを 40 こ" },
                        new SkinDef { id = "ichigo", name = "いちごみるく", hue = -118f, sat = 0.78f, val = 1.2f, dots = 0.85f, swatch = Hex("#f59ab0"), needPlaces = 15, condition = "名所を 15 か所" },
                        new SkinDef { id = "shima", name = "はちみつしましま", hue = -48f, sat = 1.35f, val = 1.25f, stripes = 13f, stripeDark = 0.72f, swatch = Hex("#f2c53a"), needSpecies = 25, condition = "いきものを 25 しゅ" },
                        new SkinDef { id = "yuki", name = "ゆきんこ", sat = 0.12f, val = 1.38f, swatch = Hex("#f0f4ec"), needDrops = 90, condition = "しずくを 90 こ" },
                        new SkinDef { id = "sumi", name = "すみいろ", sat = 0.12f, val = 0.3f, swatch = Hex("#4a4c46"), needObserved = 60, condition = "しぐさを 60 こ観察" },
                        new SkinDef { id = "rainbow", name = "レインボー", hue = -90f, hueAlong = 360f, hueSpeed = 30f, sat = 1.25f, val = 1.15f, swatch = Hex("#7ad0ff"), needSpecies = 35, condition = "いきものを 35 しゅ" },
                        // ---- ぼうし（頭の上） ----
                        new SkinDef { id = "hair", name = "ぴょこんげ", kind = WearKind.Hat, mesh = "Wear_Hair", bouncy = true, swatch = Hex("#3a2a1e"), condition = "はじめから" },
                        new SkinDef { id = "cap", name = "キャップ", kind = WearKind.Hat, mesh = "Wear_Cap", swatch = Hex("#e0443a"), needDrops = 5, condition = "しずくを 5 こ" },
                        new SkinDef { id = "acorn", name = "どんぐりぼうし", kind = WearKind.Hat, mesh = "Wear_Acorn", swatch = Hex("#8a6a3e"), needSpecies = 6, condition = "いきものを 6 しゅ" },
                        new SkinDef { id = "straw", name = "むぎわらぼうし", kind = WearKind.Hat, mesh = "Wear_Straw", swatch = Hex("#ecc96a"), needDrops = 20, condition = "しずくを 20 こ" },
                        new SkinDef { id = "ribbon", name = "リボン", kind = WearKind.Hat, mesh = "Wear_Ribbon", swatch = Hex("#ec4f6a"), needSpecies = 12, condition = "いきものを 12 しゅ" },
                        new SkinDef { id = "knit", name = "ニットぼう", kind = WearKind.Hat, mesh = "Wear_Knit", swatch = Hex("#3a6ad8"), needDrops = 60, condition = "しずくを 60 こ" },
                        new SkinDef { id = "flower", name = "花かんむり", kind = WearKind.Hat, mesh = "Wear_Flower", swatch = Hex("#f6a0c0"), needObserved = 15, condition = "しぐさを 15 こ観察" },
                        new SkinDef { id = "beret", name = "ベレーぼう", kind = WearKind.Hat, mesh = "Wear_Beret", swatch = Hex("#a83a4a"), needFriends = 2, condition = "2 しゅと、なかよし ★5" },
                        new SkinDef { id = "silk", name = "シルクハット", kind = WearKind.Hat, mesh = "Wear_Silk", swatch = Hex("#26222a"), needDrops = 140, condition = "しずくを 140 こ" },
                        new SkinDef { id = "crown", name = "おうかん", kind = WearKind.Hat, mesh = "Wear_Crown", swatch = Hex("#f2c040"), needPlaces = Areas.TotalLandmarks, condition = "名所をぜんぶ見つける" },
                        // ---- めがね（目の前） ----
                        new SkinDef { id = "round", name = "まるめがね", kind = WearKind.Glasses, mesh = "Wear_RoundGlasses", swatch = Hex("#9a7a3a"), needPlaces = 3, condition = "名所を 3 か所" },
                        new SkinDef { id = "sun", name = "サングラス", kind = WearKind.Glasses, mesh = "Wear_Sunglasses", swatch = Hex("#23262e"), needPlaces = 8, condition = "名所を 8 か所" },
                        new SkinDef { id = "heart", name = "ハートのサングラス", kind = WearKind.Glasses, mesh = "Wear_HeartGlasses", swatch = Hex("#f04a8a"), needSpecies = 20, condition = "いきものを 20 しゅ" },
                        new SkinDef { id = "star", name = "ほしのめがね", kind = WearKind.Glasses, mesh = "Wear_StarGlasses", swatch = Hex("#f2c030"), needPlaces = 25, condition = "名所を 25 か所" },
                    };
                }
                return _all;
            }
        }

        static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        public static SkinDef Get(string id)
        {
            foreach (var s in All)
                if (s.id == id) return s;
            return All[0];
        }

        /// <summary>id のきせかえ（なければ null。ぼうし・めがねの「つけていない」は空の id）。</summary>
        public static SkinDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All)
                if (s.id == id) return s;
            return null;
        }

        /// <summary>その種類のきせかえ（いろ・ぼうし・めがね）。</summary>
        public static List<SkinDef> OfKind(WearKind kind)
        {
            var list = new List<SkinDef>();
            foreach (var s in All)
                if (s.kind == kind) list.Add(s);
            return list;
        }

        /// <summary>いま身につけているぼうし・めがね（つけていない・まだ使えないときは null）。</summary>
        public static SkinDef Worn(WearKind kind)
        {
            var d = SaveSystem.Data;
            var s = Find(kind == WearKind.Hat ? d.hat : kind == WearKind.Glasses ? d.glasses : d.skin);
            return s != null && s.kind == kind ? s : null;
        }

        public static bool IsUnlocked(SkinDef s, ProgressStats st)
        {
            if (s.needAll) return st.IsComplete;
            return st.drops >= s.needDrops && st.places >= s.needPlaces && st.species >= s.needSpecies
                && st.observed >= s.needObserved && st.bestFriends >= s.needFriends;
        }

        public static List<SkinDef> NewlyUnlocked(ProgressStats before, ProgressStats after)
        {
            var list = new List<SkinDef>();
            foreach (var s in All)
                if (!IsUnlocked(s, before) && IsUnlocked(s, after)) list.Add(s);
            return list;
        }

        static readonly int HueId = Shader.PropertyToID("_HueShift");
        static readonly int SatId = Shader.PropertyToID("_SatMul");
        static readonly int ValId = Shader.PropertyToID("_ValMul");
        static readonly int AlongId = Shader.PropertyToID("_HueAlong");
        static readonly int StripesId = Shader.PropertyToID("_Stripes");
        static readonly int StripeDarkId = Shader.PropertyToID("_StripeDark");
        static readonly int DotsId = Shader.PropertyToID("_Dots");
        static MaterialPropertyBlock _mpb;

        /// <summary>体のいろを、しゃくとりむしのシェーダーにわたす（ぼうし・めがねは、いろを変えない）。</summary>
        public static void Apply(Renderer r, SkinDef s, float time)
        {
            if (r == null || s == null || s.kind != WearKind.Color) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            float hue = s.rainbow ? Mathf.Repeat(time * 40f, 360f) : s.hueSpeed != 0f ? Mathf.Repeat(s.hue + time * s.hueSpeed, 360f) : s.hue;
            _mpb.SetFloat(HueId, hue);
            _mpb.SetFloat(SatId, s.sat);
            _mpb.SetFloat(ValId, s.val);
            _mpb.SetFloat(AlongId, s.hueAlong);
            _mpb.SetFloat(StripesId, s.stripes);
            _mpb.SetFloat(StripeDarkId, s.stripeDark);
            _mpb.SetFloat(DotsId, s.dots);
            r.SetPropertyBlock(_mpb);
        }

        static readonly int RimId = Shader.PropertyToID("_RimStrength");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        /// <summary>
        /// きせかえの色（Apply のあと）に、体のようすをかける：明るさと鮮やかさの倍率、ふちの光（つや・てらされる光）の強さと色。
        /// </summary>
        public static void ApplyTint(Renderer r, float valueMul, float satMul, float rimAdd, Color rimColor)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            float baseRim = r.sharedMaterial != null && r.sharedMaterial.HasProperty(RimId) ? r.sharedMaterial.GetFloat(RimId) : 0.35f;
            _mpb.SetFloat(ValId, _mpb.GetFloat(ValId) * valueMul);
            _mpb.SetFloat(SatId, _mpb.GetFloat(SatId) * satMul);
            _mpb.SetFloat(RimId, baseRim + rimAdd);
            _mpb.SetColor(RimColorId, rimColor);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>シェーダーと同じ色の変え方（テストや UI の見本用）。</summary>
        public static Color Shift(Color c, float hueDeg, float sat, float val)
        {
            Color.RGBToHSV(c, out float h, out float sa, out float v);
            h = Mathf.Repeat(h + hueDeg / 360f, 1f);
            return Color.HSVToRGB(h, Mathf.Clamp01(sa * sat), v * val);
        }
    }
}
