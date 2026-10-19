using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// チュートリアルの「練習の森」の組み立て。森と同じ物（切り株・赤キノコ・キノコ・岩・落ち葉・草花・背景の大きな幹）で、
    /// 小道ぞいに練習の場所を作る。しずくは 3 つ（道の上・切り株のてっぺん・赤キノコのかさの上）。
    /// いきものは、小さな赤キノコの根もとのかたつむりだけ（いきものに会う練習）。
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>練習の森の、切り株のてっぺん（チュートリアルの行き先）。</summary>
        public Vector3 TutorialStumpTop { get; private set; }
        /// <summary>練習の森の、赤キノコのかさの上（チュートリアルの行き先）。</summary>
        public Vector3 TutorialCapTop { get; private set; }

        static Vector3 TG(Vector2 p) => TutorialLayout.Ground(p.x, p.y);

        /// <summary>練習の森の、木もれ日の光の筋の所（光の中のちりの演出にも使う）。</summary>
        public static IReadOnlyList<Vector2> TutorialShafts => TutorialShaftSpots;

        static readonly Vector2[] TutorialShaftSpots =
        {
            TutorialLayout.Stump + new Vector2(-1.5f, 1f), TutorialLayout.Meadow + new Vector2(1f, -1f), TutorialLayout.Mushroom + new Vector2(1.5f, 1f),
            TutorialLayout.Spawn + new Vector2(2f, 4f), TutorialLayout.SnailCap + new Vector2(1.5f, 0f), TutorialLayout.Gate + new Vector2(0f, -5f),
        };

        /// <summary>練習の場所（道のしずく・おりる先・草原・はじまり）と、切り株から赤キノコへ糸をとばす間からの、いちばん近い距離。</summary>
        static float DistToTutorialSpots(Vector2 p)
        {
            float d = Vector2.Distance(p, TutorialLayout.FirstDew);
            d = Mathf.Min(d, Vector2.Distance(p, TutorialLayout.SilkLanding));
            d = Mathf.Min(d, Vector2.Distance(p, TutorialLayout.Meadow));
            d = Mathf.Min(d, Vector2.Distance(p, TutorialLayout.Spawn));
            d = Mathf.Min(d, ShakuMath.DistToSegment(p, TutorialLayout.Stump, TutorialLayout.Mushroom) + 1f);
            return d;
        }

        // ------------------------------------------------------------------
        // 大きな物（切り株・赤キノコ・背景の幹）
        // ------------------------------------------------------------------
        void BuildTutorialSolids()
        {
            // 遠くの大きな幹（森の奥行き）と、土手の上の林（トンネルの真うしろは、あける）
            BuildBackgroundTrunks(p => false);
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) / 12f * Mathf.PI * 2f + R(-0.1f, 0.1f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(52f, 62f);
                if (Vector2.Angle(p, TutorialLayout.Gate) < 14f) continue;
                Place("BgTrunk_B", assets.bark, Area.Ground(p.x, p.y) + Vector3.down * 1.5f, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.4f, 0.55f), false, true, 500f, asRenderer: true);
            }

            // 登る練習の切り株（森の古い切り株を小さくしたもの。てっぺんは地面から 4 くらい）
            float ss = TutorialLayout.StumpScale;
            Vector3 sp = TG(TutorialLayout.Stump) + Vector3.down * (0.6f * ss);
            Place("Stump", assets.bark, sp, Quaternion.Euler(0f, 20f, 0f), ss, true, true, 300f, asRenderer: true);
            Occupy(TutorialLayout.Stump, 4.6f);

            // 糸をねらう練習の、大きな赤キノコ（かさの上は、地面から 5 くらい。切り株のてっぺんより少し高い）
            float ms = TutorialLayout.MushroomScale;
            Vector3 mp = TG(TutorialLayout.Mushroom) + Vector3.down * 0.2f;
            Place("Mushroom_Red", assets.prop, mp, Quaternion.Euler(0f, 40f, 0f), ms, true);
            Occupy(TutorialLayout.Mushroom, 1.3f * ms);
            _redCaps.Add((mp, ms));

            Physics.SyncTransforms();
            Vector3 st = TG(TutorialLayout.Stump);
            TutorialStumpTop = CastDown(st + Vector3.up * 30f, 40f, out var top) ? top.point : st + Vector3.up * 3.8f;
            Vector3 mt = TG(TutorialLayout.Mushroom);
            TutorialCapTop = CastDown(mt + Vector3.up * 30f, 40f, out var cap) ? cap.point : mt + Vector3.up * 6f;
        }

        // ------------------------------------------------------------------
        // 小物（コライダーあり）
        // ------------------------------------------------------------------
        void ScatterTutorialProps()
        {
            Material prop = assets.prop;
            // はじまりの場所と、練習の場所はあけておく
            Occupy(TutorialLayout.Spawn, 3f);
            Occupy(TutorialLayout.FirstDew, 1.6f);
            Occupy(TutorialLayout.SilkLanding, 2.4f);
            Occupy(TutorialLayout.Meadow, 2.4f);
            Occupy(TutorialLayout.SnailCap, 4.6f);   // かたつむりが、根もとをぐるりとまわれるように

            // かたつむりのいる、小さな赤キノコと、そのまわり・大きな赤キノコの東の、小さな赤キノコ
            {
                Vector2 c = TutorialLayout.SnailCap;
                float s = TutorialLayout.SnailCapScale;
                Vector3 pos = TG(c) + Vector3.down * 0.15f;
                Place("Mushroom_Red", prop, pos, Quaternion.Euler(0f, 70f, 0f), s, true);
                Occupy(c, 1.1f * s);
                _redCaps.Add((pos, s));
            }
            Vector2[] reds = { TutorialLayout.SnailCap + new Vector2(-6f, 2.5f), TutorialLayout.SnailCap + new Vector2(-3f, -5.5f), TutorialLayout.Mushroom + new Vector2(4f, -1.5f) };
            float[] redScales = { 0.45f, 0.4f, 0.5f };
            for (int i = 0; i < reds.Length; i++)
            {
                Vector2 p = reds[i];
                float s = redScales[i];
                if (!IsFree(p, 1.2f * s) || TutorialLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                Vector3 pos = TG(p) + Vector3.down * 0.15f;
                Place("Mushroom_Red", prop, pos, GroundRotation(p, R(0, 360), 0.3f, 5f), s, true);
                Occupy(p, 1.1f * s);
                _redCaps.Add((pos, s));
            }
            // あちこちのキノコ
            for (int i = 0; i < 22; i++)
            {
                Vector2 p = RandomInRing(6f, 33f);
                if (!IsFree(p, 1.5f) || TutorialLayout.DistToTrail(p) < 3f || DistToTutorialSpots(p) < 3f) continue;
                if (RI(0, 2) == 0)
                {
                    float s = R(0.5f, 1.0f);
                    Place("Mushroom_Brown", prop, TG(p) + Vector3.down * 0.1f, GroundRotation(p, R(0, 360), 0.3f, 6f), s, true);
                    Occupy(p, 1.2f * s);
                }
                else
                {
                    float s = R(0.6f, 1.1f);
                    Place("Mushroom_Cluster", prop, TG(p), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                    Occupy(p, 0.7f * s);
                }
            }
            // 切り株のまわりのキノコ（根のあいだ）
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.3f) / 4f * Mathf.PI * 2f + R(-0.3f, 0.3f);
                Vector2 p = TutorialLayout.Stump + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(5f, 6f);
                if (TutorialLayout.TrailMask(p.x, p.y) > 0.2f || Vector2.Distance(p, TutorialLayout.SilkLanding) < 2.5f) continue;
                Place("Mushroom_Cluster", prop, TG(p), GroundRotation(p, R(0, 360), 0.4f, 8f), R(0.5f, 0.8f), true);
            }

            // 岩（小石）
            for (int i = 0; i < 30; i++)
            {
                Vector2 p = RandomInRing(5f, 33f);
                float s = R(0.35f, 1.3f);
                if (!IsFree(p, 1.4f * s) || TutorialLayout.DistToTrail(p) < 3.5f + s || DistToTutorialSpots(p) < 3f) continue;
                Vector3 pos = TG(p) + Vector3.down * 0.25f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)), s, true);
                Occupy(p, 1.3f * s);
                if (s > 1.1f) _bigRocks.Add((pos, s));
            }
            // 外の大岩（林のふち。トンネルの前はあける）
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(37f, 44f);
                if (Vector2.Angle(p, TutorialLayout.Gate) < 12f) continue;
                float s = R(2f, 3.8f);
                Vector3 pos = TG(p) + Vector3.down * 0.35f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }

            // 大きな落ち葉（足場。押すと動く。小道のすぐそばには置かない：カメラの前をふさがない）
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = RandomInRing(4f, 34f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p) || DistToTutorialSpots(p) < 3f) continue;
                if (TutorialLayout.DistToTrail(p) < 4f || Vector2.Distance(p, TutorialLayout.Gate) < 8f) continue;
                float s = R(0.7f, 1.2f);
                Vector3 pos = TG(p) + Vector3.up * 0.04f;
                PlaceLoose(Pick(BigLeaves), prop, pos, GroundRotation(p, R(0, 360), 1f, 3f), s, LooseProps.Shape.BigLeaf, true, 120f);
                if (_bigLeaves.Count < 40) _bigLeaves.Add(pos);
            }
            // 小枝
            for (int i = 0; i < 8; i++)
            {
                Vector2 p = RandomInRing(6f, 32f);
                if (!IsFree(p, 3f) || TutorialLayout.TrailMask(p.x, p.y) > 0.3f || DistToTutorialSpots(p) < 4f) continue;
                Vector3 pos = TG(p) + Vector3.down * 0.05f;
                Place(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, pos, GroundRotation(p, R(0, 360), 0.8f, 2f), R(0.8f, 1.1f), true, true, 150f);
                _twigs.Add(pos);
            }
            // 苔の小山のまわりの、どんぐりと松ぼっくり（押すと転がる）
            for (int i = 0; i < 7; i++)
            {
                Vector2 p = RandomInCircle(TutorialLayout.MossMound, 8f);
                if (!IsFree(p, 1f) || TutorialLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                float s = R(0.9f, 1.2f);
                if (i < 5)
                {
                    var rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(0f, 0f, 88f);
                    var acorn = Place("Acorn", assets.propGlossy, TG(p) + Vector3.up * 0.42f * s, rot, s, true, true, 150f, true);
                    if (acorn != null) RollingProp.Make(acorn, assets.Get("Acorn"), s, 0.47f * s, Area);
                    Occupy(p, 0.7f * s);
                }
                else
                {
                    var rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(R(70f, 95f), 0f, 0f);
                    PlaceLoose("Pinecone", prop, TG(p) + Vector3.up * 0.45f * s, rot, s, LooseProps.Shape.Pinecone, true, 150f);
                    Occupy(p, 1.4f * s);
                }
            }
        }

        // ------------------------------------------------------------------
        // 草花（コライダーなし）
        // ------------------------------------------------------------------
        void BuildTutorialFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            Vector2 meadow = TutorialLayout.Meadow + new Vector2(2f, 0f);

            bool Open(Vector2 p, float trail, float spots) =>
                IsLand(p, 0.1f) && !InsideOccupied(p) && TutorialLayout.TrailMask(p.x, p.y) <= trail && DistToTutorialSpots(p) >= spots;

            // 草（草原は少し多め、ほかはまばら。小道のそばは低い草だけ：うしろのカメラが草にうもれて、しゃくとりむしが見えなくならない）
            int grass = Mathf.RoundToInt(1500 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = R01() < 0.25f ? RandomInCircle(meadow, 10f) : RandomInRing(2f, 40f);
                bool inMeadow = Vector2.Distance(p, meadow) < 9f;
                if (!inMeadow && Mathf.PerlinNoise(p.x * 0.08f + 100f, p.y * 0.08f) < 0.5f) continue;
                if (!Open(p, 0.35f, 1.6f)) continue;
                float trail = TutorialLayout.DistToTrail(p);
                if (trail < 3f || Vector2.Distance(p, TutorialLayout.Meadow) < 5.5f) continue;   // 草原のまん中は、花とクローバーの原
                // 草原（はやく・背伸びの練習の所）は、短い草
                float s = inMeadow ? R(0.38f, 0.6f) : R(0.5f, 0.95f) * Mathf.Lerp(0.55f, 1f, ShakuMath.SmoothStep(3f, 7f, trail));
                Place(Pick(Grass), fol, TG(p), GroundRotation(p, R(0, 360), 0.5f, 5f), s, false, false, 55f);
            }
            // クローバー
            int clover = Mathf.RoundToInt(420 * dens);
            for (int i = 0; i < clover; i++)
            {
                Vector2 p = RandomInRing(2f, 38f);
                if (Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 40f) < 0.58f || !Open(p, 0.4f, 1f)) continue;
                Place(R01() < 0.5f ? "Clover_A" : "Clover_B", fol, TG(p), GroundRotation(p, R(0, 360), 0.7f, 4f), R(0.7f, 1.2f), false, false, 50f);
            }
            // 苔のかたまり（西の苔の小山は、たくさん）
            int moss = Mathf.RoundToInt(520 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = R01() < 0.35f ? RandomInCircle(TutorialLayout.MossMound, 9f) : RandomInRing(1f, 40f);
                if (Mathf.PerlinNoise(p.x * 0.07f + 500f, p.y * 0.07f + 80f) < 0.5f && Vector2.Distance(p, TutorialLayout.MossMound) > 8f) continue;
                if (!IsLand(p, 0.1f) || TutorialLayout.DistToTrail(p) < 2.2f) continue;
                Place("Moss", fol, TG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 1f, 3f), R(0.5f, 1.3f), false, false, 60f);
            }
            // 落ち葉のかけらと、小さな小石（押すと動く）
            int litter = Mathf.RoundToInt(800 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = RandomInRing(1f, 40f);
                if (Mathf.PerlinNoise(p.x * 0.06f + 50f, p.y * 0.06f + 20f) < 0.5f || !IsLand(p, 0.05f)) continue;
                PlaceLoose(Pick(BigLeaves), assets.prop, TG(p) + Vector3.up * 0.01f, GroundRotation(p, R(0, 360), 1f, 6f), R(0.07f, 0.16f), LooseProps.Shape.Leaf, false, 40f);
            }
            int pebbles = Mathf.RoundToInt(220 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(1f, 38f);
                if (DistToTutorialSpots(p) < 1.2f) continue;
                PlaceLoose(Pick(Rocks), assets.prop, TG(p), Quaternion.Euler(R(-12f, 12f), R(0, 360), R(-12f, 12f)), R(0.06f, 0.18f), LooseProps.Shape.Pebble, false, 40f);
            }
            // 芽生え
            for (int i = 0; i < 16; i++)
            {
                Vector2 p = RandomInRing(3f, 32f);
                if (!Open(p, 0.4f, 2.5f)) continue;
                Place("Sprout", fol, TG(p), GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.2f), false, true, 70f);
            }
            // 花（東の草原と、はじまりの場所のまわり）
            void Flowers(string name, Vector2 center, float radius, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInCircle(center, radius);
                    if (!Open(p, 0.3f, 2f)) continue;
                    Place(name, flw, TG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Flowers("Daisy", meadow, 9f, 14, 0.8f, 1.2f);
            Flowers("Bellflower", meadow, 9f, 7, 0.8f, 1.2f);
            Flowers("Dandelion", meadow, 9f, 8, 0.8f, 1.2f);
            Flowers("DandelionPuff", meadow, 8f, 4, 0.9f, 1.2f);
            Flowers("Strawberry", meadow, 8f, 3, 0.9f, 1.2f);
            Flowers("Daisy", TutorialLayout.Spawn, 9f, 6, 0.7f, 1.0f);
            Flowers("Dandelion", Vector2.zero, 30f, 6, 0.7f, 1.0f);
            Flowers("Bellflower", TutorialLayout.MossMound, 8f, 4, 0.8f, 1.1f);
            // シダ（林のふち）
            for (int i = 0; i < 36; i++)
            {
                Vector2 p = RandomInRing(30f, 44f);
                if (Vector2.Distance(p, TutorialLayout.Gate) < 6f || !Open(p, 0.2f, 0f)) continue;
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", flw, TG(p) + Vector3.down * 0.3f, GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.3f), false, true, 200f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceTutorialDewdrops()
        {
            DewdropPoints.Clear();
            AddDewFromAbove(TutorialLayout.FirstDew, 20f);                                                                     // 道の上（すすむ練習）
            AddDewFromAbove(new Vector2(TutorialStumpTop.x + 0.6f, TutorialStumpTop.z + 0.3f), TutorialStumpTop.y + 3f);        // 切り株のてっぺん（登る練習）
            AddDewFromAbove(new Vector2(TutorialCapTop.x + 0.5f, TutorialCapTop.z), TutorialCapTop.y + 3f);                     // 赤キノコのかさの上（糸をねらう練習）
        }

        void PlaceTutorialCreatures()
        {
            // かたつむり：小さな赤キノコの根もとをぐるり（いきものに会う練習）
            Vector2 c = TutorialLayout.SnailCap;
            var g = AddMob("snail", TG(c), 1, 2.2f);
            g.path.Add(TG(c));
            g.showcase = true;
        }

        void DrawTutorialMap(Color32[] px, int size)
        {
            MapTrails(px, size, TutorialLayout.Trails, new Color32(214, 196, 150, 255));
            MapDot(px, size, TG(TutorialLayout.Stump), 4.5f, new Color32(170, 120, 72, 255));
            foreach (var (pos, s) in _redCaps) MapDot(px, size, pos, Mathf.Max(2f, 2.5f * s), new Color32(230, 70, 60, 255));
        }
    }
}
