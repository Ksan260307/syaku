using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 山エリアの組み立て：ねじれた大きな松・山小屋・岩のアーチ・岩の階段・山頂の標柱と三角点・道しるべ・
    /// 湧き水の泉とかけい・雪渓の雪・オコジョの岩のすみか・高山の花・はい松・遠くの山なみと雲海。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _mtnTops = new List<Vector3>();      // しずくを置く、物の上（上からさがす）
        readonly List<Vector2> _okojoDens = new List<Vector2>();    // オコジョの岩のすみか
        readonly List<Vector3> _stairTops = new List<Vector3>();    // 岩の階段の段の上
        Vector3 _hutStep, _springSpout, _snowTop, _archTop;
        float _hutYaw;
        GameObject _pine;                                           // 大きな松（根を地面にそわせる）

        /// <summary>湧き水のかけいの先（水が落ちはじめる所）。</summary>
        public Vector3 SpringSpout => _springSpout;
        /// <summary>岩の階段の段の上（テスト用）。</summary>
        public IReadOnlyList<Vector3> StairTops => _stairTops;
        /// <summary>オコジョの岩のすみか（テスト用）。</summary>
        public IReadOnlyList<Vector2> OkojoDens => _okojoDens;

        static readonly string[] Slabs = { "Mtn_Slab_A", "Mtn_Slab_C", "Mtn_Slab_B" };

        void ClearMountain()
        {
            _mtnTops.Clear();
            _okojoDens.Clear();
            _stairTops.Clear();
            _hutStep = _springSpout = _snowTop = _archTop = Vector3.zero;
            _hutYaw = 0f;
            _pine = null;
            MountainFixes.Clear();
        }

        static Vector3 MG(Vector2 p) => MountainLayout.Ground(p.x, p.y);

        // ------------------------------------------------------------------
        // 大きな物
        // ------------------------------------------------------------------
        void BuildMountainSolids()
        {
            BuildDistantPeaks();
            Material prop = assets.prop;

            // 大きな松
            Vector2 pn = MountainLayout.Pine;
            _pine = Place("Mtn_Pine", assets.bark, MG(pn) + Vector3.down * 0.9f, Quaternion.Euler(0f, 200f, 0f), 1f, true, true, 400f, asRenderer: true);
            Occupy(pn, 9f);

            // 山小屋：とびらは、小道の来る方（南西）を向く
            {
                Vector2 h = MountainLayout.Hut;
                Vector2 door = new Vector2(-6f, -5f).normalized;
                _hutYaw = Mathf.Atan2(door.x, door.y) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0f, _hutYaw, 0f);
                Vector3 g = new Vector3(h.x, MountainLayout.HutLevel, h.y);
                Place("Mtn_Hut", assets.bark, g, rot, 1f, true, true, 400f, asRenderer: true);
                _mtnTops.Add(g + Vector3.up * 13f);                              // 屋根のむね
                _hutStep = g + rot * new Vector3(3f, 0f, 6.6f);                   // 入り口の石段
                Occupy(h, 10.5f);
            }

            // 岩のアーチ（岩のトンネル）：西から東へ、くぐりぬける
            {
                Vector2 a = MountainLayout.RockArch;
                Place("Mtn_RockArch", prop, MG(a) + Vector3.down * 0.7f, Quaternion.Euler(0f, 90f, 0f), 1f, true, true, 300f, asRenderer: true);
                _archTop = MG(a) + Vector3.up * 12f;
                for (int i = -2; i <= 2; i++) Occupy(a + new Vector2(0f, i * 3f), 3.5f);
            }

            // 岩の階段：小道のわきに、平たい岩が段々にかさなる（いちばん上は、ながめのよい岩）
            {
                Vector2 rs = MountainLayout.RockStairs;
                Vector2 up = ((rs + new Vector2(1f, 5f)) - (rs + new Vector2(-1f, -4f))).normalized;
                Vector2 side = new Vector2(up.y, -up.x);
                for (int k = 0; k < 6; k++)
                {
                    Vector2 p = rs + side * 3.6f + up * (k * 2.4f - 7.5f);   // いちばん上の段は、名所のまん中の近く
                    float g = MountainLayout.Height(p.x, p.y);
                    float yaw = Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg + R(-12f, 12f);
                    string slab = Slabs[k % Slabs.Length];
                    Mesh m = assets.Get(slab);
                    float top = m != null ? m.bounds.max.y : 1.2f;
                    for (int j = 0; j <= k; j++)
                    {
                        string sj = j == k ? slab : Slabs[(k + j + 1) % Slabs.Length];
                        Place(sj, prop, new Vector3(p.x, g - 0.4f + j * 0.85f, p.y), Quaternion.Euler(R(-2f, 2f), yaw + j * 23f, R(-2f, 2f)), j == k ? 1f : 1.08f, true, true, 220f);
                    }
                    _stairTops.Add(new Vector3(p.x, g - 0.4f + k * 0.85f + top, p.y));
                    Occupy(p, 3.2f);
                }
            }

            // 山頂：標柱と三角点
            {
                Vector2 sm = MountainLayout.Summit;
                // 標柱と三角点は、山頂から見下ろす景色（南）のじゃまにならない所に
                Place("Mtn_SummitPost", assets.bark, MG(sm + new Vector2(5.5f, -0.5f)) + Vector3.down * 0.5f, Quaternion.Euler(0f, 200f, 0f), 1f, true, true, 400f, asRenderer: true);
                Vector3 bm = MG(sm + new Vector2(-3.6f, 0.4f)) + Vector3.down * 0.3f;
                Place("Mtn_Benchmark", prop, bm, Quaternion.Euler(0f, 25f, 0f), 1f, true, true, 250f);
                _mtnTops.Add(bm + Vector3.up * 3f);
                Occupy(sm, 5f);
            }

            // 道しるべ（分かれ道と、ふもと）
            Place("Mtn_Sign", assets.bark, MG(new Vector2(7f, -8.5f)) + Vector3.down * 0.3f, Quaternion.Euler(0f, 20f, 0f), 1f, true, true, 250f);
            Occupy(new Vector2(7f, -8.5f), 2f);
            Place("Mtn_Sign", assets.bark, MG(new Vector2(-6.5f, -40f)) + Vector3.down * 0.3f, Quaternion.Euler(0f, -10f, 0f), 1f, true, true, 250f);
            Occupy(new Vector2(-6.5f, -40f), 2f);
        }

        /// <summary>遠くの山なみ（雪をかぶった山頂）と、雲海。遊べる場所のずっと外。</summary>
        void BuildDistantPeaks()
        {
            var rnd = new System.Random(seed + 4242);
            float Rn(float a, float b) => a + (b - a) * (float)rnd.NextDouble();
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            ColorUtility.TryParseHtmlString("#6f7a86", out var rock);
            ColorUtility.TryParseHtmlString("#56705a", out var forest);
            ColorUtility.TryParseHtmlString("#f2f5fa", out var snow);
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f + Rn(-0.15f, 0.15f);
                float d = Rn(210f, 330f);
                Vector2 cxz = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                // ふもとは、山なみの低い所（ほぼ地面の外周の高さ）
                float baseY = Area.Height(cxz.x / d * 96f, cxz.y / d * 96f) - 6f;
                float h = Rn(55f, 120f) * (cxz.y > 0f ? 1.15f : 0.9f);   // 北の山ほど高い
                float r = Rn(60f, 95f);
                int seg = 20, rings = 6;
                int start = v.Count;
                for (int i = 0; i <= rings; i++)
                {
                    float u = i / (float)rings;
                    for (int s = 0; s < seg; s++)
                    {
                        float ang = s / (float)seg * Mathf.PI * 2f;
                        float wob = 1f + 0.25f * Mathf.PerlinNoise(k * 3.1f + Mathf.Cos(ang) * 1.5f, Mathf.Sin(ang) * 1.5f + u * 2f) - 0.12f;
                        float rr = r * (1f - u) * wob;
                        float y = baseY + h * Mathf.Pow(u, 0.85f);
                        v.Add(new Vector3(cxz.x + Mathf.Cos(ang) * rr, y, cxz.y + Mathf.Sin(ang) * rr));
                        float snowK = ShakuMath.SmoothStep(0.62f, 0.75f, u + 0.08f * Mathf.Sin(ang * 5f + k));
                        c.Add(Color.Lerp(Color.Lerp(forest, rock, ShakuMath.SmoothStep(0.15f, 0.5f, u)), snow, snowK));
                    }
                }
                v.Add(new Vector3(cxz.x, baseY + h + 1f, cxz.y));
                c.Add(snow);
                int top = v.Count - 1;
                for (int i = 0; i < rings; i++)
                    for (int s = 0; s < seg; s++)
                    {
                        int a0 = start + i * seg + s, a1 = start + i * seg + (s + 1) % seg;
                        int b0 = a0 + seg, b1 = a1 + seg;
                        t.Add(a0); t.Add(b0); t.Add(b1);
                        t.Add(a0); t.Add(b1); t.Add(a1);
                    }
                for (int s = 0; s < seg; s++)
                {
                    t.Add(start + rings * seg + s); t.Add(top); t.Add(start + rings * seg + (s + 1) % seg);
                }
            }
            var mesh = Own(new Mesh { name = "DistantPeaks" });
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("DistantPeaks");
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.terrain;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // 雲海：山の外がわの、低い所にうかぶ雲（山頂から見下ろすと、雲の海）。
            // 雲は、かげの側も明るく（下から見上げても、黒っぽいかたまりに見えない）、輪郭線もつけない
            var cloud = Own(new Material(assets.bark) { name = "M_Cloud" });
            cloud.SetFloat("_OutlineWidth", 0f);
            cloud.SetFloat("_ShadowBrightness", 1.9f);
            cloud.SetFloat("_RimStrength", 0.5f);
            if (cloud.HasProperty("_RimColor")) cloud.SetColor("_RimColor", Color.white);
            cloud.SetShaderPassEnabled("SRPDefaultUnlit", false);
            for (int k = 0; k < 26; k++)
            {
                float a = k / 26f * Mathf.PI * 2f + Rn(-0.1f, 0.1f);
                float d = Rn(118f, 190f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                float y = Area.Height(p.x / d * 90f, p.y / d * 90f) + Rn(-2f, 6f);
                var puff = Place("Mtn_CloudPuff", cloud, new Vector3(p.x, y, p.y), Quaternion.Euler(0f, Rn(0f, 360f), 0f), Rn(1.4f, 2.4f), false, false, 700f, asRenderer: true);
                if (puff != null && puff.TryGetComponent<MeshRenderer>(out var pr)) pr.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------
        // 小物（コライダーあり）
        // ------------------------------------------------------------------
        void ScatterMountainProps()
        {
            Material prop = assets.prop;
            Occupy(MountainLayout.Gate, 4.5f);
            Occupy(MountainLayout.Spawn, 3f);

            // 湧き水のかけい：泉の北の岩から、竹のといが水面の上へ
            {
                Vector2 c = MountainLayout.Spring;
                Vector2 at = c + new Vector2(0f, MountainLayout.SpringRadius + 1.6f);
                // 岩は岸の坂にうめて、といの先が水面の少し上に来るように
                Vector3 g = new Vector3(at.x, Mathf.Min(MG(at).y - 0.4f, MountainLayout.SpringLevel - 0.3f), at.y);
                Quaternion rot = Quaternion.Euler(0f, 180f, 0f);   // といの先（メッシュの +Z）を、泉の方へ
                Place("Mtn_Spout", prop, g, rot, 1f, true, true, 200f);
                _springSpout = g + rot * new Vector3(0f, 2.1f, 5.6f);
                Occupy(at, 3.5f);
                // 泉のとびいし（底までとどく石）
                Vector2[] stones = { new Vector2(-5.5f, -2f), new Vector2(-3f, -5f), new Vector2(4.5f, -4f), new Vector2(6f, 1f) };
                foreach (var o in stones)
                {
                    Vector2 p = c + o;
                    float s = R(0.8f, 1.1f);
                    string rock = Pick(Rocks);
                    Vector3 pos = new Vector3(p.x, MountainLayout.SpringLevel - 0.35f, p.y);
                    Place(rock, prop, pos, Quaternion.Euler(0f, R(0f, 360f), 0f), s, true);
                    StoneFooting(pos, rock, s);
                    _mtnTops.Add(pos + Vector3.up * 3f);
                }
            }

            // オコジョの岩のすみか（石がまるく積み重なり、まん中に穴）
            {
                Vector2 rs = MountainLayout.RockStairs;
                Vector2[] dens = { rs + new Vector2(-7.5f, 3f), MountainLayout.RockArch + new Vector2(-2f, -9f) };
                foreach (var d in dens)
                {
                    Place("Okojo_Rocks", prop, MG(d) + Vector3.down * 0.15f, Quaternion.Euler(0f, R(0f, 360f), 0f), 1f, true, true, 200f);
                    _okojoDens.Add(d);
                    Occupy(d, 4f);
                }
            }

            // 雪渓の雪（とけ残ったかたまり）
            {
                Vector2 sp = MountainLayout.SnowPatch;
                Vector2[] mounds = { new Vector2(0f, 0f), new Vector2(-4f, 3f), new Vector2(3.5f, 2.5f), new Vector2(-2f, -4f), new Vector2(4f, -3f), new Vector2(-6f, -1f) };
                for (int i = 0; i < mounds.Length; i++)
                {
                    Vector2 p = sp + mounds[i];
                    float s = i == 0 ? 1.5f : R(0.8f, 1.2f);
                    Vector3 pos = MG(p) + Vector3.down * 0.25f;
                    Place(i % 2 == 0 ? "Mtn_SnowMound_A" : "Mtn_SnowMound_B", prop, pos, Quaternion.Euler(0f, R(0f, 360f), 0f), s, true, true, 220f);
                    if (i == 0) _snowTop = pos + Vector3.up * 4f;
                    Occupy(p, 2.6f * s);
                }
            }

            // 岩（山はだの、大きな岩と小さな岩）
            for (int i = 0; i < 70; i++)
            {
                Vector2 p = RandomInRing(6f, 62f);
                float s = R(0.4f, 2.2f);
                if (!IsFree(p, 1.4f * s) || !IsLand(p, 0.1f) || MountainLayout.GenTrailMask(p.x, p.y) > 0.15f) continue;
                if (MountainLayout.SnowMask(p.x, p.y) > 0.3f || MountainLayout.InSpring(p.x, p.y, 2f)) continue;
                float h = MountainLayout.Height(p.x, p.y);
                float rocky = MountainLayout.RockMask(p.x, p.y, h, MountainLayout.Normal(p.x, p.y));
                if (rocky < 0.25f && R01() < 0.55f) continue;   // 草地には少なめ
                Vector3 pos = MG(p) + Vector3.down * 0.3f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)), s, true);
                Occupy(p, 1.3f * s);
                if (s > 1.2f) _bigRocks.Add((pos, s));
            }
            // 外周の大岩（自然な境界）
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 74f);
                if (Vector2.Distance(p, MountainLayout.Gate) < 9f) continue;
                float s = R(2.8f, 5.6f);
                Place(Pick(Rocks), prop, MG(p) + Vector3.down * 0.35f * s, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }
            // 山頂のケルン（石積み）
            {
                Vector2 c = MountainLayout.Summit + new Vector2(-4.5f, 2.5f);
                Vector3 g = MG(c);
                float y = g.y - 0.3f;
                for (int k = 0; k < 5; k++)
                {
                    float s = Mathf.Lerp(1.3f, 0.5f, k / 4f);
                    Place(Rocks[k % Rocks.Length], prop, new Vector3(c.x + R(-0.2f, 0.2f), y, c.y + R(-0.2f, 0.2f)), Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), s, true, true, 220f);
                    y += 0.95f * s;
                }
                Occupy(c, 2.5f);
            }
            // 松ぼっくり（押すと転がる）と、松の根もとの落ち葉
            for (int i = 0; i < 14; i++)
            {
                Vector2 p = RandomInCircle(MountainLayout.Pine, 12f);
                if (!IsFree(p, 1.4f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.3f);
                Quaternion rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(R(70f, 95f), 0f, 0f);
                PlaceLoose("Pinecone", prop, MG(p) + Vector3.up * 0.45f * s, rot, s, LooseProps.Shape.Pinecone, true, 150f);
                Occupy(p, 1.4f * s);
            }
            // 小枝
            for (int i = 0; i < 8; i++)
            {
                Vector2 p = RandomInCircle(MountainLayout.Pine, 14f);
                if (!IsFree(p, 3f) || !IsLand(p)) continue;
                LayTwig(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, p, R(0f, 360f), R(-4f, 4f), R(0.7f, 1f), 150f, true);
                Occupy(p, 3f);
            }
            // アリの行列（ふもとの日なた）
            AddAntLine(new Vector2(-10f, -40f), new Vector2(-4f, -30f), 8);
        }

        // ------------------------------------------------------------------
        // 湧き水の泉と、かけいから落ちる水
        // ------------------------------------------------------------------
        void BuildMountainWater()
        {
            int seg = 64;
            float r = MountainLayout.SpringRadius + 2.2f;
            Vector2 c = MountainLayout.Spring;
            float wl = MountainLayout.SpringLevel;
            var v = new List<Vector3> { new Vector3(c.x, wl, c.y) };
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(c.x + Mathf.Cos(a) * r, wl, c.y + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < seg; i++)
            {
                t.Add(0);
                t.Add(1 + (i + 1) % seg);
                t.Add(1 + i);
            }
            var uv = new List<Vector2>(v.Count);
            var col = new List<Color>(v.Count);
            foreach (var p in v)
            {
                uv.Add(new Vector2(p.x / 12f, p.z / 7f));
                col.Add(new Color(0f, 0f, 0f, 1f));
            }
            var mesh = Own(new Mesh { name = "Spring" });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Water");
            go.layer = ShakuConst.WaterLayer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.pond != null ? assets.pond : assets.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // かけいの水が落ちる所に、波紋が広がる
            var mpb = new MaterialPropertyBlock();
            if (_springSpout != Vector3.zero) mpb.SetVector("_Drip", new Vector4(_springSpout.x, _springSpout.z, 2.2f, 1.4f));
            mpb.SetFloat("_FoamDepth", 0.03f);   // 岸ぎわの泡は、ふちだけ（すんだ水が、白くならない）
            mr.SetPropertyBlock(mpb);
            WaterView.Register(mr);
            if (_springSpout != Vector3.zero) BuildSpringStream(_springSpout, wl);
        }

        /// <summary>かけいの先から水面まで、細く落ちる水。</summary>
        void BuildSpringStream(Vector3 tip, float waterLevel)
        {
            Vector3 toCenter = new Vector3(MountainLayout.Spring.x - tip.x, 0f, MountainLayout.Spring.y - tip.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toCenter) * 0.22f;
            const int rows = 10;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            for (int r = 0; r <= rows; r++)
            {
                float k = r / (float)rows;
                // 先から少し前へとび出して、放物線で落ちる
                Vector3 p = tip + toCenter * (0.5f * k) + Vector3.down * ((tip.y - waterLevel + 0.05f) * k * k);
                v.Add(p - side * (1f - 0.3f * k));
                v.Add(p + side * (1f - 0.3f * k));
                uv.Add(new Vector2(0f, k * 2f));
                uv.Add(new Vector2(1f, k * 2f));
                col.Add(new Color(1f, 1f, 1f, 1f));
                col.Add(new Color(1f, 1f, 1f, 1f));
            }
            for (int r = 0; r < rows; r++)
            {
                int a = r * 2;
                t.Add(a); t.Add(a + 2); t.Add(a + 1);
                t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
                t.Add(a); t.Add(a + 1); t.Add(a + 2);      // うらからも見える
                t.Add(a + 1); t.Add(a + 3); t.Add(a + 2);
            }
            var mesh = Own(new Mesh { name = "SpringStream" });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("SpringStream");
            go.layer = ShakuConst.WaterLayer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.waterfall != null ? assets.waterfall : assets.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------
        // 草花
        // ------------------------------------------------------------------
        /// <summary>草花を植えない所（歩く小道・雪渓・泉）。しずくを置いたあとの小物に使う。</summary>
        bool MountainBare(Vector2 p) =>
            MountainLayout.TrailMask(p.x, p.y) > 0.35f || MountainLayout.SnowMask(p.x, p.y) > 0.25f || MountainLayout.InSpring(p.x, p.y, 0.5f);

        /// <summary>しずくを置く前の草花の置き方（はじめの道すじ。乱数の使い方を変えないため）。</summary>
        bool MountainBareGen(Vector2 p) =>
            MountainLayout.GenTrailMask(p.x, p.y) > 0.35f || MountainLayout.SnowMask(p.x, p.y) > 0.25f || MountainLayout.InSpring(p.x, p.y, 0.5f);

        void BuildMountainFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            // 高山の短い草（ふもとは多く、高い所・岩はだは少ない）
            int grass = Mathf.RoundToInt(4200 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = R01() < 0.3f ? RandomInCircle(MountainLayout.Meadow, 16f) : RandomInRing(2f, 74f);
                if (!IsLand(p, 0.12f) || InsideOccupied(p) || MountainBareGen(p)) continue;
                if (Vector2.Distance(p, MountainLayout.Spawn) < 2.5f) continue;
                float h = MountainLayout.Height(p.x, p.y);
                float rocky = MountainLayout.RockMask(p.x, p.y, h, MountainLayout.Normal(p.x, p.y));
                float patch = Mathf.PerlinNoise(p.x * 0.09f + 30f, p.y * 0.09f + 12f);
                if (patch < 0.35f + rocky * 0.5f) continue;
                float s = Mathf.Lerp(R(0.55f, 1.0f), R(0.35f, 0.6f), ShakuMath.SmoothStep(4f, 20f, h));
                Place(Pick(Grass), fol, MG(p), GroundRotation(p, R(0, 360), 0.5f, 5f), s, false, false, 55f);
            }
            // 苔（泉のまわりと、日かげの岩のそば）
            int moss = Mathf.RoundToInt(700 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = R01() < 0.5f ? RandomInCircle(MountainLayout.Spring, MountainLayout.SpringRadius + 7f) : RandomInRing(4f, 64f);
                if (!IsLand(p, 0.08f) || MountainBareGen(p)) continue;
                if (Vector2.Distance(p, MountainLayout.Spring) > MountainLayout.SpringRadius + 8f && Mathf.PerlinNoise(p.x * 0.08f + 7f, p.y * 0.08f) < 0.6f) continue;
                Place("Moss", fol, MG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 1f, 3f), R(0.5f, 1.2f), false, false, 60f);
            }
            // はい松のしげみ（高い所の尾根）
            for (int i = 0; i < 70; i++)
            {
                Vector2 p = R01() < 0.5f ? RandomInCircle(MountainLayout.Ridge, 18f) : RandomInRing(20f, 66f);
                if (!IsLand(p, 0.2f) || InsideOccupied(p) || MountainBareGen(p)) continue;
                if (MountainLayout.Height(p.x, p.y) < 8f) continue;
                Place("Mtn_Haimatsu", fol, MG(p) + Vector3.down * 0.25f, GroundRotation(p, R(0, 360), 0.6f, 4f), R(0.8f, 1.4f), false, true, 140f);
            }
            // 高山の花
            void Flowers(string name, Vector2 center, float radius, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInCircle(center, radius);
                    if (!IsLand(p, 0.2f) || InsideOccupied(p) || MountainBareGen(p)) continue;
                    if (Vector2.Distance(p, MountainLayout.Spawn) < 3f) continue;
                    Place(name, flw, MG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.3f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Vector2 md = MountainLayout.Meadow;
            Flowers("Mtn_Chinguruma", md, 14f, 46, 0.9f, 1.3f);
            Flowers("Mtn_ChingurumaSeed", md + new Vector2(5f, 4f), 9f, 14, 0.9f, 1.2f);
            Flowers("Mtn_Kurumayuri", md, 13f, 16, 0.8f, 1.1f);
            Flowers("Bellflower", md, 15f, 14, 0.8f, 1.1f);
            Flowers("Mtn_Komakusa", MountainLayout.Ridge, 14f, 30, 0.9f, 1.3f);   // コマクサは、すなの多い尾根
            Flowers("Mtn_Komakusa", MountainLayout.Summit + new Vector2(-6f, -8f), 9f, 14, 0.9f, 1.2f);
            Flowers("Mtn_Chinguruma", MountainLayout.SnowPatch + new Vector2(6f, -7f), 8f, 16, 0.8f, 1.1f);   // 雪どけのあとに、さく
            Flowers("Mtn_Kurumayuri", MountainLayout.Hut, 14f, 8, 0.8f, 1.1f);
            Flowers("Daisy", Vector2.zero + new Vector2(0f, -36f), 14f, 12, 0.7f, 1.0f);
            // シダ（ふもとの、しめった所）
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = R01() < 0.6f ? RandomInRing(40f, 72f) : RandomInCircle(MountainLayout.Spring, 14f);
                if (!IsLand(p, 0.3f) || InsideOccupied(p) || MountainBareGen(p)) continue;
                if (MountainLayout.Height(p.x, p.y) > 14f) continue;
                if (Vector2.Distance(p, MountainLayout.Spawn) < 10f) continue;
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", flw, MG(p) + Vector3.down * 0.3f, GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.6f, 1.1f), false, true, 200f);
            }
            // 小さな小石（小道と岩はだ）と、落ち葉のかけら（松の下）
            int pebbles = Mathf.RoundToInt(700 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(1f, 66f);
                float h = MountainLayout.Height(p.x, p.y);
                float rocky = MountainLayout.RockMask(p.x, p.y, h, MountainLayout.Normal(p.x, p.y));
                if (MountainLayout.GenTrailMask(p.x, p.y) < 0.4f && rocky < 0.3f) continue;
                if (!IsLand(p, 0.0f) || InsideOccupied(p)) continue;
                PlaceLoose(Pick(Rocks), assets.prop, MG(p), Quaternion.Euler(R(-12f, 12f), R(0, 360), R(-12f, 12f)), R(0.05f, 0.16f), LooseProps.Shape.Pebble, false, 40f);
            }
            int litter = Mathf.RoundToInt(260 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = RandomInCircle(MountainLayout.Pine, 16f);
                if (!IsLand(p, 0.05f) || InsideOccupied(p)) continue;
                PlaceLoose(R01() < 0.6f ? "Leaf_Oak_Brown" : "Leaf_Maple_Yellow", assets.prop, MG(p) + Vector3.up * 0.01f, GroundRotation(p, R(0, 360), 1f, 6f), R(0.06f, 0.12f), LooseProps.Shape.Leaf, false, 40f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceMountainDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = MountainLayout.Spawn;
            AddDewFromAbove(s + new Vector2(-3f, 3f));
            AddDewFromAbove(s + new Vector2(4f, 6f));
            // 岩の階段：いちばん上と、まん中の段
            if (_stairTops.Count > 0) AddDewFromAbove(new Vector2(_stairTops[_stairTops.Count - 1].x, _stairTops[_stairTops.Count - 1].z), _stairTops[_stairTops.Count - 1].y + 2f);
            if (_stairTops.Count > 3) AddDewFromAbove(new Vector2(_stairTops[2].x, _stairTops[2].z), _stairTops[2].y + 2f);
            // 物の上（屋根・三角点・泉のとびいし）
            foreach (var t in _mtnTops) AddDewFromAbove(new Vector2(t.x, t.z), t.y);
            if (_hutStep != Vector3.zero) AddDewFromAbove(new Vector2(_hutStep.x, _hutStep.z), _hutStep.y + 3f);
            if (_snowTop != Vector3.zero) AddDewFromAbove(new Vector2(_snowTop.x, _snowTop.z), _snowTop.y);
            if (_archTop != Vector3.zero)
            {
                AddDewFromAbove(new Vector2(_archTop.x, _archTop.z), _archTop.y);                 // アーチの上
                Vector3 under = MG(MountainLayout.RockArch);
                if (CastDown(under + Vector3.up * 3f, 6f, out var hit)) AddDew(hit.point);         // アーチの下（トンネルの中）
            }
            // 大きな松の枝の、葉のかたまりの上（上からさがして、地面より高い所にあたったものだけ）
            {
                Vector2 pn = MountainLayout.Pine;
                float gy = MountainLayout.Height(pn.x, pn.y);
                int added = 0;
                for (int k = 0; k < 24 && added < 4; k++)
                {
                    float a = k * 2.39996f;
                    float r = 4f + (k % 6) * 1.6f;
                    Vector2 p = pn + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!CastDown(new Vector3(p.x, gy + 45f, p.y), 60f, out var hit)) continue;
                    if (hit.point.y - MountainLayout.Height(p.x, p.y) < 5f) continue;
                    int before = DewdropPoints.Count;
                    AddDew(hit.point);
                    if (DewdropPoints.Count > before) added++;
                }
            }
            // 泉のかけいの岩・花畑・雪渓のふち・山頂
            if (_springSpout != Vector3.zero) AddDewFromAbove(MountainLayout.Spring + new Vector2(0f, MountainLayout.SpringRadius + 2.2f));
            AddDewFromAbove(MountainLayout.Meadow + new Vector2(2f, 1f));
            AddDewFromAbove(MountainLayout.Meadow + new Vector2(-6f, 5f));
            AddDewFromAbove(MountainLayout.SnowPatch + new Vector2(7f, -3f));
            AddDewFromAbove(MountainLayout.Summit + new Vector2(1f, -3.5f));
            AddDewFromAbove(MountainLayout.Ridge + new Vector2(0f, 2f));
            foreach (var d in _okojoDens) AddDewFromAbove(d + new Vector2(3.6f, 0f));
            FillDewdrops(8f, 58f);
        }

        void PlaceMountainCreatures()
        {
            Vector2 md = MountainLayout.Meadow;
            Vector2 pn = MountainLayout.Pine;
            Vector2 hut = MountainLayout.Hut;
            // 地面を歩くいきものは、小道・泉・着いたときの景色の通り道からはなれた所にいる（道をふさがない・景色の前に立たない）
            Vector3 Calm(Vector2 want, float keep = 3.5f) => MG(CalmSpot(want, keep));
            // ライチョウ：山小屋のまわりと、はい松の尾根を、のんびり歩く（降りる場所も、小道のわき）
            {
                var g = AddMob("raichou", Calm(hut + new Vector2(4f, -12f), 4f), 1, 3f);
                Vector2[] spots = { hut + new Vector2(4f, -12f), MountainLayout.Ridge + new Vector2(4f, -6f), MountainLayout.SnowPatch + new Vector2(12f, 6f), md + new Vector2(10f, 4f) };
                foreach (var p in spots) g.path.Add(Calm(p, 4f));
            }
            // ニホンリス：大きな松の北（松の景色の通り道の外）と、山小屋のうら
            AddMob("risu", Calm(pn + new Vector2(6f, 6f), 4f), 1, 7f);
            AddMob("risu", Calm(hut + new Vector2(-2f, 14f), 4f), 1, 6f);   // 山小屋の北（うらのふみ板からはなれた所）
            // オコジョ：岩のすみか
            foreach (var d in _okojoDens) AddMob("okojo", MG(d), 1, 0.3f);
            // ナキウサギ：雪渓のそばの岩場と、山頂の岩
            AddMob("nakiusagi", Calm(MountainLayout.SnowPatch + new Vector2(12f, -10f)), 2, 5f);
            AddMob("nakiusagi", Calm(MountainLayout.Summit + new Vector2(-8f, -6f)), 1, 4f);
            // ハコネサンショウウオ：泉のほとり（小道の終わりの、上がり口はあけておく）
            Vector2 sp = MountainLayout.Spring;
            foreach (var want in new[] { sp + new Vector2(7.5f, -6.5f), sp + new Vector2(-MountainLayout.SpringRadius - 1.2f, -3f) })
            {
                Vector2 at = want;
                for (int i = 0; i < 24; i++)
                {
                    float a = i * 0.26f;
                    Vector2 q = sp + (want - sp).normalized * (MountainLayout.SpringRadius + 1.6f + (i % 3) * 0.6f);
                    q = sp + (Vector2)(Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg * ((i % 2) * 2 - 1)) * (q - sp));
                    if (MountainLayout.DistToTrail(q) > 2.8f && IsLand(q, 0.1f)) { at = q; break; }
                }
                AddMob("sanshouuo", MG(at), 1, 2.5f);
            }
            // 花畑のチョウとハチ
            AddMob("asagimadara", MG(md), 2, 11f, 3.8f);
            AddMob("asagimadara", MG(MountainLayout.Ridge), 1, 9f, 4.2f);
            AddMob("maruhanabachi", MG(md + new Vector2(-4f, 3f)), 2, 8f, 1.6f);
            AddMob("maruhanabachi", MG(MountainLayout.Ridge + new Vector2(2f, -2f)), 1, 8f, 1.6f);
            AddMob("butterfly", MG(md + new Vector2(6f, -4f)), 1, 9f, 3.2f);
            // オニヤンマ：泉の上と、山道の上。アキアカネ（夏の山へのぼってくる）
            AddMob("oniyanma", new Vector3(sp.x, MountainLayout.SpringLevel, sp.y), 1, 10f, 2.8f);
            AddMob("oniyanma", MG(new Vector2(14f, -2f)), 1, 12f, 2.6f);
            AddMob("dragonfly", MG(md + new Vector2(-8f, -6f)), 2, 10f, 3.2f);
            // ヒグラシ：松の幹（小道と反対の、北がわ）
            AddMob("higurashi", MG(pn + new Vector2(1.8f, 0.5f)), 1, 1.5f).showcase = true;   // 松の幹（幹の中なら、いきものの直しで根もとの外へ）
            AddMob("higurashi", MG(pn + new Vector2(0.5f, 2.2f)), 1, 1.5f).showcase = true;
            // マイマイカブリ：小屋のうらの日かげと、松の根もと（かたつむりもいる）
            AddMob("maimaikaburi", Calm(hut + new Vector2(11f, 1f)), 1, 4f);   // 小屋の東の日かげ（うらのふみ板からはなす）
            AddMob("maimaikaburi", Calm(pn + new Vector2(-6f, -4f)), 1, 4f);
            AddMob("snail", Calm(hut + new Vector2(9f, 8f), 2.5f), 1, 2f);
            // そのほか（山にもいる、なじみのいきもの）
            AddMob("grasshopper", Calm(md + new Vector2(4f, 8f)), 2, 8f);
            AddMob("ladybug", Calm(md + new Vector2(-2f, -3f), 2.5f), 1, 3f);
            AddMob("spider", Calm(MountainLayout.RockArch + new Vector2(-6f, 8f)), 1, 3f);
            {
                // カラス：ふもとの草地と、尾根の岩（山頂のまん中には下りない。名所と景色をふさがない）
                var g = AddMob("crow", Calm(new Vector2(-30f, -30f), 4f), 1, 2f);
                Vector2[] spots = { new Vector2(-30f, -30f), MountainLayout.Ridge + new Vector2(-6f, 4f) };
                foreach (var p in spots) g.path.Add(Calm(p, 4f));
            }
        }

        /// <summary>
        /// 山の小物の追加（しずくを置いたあと）：小道のわきの、道しるべの石積みと、花のあとの綿毛。
        /// </summary>
        void BuildMountainExtras()
        {
            Material prop = assets.prop;
            // 小道のわきの、小さな石積み（道に迷わないための、目じるし）
            foreach (var t in MountainLayout.Trails)
                for (int i = 0; i < t.Length - 1; i++)
                {
                    Vector2 a = t[i], b = t[i + 1];
                    if (Vector2.Distance(a, b) < 10f) continue;
                    Vector2 d = (b - a).normalized;
                    Vector2 side = new Vector2(-d.y, d.x) * (XR01() < 0.5f ? 2.6f : -2.6f);
                    Vector2 p = Vector2.Lerp(a, b, XR(0.35f, 0.65f)) + side;
                    if (!ExtraOk(p, 1.4f, true) || MountainBare(p) || !IsLand(p, 0.1f)) continue;
                    float y = MountainLayout.Height(p.x, p.y) - 0.15f;
                    for (int k = 0; k < 3; k++)
                    {
                        float s = Mathf.Lerp(0.55f, 0.3f, k / 2f);
                        Place(Rocks[(i + k) % Rocks.Length], prop, new Vector3(p.x, y, p.y), Quaternion.Euler(XR(-5f, 5f), XR(0f, 360f), XR(-5f, 5f)), s, true, true, 150f);
                        y += 0.8f * s;
                    }
                    Occupy(p, 1.4f);
                    Mark("cairn", MG(p));
                }
            // チングルマの綿毛（花畑のはしと、雪渓のそば）
            for (int i = 0; i < 18; i++)
            {
                Vector2 c = i < 10 ? MountainLayout.Meadow + new Vector2(-9f, 6f) : MountainLayout.SnowPatch + new Vector2(9f, -4f);
                Vector2 p = c + new Vector2(XR(-5f, 5f), XR(-5f, 5f));
                if (!ExtraOk(p, 0.4f, false) || MountainBare(p)) continue;
                PutDeco("Mtn_ChingurumaSeed", assets.flowers, MG(p) + Vector3.down * 0.05f, Quaternion.Euler(XR(-4f, 4f), XR(0f, 360f), XR(-4f, 4f)), XR(0.9f, 1.2f), true, 110f);
            }
        }

        void DrawMountainMap(Color32[] px, int size, bool generated)
        {
            MapTrails(px, size, MountainLayout.Trails, new Color32(214, 196, 160, 255));
            MapDot(px, size, new Vector3(MountainLayout.Spring.x, 0f, MountainLayout.Spring.y), MountainLayout.SpringRadius * 1.6f, new Color32(96, 168, 214, 255));
            // 大きな松・山小屋・岩のアーチ・山頂
            MapDot(px, size, new Vector3(MountainLayout.Pine.x, 0f, MountainLayout.Pine.y), 5f, new Color32(54, 96, 62, 255));
            Vector2 h = MountainLayout.Hut;
            for (int i = -3; i <= 3; i++)
                for (int j = -2; j <= 2; j++) MapDot(px, size, new Vector3(h.x + i * 1.6f, 0f, h.y + j * 1.6f), 1.4f, new Color32(150, 72, 52, 255));
            Vector2 a = MountainLayout.RockArch;
            for (int i = -2; i <= 2; i++) MapDot(px, size, new Vector3(a.x, 0f, a.y + i * 2.5f), i == 0 ? 1.2f : 2.4f, new Color32(140, 140, 146, 255));
            MapDot(px, size, new Vector3(MountainLayout.Summit.x, 0f, MountainLayout.Summit.y), 2.4f, new Color32(250, 214, 96, 255));
            if (!generated) return;
            foreach (var t in _stairTops) MapDot(px, size, t, 1.8f, new Color32(168, 166, 160, 255));
            foreach (var d in _okojoDens) MapDot(px, size, new Vector3(d.x, 0f, d.y), 2f, new Color32(120, 116, 112, 255));
            // 道しるべ・泉のとびいし・ふみ板と倒れた木（登れる所）
            var wood = new Color32(176, 138, 90, 255);
            foreach (var p in ExtraSpots("fix_sign")) MapDot(px, size, p, 1.3f, wood);
            foreach (var p in ExtraSpots("fix_springstone")) MapDot(px, size, p, 0.9f, new Color32(186, 182, 172, 255));
            foreach (var p in ExtraSpots("fix_sidestairs")) MapDot(px, size, p, 0.9f, new Color32(196, 190, 176, 255));
            foreach (var kind in new[] { "fix_hutplank", "fix_archlog", "fix_pinelog" })
                foreach (var p in ExtraSpots(kind)) MapDot(px, size, p, 1.2f, wood);
        }
    }
}
