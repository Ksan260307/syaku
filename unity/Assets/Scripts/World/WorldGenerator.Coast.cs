using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 海岸エリアの組み立て：大きな巻き貝・流木の丘・磯の岩場（フジツボの岩・潮だまり・岬の岩）・なくしたビーチサンダル・ガラスの浮き玉・
    /// シオマネキの巣穴・砂丘の草花・打ち上げられた海藻と貝がら・海の水面（波打ちぎわへよせる波）・沖の小島と灯台。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _coastTops = new List<Vector3>();   // しずくを置く、物の上（上からさがす）
        readonly List<Vector3> _burrows = new List<Vector3>();     // シオマネキの巣穴
        readonly List<Vector3> _reefTops = new List<Vector3>();    // 磯の岩のてっぺん
        Vector3 _conchMouth, _driftTop, _capeTop;

        /// <summary>シオマネキの巣穴（テスト用）。</summary>
        public IReadOnlyList<Vector3> Burrows => _burrows;
        /// <summary>大きな巻き貝の口の中の床（テスト用）。</summary>
        public Vector3 ConchMouth => _conchMouth;

        static readonly string[] Reefs = { "Coast_Reef_A", "Coast_Reef_B", "Coast_Reef_C" };
        static readonly string[] Shells = { "Coast_Clam", "Coast_Scallop", "Coast_Spiral", "Coast_Clam", "Coast_SeaGlass" };

        /// <summary>シオマネキの巣穴の場所（干潟のどろの上）。</summary>
        public static readonly Vector2[] BurrowSpots = { new Vector2(-38.5f, -3f), new Vector2(-36.5f, -8.5f), new Vector2(-51f, -6f), new Vector2(-55f, 1f) };

        void ClearCoast()
        {
            _coastTops.Clear();
            _burrows.Clear();
            _reefTops.Clear();
            _conchMouth = _driftTop = _capeTop = Vector3.zero;
        }

        static Vector3 CG(Vector2 p) => CoastLayout.Ground(p.x, p.y);

        // ------------------------------------------------------------------
        // 大きな物
        // ------------------------------------------------------------------
        void BuildCoastSolids()
        {
            BuildCoastBackdrop();
            Material prop = assets.prop;

            // 大きな巻き貝：口を小道の方（北）へ向けて、砂浜にころがる。口から中へ入れる
            {
                Vector2 c = CoastLayout.Conch;
                Quaternion rot = Quaternion.Euler(0f, 160f, 0f);
                Place("Coast_Conch", prop, CG(c) + Vector3.down * 0.15f, rot, 1f, true, true, 400f, asRenderer: true);
                Occupy(c, 6.5f);
                _coastTops.Add(CG(c) + Vector3.up * 12f);
                // 口の中の床：口の外から殻の中へ光をとばして、いちばん奥まで入る所をさがし、
                // そこから少し口の方へもどった所の、上に殻の天井がある床（殻の中）
                Physics.SyncTransforms();
                Vector3 mouthDir = rot * Vector3.back;
                Vector3 side = Vector3.Cross(Vector3.up, mouthDir);
                float best = 0f;
                for (float h = 0.6f; h <= 3.01f; h += 0.3f)
                for (float sx = -3f; sx <= 3.01f; sx += 0.5f)
                {
                    Vector3 from = CG(c) + mouthDir * 9f + side * sx + Vector3.up * h;
                    if (!Physics.Raycast(from, -mouthDir, out var wall, 20f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || wall.collider.name != "Coast_Conch") continue;
                    float depth = 20f - wall.distance;
                    if (depth <= best) continue;
                    Vector3 back = wall.point + mouthDir * 0.9f + Vector3.up * 0.3f;
                    if (!CastDown(back, 3f, out var floor) || floor.collider.name != "Coast_Conch" || floor.normal.y < 0.55f) continue;
                    if (floor.point.y > CG(c).y + 2.5f) continue;
                    if (!Physics.Raycast(floor.point + Vector3.up * 0.1f, Vector3.up, out var roof, 8f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || roof.collider.name != "Coast_Conch") continue;
                    best = depth;
                    _conchMouth = floor.point;
                }
            }

            // 流木の丘：砂丘の上に、白くさらされた大きな流木（上を歩ける）
            {
                Vector2 d = CoastLayout.Driftwood;
                Quaternion rot = Quaternion.Euler(0f, 28f, 0f);
                Place("Coast_Driftwood", assets.bark, CG(d) + Vector3.down * 0.45f, rot, 1f, true, true, 400f, asRenderer: true);
                _driftTop = CG(d) + Vector3.up * 3f;
                _coastTops.Add(CG(d) + rot * new Vector3(-3f, 0f, 0f) + Vector3.up * 3f);
                _coastTops.Add(CG(d) + rot * new Vector3(4f, 0f, 0f) + Vector3.up * 3f);
                for (int i = -3; i <= 3; i++) Occupy(d + Rot2(rot, new Vector2(i * 2.6f, 0f)), 2.6f);
            }

            // 岬の先の大岩（てっぺんから、沖の灯台が見える）
            {
                Vector2 p = CoastLayout.Cape;
                Place("Coast_Cape", prop, CG(p) + Vector3.down * 0.35f, Quaternion.Euler(0f, 205f, 0f), 1f, true, true, 400f, asRenderer: true);
                _capeTop = CG(p) + Vector3.up * 9f;
                _coastTops.Add(_capeTop);
                Occupy(p, 7.5f);
            }

            // 磯の岩：フジツボと海藻のついた岩が、岩場の上にならぶ
            {
                Vector2[] spots =
                {
                    CoastLayout.Reef + new Vector2(0f, 0f), CoastLayout.Reef + new Vector2(5f, -3f), CoastLayout.Reef + new Vector2(-3.5f, 4f),
                    new Vector2(30f, 6f), new Vector2(47f, -8f), new Vector2(40f, -28f), new Vector2(46f, -16f), new Vector2(33f, -16f),
                    new Vector2(50f, -20f), new Vector2(27.5f, -7f),
                };
                for (int i = 0; i < spots.Length; i++)
                {
                    Vector2 p = spots[i];
                    if (CoastLayout.PoolAt(p.x, p.y, 1.8f) >= 0) continue;
                    string mesh = Reefs[i % Reefs.Length];
                    float s = i < 3 ? R(1.05f, 1.25f) : R(0.7f, 1.05f);
                    Vector3 pos = CG(p) + Vector3.down * 0.35f * s;
                    Place(mesh, prop, pos, Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f)), s, true, true, 260f);
                    Occupy(p, 2.6f * s);
                    _reefTops.Add(pos + Vector3.up * 4f);
                }
            }

            // なくしたビーチサンダル（波打ちぎわの近く）と、ガラスの浮き玉（砂浜）
            {
                Vector2 sp = new Vector2(-26f, CoastLayout.ShoreZ(-26f) + 7f);
                Place("Coast_Sandal", prop, CG(sp) + Vector3.down * 0.08f, Quaternion.Euler(0f, 24f, 0f), 1f, true, true, 300f);
                _coastTops.Add(CG(sp) + Vector3.up * 3f);
                Occupy(sp, 6f);
                Vector2 fp = new Vector2(20f, CoastLayout.ShoreZ(20f) + 9f);
                Place("Coast_Float", prop, CG(fp) + Vector3.down * 0.3f, Quaternion.Euler(0f, 70f, 8f), 1f, true, true, 300f);
                _coastTops.Add(CG(fp) + Vector3.up * 4f);
                Occupy(fp, 2.6f);
            }
        }

        static Vector2 Rot2(Quaternion q, Vector2 v)
        {
            Vector3 r = q * new Vector3(v.x, 0f, v.y);
            return new Vector2(r.x, r.z);
        }

        /// <summary>沖の小島と灯台・遠くの岬（遊べる場所のずっと外）。</summary>
        void BuildCoastBackdrop()
        {
            Material prop = assets.prop;
            void Islet(Vector2 p, float s, float yaw)
            {
                Place("Coast_Islet", prop, new Vector3(p.x, -2.2f * s, p.y), Quaternion.Euler(0f, yaw, 0f), s, false, false, 900f, asRenderer: true);
            }
            Vector2 lh = CoastLayout.Lighthouse;
            Islet(lh, 1.2f, 20f);
            Place("Coast_Lighthouse", prop, new Vector3(lh.x + 2f, 5.4f, lh.y + 1f), Quaternion.Euler(0f, 30f, 0f), 1.1f, false, false, 900f, asRenderer: true);
            Islet(new Vector2(-150f, -150f), 1.6f, 70f);
            Islet(new Vector2(-118f, -175f), 0.8f, 140f);
            Islet(new Vector2(30f, -280f), 2.2f, 10f);
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------
        void ScatterCoastProps()
        {
            Material prop = assets.prop;
            Occupy(CoastLayout.Gate, 4.5f);
            Occupy(CoastLayout.Spawn, 3f);

            // シオマネキの巣穴（干潟のどろの上の、小さなどろの山とまるい穴）
            foreach (var b in BurrowSpots)
            {
                Vector3 g = CG(b);
                Place("Coast_Burrow", prop, g + Vector3.down * 0.06f, Quaternion.Euler(0f, R(0f, 360f), 0f), 1f, false, true, 140f);
                _burrows.Add(g);
                Occupy(b, 1.6f);
            }
            // 打ち上げられた海藻（波のとどく、いちばん上の線）
            for (int i = 0; i < 26; i++)
            {
                float x = R(-58f, 58f);
                Vector2 p = new Vector2(x, CoastLayout.ShoreZ(x) + R(3.2f, 6f));
                if (p.magnitude > 62f || !IsFree(p, 1.4f) || CoastLayout.ShelfMask(p.x, p.y) > 0.2f || CoastLayout.MudMask(p.x, p.y) > 0.3f) continue;
                Place("Coast_Wrack", prop, CG(p) + Vector3.down * 0.02f, GroundRotation(p, R(0f, 360f), 1f, 2f), R(0.8f, 1.4f), false, false, 90f);
                Occupy(p, 1.2f);
            }
            // 流れついた小さな流木
            for (int i = 0; i < 7; i++)
            {
                float x = R(-50f, 50f);
                Vector2 p = new Vector2(x, CoastLayout.ShoreZ(x) + R(5f, 14f));
                if (p.magnitude > 60f || !IsFree(p, 3f) || CoastLayout.ShelfMask(p.x, p.y) > 0.1f || CoastLayout.MudMask(p.x, p.y) > 0.2f) continue;
                Place("DriftLog", assets.bark, CG(p) + Vector3.down * 0.15f, GroundRotation(p, R(0f, 360f), 0.6f, 2f), R(0.6f, 0.9f), true, true, 200f);
                Occupy(p, 3.2f);
            }
            // 外周の大岩（陸の側だけ。海の側は、そのまま沖へ）
            for (int i = 0; i < 30; i++)
            {
                float a = i / 30f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 74f);
                if (CoastLayout.Inland(p.x, p.y) < 4f || Vector2.Distance(p, CoastLayout.Gate) < 9f) continue;
                float s = R(2.6f, 5f);
                Place(Pick(Rocks), prop, CG(p) + Vector3.down * 0.35f * s, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }
        }

        // ------------------------------------------------------------------
        // 海と潮だまりの水
        // ------------------------------------------------------------------
        void BuildCoastWater()
        {
            BuildSeaSurface();
            BuildFarSea();
            foreach (var (c, r) in CoastLayout.Pools) BuildPoolWater(c, r);
        }

        Material SeaMaterial => assets.sea != null ? assets.sea : assets.river != null ? assets.river : assets.water;

        /// <summary>海の水面の頂点色：R = 波の強さ（海は 1、小川と干潟の中は 0）、B = 波打ちぎわの近さ（波がくだける所）。</summary>
        public static Color SeaColor(float x, float z)
        {
            float d = CoastLayout.Inland(x, z);
            float wave = ShakuMath.SmoothStep(4f, 1.5f, d);
            float shore = ShakuMath.SmoothStep(-9f, -0.5f, d) * wave;
            return new Color(wave, 0f, shore, 1f);
        }

        /// <summary>
        /// 海の水面：地面が水面より低い所（海・小川・干潟のくぼみ）を、こまかい格子でおおう。
        /// UV = (x, 波打ちぎわから沖へのきょり)：波は、沖から波打ちぎわへよせてくる（シェーダーで上下に動かす）。
        /// </summary>
        void BuildSeaSurface()
        {
            const float step = 1f;
            const float x0 = -104f, z0 = -104f;
            const int nx = 208, nz = 148;   // x: -104..104, z: -104..44
            var h = new float[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
                h[j * (nx + 1) + i] = CoastLayout.Height(x0 + i * step, z0 + j * step);
            var index = new int[(nx + 1) * (nz + 1)];
            for (int k = 0; k < index.Length; k++) index[k] = -1;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            int Vert(int i, int j)
            {
                int k = j * (nx + 1) + i;
                if (index[k] >= 0) return index[k];
                float x = x0 + i * step, z = z0 + j * step;
                index[k] = v.Count;
                v.Add(new Vector3(x, CoastLayout.SeaLevel, z));
                uv.Add(new Vector2(x, -CoastLayout.Inland(x, z)));
                col.Add(SeaColor(x, z));
                return index[k];
            }
            const float above = CoastLayout.SeaLevel + 0.35f;   // 波でぬれる所まで
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i;
                if (h[a] > above && h[a + 1] > above && h[a + nx + 1] > above && h[a + nx + 2] > above) continue;
                int p00 = Vert(i, j), p10 = Vert(i + 1, j), p01 = Vert(i, j + 1), p11 = Vert(i + 1, j + 1);
                t.Add(p00); t.Add(p01); t.Add(p11);
                t.Add(p00); t.Add(p11); t.Add(p10);
            }
            var mesh = Own(new Mesh { name = "Sea" });
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // 波で上下に動く分、少し広く（カメラの外で消えないように）
            var b = mesh.bounds;
            b.Expand(new Vector3(0f, 0.4f, 0f));
            mesh.bounds = b;
            AddWater(mesh, "Sea", SeaMaterial);
        }

        /// <summary>沖の海（こまかい格子の外から、水平線まで）。波はなく、平ら。</summary>
        void BuildFarSea()
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            void Rect(float ax, float az, float bx, float bz)
            {
                int cx = Mathf.Max(1, Mathf.CeilToInt((bx - ax) / 60f)), cz = Mathf.Max(1, Mathf.CeilToInt((bz - az) / 60f));
                int start = v.Count;
                for (int j = 0; j <= cz; j++)
                for (int i = 0; i <= cx; i++)
                {
                    float x = Mathf.Lerp(ax, bx, i / (float)cx), z = Mathf.Lerp(az, bz, j / (float)cz);
                    v.Add(new Vector3(x, CoastLayout.SeaLevel, z));
                    uv.Add(new Vector2(x, -CoastLayout.Inland(x, z)));
                    col.Add(new Color(1f, 0f, 0f, 1f));
                }
                for (int j = 0; j < cz; j++)
                for (int i = 0; i < cx; i++)
                {
                    int a = start + j * (cx + 1) + i, b = a + 1, c = a + cx + 1, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(d);
                    t.Add(a); t.Add(d); t.Add(b);
                }
            }
            const float far = 900f;
            Rect(-far, -far, -104f, 60f);
            Rect(104f, -far, far, 60f);
            Rect(-104f, -far, 104f, -104f);
            var mesh = Own(new Mesh { name = "FarSea" });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AddWater(mesh, "FarSea", SeaMaterial);
        }

        /// <summary>潮だまりの水面（岩のくぼみの、しずかな水。底のヒトデやウニが見える）。</summary>
        void BuildPoolWater(Vector2 c, float r)
        {
            int seg = 48;
            float rr = r + 0.7f;
            float wl = CoastLayout.PoolLevel;
            var v = new List<Vector3> { new Vector3(c.x, wl, c.y) };
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(c.x + Mathf.Cos(a) * rr, wl, c.y + Mathf.Sin(a) * rr));
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
                uv.Add(new Vector2(p.x / 10f, p.z / 6f));
                col.Add(new Color(0f, 0f, 0f, 1f));
            }
            var mesh = Own(new Mesh { name = "TidePool" });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var mr = AddWater(mesh, "TidePool", assets.pond != null ? assets.pond : assets.water);
            // とてもすんだ、浅い水：底のヒトデやウニが、よく見える。岸ぎわの泡は、ふちだけ（白くならない）
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_FoamDepth", 0.02f);
            mpb.SetFloat("_Clarity", 5f);
            mpb.SetFloat("_Glint", 0.35f);
            mpb.SetFloat("_FresnelStrength", 0.4f);
            mr.SetPropertyBlock(mpb);
        }

        MeshRenderer AddWater(Mesh mesh, string name, Material mat)
        {
            var go = new GameObject(name);
            go.layer = ShakuConst.WaterLayer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            WaterView.Register(mr);   // 水が映るときだけ、深さと色の写しを作る
            return mr;
        }

        // ------------------------------------------------------------------
        // 草花・貝がら
        // ------------------------------------------------------------------
        void BuildCoastFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            // 砂丘のコウボウムギ（かたまって生える）
            int dune = Mathf.RoundToInt(1500 * dens);
            for (int i = 0; i < dune; i++)
            {
                Vector2 p = RandomInRing(2f, 74f);
                float d = CoastLayout.Inland(p.x, p.y);
                if (d < 13f || CoastLayout.ShelfMask(p.x, p.y) > 0.1f || CoastLayout.MudMask(p.x, p.y) > 0.1f) continue;
                float patch = Mathf.PerlinNoise(p.x * 0.11f + 7f, p.y * 0.11f + 2f);
                if (patch < 0.42f + 0.25f * ShakuMath.SmoothStep(30f, 14f, d)) continue;
                if (!IsLand(p, 0.3f) || InsideOccupied(p) || CoastLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                if (Vector2.Distance(p, CoastLayout.Spawn) < 2.5f) continue;
                Place(R01() < 0.5f ? "Coast_Grass_A" : "Coast_Grass_B", fol, CG(p), GroundRotation(p, R(0, 360), 0.4f, 5f), R(0.7f, 1.2f), false, false, 70f);
            }
            // 砂丘のむこうの草の原
            int grass = Mathf.RoundToInt(1400 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = RandomInRing(30f, 76f);
                if (CoastLayout.Inland(p.x, p.y) < 40f || !IsLand(p, 0.3f) || InsideOccupied(p) || CoastLayout.TrailMask(p.x, p.y) > 0.35f) continue;
                if (Vector2.Distance(p, CoastLayout.Spawn) < 2.5f) continue;
                Place(Pick(Grass), fol, CG(p), GroundRotation(p, R(0, 360), 0.5f, 5f), R(0.55f, 1.1f), false, false, 55f);
            }
            // ハマヒルガオ（砂丘の花のまわりに、たくさん）
            for (int i = 0; i < 90; i++)
            {
                Vector2 p = i < 50 ? RandomInCircle(CoastLayout.DuneFlowers, 10f) : RandomInRing(10f, 60f);
                float d = CoastLayout.Inland(p.x, p.y);
                if (d < 10f || !IsLand(p, 0.4f) || InsideOccupied(p) || CoastLayout.TrailMask(p.x, p.y) > 0.3f || CoastLayout.MudMask(p.x, p.y) > 0.1f) continue;
                Place("Coast_Hirugao", flw, CG(p) + Vector3.down * 0.03f, GroundRotation(p, R(0, 360), 0.9f, 3f), R(0.8f, 1.2f), false, true, 120f);
            }
            // 草の原の花
            for (int i = 0; i < 30; i++)
            {
                Vector2 p = RandomInRing(40f, 64f);
                if (CoastLayout.Inland(p.x, p.y) < 42f || !IsLand(p, 0.5f) || InsideOccupied(p) || CoastLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                Place(R01() < 0.6f ? "Daisy" : "Dandelion", flw, CG(p) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(0.7f, 1.05f), false, true, 120f);
            }
            // 潮だまりの海藻
            foreach (var (c, r) in CoastLayout.Pools)
            {
                int n = Mathf.RoundToInt(r * 2.2f);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = RandomInCircle(c, r * 0.85f);
                    Place("Coast_Kelp", fol, CG(p) + Vector3.down * 0.05f, Quaternion.Euler(0f, R(0, 360), 0f), R(0.45f, 0.8f), false, false, 80f);
                }
            }
            // 貝がらとシーグラス（押すと動く。波打ちぎわから、波のとどく線まで）
            int shells = Mathf.RoundToInt(160 * dens);
            for (int i = 0; i < shells; i++)
            {
                float x = R(-62f, 62f);
                Vector2 p = new Vector2(x, CoastLayout.ShoreZ(x) + R(0.4f, 9f));
                if (p.magnitude > 64f || !IsLand(p, 0.04f) || InsideOccupied(p) || CoastLayout.ShelfMask(p.x, p.y) > 0.2f) continue;
                string mesh = Shells[RI(0, Shells.Length)];
                float s = mesh == "Coast_SeaGlass" ? R(0.5f, 0.9f) : R(0.45f, 0.9f);
                PlaceLoose(mesh, assets.prop, CG(p), Quaternion.Euler(R(-8f, 8f), R(0, 360), R(-8f, 8f)), s, LooseProps.Shape.Pebble, false, 45f);
            }
            // 小石（砂丘のすそ）
            int pebbles = Mathf.RoundToInt(120 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(4f, 64f);
                float d = CoastLayout.Inland(p.x, p.y);
                if (d < 2f || d > 30f || !IsLand(p, 0.1f) || CoastLayout.MudMask(p.x, p.y) > 0.3f) continue;
                PlaceLoose(Pick(RiverStones), assets.prop, CG(p), Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), R(0.08f, 0.22f), LooseProps.Shape.Pebble, false, 45f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceCoastDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = CoastLayout.Spawn;
            AddDewFromAbove(s + new Vector2(3.5f, -3f));
            // 流木・巻き貝・岬の岩・サンダル・浮き玉の上
            foreach (var top in _coastTops) AddDewFromAbove(new Vector2(top.x, top.z));
            // 巻き貝の口の中（入ってみるとごほうび）
            if (_conchMouth != Vector3.zero) AddDew(_conchMouth);
            // 磯の岩の上
            for (int i = 0; i < Mathf.Min(3, _reefTops.Count); i++) AddDewFromAbove(new Vector2(_reefTops[i].x, _reefTops[i].z));
            // 潮だまりのふち
            foreach (var (c, r) in CoastLayout.Pools)
            {
                Vector2 rim = c + new Vector2(-1f, 0.6f).normalized * (r + 0.9f);
                AddDewFromAbove(rim);
            }
            // 砂丘の花・干潟・波打ちぎわ
            AddDewFromAbove(CoastLayout.DuneFlowers + new Vector2(2f, 3f));
            AddDewFromAbove(CoastLayout.DuneFlowers + new Vector2(-5f, -2f));
            AddDewFromAbove(CoastLayout.Mudflat + new Vector2(5f, 3f));
            AddDewFromAbove(new Vector2(-52f, -9f));
            AddDewFromAbove(new Vector2(CoastLayout.Surf.x - 6f, CoastLayout.ShoreZ(CoastLayout.Surf.x - 6f) + 1.4f));
            AddDewFromAbove(new Vector2(CoastLayout.Surf.x + 9f, CoastLayout.ShoreZ(CoastLayout.Surf.x + 9f) + 1.6f));
            // のこりは、陸の上のどこかへ（南は海なので、方角ごとに分けずに、陸だけからえらぶ）
            int guard = 0;
            while (DewdropPoints.Count < Area.DropCount && guard++ < 4000)
            {
                Vector2 p = RandomInRing(8f, 60f);
                if (!IsLand(p, 0.3f) || CoastLayout.PoolAt(p.x, p.y, 1f) >= 0) continue;
                AddDewFromAbove(p);
            }
        }

        void PlaceCoastCreatures()
        {
            // ヤドカリ：打ち上げられた海藻と貝がらのまわり
            AddMob("yadokari", CG(new Vector2(-6f, CoastLayout.ShoreZ(-6f) + 4.5f)), 2, 3f);
            AddMob("yadokari", CG(CoastLayout.Conch + new Vector2(6f, -2f)), 2, 2.5f);
            AddMob("yadokari", CG(new Vector2(18f, CoastLayout.ShoreZ(18f) + 3.5f)), 1, 2.5f);
            // シオマネキ：干潟の巣穴ごとに 1 ぴき
            foreach (var b in _burrows) AddMob("shiomaneki", b, 1, 2.2f).showcase = true;
            // ヒトデ・ウニ：潮だまりの中
            AddPoolMob("hitode", 0, new Vector2(-1f, -1f), 2);
            AddPoolMob("hitode", 1, new Vector2(0.5f, 0.6f), 1);
            AddPoolMob("hitode", 2, Vector2.zero, 1);
            AddPoolMob("uni", 0, new Vector2(1.3f, 0.9f), 1);
            AddPoolMob("uni", 1, new Vector2(-0.8f, -0.5f), 2);
            // フナムシ：磯の岩場
            AddMob("funamushi", CG(CoastLayout.Reef + new Vector2(-2f, -4f)), 3, 3f);
            AddMob("funamushi", CG(new Vector2(45f, -12f)), 3, 3f);
            // カモメ：流木・岩場・波打ちぎわに下りる
            {
                var g = AddMob("kamome", CG(CoastLayout.Driftwood + new Vector2(-4f, -6f)), 1, 3f);
                Vector2[] spots = { CoastLayout.Driftwood + new Vector2(-4f, -6f), CoastLayout.Reef + new Vector2(-6f, -2f),
                    new Vector2(-18f, CoastLayout.ShoreZ(-18f) + 3f), new Vector2(10f, CoastLayout.ShoreZ(10f) + 2.5f), CoastLayout.DuneFlowers + new Vector2(8f, -8f) };
                foreach (var p in spots) g.path.Add(CG(p));
            }
            // 砂丘のチョウとバッタ
            AddMob("butterfly", CG(CoastLayout.DuneFlowers), 2, 9f, 3.2f);
            AddMob("grasshopper", CG(new Vector2(-12f, 30f)), 2, 7f);
            AddMob("grasshopper", CG(new Vector2(24f, 34f)), 2, 7f);
        }

        /// <summary>潮だまりの中のいきもの（水の底）。</summary>
        void AddPoolMob(string species, int pool, Vector2 offset, int count)
        {
            var (c, r) = CoastLayout.Pools[pool];
            Vector2 p = c + offset;
            AddMob(species, CG(p), count, r * 0.6f).showcase = true;
        }

        // ------------------------------------------------------------------
        // 地図
        // ------------------------------------------------------------------
        void DrawCoastMap(Color32[] px, int size, bool generated)
        {
            MapTrails(px, size, CoastLayout.Trails, new Color32(222, 204, 160, 255));
            // 波打ちぎわの白い線
            for (float x = -80f; x <= 80f; x += 0.6f) MapDot(px, size, new Vector3(x, 0f, CoastLayout.ShoreZ(x) - 0.6f), 0.7f, new Color32(236, 246, 250, 255));
            // 潮だまり
            foreach (var (c, r) in CoastLayout.Pools)
                for (int k = 0; k < 20; k++)
                {
                    float a = k / 20f * Mathf.PI * 2f;
                    MapDot(px, size, new Vector3(c.x + Mathf.Cos(a) * (r + 0.4f), 0f, c.y + Mathf.Sin(a) * (r + 0.4f)), 0.7f, new Color32(80, 80, 78, 255));
                }
            // 大きな巻き貝・流木・岬の岩
            MapDot(px, size, new Vector3(CoastLayout.Conch.x, 0f, CoastLayout.Conch.y), 3f, new Color32(240, 214, 176, 255));
            Quaternion dr = Quaternion.Euler(0f, 28f, 0f);
            for (int i = -7; i <= 7; i++)
            {
                Vector3 p = new Vector3(CoastLayout.Driftwood.x, 0f, CoastLayout.Driftwood.y) + dr * new Vector3(i * 0.6f, 0f, 0f);
                MapDot(px, size, p, 1.3f, new Color32(226, 220, 204, 255));
            }
            MapDot(px, size, new Vector3(CoastLayout.Cape.x, 0f, CoastLayout.Cape.y), 3.2f, new Color32(96, 94, 90, 255));
            foreach (var b in BurrowSpots) MapDot(px, size, new Vector3(b.x, 0f, b.y), 1f, new Color32(60, 50, 40, 255));
        }
    }
}
