using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 神社エリアの組み立て：大鳥居・石灯籠・石畳の参道と石段・基壇と玉垣・お社（鈴と鈴緒）・さい銭箱・キツネの石像・
    /// 手水舎（水盤の水）・ご神木（しめ縄・うろ・太い枝）・千本鳥居と奥の宮・池と太鼓橋・絵馬かけ・おみくじかけ・
    /// 鎮守の森の大きな幹・苔とシダとササ・ヒガンバナ・イチョウとモミジの落ち葉。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _shrineTops = new List<Vector3>();     // しずくを置く、物の上（上からさがす）
        readonly List<Vector3> _lanternLights = new List<Vector3>();  // 石灯籠の火袋のまん中（ガが集まる）
        readonly List<Vector3> _owlPerches = new List<Vector3>();     // フクロウ（とカラス）が下りる、高い所
        readonly List<Vector3> _smallTorii = new List<Vector3>();     // 千本鳥居の小さな鳥居の足もと
        Vector3 _treeBase;

        /// <summary>石灯籠の火袋のまん中（テストと、あかりの光）。</summary>
        public IReadOnlyList<Vector3> LanternLights => _lanternLights;
        /// <summary>フクロウが下りる高い所（テスト用）。</summary>
        public IReadOnlyList<Vector3> OwlPerches => _owlPerches;
        /// <summary>千本鳥居の小さな鳥居（テスト用）。</summary>
        public IReadOnlyList<Vector3> SmallTorii => _smallTorii;

        void ClearShrine()
        {
            _shrineTops.Clear();
            _lanternLights.Clear();
            _owlPerches.Clear();
            _smallTorii.Clear();
            _treeBase = Vector3.zero;
        }

        static Vector3 SG(Vector2 p) => ShrineLayout.Ground(p.x, p.y);

        /// <summary>鎮守の森の、木もれ日の光の筋が落ちる所（参道・手水舎・池・千本鳥居）。</summary>
        public static readonly Vector2[] ShrineShaftSpots =
        {
            new Vector2(-5f, -30f), new Vector2(9f, -12f), new Vector2(-18f, -4f), new Vector2(-24f, 18f), new Vector2(24f, -16f), new Vector2(30f, 12f), new Vector2(10f, 30f),
        };

        /// <summary>まわした物の、ローカルの点（x, y, z）の世界での場所。</summary>
        static Vector3 At(Vector3 origin, Quaternion rot, float x, float y, float z) => origin + rot * new Vector3(x, y, z);

        // ------------------------------------------------------------------
        // 大きな物
        // ------------------------------------------------------------------
        void BuildShrineSolids()
        {
            BuildShrineBackdrop();
            Material prop = assets.prop;
            Material bark = assets.bark;
            float floor = ShrineLayout.FlatHeight;
            float plat = ShrineLayout.PlatformY;

            // 大鳥居（額は南、参道から来る方を向く）
            {
                Vector3 o = new Vector3(ShrineLayout.GreatTorii.x, floor, ShrineLayout.GreatTorii.y);
                Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
                Place("Shrine_Torii", prop, o + Vector3.down * 0.25f, rot, 1f, true, true, 500f, asRenderer: true);
                for (int sx = -1; sx <= 1; sx += 2) Occupy(new Vector2(o.x + 11f * sx, o.z), 3.5f);
                _shrineTops.Add(At(o, rot, -8f, 40f, 0f));
                _shrineTops.Add(At(o, rot, 8f, 40f, 0f));
                _owlPerches.Add(At(o, rot, 0f, 40f, 0f));
            }

            // 石灯籠（火袋の窓は、参道の方を向く）
            for (int i = 0; i < ShrineLayout.Lanterns.Length; i++)
            {
                Vector2 l = ShrineLayout.Lanterns[i];
                bool onPlatform = i >= 4;
                Vector3 o = new Vector3(l.x, onPlatform ? plat : floor, l.y);
                Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                Place("Shrine_Lantern", prop, o + Vector3.down * 0.15f, rot, 1f, true, true, 300f, asRenderer: true);
                Occupy(l, 3.6f);
                _lanternLights.Add(o + new Vector3(0f, 11.1f, 0f));
                if (i < 4 || i == 5) _shrineTops.Add(o + Vector3.up * 18f);
            }

            // 参道の石畳（見た目だけ。地面のすぐ上）と、手水舎・ご神木・池へのわかれ道の飛び石
            for (float z = -54f; z <= ShrineLayout.StepsZ0 - 1.2f; z += 2.15f)
                for (int k = -1; k <= 1; k++)
                {
                    float x = k * 2.2f + ((Mathf.RoundToInt(z * 10f) % 2 == 0) ? 0.5f : -0.5f);
                    if (Mathf.Abs(x) > ShrineLayout.PathHalf - 0.6f) x = Mathf.Sign(x) * (ShrineLayout.PathHalf - 0.6f);
                    Vector3 g = SG(new Vector2(x, z));
                    Place("Shrine_Slab", prop, g + Vector3.down * 0.13f, Quaternion.Euler(0f, R(-3f, 3f), 0f), R(0.9f, 1.02f), false, false, 160f);
                }
            Vector2[] stones =
            {
                new Vector2(-5.5f, -14f), new Vector2(-8.5f, -14.2f), new Vector2(-11.5f, -13.8f),
                new Vector2(-5f, 3.6f), new Vector2(-8f, 4.8f), new Vector2(-11f, 5.9f), new Vector2(-14f, 6.9f),
                new Vector2(5f, -29f), new Vector2(8f, -27.8f), new Vector2(11f, -26.6f),
            };
            foreach (var p in stones)
                Place("Shrine_Slab", prop, SG(p) + Vector3.down * 0.13f, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.7f, 0.85f), false, false, 140f);

            // 石段（坂の上にのせる。上を歩ける）
            for (int i = 0; i < ShrineLayout.StepCount; i++)
            {
                Vector2 st = ShrineLayout.Step(i);
                Place("Shrine_Step", prop, new Vector3(0f, st.x - 0.3f, st.y), Quaternion.identity, 1f, true, true, 240f);
            }
            Occupy(new Vector2(0f, 12f), 8.5f);

            // 基壇の前の玉垣（石段とキツネの像の前はあける：像の台座とのあいだに、しゃくとりむしがはさまるすき間ができない）
            foreach (float x in new[] { -20.0f, 20.0f })
                Place("Shrine_Fence", prop, new Vector3(x, plat - 0.1f, ShrineLayout.PlatformMin.y + 0.35f), Quaternion.identity, 1f, true, true, 220f);

            // お社（南の石段の方を向く）と、さい銭箱。鈴緒を引くと、鈴が鳴る
            {
                Vector3 o = new Vector3(ShrineLayout.Hall.x, plat, ShrineLayout.Hall.y);
                Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
                Place("Shrine_Hall", prop, o + Vector3.down * 0.05f, rot, 1f, true, true, 500f, asRenderer: true);
                Occupy(ShrineLayout.Hall + new Vector2(0f, 0f), 16f);
                _shrineTops.Add(At(o, rot, 0f, 50f, 1f));        // 屋根のむね
                _shrineTops.Add(At(o, rot, 7.5f, 7.2f, 9.5f));    // 縁の上
                _owlPerches.Add(At(o, rot, -11f, 50f, 1f));
                Vector3 s = new Vector3(ShrineLayout.Saisen.x, plat, ShrineLayout.Saisen.y);
                Place("Shrine_Saisen", prop, s, rot, 1f, true, true, 260f);
                _shrineTops.Add(s + Vector3.up * 6f);
                Occupy(ShrineLayout.Saisen, 4f);
                var bellGo = new GameObject("ShrineBell");
                bellGo.transform.SetParent(Root, false);
                var bell = bellGo.AddComponent<ShrineBell>();
                bell.ropeLow = At(o, rot, 0f, 6.6f, 12.65f);
                bell.ropeHigh = At(o, rot, 0f, 19.2f, 12.6f);
                bell.bell = At(o, rot, 0f, 20.4f, 12.6f);
            }

            // キツネの石像（参道の方を、少し内がわへ向く）
            for (int i = 0; i < ShrineLayout.FoxStatues.Length; i++)
            {
                Vector2 f = ShrineLayout.FoxStatues[i];
                Quaternion rot = Quaternion.Euler(0f, i == 0 ? 160f : 200f, 0f);
                Vector3 o = new Vector3(f.x, plat, f.y);
                Place(i == 0 ? "Shrine_Fox_A" : "Shrine_Fox_B", prop, o + Vector3.down * 0.1f, rot, 1f, true, true, 300f, asRenderer: true);
                Occupy(f, 4.5f);
                _shrineTops.Add(At(o, rot, 0f, 20f, 1.4f));
            }

            // 手水舎（水盤の長い向きが南北。参道の方があいている）
            {
                Vector3 o = new Vector3(ShrineLayout.Chozuya.x, ShrineLayout.ChozuyaFloor, ShrineLayout.Chozuya.y);
                Quaternion rot = Quaternion.Euler(0f, ShrineLayout.ChozuyaYaw, 0f);
                Place("Shrine_Chozuya", prop, o + Vector3.down * 0.05f, rot, 1f, true, true, 300f, asRenderer: true);
                Occupy(ShrineLayout.Chozuya, 10f);
                _shrineTops.Add(At(o, rot, 4f, 26f, 0f));          // 屋根のむね
                _shrineTops.Add(At(o, rot, -4.5f, 7f, 2.7f));      // 水盤のふち
            }

            // ご神木（しめ縄と紙垂・うろは、参道の方＝東を向く）
            {
                Vector3 o = SG(ShrineLayout.Tree);
                _treeBase = o;
                Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                Place("Shrine_Tree", bark, o + Vector3.down * 0.6f, rot, 1f, true, true, 600f, asRenderer: true);
                Occupy(ShrineLayout.Tree, 14f);
                Physics.SyncTransforms();
                // しめ縄の上（参道がわ）と、根の上
                _shrineTops.Add(o + rot * new Vector3(-3f, 0f, 9.9f) + Vector3.up * 15.5f);
                _shrineTops.Add(o + rot * new Vector3(6f, 0f, 12f) + Vector3.up * 8f);
                // 高い所の太い枝の上（フクロウがとまる）
                foreach (var dir in new[] { new Vector3(0.92f, 0f, 0.38f), new Vector3(-0.7f, 0f, 0.7f) })
                {
                    Vector3 d = (rot * dir).normalized;
                    Vector3 probe = o + d * 21f + Vector3.up * 75f;
                    if (Physics.Raycast(probe, Vector3.down, out var hit, 60f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) && hit.point.y - o.y > 30f)
                        _owlPerches.Add(hit.point);
                }
            }

            // 千本鳥居（参道から奥の宮へ、朱の小さな鳥居がならぶ）と、奥の宮の祠
            {
                var path = ShrineLayout.ToriiPath;
                float total = 0f;
                for (int i = 0; i < path.Length - 1; i++) total += Vector2.Distance(path[i], path[i + 1]);
                for (float s = 7.5f; s < total - 2.5f; s += 3.3f)
                {
                    Vector2 p = PointOnPath(path, s, out Vector2 dir);
                    Vector3 g = SG(p);
                    Quaternion rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
                    Place("Shrine_ToriiSmall", prop, g + Vector3.down * 0.25f, rot, 1f, true, true, 300f);
                    _smallTorii.Add(g);
                    Occupy(p + new Vector2(dir.y, -dir.x) * 4.6f, 1.4f);
                    Occupy(p - new Vector2(dir.y, -dir.x) * 4.6f, 1.4f);
                }
                if (_smallTorii.Count > 4)
                {
                    _shrineTops.Add(_smallTorii[2] + Vector3.up * 26f);
                    _shrineTops.Add(_smallTorii[_smallTorii.Count - 3] + Vector3.up * 26f);
                }
                Vector2 hc = ShrineLayout.InnerShrine;
                Vector2 toExit = path[path.Length - 1] - hc;
                Place("Shrine_Hokora", prop, SG(hc) + Vector3.down * 0.4f, Quaternion.LookRotation(new Vector3(toExit.x, 0f, toExit.y), Vector3.up), 1f, true, true, 300f, asRenderer: true);
                Occupy(hc, 6f);
                _shrineTops.Add(SG(hc) + Vector3.up * 16f);
                _owlPerches.Add(SG(hc) + Vector3.up * 16f);
            }

            // 池にかかる太鼓橋（東西にわたす）
            {
                Vector3 o = new Vector3(ShrineLayout.Pond.x, 0f, ShrineLayout.Pond.y);
                Place("Shrine_Bridge", prop, o, Quaternion.identity, 1f, true, true, 300f, asRenderer: true);
                for (float x = -12f; x <= 12f; x += 4f) Occupy(new Vector2(o.x + x, o.z), 3.5f);
                _shrineTops.Add(o + Vector3.up * 12f);
            }

            // 絵馬かけ（東）・おみくじかけ（西）：お社の横で、まん中を向く
            {
                Vector3 e = new Vector3(ShrineLayout.Ema.x, plat, ShrineLayout.Ema.y);
                Place("Shrine_Ema", prop, e, Quaternion.Euler(0f, -90f, 0f), 1f, true, true, 240f);
                Occupy(ShrineLayout.Ema, 4f);
                _shrineTops.Add(e + Vector3.up * 18f);
                Vector3 m = new Vector3(ShrineLayout.Omikuji.x, plat, ShrineLayout.Omikuji.y);
                Place("Shrine_Omikuji", prop, m, Quaternion.Euler(0f, 90f, 0f), 1f, true, true, 240f);
                Occupy(ShrineLayout.Omikuji, 3.5f);
            }
        }

        /// <summary>着く所（はじまり・トンネルの出口・名所へ移動したときの所）の近くか。</summary>
        bool NearArrival(Vector2 p, float r)
        {
            if (DistToArrival(p) < r) return true;
            foreach (var lm in Area.Landmarks)
                if (lm.TravelView != null && Vector2.Distance(p, lm.TravelView.from) < r) return true;
            return false;
        }

        /// <summary>折れ線 path の、はじめから s の所の点と、その向き。</summary>
        static Vector2 PointOnPath(Vector2[] path, float s, out Vector2 dir)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                float len = Vector2.Distance(path[i], path[i + 1]);
                dir = (path[i + 1] - path[i]).normalized;
                if (s <= len || i == path.Length - 2) return path[i] + dir * Mathf.Min(s, len);
                s -= len;
            }
            dir = Vector2.up;
            return path[path.Length - 1];
        }

        /// <summary>鎮守の森の、大きな幹（遊べる場所の外）。</summary>
        void BuildShrineBackdrop()
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2f + R(-0.08f, 0.08f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(84f, 100f);
                if (Vector2.Distance(p, ShrineLayout.Gate * 1.5f) < 14f) continue;
                Place(R01() < 0.5f ? "BgTrunk_A" : "BgTrunk_B", assets.bark, new Vector3(p.x, ShrineLayout.Height(p.x * 0.96f, p.y * 0.96f) - 4f, p.y),
                    Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.55f, 0.8f), false, true, 900f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------
        void ScatterShrineProps()
        {
            Material prop = assets.prop;
            Occupy(ShrineLayout.Gate, 4.5f);
            Occupy(ShrineLayout.Spawn, 3f);
            // 苔むした岩（池のふちと、森のすそ）
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f + R(-0.2f, 0.2f);
                Vector2 p = ShrineLayout.Pond + new Vector2(Mathf.Cos(a) * (ShrineLayout.PondRadii.x + 1.6f), Mathf.Sin(a) * (ShrineLayout.PondRadii.y + 1.6f));
                if (Mathf.Abs(p.y - ShrineLayout.Pond.y) < 4f || !IsFree(p, 1.2f)) continue;   // 橋のたもとは、あける
                float s = R(0.5f, 0.9f);
                Place(Pick(Rocks), prop, SG(p) + Vector3.down * 0.3f * s, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), s, true, true, 200f);
                Occupy(p, 1.6f * s);
            }
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(63f, 73f);
                if (Vector2.Distance(p, ShrineLayout.Gate) < 9f) continue;
                float s = R(2.4f, 4.6f);
                Place(Pick(Rocks), prop, SG(p) + Vector3.down * 0.35f * s, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }
            // スイレンの葉（池の上。乗れる）
            Vector2 pc = ShrineLayout.Pond;
            Vector2[] pads = { new Vector2(-4.5f, 6f), new Vector2(3.5f, 8.5f), new Vector2(-2f, -7f), new Vector2(4.5f, -6f), new Vector2(-5f, -2.5f), new Vector2(5.5f, 3f) };
            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 p = pc + pads[i];
                Vector3 pos = new Vector3(p.x, ShrineLayout.PondLevel + 0.14f + i * 0.012f, p.y);
                Place("LilyPad", prop, pos, Quaternion.Euler(0f, R(0, 360), 0f), R(0.7f, 0.95f), true, false, 150f);
            }
            Place("WaterLily", assets.flowers, new Vector3(pc.x - 3.5f, ShrineLayout.PondLevel + 0.1f, pc.y + 9f), Quaternion.Euler(0f, 30f, 0f), 0.9f, false);
            Place("WaterLily", assets.flowers, new Vector3(pc.x + 4f, ShrineLayout.PondLevel + 0.1f, pc.y - 8.5f), Quaternion.Euler(0f, 110f, 0f), 0.8f, false);
        }

        // ------------------------------------------------------------------
        // 水：池と、手水舎の水盤
        // ------------------------------------------------------------------
        void BuildShrineWater()
        {
            Material pond = assets.pond != null ? assets.pond : assets.water;
            // 池（だ円）
            {
                int seg = 72;
                Vector2 c = ShrineLayout.Pond, rr = ShrineLayout.PondRadii * 1.12f;
                float wl = ShrineLayout.PondLevel;
                var v = new List<Vector3> { new Vector3(c.x, wl, c.y) };
                var t = new List<int>();
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(c.x + Mathf.Cos(a) * rr.x, wl, c.y + Mathf.Sin(a) * rr.y));
                }
                for (int i = 0; i < seg; i++) { t.Add(0); t.Add(1 + (i + 1) % seg); t.Add(1 + i); }
                var mr = AddWater(FlatWaterMesh("ShrinePond", v, t), "ShrinePond", pond);
                // こい緑の、しずかな池：岸ぎわの泡はふちだけ（白くならない）。ときどき波紋
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_FoamDepth", 0.03f);
                mpb.SetFloat("_Clarity", 2.2f);
                mpb.SetFloat("_Glint", 0.4f);
                mpb.SetFloat("_FresnelStrength", 0.45f);
                mpb.SetVector("_Drip", new Vector4(c.x - 2f, c.y + 4f, 2.2f, 0.5f));
                mr.SetPropertyBlock(mpb);
            }
            // 手水舎の水盤（四角）
            {
                float wl = ShrineLayout.BasinWater;
                Vector2 c = ShrineLayout.Chozuya;
                float hx = ShrineLayout.BasinHalfX + 0.05f, hz = ShrineLayout.BasinHalfZ + 0.05f;
                var v = new List<Vector3>
                {
                    new Vector3(c.x - hx, wl, c.y - hz), new Vector3(c.x + hx, wl, c.y - hz), new Vector3(c.x + hx, wl, c.y + hz), new Vector3(c.x - hx, wl, c.y + hz),
                };
                var t = new List<int> { 0, 2, 1, 0, 3, 2 };
                var mr = AddWater(FlatWaterMesh("ShrineBasin", v, t), "ShrineBasin", pond);
                // すんだ水：底の石が見える。筧のしずくで、波紋が広がる
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_FoamDepth", 0.02f);
                mpb.SetFloat("_Clarity", 5f);
                mpb.SetFloat("_Glint", 0.3f);
                mpb.SetFloat("_FresnelStrength", 0.35f);
                Vector3 drip = SpoutTip;
                mpb.SetVector("_Drip", new Vector4(drip.x, drip.z, 0.9f, 0.9f));
                mr.SetPropertyBlock(mpb);
            }
        }

        /// <summary>手水舎の竹の筧の先（Blender の SPOUT_TIP を、置いた向きに）。</summary>
        public static Vector3 SpoutTip
        {
            get
            {
                Vector3 o = new Vector3(ShrineLayout.Chozuya.x, ShrineLayout.ChozuyaFloor, ShrineLayout.Chozuya.y);
                return At(o, Quaternion.Euler(0f, ShrineLayout.ChozuyaYaw, 0f), 1.8f, 7.9f, -0.6f);
            }
        }

        Mesh FlatWaterMesh(string name, List<Vector3> v, List<int> t)
        {
            var uv = new List<Vector2>(v.Count);
            var col = new List<Color>(v.Count);
            foreach (var p in v)
            {
                uv.Add(new Vector2(p.x / 12f, p.z / 7f));
                col.Add(new Color(0f, 0f, 0f, 1f));
            }
            var mesh = Own(new Mesh { name = name });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        // 草花・落ち葉
        // ------------------------------------------------------------------
        void BuildShrineFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            bool Clear(Vector2 p, float trail = 0.3f)
            {
                if (!IsLand(p, 0.25f) || InsideOccupied(p) || ShrineLayout.TrailMask(p.x, p.y) > trail) return false;
                if (ShrineLayout.GravelMask(p.x, p.y) > 0.3f) return false;   // 玉砂利の所には生えない
                if (Vector2.Distance(p, ShrineLayout.Spawn) < 3f) return false;
                return true;
            }
            // 根もとのまわりの地面の高さが大きくちがう（基壇の石がきのふち）：草の根もとが、石がきの面から宙にとび出してしまう。
            // 乱数は、置かないときも同じだけ使う（しずくの場所を変えない）
            bool OverEdge(Vector2 p, float r)
            {
                float h0 = ShrineLayout.Height(p.x, p.y), lo = h0, hi = h0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    float h = ShrineLayout.Height(p.x + Mathf.Cos(a) * r, p.y + Mathf.Sin(a) * r);
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
                return hi - lo > Mathf.Max(0.6f, r * 0.6f);
            }
            // 苔のじゅうたん（ご神木のまわりと、森のすそ）
            int moss = Mathf.RoundToInt(900 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = i % 3 == 0 ? RandomInCircle(ShrineLayout.Tree, 22f) : RandomInRing(10f, 66f);
                if (!Clear(p, 0.5f)) continue;
                var rot = GroundRotation(p, R(0, 360), 0.9f, 3f);
                float sc = R(0.7f, 1.3f);
                if (OverEdge(p, 1.2f * sc)) continue;
                Place("Moss", fol, SG(p) + Vector3.down * 0.02f, rot, sc, false, false, 60f);
            }
            // シダと草（森のすそほど、こい）
            int ferns = Mathf.RoundToInt(520 * dens);
            for (int i = 0; i < ferns; i++)
            {
                Vector2 p = RandomInRing(12f, 72f);
                float edge = ShakuMath.SmoothStep(30f, 58f, p.magnitude);
                if (R01() > 0.25f + 0.75f * edge || !Clear(p)) continue;
                string mesh = R01() < 0.5f ? "Fern_A" : "Fern_B";
                var rot = GroundRotation(p, R(0, 360), 0.6f, 5f);
                float sc = R(0.5f, 0.95f);
                if (OverEdge(p, 1.6f * sc)) continue;
                Place(mesh, fol, SG(p), rot, sc, false, false, 80f);
            }
            int grass = Mathf.RoundToInt(1600 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = RandomInRing(6f, 74f);
                if (!Clear(p)) continue;
                string mesh = Pick(Grass);
                var rot = GroundRotation(p, R(0, 360), 0.5f, 5f);
                float sc = R(0.45f, 0.9f);
                if (OverEdge(p, 1.0f * sc)) continue;
                Place(mesh, fol, SG(p), rot, sc, false, false, 55f);
            }
            // ササのしげみ（森のすそと、池の東）
            int sasa = Mathf.RoundToInt(220 * dens);
            for (int i = 0; i < sasa; i++)
            {
                Vector2 p = i % 4 == 0 ? RandomInCircle(ShrineLayout.Pond + new Vector2(14f, 4f), 9f) : RandomInRing(46f, 70f);
                if (!Clear(p, 0.2f) || ShrineLayout.InPond(p.x, p.y, 1.5f)) continue;
                var rot = GroundRotation(p, R(0, 360), 0.4f, 6f);
                float sc = R(0.8f, 1.3f);
                if (OverEdge(p, 1.8f * sc)) continue;
                Place("Shrine_Sasa", fol, SG(p), rot, sc, false, false, 90f);
            }
            // ヒガンバナ（参道のわきの土手と、池のまわり）
            for (int i = 0; i < 70; i++)
            {
                Vector2 p = i < 30 ? new Vector2((R01() < 0.5f ? -1f : 1f) * R(10f, 16f), R(-50f, -2f))
                          : i < 50 ? ShrineLayout.Pond + new Vector2(Mathf.Cos(i) * (ShrineLayout.PondRadii.x + R(2.5f, 5f)), Mathf.Sin(i) * (ShrineLayout.PondRadii.y + R(2.5f, 5f)))
                          : RandomInRing(30f, 60f);
                if (!Clear(p) || ShrineLayout.InPond(p.x, p.y, 1.5f)) continue;
                var rot = GroundRotation(p, R(0, 360), 0.2f, 5f);
                float sc = R(0.8f, 1.15f);
                if (OverEdge(p, 1.0f * sc)) continue;
                Place("Shrine_Higanbana", flw, SG(p) + Vector3.down * 0.05f, rot, sc, false, true, 120f);
            }
            // 落ち葉（イチョウとモミジ。押すと動く）
            int leaves = Mathf.RoundToInt(170 * dens);
            for (int i = 0; i < leaves; i++)
            {
                Vector2 p = RandomInRing(5f, 62f);
                bool ginkgo = R01() < 0.55f;
                if (!IsLand(p, 0.2f) || InsideOccupied(p) || ShrineLayout.InPond(p.x, p.y, 1f) || !FreeAt(SG(p), 0.6f, 1.2f)) continue;   // ご神木の根などに、めりこまない
                if (NearArrival(p, 4f)) continue;   // 着いたしゃくとりむしの上に、落ち葉がかぶさらない
                PlaceLoose(ginkgo ? "Shrine_Ginkgo" : (R01() < 0.6f ? "Leaf_Maple_Red" : "Leaf_Maple_Yellow"), assets.prop, SG(p) + Vector3.up * 0.03f,
                    Quaternion.Euler(R(-6f, 6f), R(0, 360), R(-6f, 6f)), ginkgo ? R(0.9f, 1.3f) : R(0.32f, 0.45f), LooseProps.Shape.Leaf, false, 45f);
            }
            // 小石とどんぐり（ご神木の下）
            int pebbles = Mathf.RoundToInt(90 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(6f, 60f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p) || !FreeAt(SG(p), 0.4f, 1f)) continue;
                PlaceLoose(Pick(RiverStones), assets.prop, SG(p), Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), R(0.08f, 0.2f), LooseProps.Shape.Pebble, false, 45f);
            }
            for (int i = 0; i < 18; i++)
            {
                Vector2 p = RandomInCircle(ShrineLayout.Tree, 20f);
                if (Vector2.Distance(p, ShrineLayout.Tree) < 10f || !IsLand(p, 0.1f) || InsideOccupied(p) || !FreeAt(SG(p), 0.6f, 1.2f)) continue;
                PlaceLoose("Acorn", assets.prop, SG(p) + Vector3.up * 0.2f, Quaternion.Euler(R(0, 360), R(0, 360), 0f), R(0.8f, 1.1f), LooseProps.Shape.Pebble, false, 60f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceShrineDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = ShrineLayout.Spawn;
            AddDewFromAbove(s + new Vector2(3.5f, 3f));
            // 物の上（鳥居・灯籠・お社・さい銭箱・キツネの像・手水舎・ご神木・小さな鳥居・奥の宮・太鼓橋・絵馬かけ）
            foreach (var top in _shrineTops) AddDewFromAbove(new Vector2(top.x, top.z), top.y);
            // 手水舎の水盤の、ひしゃくの柄の上
            AddDewFromAbove(new Vector2(ShrineLayout.Chozuya.x + 0.2f, ShrineLayout.Chozuya.y - 1.2f), 12f);
            // 石段のまん中・池のふち・千本鳥居の道
            Vector2 mid = ShrineLayout.Step(4);
            AddDewFromAbove(new Vector2(2.5f, mid.y));
            AddDewFromAbove(ShrineLayout.Pond + new Vector2(-ShrineLayout.PondRadii.x - 1.8f, 6f));
            Vector2 tp = ShrineLayout.ToriiPath[2] + new Vector2(0.6f, -0.4f);
            AddDewFromAbove(tp, ShrineLayout.Height(tp.x, tp.y) + 4f);   // 千本鳥居の下の道（鳥居の上ではなく）
            // のこりは、陸の上のどこかへ（池と水盤はよける）
            int guard = 0;
            while (DewdropPoints.Count < Area.DropCount && guard++ < 4000)
            {
                Vector2 p = RandomInRing(8f, 60f);
                if (!IsLand(p, 0.3f) || ShrineLayout.InPond(p.x, p.y, 1.5f)) continue;
                AddDewFromAbove(p);
            }
        }

        void PlaceShrineCreatures()
        {
            float plat = ShrineLayout.PlatformY;
            // キツネ：手水舎とご神木のあいだの、ひらけた所（参道からはなれて）
            AddMob("kitsune", SG(new Vector2(-24f, 3f)), 1, 8f);
            // タヌキ：池の東のササやぶのそばと、南西の森のすそ
            AddMob("tanuki", SG(new Vector2(46f, -8f)), 1, 6f);
            AddMob("tanuki", SG(new Vector2(-34f, -34f)), 1, 6f);
            // フクロウ：ご神木の太い枝にとまる。近づくと、鳥居・お社の屋根・奥の宮へ、音もなく飛んでいく
            if (_owlPerches.Count > 0)
            {
                Vector3 first = _owlPerches[_owlPerches.Count > 3 ? 3 : 0];
                var g = AddMob("fukurou", first, 1, 1.5f);
                foreach (var p in _owlPerches) g.path.Add(p);
                g.showcase = true;
            }
            // オオミズアオ（ガ）：石灯籠のあかりのまわり（参道の 2 対）
            for (int pair = 0; pair < 2; pair++)
            {
                Vector3 l = _lanternLights[pair * 2 + (pair == 0 ? 1 : 0)];
                var g = AddMob("ga", l, 1, 3f, 1.5f);
                foreach (var ll in _lanternLights) g.path.Add(ll);
                g.showcase = true;
            }
            // カナブン：ご神木の幹（樹液の出る所）
            if (_treeBase != Vector3.zero)
            {
                Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                foreach (var (a, y) in new[] { (20f, 5.5f), (-25f, 7.5f), (60f, 4.2f) })
                {
                    Vector3 d = rot * (Quaternion.Euler(0f, a, 0f) * Vector3.forward);
                    Vector3 from = _treeBase + d * 12.5f + Vector3.up * y;
                    if (!Physics.Raycast(from, -d, out var hit, 12.5f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || !hit.collider.name.StartsWith("Shrine_Tree")) continue;
                    var g = AddMob("kanabun", hit.point, 1, 1.4f);
                    g.up = hit.normal;
                    g.showcase = true;
                }
            }
            // カラス：大鳥居の上・ご神木・お社の屋根に下りる
            {
                var g = AddMob("crow", SG(new Vector2(-16f, -30f)), 1, 3f);
                g.path.Add(SG(new Vector2(-16f, -30f)));
                g.path.Add(SG(new Vector2(12f, -10f)));
                g.path.Add(new Vector3(-12f, plat, 38f));
                g.path.Add(SG(new Vector2(36f, 2f)));
            }
            // アキアカネ（池の上）・ハエトリグモ（灯籠の足もと）・てんとうむし（苔の上）
            AddMob("dragonfly", SG(ShrineLayout.Pond) + Vector3.up * 1.5f, 2, 9f, 2.5f);
            AddMob("spider", SG(new Vector2(10.5f, -22f)), 1, 2f);
            AddMob("ladybug", SG(new Vector2(-30f, 12f)), 2, 3f);
            // スズムシ（手水舎のわきの草むら）・エンマコオロギ（池の南の草むら）。夕ぐれに鳴く
            AddMob("suzumushi", SG(new Vector2(-30f, -10f)), 2, 2.5f);
            AddMob("korogi", SG(new Vector2(16f, -38f)), 2, 2.5f);
        }

        // ------------------------------------------------------------------
        // 地図
        // ------------------------------------------------------------------
        void DrawShrineMap(Color32[] px, int size)
        {
            MapTrails(px, size, ShrineLayout.Trails, new Color32(214, 200, 168, 255));
            var red = new Color32(222, 72, 46, 255);
            // 大鳥居
            for (float x = -11f; x <= 11f; x += 1f) MapDot(px, size, new Vector3(ShrineLayout.GreatTorii.x + x, 0f, ShrineLayout.GreatTorii.y), 0.9f, red);
            // 千本鳥居
            var path = ShrineLayout.ToriiPath;
            for (int i = 0; i < path.Length - 1; i++)
                for (float k = 0f; k <= 1f; k += 0.05f)
                {
                    Vector2 p = Vector2.Lerp(path[i], path[i + 1], k);
                    if (Vector2.Distance(p, path[0]) > 7f) MapDot(px, size, new Vector3(p.x, 0f, p.y), 1.6f, red);
                }
            // お社（緑の屋根）と奥の宮
            for (float x = -13f; x <= 13f; x += 1.5f)
                for (float z = -11f; z <= 16f; z += 1.5f)
                    MapDot(px, size, new Vector3(ShrineLayout.Hall.x + x, 0f, ShrineLayout.Hall.y - z), 1.2f, new Color32(86, 150, 132, 255));
            MapDot(px, size, new Vector3(ShrineLayout.InnerShrine.x, 0f, ShrineLayout.InnerShrine.y), 3f, new Color32(86, 150, 132, 255));
            // 石灯籠・キツネの像・手水舎・ご神木・太鼓橋
            foreach (var l in ShrineLayout.Lanterns) MapDot(px, size, new Vector3(l.x, 0f, l.y), 1.6f, new Color32(150, 148, 140, 255));
            foreach (var f in ShrineLayout.FoxStatues) MapDot(px, size, new Vector3(f.x, 0f, f.y), 2f, new Color32(236, 232, 220, 255));
            MapDot(px, size, new Vector3(ShrineLayout.Chozuya.x, 0f, ShrineLayout.Chozuya.y), 4f, new Color32(120, 92, 70, 255));
            MapDot(px, size, new Vector3(ShrineLayout.Tree.x, 0f, ShrineLayout.Tree.y), 8.5f, new Color32(84, 66, 48, 255));
            for (float x = -12f; x <= 12f; x += 0.8f) MapDot(px, size, new Vector3(ShrineLayout.Pond.x + x, 0f, ShrineLayout.Pond.y), 1.6f, red);
        }
    }
}
