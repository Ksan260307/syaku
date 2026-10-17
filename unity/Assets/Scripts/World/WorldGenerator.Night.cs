using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 夜の森エリアの組み立て：枯れ沢の谷と丸木橋・月見の池とススキ・イノシシのぬた場（どろ水）・竹やぶとタケノコ・
    /// ツキヨタケの光る倒木・ミノムシの木・シマエナガのねぐらの木・星見の丘と星見の岩・雑木林の木・苔とシダと落ち葉。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _nightTops = new List<Vector3>();       // しずくを置く、物の上（上からさがす）
        readonly List<Vector3> _roostPerches = new List<Vector3>();    // シマエナガが下りる、太い枝の上
        readonly List<(Vector3 anchor, float len)> _bagworms = new List<(Vector3, float)>();   // ミノムシの糸の付く、枝の下と、糸の長さ
        readonly List<Vector3> _glowCaps = new List<Vector3>();        // 光るキノコ（倒木の上）
        readonly List<Vector3> _bambooShoots = new List<Vector3>();

        /// <summary>シマエナガが下りる、ねぐらの木の枝の上（テスト用）。</summary>
        public IReadOnlyList<Vector3> RoostPerches => _roostPerches;
        /// <summary>ミノムシのぶらさがる所（テスト用）。</summary>
        public IReadOnlyList<(Vector3 anchor, float len)> Bagworms => _bagworms;
        /// <summary>倒木の、光るキノコ（光の演出）。</summary>
        public IReadOnlyList<Vector3> GlowCaps => _glowCaps;

        void ClearNight()
        {
            _nightTops.Clear();
            _roostPerches.Clear();
            _bagworms.Clear();
            _glowCaps.Clear();
            _bambooShoots.Clear();
        }

        static Vector3 NG(Vector2 p) => NightLayout.Ground(p.x, p.y);

        // Blender の木の太い枝：（向き（ラジアン。Blender の xy）、付け根の高さ、長さ、そりあがり）（build_night_kit.py と同じ）
        static readonly Vector4[] RoostBranches = { new Vector4(0.3f, 13f, 14f, 3f), new Vector4(1.6f, 15.5f, 12.5f, 3.5f), new Vector4(2.9f, 12.5f, 13.5f, 2.6f), new Vector4(4.2f, 17.5f, 11.5f, 4f), new Vector4(5.3f, 20.5f, 10f, 4f) };
        static readonly Vector4[] MinoBranches = { new Vector4(0.5f, 8.5f, 10f, 1.2f), new Vector4(2.1f, 9.5f, 9f, 1.6f), new Vector4(3.6f, 8f, 10.5f, 1f), new Vector4(5f, 10.5f, 8.5f, 1.8f) };

        /// <summary>Blender の xy の向き a を、Unity の向きに（Blender (x, y) は Unity (-x, -z)）。</summary>
        static Vector3 BlenderDir(float a) => new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a));

        // ------------------------------------------------------------------
        // 大きな物
        // ------------------------------------------------------------------
        void BuildNightSolids()
        {
            BuildNightBackdrop();
            Material prop = assets.prop;
            Material bark = assets.bark;

            // 丸木橋（谷にわたす。両はしは、土手の上にのる）
            {
                Vector2 bc = NightLayout.BridgeCenter;
                float half = NightLayout.BridgeHalf - 1f;
                float hS = NightLayout.Height(bc.x, bc.y - half), hN = NightLayout.Height(bc.x, bc.y + half);
                float pitch = -Mathf.Atan2(hN - hS, 2f * half) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(pitch, 4f, 0f);
                Vector3 o = new Vector3(bc.x, (hN + hS) * 0.5f + 1.25f - 0.35f, bc.y);
                Place("Night_LogBridge", bark, o, rot, 1f, true, true, 400f, asRenderer: true);
                Occupy(new Vector2(bc.x, bc.y - half - 0.5f), 2.5f);
                Occupy(new Vector2(bc.x, bc.y + half + 0.5f), 2.5f);
                Occupy(bc, 3f);
                _nightTops.Add(o + Vector3.up * 3f);
                _nightTops.Add(o + rot * new Vector3(0f, 3f, 6f));
            }

            // 星見の岩（丘のてっぺん）
            {
                Vector3 o = NG(NightLayout.StarRock);
                Place("Night_StarRock", prop, o + Vector3.down * 0.7f, Quaternion.Euler(0f, 20f, 0f), 1f, true, true, 500f, asRenderer: true);
                Occupy(NightLayout.StarRock, 8f);
                _nightTops.Add(o + Vector3.up * 9f);
            }

            // シマエナガのねぐらの木：太い枝の上（シマエナガが下りる）
            {
                Vector3 o = NG(NightLayout.RoostTree);
                Quaternion rot = Quaternion.Euler(0f, 35f, 0f);
                Place("Night_RoostTree", bark, o + Vector3.down * 0.3f, rot, 1f, true, true, 600f, asRenderer: true);
                Occupy(NightLayout.RoostTree, 5f);
                Physics.SyncTransforms();
                foreach (var b in RoostBranches)
                {
                    Vector3 d = rot * BlenderDir(b.x);
                    foreach (float t in new[] { 0.5f, 0.62f })
                    {
                        Vector3 from = o + d * (b.z * t) + Vector3.up * (b.y + b.w + 2.5f);
                        if (Physics.Raycast(from, Vector3.down, out var hit, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                            && hit.collider.name.StartsWith("Night_RoostTree") && hit.normal.y > 0.6f && hit.point.y > o.y + b.y - 1f)
                        {
                            _roostPerches.Add(hit.point);
                            break;
                        }
                    }
                }
                if (_roostPerches.Count > 0) _nightTops.Add(_roostPerches[0] + Vector3.up * 2f);
            }

            // ミノムシの木：低い枝の下に、ミノムシがぶらさがる
            {
                Vector3 o = NG(NightLayout.MinoTree);
                Quaternion rot = Quaternion.Euler(0f, -20f, 0f);
                Place("Night_MinoTree", bark, o + Vector3.down * 0.3f, rot, 1f, true, true, 500f, asRenderer: true);
                Occupy(NightLayout.MinoTree, 3.5f);
                Physics.SyncTransforms();
                foreach (var b in MinoBranches)
                {
                    Vector3 d = rot * BlenderDir(b.x);
                    foreach (float t in new[] { 0.5f, 0.68f })
                    {
                        Vector3 from = o + d * (b.z * t) + Vector3.up * (b.y - 3f);
                        if (!Physics.Raycast(from, Vector3.up, out var hit, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || !hit.collider.name.StartsWith("Night_MinoTree")) continue;
                        float ground = NightLayout.Height(hit.point.x, hit.point.z);
                        float len = Mathf.Clamp(hit.point.y - ground - 2.0f - 1.1f, 0.6f, 6f);   // みのの下のはしは、しゃくとりむしが立ち上がると、とどく高さ
                        _bagworms.Add((hit.point + Vector3.down * 0.05f, len));
                        break;
                    }
                    // 枝の上（しずく）
                    Vector3 top = o + d * (b.z * 0.4f) + Vector3.up * (b.y + 6f);
                    if (_nightTops.Count < 40) _nightTops.Add(top);
                }
            }

            // ツキヨタケの光る倒木（ななめに横たわる。上と横に、光るキノコ）
            {
                Vector2 c = NightLayout.GlowLog;
                Quaternion rot = Quaternion.Euler(0f, NightLayout.GlowLogYaw, 0f);
                Vector3 o = NG(c) + Vector3.up * 1.2f;
                Place("Night_RotLog", bark, o, rot, 1f, true, true, 400f, asRenderer: true);
                Vector3 along = rot * Vector3.forward, side = rot * Vector3.right;
                for (float s = -8.5f; s <= 8.5f; s += 4.25f) Occupy(new Vector2(c.x + along.x * s, c.y + along.z * s), 3f);
                Physics.SyncTransforms();
                for (int k = 0; k < 12; k++)
                {
                    float s = Mathf.Lerp(-8f, 8f, k / 11f) + R(-0.6f, 0.6f);
                    float sd = (k % 3 - 1) * 1.6f;
                    Vector3 from = o + along * s + side * sd + Vector3.up * 6f;
                    if (!Physics.Raycast(from, Vector3.down, out var hit, 10f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || !hit.collider.name.StartsWith("Night_RotLog")) continue;
                    var up = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, R(0f, 360f), 0f);
                    float sc = R(0.35f, 0.6f);
                    Place("Mushroom_Glow", assets.glow, hit.point - hit.normal * 0.08f, up, sc, true, true, 150f);
                    _glowCaps.Add(hit.point + hit.normal * (sc * 1.5f));
                }
                _nightTops.Add(o + Vector3.up * 5f);
                _nightTops.Add(o + along * 7f + Vector3.up * 5f);
            }

            // 竹やぶ（竹と、タケノコ）
            {
                var culms = new List<Vector2>();
                for (int i = 0; i < 160 && culms.Count < 28; i++)
                {
                    Vector2 p = RandomInCircle(NightLayout.Bamboo, NightLayout.BambooR);
                    if (NightLayout.DistToTrail(p) < 2.6f || NearArrival(p, 4f)) continue;
                    bool ok = true;
                    foreach (var q in culms) if ((q - p).sqrMagnitude < 3.3f * 3.3f) { ok = false; break; }
                    if (!ok || !IsFree(p, 1.4f)) continue;
                    culms.Add(p);
                    Place("Night_Bamboo", prop, NG(p) + Vector3.down * 0.2f, Quaternion.Euler(R(-2f, 2f), R(0f, 360f), R(-2f, 2f)), R(0.8f, 1.12f), true, true, 300f);
                    Occupy(p, 1.6f);
                }
                for (int i = 0; i < 40 && _bambooShoots.Count < 6; i++)
                {
                    Vector2 p = RandomInCircle(NightLayout.Bamboo, NightLayout.BambooR - 2f);
                    if (!IsFree(p, 2f) || NightLayout.DistToTrail(p) < 2f) continue;
                    float s = R(0.8f, 1.25f);
                    Vector3 g = NG(p);
                    Place("Night_BambooShoot", prop, g + Vector3.down * 0.3f, Quaternion.Euler(R(-4f, 4f), R(0f, 360f), R(-4f, 4f)), s, true, true, 250f);
                    Occupy(p, 1.8f * s);
                    _bambooShoots.Add(g + Vector3.up * (5f * s));
                }
            }

            // 雑木林の木（遊べる場所のまわり）
            Vector2[] trees =
            {
                new Vector2(-48f, -14f), new Vector2(-50f, 4f), new Vector2(-28f, 44f), new Vector2(-6f, 54f), new Vector2(28f, 52f), new Vector2(52f, 14f),
                new Vector2(50f, -6f), new Vector2(44f, -38f), new Vector2(18f, -46f), new Vector2(-18f, -46f), new Vector2(-44f, -40f), new Vector2(-14f, -2f),
            };
            for (int i = 0; i < trees.Length; i++)
            {
                Vector2 p = trees[i];
                if (!IsFree(p, 4f)) continue;
                bool roost = i % 3 == 1;
                float s = R(0.85f, 1.25f);
                Place(roost ? "Night_RoostTree" : "Night_MinoTree", bark, NG(p) + Vector3.down * 0.3f, Quaternion.Euler(0f, R(0f, 360f), 0f), s, true, true, 600f, asRenderer: true);
                Occupy(p, (roost ? 3.5f : 2.6f) * s);
            }
        }

        /// <summary>雑木林の、大きな幹（遊べる場所の外）。</summary>
        void BuildNightBackdrop()
        {
            for (int i = 0; i < 22; i++)
            {
                float a = i / 22f * Mathf.PI * 2f + R(-0.08f, 0.08f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(82f, 100f);
                if (Vector2.Distance(p, NightLayout.Gate * 1.5f) < 14f) continue;
                Place(R01() < 0.5f ? "BgTrunk_A" : "BgTrunk_B", assets.bark, new Vector3(p.x, NightLayout.Height(p.x * 0.96f, p.y * 0.96f) - 4f, p.y),
                    Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.5f, 0.75f), false, true, 900f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------
        void ScatterNightProps()
        {
            Material prop = assets.prop;
            Occupy(NightLayout.Gate, 4.5f);
            Occupy(NightLayout.Spawn, 3f);
            // 枯れ沢の谷底の、丸い石
            for (int i = 0; i < 70; i++)
            {
                float x = R(-56f, 56f);
                float z = NightLayout.GullyZ(x) + R(-2.4f, 2.4f);
                Vector2 p = new Vector2(x, z);
                if (NightLayout.GullyK(x, z) < 0.85f || !IsFree(p, 0.8f)) continue;
                float s = R(0.25f, 0.8f);
                Place(Pick(RiverStones), prop, NG(p) + Vector3.down * 0.12f * s, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 150f);
                Occupy(p, 1.1f * s);
            }
            // 苔むした岩（谷の土手と、池のふち）
            for (int i = 0; i < 14; i++)
            {
                float x = R(-50f, 50f);
                float z = NightLayout.GullyZ(x) + (R01() < 0.5f ? -1f : 1f) * R(6f, 9f);
                Vector2 p = new Vector2(x, z);
                if (!IsFree(p, 2f) || NightLayout.DistToTrail(p) < 2.5f || Vector2.Distance(p, NightLayout.BridgeCenter) < 14f) continue;   // 丸木橋の景色をふさがない
                float s = R(0.5f, 1f);
                Place(Pick(Rocks), prop, NG(p) + Vector3.down * 0.3f * s, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), s, true, true, 200f);
                Occupy(p, 1.6f * s);
            }
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f + R(-0.2f, 0.2f);
                Vector2 p = NightLayout.Pond + new Vector2(Mathf.Cos(a) * (NightLayout.PondRadii.x + 1.6f), Mathf.Sin(a) * (NightLayout.PondRadii.y + 1.6f));
                if (!IsFree(p, 1.2f) || NightLayout.DistToTrail(p) < 2f) continue;
                float s = R(0.45f, 0.8f);
                Place(Pick(Rocks), prop, NG(p) + Vector3.down * 0.3f * s, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), s, true, true, 200f);
                Occupy(p, 1.6f * s);
                if (i == 1) _nightTops.Add(NG(p) + Vector3.up * 4f);
            }
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(63f, 73f);
                if (Vector2.Distance(p, NightLayout.Gate) < 9f) continue;
                float s = R(2.4f, 4.4f);
                Place(Pick(Rocks), prop, NG(p) + Vector3.down * 0.35f * s, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }
            // スイレンの葉（池の上。乗れる）
            Vector2 pc = NightLayout.Pond;
            Vector2[] pads = { new Vector2(-4f, 3.5f), new Vector2(3f, 4.5f), new Vector2(-1.5f, -4f), new Vector2(5f, -2f) };
            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 p = pc + pads[i];
                Place("LilyPad", prop, new Vector3(p.x, NightLayout.PondLevel + 0.14f + i * 0.012f, p.y), Quaternion.Euler(0f, R(0, 360), 0f), R(0.7f, 0.9f), true, false, 150f);
            }
            // 古い切り株（ぬた場のそば。イノシシが体をこすりつける）
            {
                Vector2 p = NightLayout.Wallow + new Vector2(9f, 6f);
                Place("Stump", assets.bark, NG(p) + Vector3.down * 0.4f, Quaternion.Euler(0f, 120f, 0f), 0.42f, true, true, 300f, asRenderer: true);
                Occupy(p, 4f);
                _nightTops.Add(NG(p) + Vector3.up * 14f);
            }
        }

        // ------------------------------------------------------------------
        // 水：月見の池と、ぬた場のどろ水
        // ------------------------------------------------------------------
        void BuildNightWater()
        {
            Material pond = assets.pond != null ? assets.pond : assets.water;
            {
                int seg = 72;
                Vector2 c = NightLayout.Pond, rr = NightLayout.PondRadii * 1.12f;
                float wl = NightLayout.PondLevel;
                var v = new List<Vector3> { new Vector3(c.x, wl, c.y) };
                var t = new List<int>();
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(c.x + Mathf.Cos(a) * rr.x, wl, c.y + Mathf.Sin(a) * rr.y));
                }
                for (int i = 0; i < seg; i++) { t.Add(0); t.Add(1 + (i + 1) % seg); t.Add(1 + i); }
                var mr = AddWater(FlatWaterMesh("NightPond", v, t), "NightPond", pond);
                // 夜の池：こい藍色。照り返しは夜空の色（白くならない）。岸ぎわの泡は、ほんのり
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_ShallowColor", new Color(0.1f, 0.2f, 0.3f, 0.45f));
                mpb.SetColor("_DeepColor", new Color(0.02f, 0.05f, 0.12f, 0.95f));
                mpb.SetColor("_ReflectColor", new Color(0.2f, 0.26f, 0.42f, 1f));
                mpb.SetColor("_HorizonReflect", new Color(0.08f, 0.11f, 0.17f, 1f));   // 水ぎわに近い所は、まわりの暗い林がうつる
                mpb.SetColor("_StreakColor", new Color(0.28f, 0.34f, 0.48f, 1f));
                mpb.SetColor("_FoamColor", new Color(0.42f, 0.48f, 0.6f, 1f));
                mpb.SetFloat("_FoamDepth", 0.03f);
                mpb.SetFloat("_Clarity", 1.6f);
                mpb.SetFloat("_Glint", 0.5f);
                mpb.SetFloat("_FresnelStrength", 0.4f);
                mpb.SetVector("_Drip", new Vector4(c.x + 3f, c.y - 2f, 2.2f, 0.4f));
                mr.SetPropertyBlock(mpb);
            }
            {
                int seg = 40;
                Vector2 c = NightLayout.Wallow;
                float rr = NightLayout.WallowR * 1.05f;
                float wl = NightLayout.WallowLevel;
                var v = new List<Vector3> { new Vector3(c.x, wl, c.y) };
                var t = new List<int>();
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float k = 1f + 0.08f * Mathf.Sin(a * 3f + 1f);
                    v.Add(new Vector3(c.x + Mathf.Cos(a) * rr * k, wl, c.y + Mathf.Sin(a) * rr * k));
                }
                for (int i = 0; i < seg; i++) { t.Add(0); t.Add(1 + (i + 1) % seg); t.Add(1 + i); }
                var mr = AddWater(FlatWaterMesh("NightWallow", v, t), "NightWallow", pond);
                // どろ水：こい茶色で、にごっている（底は見えない）。照り返しは弱く、きらめきも泡もない（白っぽくならない）
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_ShallowColor", new Color(0.2f, 0.15f, 0.1f, 1f));
                mpb.SetColor("_DeepColor", new Color(0.13f, 0.09f, 0.06f, 1f));
                mpb.SetColor("_ReflectColor", new Color(0.16f, 0.17f, 0.22f, 1f));
                mpb.SetColor("_HorizonReflect", new Color(0.09f, 0.08f, 0.07f, 1f));
                mpb.SetColor("_StreakColor", new Color(0.18f, 0.14f, 0.1f, 1f));
                mpb.SetColor("_FoamColor", new Color(0.2f, 0.16f, 0.11f, 1f));
                mpb.SetVector("_Absorb", new Vector4(9f, 9f, 9f, 0f));   // すぐに底が見えなくなる
                mpb.SetFloat("_FoamDepth", 0.001f);
                mpb.SetFloat("_Clarity", 0.05f);
                mpb.SetFloat("_Glint", 0f);
                mpb.SetFloat("_SpecStrength", 0.35f);
                mpb.SetFloat("_FresnelStrength", 0.12f);
                mpb.SetFloat("_RippleStrength", 0.12f);
                mr.SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------------
        // 草花・落ち葉
        // ------------------------------------------------------------------
        void BuildNightFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            bool Clear(Vector2 p, float trail = 0.3f)
            {
                if (!IsLand(p, 0.25f) || InsideOccupied(p) || NightLayout.TrailMask(p.x, p.y) > trail) return false;
                if (NightLayout.InWallow(p.x, p.y, 1f)) return false;
                if (Vector2.Distance(p, NightLayout.Spawn) < 3f) return false;
                return true;
            }
            // 苔のじゅうたん（谷の土手・倒木のまわり・森のすそ）
            int moss = Mathf.RoundToInt(800 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = i % 3 == 0 ? RandomInCircle(NightLayout.GlowLog, 14f) : RandomInRing(8f, 66f);
                if (!Clear(p, 0.5f)) continue;
                Place("Moss", fol, NG(p) + Vector3.down * 0.02f, GroundRotation(p, R(0, 360), 0.9f, 3f), R(0.7f, 1.3f), false, false, 60f);
            }
            // シダ（谷の中と、木の下）
            int ferns = Mathf.RoundToInt(520 * dens);
            for (int i = 0; i < ferns; i++)
            {
                Vector2 p;
                if (i % 2 == 0)
                {
                    float x = R(-56f, 56f);
                    p = new Vector2(x, NightLayout.GullyZ(x) + R(-7f, 7f));
                }
                else p = RandomInRing(12f, 70f);
                if (!Clear(p) || NightLayout.GullyK(p.x, p.y) > 0.6f) continue;   // 谷底の河原は、ひらけている（土手だけ）
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", fol, NG(p), GroundRotation(p, R(0, 360), 0.6f, 5f), R(0.5f, 0.95f), false, false, 80f);
            }
            int grass = Mathf.RoundToInt(1300 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = RandomInRing(6f, 72f);
                if (!Clear(p) || Vector2.Distance(p, NightLayout.Bamboo) < NightLayout.BambooR - 2f) continue;   // 竹やぶの中は、かれた竹の葉
                Place(Pick(Grass), fol, NG(p), GroundRotation(p, R(0, 360), 0.5f, 5f), R(0.45f, 0.9f), false, false, 55f);
            }
            // ススキの原（月見の池のまわりと、南の野原）
            int susuki = Mathf.RoundToInt(110 * dens);
            for (int i = 0; i < susuki; i++)
            {
                Vector2 p;
                if (i % 3 != 2)
                {
                    float a = R(0f, Mathf.PI * 2f);
                    float k = R(1.25f, 2.1f);
                    p = NightLayout.Pond + new Vector2(Mathf.Cos(a) * NightLayout.PondRadii.x * k, Mathf.Sin(a) * NightLayout.PondRadii.y * k);
                }
                else p = new Vector2(R(-30f, 40f), R(-50f, -12f));
                if (!Clear(p, 0.2f) || NightLayout.InPond(p.x, p.y, 1.5f)) continue;
                Place("Night_Susuki", fol, NG(p), GroundRotation(p, R(0, 360), 0.3f, 5f), R(0.75f, 1.15f), false, false, 110f);
            }
            // 落ち葉（ナラとモミジ。押すと動く）
            int leaves = Mathf.RoundToInt(160 * dens);
            for (int i = 0; i < leaves; i++)
            {
                Vector2 p = RandomInRing(5f, 62f);
                if (!IsLand(p, 0.2f) || InsideOccupied(p) || NightLayout.InPond(p.x, p.y, 1f) || NightLayout.InWallow(p.x, p.y, 0.5f) || !FreeAt(NG(p), 0.6f, 1.2f)) continue;
                if (NearArrival(p, 4f)) continue;
                string[] kinds = { "Leaf_Oak_Brown", "Leaf_Oak_Orange", "Leaf_Maple_Red", "Leaf_Maple_Yellow" };
                PlaceLoose(kinds[Mathf.Min(3, (int)(R01() * 4f))], assets.prop, NG(p) + Vector3.up * 0.03f,
                    Quaternion.Euler(R(-6f, 6f), R(0, 360), R(-6f, 6f)), R(0.32f, 0.45f), LooseProps.Shape.Leaf, false, 45f);
            }
            // 小石と、どんぐり
            int pebbles = Mathf.RoundToInt(80 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(6f, 60f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p) || NightLayout.InWallow(p.x, p.y, 0.5f) || !FreeAt(NG(p), 0.4f, 1f)) continue;
                PlaceLoose(Pick(RiverStones), assets.prop, NG(p), Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), R(0.08f, 0.2f), LooseProps.Shape.Pebble, false, 45f);
            }
            for (int i = 0; i < 20; i++)
            {
                Vector2 p = RandomInCircle(NightLayout.RoostTree, 12f);
                if (Vector2.Distance(p, NightLayout.RoostTree) < 4f || !IsLand(p, 0.1f) || InsideOccupied(p) || !FreeAt(NG(p), 0.6f, 1.2f)) continue;
                if (NearArrival(p, 8f)) continue;   // 着いたときの景色のカメラに、どんぐりが当たらない
                PlaceLoose("Acorn", assets.prop, NG(p) + Vector3.up * 0.2f, Quaternion.Euler(R(0, 360), R(0, 360), 0f), R(0.8f, 1.1f), LooseProps.Shape.Pebble, false, 60f);
            }
            // 茶色いキノコ（倒木のまわり）
            for (int i = 0; i < 10; i++)
            {
                Vector2 p = RandomInCircle(NightLayout.GlowLog, 9f);
                if (!Clear(p, 0.4f) || !IsFree(p, 0.8f)) continue;
                Place("Mushroom_Brown", assets.prop, NG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.5f, 8f), R(0.25f, 0.45f), false, true, 120f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceNightDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = NightLayout.Spawn;
            AddDewFromAbove(s + new Vector2(3.5f, 3f));
            // 物の上（丸木橋・星見の岩・ねぐらの木の枝・ミノムシの木の枝・倒木・池のふちの岩・切り株）
            foreach (var top in _nightTops) AddDewFromAbove(new Vector2(top.x, top.z), top.y);
            // タケノコのてっぺん（2 つ）
            for (int i = 0; i < _bambooShoots.Count && i < 2; i++) AddDewFromAbove(new Vector2(_bambooShoots[i].x, _bambooShoots[i].z), _bambooShoots[i].y + 2f);
            // 谷底の河原・ぬた場のふち
            Vector2 g = new Vector2(28f, NightLayout.GullyZ(28f));
            AddDewFromAbove(g, NightLayout.Height(g.x, g.y) + 3f);
            AddDewFromAbove(NightLayout.Wallow + new Vector2(NightLayout.WallowR + 1.6f, -1f));
            // のこりは、陸の上の、探索が楽しくなる所へ（池とぬた場はよける）
            FillDewsToExplore(8f, 60f, p => IsLand(p, 0.3f) && !NightLayout.InPond(p.x, p.y, 1.5f) && !NightLayout.InWallow(p.x, p.y, 0.8f));
        }

        void PlaceNightCreatures()
        {
            // ミノムシ：ミノムシの木の、低い枝の下に 3 つ
            for (int i = 0; i < _bagworms.Count && i < 3; i++)
            {
                var g = AddMob("minomushi", _bagworms[i].anchor, 1, 0f, _bagworms[i].len);
                g.showcase = true;
            }
            // ザトウムシ：光る倒木の上と、ミノムシの木の根もと
            {
                Vector3 top = TopSurface(NightLayout.GlowLog);
                var g = AddMob("zatoumushi", top, 1, 3f);
                g.showcase = true;
                AddMob("zatoumushi", NG(NightLayout.MinoTree + new Vector2(3.5f, -2.5f)), 1, 3f);
            }
            // キリギリス：池のまわりのススキの原と、丸木橋の南のたもと
            AddMob("kirigirisu", NG(NightLayout.Pond + new Vector2(-12.5f, 5f)), 2, 2.5f);
            AddMob("kirigirisu", NG(NightLayout.BridgeCenter + new Vector2(4f, -NightLayout.BridgeHalf - 3f)), 1, 2f);
            // イノシシのおかあさんと、ウリボウ 3 びき（ぬた場のまわり）
            {
                Vector2 c = NightLayout.Wallow + new Vector2(2f, NightLayout.WallowR + 4f);
                AddMob("inoshishi", NG(c), 1, 10f);
                AddMob("uribou", NG(c + new Vector2(-3f, -3f)), 3, 4f);
            }
            // シマエナガ：ねぐらの木の枝（ほかに、ミノムシの木と星見の岩）
            if (_roostPerches.Count > 0)
            {
                var g = AddMob("shimaenaga", _roostPerches[0], 1, 1.5f);
                foreach (var p in _roostPerches) g.path.Add(p);
                g.path.Add(TopSurface(NightLayout.StarRock));
                g.showcase = true;
            }
            // ゲンジボタル（月見の池の上）・アブラコウモリ（丘の上の夜空）・アマガエル（池のふち）・かたつむり（倒木）
            AddMob("firefly", NG(NightLayout.Pond + new Vector2(-6f, 7f)), 5, 6f, 1.3f);
            AddMob("firefly", NG(new Vector2(18f, NightLayout.GullyZ(18f))), 3, 5f, 1.2f);
            AddMob("koumori", NG(NightLayout.Hill + new Vector2(-8f, -10f)), 3, 6f, 3.5f);
            AddMob("frog", NG(NightLayout.Pond + new Vector2(-NightLayout.PondRadii.x - 1.2f, -2f)), 1, 2f);
            AddMob("frog", NG(NightLayout.Pond + new Vector2(2f, NightLayout.PondRadii.y + 1.4f)), 1, 2f);
            AddMob("snail", TopSurface(NightLayout.GlowLog + new Vector2(3f, 2f)), 1, 2f);
        }

        // ------------------------------------------------------------------
        // 地図
        // ------------------------------------------------------------------
        void DrawNightMap(Color32[] px, int size)
        {
            MapTrails(px, size, NightLayout.Trails, new Color32(176, 162, 138, 255));
            // 丸木橋
            Vector2 bc = NightLayout.BridgeCenter;
            for (float z = -NightLayout.BridgeHalf; z <= NightLayout.BridgeHalf; z += 0.8f) MapDot(px, size, new Vector3(bc.x, 0f, bc.y + z), 1.3f, new Color32(150, 112, 74, 255));
            // 竹やぶ
            for (int i = 0; i < 26; i++)
            {
                float a = i * 2.39996f, r = NightLayout.BambooR * Mathf.Sqrt((i + 0.5f) / 26f);
                MapDot(px, size, new Vector3(NightLayout.Bamboo.x + Mathf.Cos(a) * r, 0f, NightLayout.Bamboo.y + Mathf.Sin(a) * r), 1.2f, new Color32(126, 168, 84, 255));
            }
            // 光る倒木
            float yaw = NightLayout.GlowLogYaw * Mathf.Deg2Rad;
            Vector2 along = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            for (float s = -9f; s <= 9f; s += 0.8f) MapDot(px, size, new Vector3(NightLayout.GlowLog.x + along.x * s, 0f, NightLayout.GlowLog.y + along.y * s), 1.8f, new Color32(120, 236, 190, 255));
            // 木と岩
            MapDot(px, size, new Vector3(NightLayout.RoostTree.x, 0f, NightLayout.RoostTree.y), 4f, new Color32(214, 160, 70, 255));
            MapDot(px, size, new Vector3(NightLayout.MinoTree.x, 0f, NightLayout.MinoTree.y), 3.2f, new Color32(190, 92, 60, 255));
            MapDot(px, size, new Vector3(NightLayout.StarRock.x, 0f, NightLayout.StarRock.y), 5f, new Color32(176, 180, 196, 255));
        }
    }
}
