using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 川辺エリアの組み立て：小川・滝・中州と葉っぱの渡し舟・とびいし・倒れ枝の橋・睡蓮のよどみ。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _riverStones = new List<Vector3>();
        readonly List<Vector3> _stepStones = new List<Vector3>();
        readonly List<Vector3> _poolPads = new List<Vector3>();
        readonly List<Vector3> _fallRocks = new List<Vector3>();
        Vector3 _bridgeA, _bridgeB;

        public RiverFerry Ferry { get; private set; }
        public IReadOnlyList<Vector3> StepStones => _stepStones;
        public Vector3 BridgeA => _bridgeA;
        public Vector3 BridgeB => _bridgeB;

        /// <summary>岸の水ぎわの X（川の外側から内側へさがす）。</summary>
        public static float RiverBankEdgeX(float z, float side)
        {
            float cx = RiverLayout.CenterX(z);
            float wl = RiverLayout.WaterLevel(z);
            float w = RiverLayout.HalfWidth(z);
            for (float d = w + 9f; d > 0f; d -= 0.2f)
            {
                float x = cx + side * d;
                if (RiverLayout.Height(x, z) < wl) return x + side * 0.2f;
            }
            return cx;
        }

        /// <summary>中州の水ぎわの X（中州の中心から外へさがす）。</summary>
        public static float IslandEdgeX(float side)
        {
            Vector2 c = RiverLayout.IslandCenter;
            float wl = RiverLayout.WaterLevel(c.y);
            for (float d = 0f; d < 12f; d += 0.2f)
            {
                float x = c.x + side * d;
                if (RiverLayout.Height(x, c.y) < wl) return x - side * 0.2f;
            }
            return c.x;
        }

        // ------------------------------------------------------------------
        // 大きなもの
        // ------------------------------------------------------------------
        void BuildRiverSolids()
        {
            BuildBackgroundTrunks(p => false);
            Material prop = assets.prop;

            // 滝の両わきの大岩と、段差に沿った岩
            float fz = RiverLayout.FallZ;
            float fcx = RiverLayout.CenterX(fz);
            float fw = RiverLayout.HalfWidth(fz);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 p = new Vector2(fcx + side * (fw + 1.2f), fz + 0.8f);
                Vector3 pos = RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.5f;
                Place(side < 0 ? "RiverStone_B" : "RiverStone_A", prop, pos, Quaternion.Euler(R(-8, 8), R(0, 360), R(-8, 8)), R(2.6f, 3.2f), true, true, 250f);
                Occupy(p, 5f);
                _fallRocks.Add(pos);
            }
            for (float x = -60f; x <= 60f; x += R(6f, 10f))
            {
                if (Mathf.Abs(x - fcx) < fw + 6f) continue;
                Vector2 p = new Vector2(x, fz - 1.5f + R(-1f, 1f));
                if (p.magnitude > 64f) continue;
                float s = R(1.6f, 3.4f);
                Place(Pick(Rocks), prop, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f * s, Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10)), s, true, true, 250f);
                Occupy(p, 1.3f * s);
            }

            // 倒れ枝の橋
            {
                float bz = RiverLayout.BridgeZ;
                float west = RiverBankEdgeX(bz, -1f) - 4f;
                float east = RiverBankEdgeX(bz, 1f) + 4f;
                Vector3 A = RiverLayout.Ground(west, bz + 0.6f) + Vector3.up * 0.2f;
                Vector3 B = RiverLayout.Ground(east, bz - 0.6f) + Vector3.up * 0.2f;
                float len = Vector3.Distance(A, B);
                float sc = len / 13.2f;
                Quaternion rot = Quaternion.FromToRotation(Vector3.right, (B - A).normalized);
                Vector3 mid = (A + B) * 0.5f;
                Place("Twig_A", prop, mid - rot * Vector3.up * (0.35f * sc), rot, sc, true, true, 250f);
                _bridgeA = A;
                _bridgeB = B;
                Occupy(new Vector2(west, bz), 3.5f);
                Occupy(new Vector2(east, bz), 3.5f);
            }

            // とびいし
            {
                float sz = RiverLayout.StonesZ;
                float x0 = RiverBankEdgeX(sz, -1f) - 0.6f;
                float x1 = RiverBankEdgeX(sz, 1f) + 0.6f;
                Mesh stone = assets.Get("RiverStone_C");
                int n = Mathf.CeilToInt((x1 - x0) / 2.5f) + 1;
                float wl = RiverLayout.WaterLevel(sz);
                for (int i = 0; i < n; i++)
                {
                    float x = Mathf.Lerp(x0, x1, i / (float)(n - 1));
                    float z = sz + Mathf.Sin(i * 1.7f) * 0.6f;
                    float sc = R(1.0f, 1.15f);
                    float top = stone != null ? stone.bounds.max.y * sc : 0.5f;
                    var pos = new Vector3(x, wl + 0.3f - top, z);
                    Place("RiverStone_C", prop, pos, Quaternion.Euler(0f, R(-25f, 25f) + 90f, 0f), sc, true, true, 200f);
                    _stepStones.Add(new Vector3(x, wl + 0.3f, z));
                }
                Occupy(new Vector2(x0, sz), 3f);
                Occupy(new Vector2(x1, sz), 3f);
            }

            BuildFerry();

            // 睡蓮のよどみ：岸から葉っぱの道
            {
                float pz = RiverLayout.PoolZ;
                float wl = RiverLayout.WaterLevel(pz);
                float x = RiverBankEdgeX(pz, -1f) + 2.2f;
                float cx = RiverLayout.CenterX(pz);
                int i = 0;
                while (x < cx + 4f && i < 8)
                {
                    float z = pz + (i % 2 == 0 ? 1.8f : -1.8f);
                    var pos = new Vector3(x, wl + 0.14f + i * 0.012f, z);
                    Place("LilyPad", prop, pos, Quaternion.Euler(0f, R(0, 360), 0f), R(1.3f, 1.5f), true, false, 150f);
                    _poolPads.Add(pos);
                    x += 5.2f;
                    i++;
                }
                for (int k = 0; k < 7; k++)
                {
                    Vector2 p = new Vector2(cx + R(-9f, 9f), pz + R(-10f, 10f));
                    if (RiverLayout.Height(p.x, p.y) > wl - 0.4f) continue;
                    Place("LilyPad", prop, new Vector3(p.x, wl + 0.2f + k * 0.012f, p.y), Quaternion.Euler(0f, R(0, 360), 0f), R(0.7f, 1.1f), true, false, 150f);
                }
                Place("WaterLily", assets.flowers, new Vector3(cx + 2.5f, wl + 0.12f, pz - 4f), Quaternion.Euler(0f, 20f, 0f), 1.1f, false);
                Place("WaterLily", assets.flowers, new Vector3(cx - 4f, wl + 0.12f, pz + 6f), Quaternion.Euler(0f, 70f, 0f), 0.9f, false);
                Place("WaterLily", assets.flowers, new Vector3(cx + 6f, wl + 0.12f, pz + 3f), Quaternion.Euler(0f, 140f, 0f), 1.0f, false);
            }
        }

        void BuildFerry()
        {
            Mesh leaf = assets.Get("Leaf_Oak_Green");
            if (leaf == null) return;
            float iz = RiverLayout.IslandZ;
            float wl = RiverLayout.WaterLevel(iz);
            float bankX = RiverBankEdgeX(iz, 1f);
            float islX = IslandEdgeX(1f);
            const float sc = 1.55f;
            // 反りを浅くして、乗り降りしやすい舟にする
            Vector3 scale = new Vector3(sc, sc * 0.55f, sc);
            Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
            float halfLen = leaf.bounds.extents.z * sc;
            Vector3 centerOffset = rot * Vector3.Scale(leaf.bounds.center, scale);
            float bankCenter = bankX - halfLen + 1.5f;
            float islCenter = islX + halfLen - 1.5f;
            if (bankCenter - islCenter < 2.5f)
            {
                float m = (bankCenter + islCenter) * 0.5f;
                bankCenter = m + 1.25f;
                islCenter = m - 1.25f;
            }
            float y = wl + 0.1f;
            var go = new GameObject("LeafFerry");
            go.transform.SetParent(_solidRoot, false);
            go.layer = ShakuConst.SurfaceLayer;
            go.AddComponent<MeshFilter>().sharedMesh = leaf;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.prop;
            go.transform.localScale = scale;
            go.AddComponent<MeshCollider>().sharedMesh = leaf;
            go.AddComponent<MovingPlatform>();
            var ferry = go.AddComponent<RiverFerry>();
            ferry.baseRotation = rot;
            ferry.dockA = new Vector3(bankCenter, y, iz) - centerOffset;
            ferry.dockB = new Vector3(islCenter, y, iz) - centerOffset;
            ferry.SetClock(0f);
            Ferry = ferry;
            Occupy(new Vector2(bankX + 2f, iz), 3f);
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------
        void ScatterRiverProps()
        {
            Material prop = assets.prop;
            // 川石（岸と浅瀬）
            for (int i = 0; i < 70; i++)
            {
                float z = R(-62f, 62f);
                float side = R01() < 0.5f ? -1f : 1f;
                float d = RiverLayout.HalfWidth(z) + R(-2f, 9f);
                Vector2 p = new Vector2(RiverLayout.CenterX(z) + side * d, z);
                if (p.magnitude > 63f || !IsFree(p, 2f)) continue;
                if (Mathf.Abs(z - RiverLayout.StonesZ) < 4f || Mathf.Abs(z - RiverLayout.BridgeZ) < 4f || Mathf.Abs(z - RiverLayout.IslandZ) < 6f && side > 0f) continue;
                float s = R(0.5f, 1.9f);
                Vector3 pos = RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.2f * s;
                Place(Pick(RiverStones), prop, pos, Quaternion.Euler(R(-8f, 8f), R(0, 360), R(-8f, 8f)), s, true, true, 160f);
                Occupy(p, 1.6f * s);
                if (s > 1.2f && !RiverLayout.IsUnderwater(pos + Vector3.up * 0.8f * s)) _riverStones.Add(pos);
            }
            // 中州の小石
            Vector2 ic = RiverLayout.IslandCenter;
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = ic + new Vector2(R(-2.5f, 2.5f), R(-5f, 5f));
                if (RiverLayout.IslandRadius01(p.x, p.y) > 0.85f || !IsFree(p, 1f)) continue;
                float s = R(0.35f, 0.7f);
                Place(Pick(RiverStones), prop, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, Quaternion.Euler(0f, R(0, 360), 0f), s, true);
                Occupy(p, 1.4f * s);
            }
            // 外周の大岩
            for (int i = 0; i < 34; i++)
            {
                float a = i / 34f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 74f);
                if (Vector2.Distance(p, RiverLayout.Gate) < 8f) continue;
                if (RiverLayout.InChannel(p.x, p.y, 3f)) continue;
                float s = R(2.6f, 5.2f);
                Vector3 pos = RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.35f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }
            // 岸の落ち葉
            for (int i = 0; i < 50; i++)
            {
                Vector2 p = RandomInRing(4f, 62f);
                if (!IsLand(p, 0.3f) || InsideOccupied(p) || RiverLayout.InChannel(p.x, p.y, 2f)) continue;
                float s = R(0.6f, 1.1f);
                Place(Pick(BigLeaves), prop, RiverLayout.Ground(p.x, p.y) + Vector3.up * 0.04f, GroundRotation(p, R(0, 360), 1f, 3f), s, true, true, 120f);
                if (_bigLeaves.Count < 20) _bigLeaves.Add(RiverLayout.Ground(p.x, p.y));
            }
            // 小枝
            for (int i = 0; i < 10; i++)
            {
                Vector2 p = RandomInRing(10f, 60f);
                if (!IsFree(p, 3f) || !IsLand(p, 0.4f) || RiverLayout.InChannel(p.x, p.y, 3f)) continue;
                Place(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.8f, 2f), R(0.7f, 1.1f), true, true, 150f);
            }
            // キノコ（森に近い外側）
            for (int i = 0; i < 14; i++)
            {
                Vector2 p = RandomInRing(40f, 62f);
                if (!IsFree(p, 1.5f) || !IsLand(p, 0.5f)) continue;
                float s = R(0.5f, 1.0f);
                Place(R01() < 0.6f ? "Mushroom_Cluster" : "Mushroom_Brown", prop, RiverLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                Occupy(p, 1.1f * s);
            }
            // アリの行列（森へもどるトンネルの近く）
            AddAntLine(new Vector2(-47f, -19f), new Vector2(-40f, -9f), 9);
        }

        // ------------------------------------------------------------------
        // 水
        // ------------------------------------------------------------------
        void BuildRiverWater()
        {
            float fz = RiverLayout.FallZ;
            RiverStrip(fz + 0.4f, 100f, "RiverUpper");
            RiverStrip(-100f, fz - 0.3f, "RiverLower");
            BuildWaterfall();
        }

        void RiverStrip(float z0, float z1, string name)
        {
            const int cols = 8;
            const float step = 1.5f;
            int rows = Mathf.CeilToInt((z1 - z0) / step);
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float flow = 0f;
            for (int r = 0; r <= rows; r++)
            {
                float z = Mathf.Lerp(z1, z0, r / (float)rows);   // 上流 → 下流
                float cx = RiverLayout.CenterX(z);
                float w = RiverLayout.HalfWidth(z) + 5f;
                float y = RiverLayout.WaterLevel(z);
                if (r > 0)
                {
                    float zPrev = Mathf.Lerp(z1, z0, (r - 1) / (float)rows);
                    flow += Vector2.Distance(new Vector2(cx, z), new Vector2(RiverLayout.CenterX(zPrev), zPrev));
                }
                for (int c = 0; c <= cols; c++)
                {
                    float u = c / (float)cols;
                    v.Add(new Vector3(cx + Mathf.Lerp(-w, w, u), y, z));
                    uv.Add(new Vector2(u, flow / 8f));
                    n.Add(Vector3.up);
                }
            }
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int a = r * (cols + 1) + c;
                int b = a + 1;
                int d = a + cols + 1;
                int e = d + 1;
                t.Add(a); t.Add(b); t.Add(e);
                t.Add(a); t.Add(e); t.Add(d);
            }
            var mesh = Own(new Mesh { name = name });
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.layer = ShakuConst.WaterLayer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = assets.river != null ? assets.river : assets.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void BuildWaterfall()
        {
            float fz = RiverLayout.FallZ;
            float cx = RiverLayout.CenterX(fz);
            float w = RiverLayout.HalfWidth(fz) - 0.3f;
            float top = RiverLayout.UpperLevel + 0.02f;
            float bottom = RiverLayout.WaterLevel(fz - 1f) - 0.15f;
            const int cols = 18, rows = 12;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            for (int r = 0; r <= rows; r++)
            {
                float k = r / (float)rows;
                float z = fz + 0.5f - 3.0f * Mathf.Pow(k, 0.75f);
                float y = Mathf.Lerp(top, bottom, Mathf.Pow(k, 1.7f));
                for (int c = 0; c <= cols; c++)
                {
                    float u = c / (float)cols;
                    float x = cx + Mathf.Lerp(-w, w, u) + Mathf.Sin(u * 17f + k * 3f) * 0.15f;
                    v.Add(new Vector3(x, y, z + Mathf.Sin(u * 11f) * 0.2f * k));
                    uv.Add(new Vector2(u * 4f, k));
                    col.Add(new Color(1f, 1f, 1f, 1f - k * 0.2f));
                }
            }
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int a = r * (cols + 1) + c;
                int b = a + 1;
                int d = a + cols + 1;
                int e = d + 1;
                t.Add(a); t.Add(e); t.Add(b);
                t.Add(a); t.Add(d); t.Add(e);
            }
            var mesh = Own(new Mesh { name = "Waterfall" });
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Waterfall");
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
        void BuildRiverFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            int grass = Mathf.RoundToInt(3600 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = RandomInRing(2f, 72f);
                float d = RiverLayout.DistToRiver(p.x, p.y);
                float w = RiverLayout.HalfWidth(p.y);
                float patch = Mathf.PerlinNoise(p.x * 0.07f + 33f, p.y * 0.07f);
                if (patch < 0.45f && d > w + 10f) continue;
                if (d < w + 3.5f || !IsLand(p, 0.3f) || InsideOccupied(p)) continue;
                if (RiverLayout.TrailMask(p.x, p.y) > 0.35f) continue;
                if (Vector2.Distance(p, RiverLayout.Spawn) < 2.5f) continue;
                Place(Pick(Grass), fol, RiverLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.5f, 5f), R(0.55f, 1.15f), false, false, 55f);
            }
            // 水ぎわの葦・カキツバタ・スギナ
            for (int i = 0; i < 120; i++)
            {
                float z = R(-64f, 64f);
                float side = R01() < 0.5f ? -1f : 1f;
                float d = RiverLayout.HalfWidth(z) + R(0.5f, 4.5f);
                Vector2 p = new Vector2(RiverLayout.CenterX(z) + side * d, z);
                if (p.magnitude > 64f || InsideOccupied(p) || !IsLand(p, 0.0f)) continue;
                if (Mathf.Abs(z - RiverLayout.StonesZ) < 3f || Mathf.Abs(z - RiverLayout.BridgeZ) < 3f) continue;
                int k = RI(0, 10);
                Vector3 g = RiverLayout.Ground(p.x, p.y);
                if (k < 4) Place("Reed", flw, g + Vector3.down * 0.2f, Quaternion.Euler(0, R(0, 360), 0), R(0.5f, 0.95f), false, true, 120f);
                else if (k < 7) Place("Iris", flw, g + Vector3.down * 0.1f, Quaternion.Euler(0, R(0, 360), 0), R(0.6f, 1.0f), false, true, 120f);
                else Place("Horsetail", flw, g + Vector3.down * 0.1f, Quaternion.Euler(0, R(0, 360), 0), R(0.7f, 1.1f), false, true, 100f);
            }
            int clover = Mathf.RoundToInt(700 * dens);
            for (int i = 0; i < clover; i++)
            {
                Vector2 p = RandomInRing(2f, 66f);
                if (Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 40f) < 0.6f) continue;
                if (!IsLand(p, 0.5f) || InsideOccupied(p) || RiverLayout.InChannel(p.x, p.y, 4f)) continue;
                Place(R01() < 0.5f ? "Clover_A" : "Clover_B", fol, RiverLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.7f, 4f), R(0.7f, 1.2f), false, false, 50f);
            }
            int moss = Mathf.RoundToInt(900 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = R01() < 0.35f ? new Vector2(R(-60f, 60f), RiverLayout.FallZ + R(-3f, 2f)) : RandomInRing(1f, 70f);
                if (Mathf.PerlinNoise(p.x * 0.07f + 500f, p.y * 0.07f + 80f) < 0.5f && Mathf.Abs(p.y - RiverLayout.FallZ) > 4f) continue;
                if (!IsLand(p, 0.3f) || RiverLayout.InChannel(p.x, p.y, 2f)) continue;
                Place("Moss", fol, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 1f, 3f), R(0.5f, 1.4f), false, false, 60f);
            }
            int pebbles = Mathf.RoundToInt(900 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                float z = R(-66f, 66f);
                float side = R01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverLayout.CenterX(z) + side * (RiverLayout.HalfWidth(z) + R(-2.5f, 7f)), z);
                if (p.magnitude > 70f) continue;
                Place(Pick(RiverStones), assets.prop, RiverLayout.Ground(p.x, p.y), Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), R(0.08f, 0.3f), false, false, 45f);
            }
            int litter = Mathf.RoundToInt(700 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = RandomInRing(8f, 72f);
                if (!IsLand(p, 0.4f) || RiverLayout.InChannel(p.x, p.y, 5f)) continue;
                if (Mathf.PerlinNoise(p.x * 0.05f + 50f, p.y * 0.05f + 20f) < 0.5f) continue;
                Place(Pick(BigLeaves), assets.prop, RiverLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, R(0, 360), 1f, 6f), R(0.07f, 0.16f), false, false, 40f);
            }
            void Flowers(string meshName, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInRing(6f, 62f);
                    if (!IsLand(p, 0.5f) || InsideOccupied(p) || RiverLayout.InChannel(p.x, p.y, 4f) || RiverLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                    Place(meshName, flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Flowers("Daisy", 18, 0.7f, 1.1f);
            Flowers("Dandelion", 12, 0.7f, 1.0f);
            Flowers("DandelionPuff", 5, 0.8f, 1.1f);
            Flowers("Bellflower", 8, 0.8f, 1.1f);
            Flowers("Sprout", 16, 0.7f, 1.2f);
            for (int i = 0; i < 46; i++)
            {
                Vector2 p = RandomInRing(54f, 76f);
                if (!IsLand(p, 0.4f) || InsideOccupied(p) || RiverLayout.InChannel(p.x, p.y, 4f)) continue;
                if (Vector2.Distance(p, RiverLayout.Gate) < 6f) continue;
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.3f), false, true, 200f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceRiverDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = RiverLayout.Spawn;
            AddDewFromAbove(s + new Vector2(3.5f, 2.5f));
            AddDewFromAbove(s + new Vector2(6f, -3.5f));
            // とびいし
            if (_stepStones.Count > 5)
            {
                AddDewFromAbove(new Vector2(_stepStones[2].x, _stepStones[2].z));
                AddDewFromAbove(new Vector2(_stepStones[_stepStones.Count - 3].x, _stepStones[_stepStones.Count - 3].z));
            }
            // 中州（渡し舟でしか行けない）
            Vector2 ic = RiverLayout.IslandCenter;
            AddDewFromAbove(ic + new Vector2(0f, 3.8f));
            AddDewFromAbove(ic + new Vector2(-0.5f, -0.5f));
            AddDewFromAbove(ic + new Vector2(0.3f, -4.5f));
            // 倒れ枝の橋の上
            AddDewFromAbove(new Vector2(Vector3.Lerp(_bridgeA, _bridgeB, 0.5f).x, Vector3.Lerp(_bridgeA, _bridgeB, 0.5f).z));
            AddDewFromAbove(new Vector2(Vector3.Lerp(_bridgeA, _bridgeB, 0.22f).x, Vector3.Lerp(_bridgeA, _bridgeB, 0.22f).z));
            // 滝のふちの大岩
            foreach (var r in _fallRocks) AddDewFromAbove(new Vector2(r.x, r.z));
            // 滝の上の台地
            AddDewFromAbove(new Vector2(RiverLayout.CenterX(44f) - RiverLayout.HalfWidth(44f) - 6f, 44f));
            AddDewFromAbove(new Vector2(RiverLayout.CenterX(52f) + RiverLayout.HalfWidth(52f) + 5f, 52f));
            AddDewFromAbove(new Vector2(-30f, 46f));
            // 睡蓮の葉
            if (_poolPads.Count > 3)
            {
                AddDewFromAbove(new Vector2(_poolPads[1].x, _poolPads[1].z));
                AddDewFromAbove(new Vector2(_poolPads[_poolPads.Count - 1].x, _poolPads[_poolPads.Count - 1].z));
            }
            // 大きな川石の上
            for (int i = 0; i < Mathf.Min(4, _riverStones.Count); i++)
                AddDewFromAbove(new Vector2(_riverStones[i].x, _riverStones[i].z));
            FillDewdrops(8f, 60f);
        }

        void PlaceRiverCreatures()
        {
            float pz = RiverLayout.PoolZ;
            float pcx = RiverLayout.CenterX(pz);
            float pwl = RiverLayout.WaterLevel(pz);
            AddMob("waterstrider", new Vector3(pcx - 2f, pwl, pz + 4f), 4, 6f);
            AddMob("waterstrider", new Vector3(RiverLayout.CenterX(45f), RiverLayout.UpperLevel, 45f), 2, 5f);
            AddMob("dragonfly", new Vector3(RiverLayout.CenterX(RiverLayout.BridgeZ), pwl, RiverLayout.BridgeZ), 1, 6f, 3.5f);
            AddMob("dragonfly", new Vector3(pcx, pwl, pz), 1, 8f, 2.5f);
            AddMob("dragonfly", new Vector3(RiverLayout.CenterX(RiverLayout.FallZ - 8f), RiverLayout.WaterLevel(RiverLayout.FallZ - 8f), RiverLayout.FallZ - 8f), 1, 6f, 3f);
            // カエル：葉の上と岸
            if (_poolPads.Count > 0)
            {
                Vector3 last = _poolPads[_poolPads.Count - 1];
                if (CastDown(last + Vector3.up * 3f, 5f, out var h1)) AddMob("frog", h1.point, 1, 0.8f);
                Vector3 mid = _poolPads[_poolPads.Count / 2];
                if (CastDown(mid + Vector3.up * 3f, 5f, out var h2)) AddMob("frog", h2.point, 1, 0.8f);
            }
            AddMob("frog", RiverLayout.Ground(RiverBankEdgeX(-30f, 1f) + 2f, -30f), 1, 2f);
            AddMob("frog", RiverLayout.Ground(RiverBankEdgeX(8f, -1f) - 2f, 8f), 1, 2f);
            // サワガニ：中州と岸
            Vector2 ic = RiverLayout.IslandCenter;
            AddMob("crab", RiverLayout.Ground(ic.x, ic.y), 2, 2.5f);
            AddMob("crab", RiverLayout.Ground(RiverBankEdgeX(RiverLayout.StonesZ + 4f, -1f) - 1.5f, RiverLayout.StonesZ + 4f), 1, 2f);
            // カワニナ：とびいしと水ぎわ
            if (_stepStones.Count > 4)
            {
                AddMob("riversnail", _stepStones[1], 1, 0.6f);
                AddMob("riversnail", _stepStones[_stepStones.Count - 2], 1, 0.6f);
            }
            AddMob("riversnail", RiverLayout.Ground(RiverBankEdgeX(-8f, -1f) - 1f, -8f), 2, 2f);
            // ゲンジボタル
            AddMob("firefly", RiverLayout.Ground(RiverBankEdgeX(pz, -1f) - 3f, pz + 5f), 5, 6f, 1.3f);
            AddMob("firefly", RiverLayout.Ground(RiverBankEdgeX(RiverLayout.FallZ - 6f, 1f) + 2f, RiverLayout.FallZ - 6f), 3, 4f, 1.2f);
            // 岸のいきもの
            AddMob("butterfly", RiverLayout.Ground(-30f, 0f), 2, 9f, 3.2f);
            AddMob("butterfly", RiverLayout.Ground(30f, -20f), 1, 8f, 3.2f);
            AddMob("grasshopper", RiverLayout.Ground(-32f, -18f), 2, 8f);
            AddMob("grasshopper", RiverLayout.Ground(32f, 10f), 2, 8f);
            AddMob("ladybug", RiverLayout.Ground(-24f, 8f), 2, 4f);
            AddMob("ant", RiverLayout.Ground(26f, -36f), 3, 5f);
            {
                var g = AddMob("sparrow", RiverLayout.Ground(-28f, -6f), 2, 3f);
                Vector2[] spots = { new Vector2(-28f, -6f), new Vector2(-34f, 22f), new Vector2(30f, -8f), new Vector2(-20f, 48f) };
                foreach (var p in spots) g.path.Add(RiverLayout.Ground(p.x, p.y));
            }
            {
                var g = AddMob("crow", RiverLayout.Ground(-24f, 44f), 1, 2f);
                Vector2[] spots = { new Vector2(-24f, 44f), new Vector2(34f, -30f), new Vector2(26f, 40f) };
                foreach (var p in spots) g.path.Add(RiverLayout.Ground(p.x, p.y));
                g.path.Add(TopSurface(ic));
            }
        }

        List<Vector2> RiverShaftSpots()
        {
            return new List<Vector2>
            {
                RiverLayout.Spawn + new Vector2(3f, 1f), RiverLayout.IslandCenter, new Vector2(RiverLayout.CenterX(RiverLayout.BridgeZ), RiverLayout.BridgeZ),
                new Vector2(RiverLayout.CenterX(RiverLayout.PoolZ), RiverLayout.PoolZ), new Vector2(RiverLayout.CenterX(RiverLayout.FallZ - 4f), RiverLayout.FallZ - 4f),
                new Vector2(-30f, 40f), new Vector2(30f, -10f),
            };
        }

        void DrawRiverMap(Color32[] px, int size)
        {
            foreach (var s in _stepStones) MapDot(px, size, s, 2.2f, new Color32(200, 196, 186, 255));
            for (int i = 0; i <= 20; i++)
                MapDot(px, size, Vector3.Lerp(_bridgeA, _bridgeB, i / 20f), 1.6f, new Color32(120, 84, 52, 255));
            float fz = RiverLayout.FallZ;
            float cx = RiverLayout.CenterX(fz);
            float w = RiverLayout.HalfWidth(fz);
            for (float x = -w; x <= w; x += 0.6f) MapDot(px, size, new Vector3(cx + x, 0f, fz - 1f), 1.2f, new Color32(240, 250, 255, 255));
            foreach (var p in _poolPads) MapDot(px, size, p, 3f, new Color32(84, 160, 80, 255));
            if (Ferry != null) MapDot(px, size, (Ferry.dockA + Ferry.dockB) * 0.5f, 2.5f, new Color32(120, 190, 80, 255));
        }
    }
}
