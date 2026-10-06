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

        public static ProgressStats FromSave()
        {
            var d = SaveSystem.Data;
            return new ProgressStats { drops = d.drops.Count, places = d.places.Count, species = d.creatures.Count };
        }

        public bool IsComplete => drops >= Areas.TotalDrops && places >= Areas.TotalLandmarks && species >= SpeciesCatalog.Count;
    }

    public class SkinDef
    {
        public string id;
        public string name;
        public float hue;        // 色相をずらす角度（度）
        public float sat = 1f;
        public float val = 1f;
        public Color swatch;
        public string condition;
        public int needDrops, needPlaces, needSpecies;
        public bool needAll;
        public bool rainbow;
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
                        new SkinDef { id = "kogane", name = "こがね", hue = -45f, sat = 1.35f, val = 1.25f, swatch = Hex("#f2c53a"), needSpecies = 16, condition = "いきもの図鑑をコンプリート" },
                        new SkinDef { id = "niji", name = "にじいろ", rainbow = true, sat = 1.1f, val = 1.1f, swatch = Hex("#ff7ad0"), needAll = true, condition = "ぜんぶ集める" },
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

        public static bool IsUnlocked(SkinDef s, ProgressStats st)
        {
            if (s.needAll) return st.IsComplete;
            return st.drops >= s.needDrops && st.places >= s.needPlaces && st.species >= s.needSpecies;
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
        static MaterialPropertyBlock _mpb;

        public static void Apply(Renderer r, SkinDef s, float time)
        {
            if (r == null || s == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            float hue = s.rainbow ? Mathf.Repeat(time * 40f, 360f) : s.hue;
            _mpb.SetFloat(HueId, hue);
            _mpb.SetFloat(SatId, s.sat);
            _mpb.SetFloat(ValId, s.val);
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
