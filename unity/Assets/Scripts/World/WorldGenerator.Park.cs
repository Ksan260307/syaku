using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 公園エリアの組み立て：すべり台（すべれる）・ブランコ（ゆれる）・シーソー（かたむく）・ジャングルジム・砂場・どかん・
    /// タイヤ・ベンチ・水飲み場と水たまり・チューリップの花だん・クヌギの木・ボール・さく・街灯。
    /// </summary>
    public partial class WorldGenerator
    {
        public SlideRide Slide { get; private set; }
        public SeesawRide Seesaw { get; private set; }
        public readonly List<SwingRide> Swings = new List<SwingRide>();
        readonly List<Vector3> _parkTops = new List<Vector3>();   // しずくを置く、遊具の上
        Vector3 _fountainTop;
        Vector3 _benchSeat, _sandTop, _bucketPos, _dokanCenter;
        static readonly string[] Tulips = { "Tulip_Red", "Tulip_Yellow", "Tulip_Pink" };

        void ClearPark()
        {
            Slide = null;
            Seesaw = null;
            Swings.Clear();
            _parkTops.Clear();
        }

        /// <summary>メッシュ 1 つの、見た目と当たり判定のある物（動く遊具の部品用）。</summary>
        GameObject MakePart(string meshName, Material mat, Transform parent, Vector3 localPos, Quaternion localRot, bool collider, bool shadows = true)
        {
            Mesh m = assets.Get(meshName);
            var go = new GameObject(meshName);
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(localPos, localRot);
            if (m == null) return go;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = DetailMeshes.ForCollision(m);
            return go;
        }

        // ------------------------------------------------------------------
        // 遊具と大きな物
        // ------------------------------------------------------------------
        void BuildParkSolids()
        {
            BuildBackgroundTrunks(p => false);
            Material prop = assets.prop;
            Material shiny = assets.propGlossy != null ? assets.propGlossy : prop;

            // クヌギの木
            Place("Park_Kunugi", assets.bark, ParkLayout.Ground(ParkLayout.Kunugi.x, ParkLayout.Kunugi.y) + Vector3.down * 0.8f,
                Quaternion.Euler(0f, 40f, 0f), 1f, true, true, 400f, asRenderer: true);
            Occupy(ParkLayout.Kunugi, 8f);

            // すべり台：すべる先は東（+X）
            {
                Vector2 s = ParkLayout.Slide;
                Vector3 g = ParkLayout.Ground(s.x, s.y);
                Quaternion rot = Quaternion.Euler(0f, -90f, 0f);
                var frame = Place("Park_SlideFrame", prop, g, rot, 1f, true, true, 300f, asRenderer: true);
                Place("Park_SlideRamp", shiny, g, rot, 1f, true, true, 300f, asRenderer: true);
                Transform slideT = frame != null ? frame.transform : null;
                if (slideT != null)
                {
                    var sled = new GameObject("SlideSled") { layer = ShakuConst.SurfaceLayer };
                    sled.transform.SetParent(_solidRoot, false);
                    var box = sled.AddComponent<BoxCollider>();
                    box.size = new Vector3(3.3f, 0.12f, 2.8f);
                    sled.AddComponent<MovingPlatform>();
                    Slide = sled.AddComponent<SlideRide>();
                    Slide.slide = slideT;
                    _parkTops.Add(g + Vector3.up * 10.2f);
                }
                for (int i = 0; i <= 6; i++) Occupy(s + new Vector2(-4f + i * 4.2f, 0f), 3.6f);
            }

            // ブランコ：わくと、ゆれる座板 2 つ
            {
                Vector2 w = ParkLayout.Swing;
                Vector3 g = ParkLayout.Ground(w.x, w.y);
                Place("Park_SwingFrame", prop, g, Quaternion.identity, 1f, true, true, 300f, asRenderer: true);
                for (int k = 0; k < 2; k++)
                {
                    float x = k == 0 ? -4.6f : 4.6f;
                    var pivot = new GameObject("SwingPivot") { layer = ShakuConst.SurfaceLayer };
                    pivot.transform.SetParent(_solidRoot, false);
                    pivot.transform.position = g + new Vector3(x, 16f, 0f);
                    MakePart("Park_SwingChain", prop, pivot.transform, new Vector3(-1.9f, 0f, 0f), Quaternion.identity, false);
                    MakePart("Park_SwingChain", prop, pivot.transform, new Vector3(1.9f, 0f, 0f), Quaternion.identity, false);
                    var seat = MakePart("Park_SwingSeat", prop, pivot.transform, new Vector3(0f, -13.6f, 0f), Quaternion.identity, false);
                    var sb = seat.AddComponent<BoxCollider>();
                    sb.center = new Vector3(0f, -0.18f, 0f);
                    sb.size = new Vector3(4.2f, 0.4f, 1.7f);
                    pivot.AddComponent<MovingPlatform>();
                    var ride = pivot.AddComponent<SwingRide>();
                    ride.phase = k * 1.3f;
                    Swings.Add(ride);
                }
                Occupy(w, 11f);
            }

            // シーソー：板は東西
            {
                Vector2 s = ParkLayout.Seesaw;
                Vector3 g = ParkLayout.Ground(s.x, s.y);
                Place("Park_SeesawBase", prop, g, Quaternion.identity, 1f, true, true, 300f, asRenderer: true);
                var plank = MakePart("Park_SeesawPlank", prop, _solidRoot, g + Vector3.up * 2.8f, Quaternion.identity, true);
                plank.AddComponent<MovingPlatform>();
                Seesaw = plank.AddComponent<SeesawRide>();
                Occupy(s, 4f);
                Occupy(s + new Vector2(-9f, 0f), 3f);
                Occupy(s + new Vector2(9f, 0f), 3f);
            }

            // ジャングルジム
            {
                Vector2 j = ParkLayout.JungleGym;
                Vector3 g = ParkLayout.Ground(j.x, j.y);
                Place("Park_JungleGym", prop, g, Quaternion.Euler(0f, 15f, 0f), 1f, true, true, 300f, asRenderer: true);
                _parkTops.Add(g + Vector3.up * 12.3f);
                Occupy(j, 9f);
            }

            // 砂場：木のわく・砂山・バケツ・スコップ
            {
                Vector2 c = ParkLayout.Sandbox;
                float edgeH = ParkLayout.Height(c.x + ParkLayout.SandboxSize.x * 0.5f + 1.5f, c.y);
                Place("Park_SandboxFrame", prop, new Vector3(c.x, edgeH, c.y), Quaternion.identity, 1f, true, true, 250f, asRenderer: true);
                Vector2 m = c + new Vector2(-4f, 1f);
                Vector3 mg = ParkLayout.Ground(m.x, m.y);
                Place("Park_SandMound", prop, mg, Quaternion.Euler(0f, R(0, 360), 0f), 1f, true, true, 250f);
                _sandTop = mg + Vector3.up * 2.9f;
                Vector2 b = c + new Vector2(6f, -3f);
                _bucketPos = ParkLayout.Ground(b.x, b.y);
                Place("Park_Bucket", shiny, _bucketPos, Quaternion.Euler(0f, 30f, 0f), 1f, true, true, 200f);
                Vector2 sh = c + new Vector2(3f, 4.5f);
                Place("Park_Shovel", shiny, ParkLayout.Ground(sh.x, sh.y) + Vector3.up * 0.15f, Quaternion.Euler(0f, 110f, 0f), 1f, true, true, 150f);
                for (int i = 0; i <= 4; i++) Occupy(c + new Vector2(-12f + i * 6f, 9f), 1.5f);
                for (int i = 0; i <= 4; i++) Occupy(c + new Vector2(-12f + i * 6f, -9f), 1.5f);
                Occupy(m, 7f);
                Occupy(b, 2f);
            }

            // どかん（コンクリートの土管）：東西にくぐれる
            {
                Vector2 d = ParkLayout.Dokan;
                Vector3 g = ParkLayout.Ground(d.x, d.y);
                _dokanCenter = g + Vector3.up * 2.2f;
                Place("Park_Dokan", prop, _dokanCenter, Quaternion.identity, 1f, true, true, 300f, asRenderer: true);
                _parkTops.Add(g + Vector3.up * 5.3f);
                for (int i = -2; i <= 2; i++) Occupy(d + new Vector2(i * 2.5f, 0f), 3.4f);
            }

            // ベンチ：西（公園のまん中）を向く
            {
                Vector2 b = ParkLayout.Bench;
                Vector3 g = ParkLayout.Ground(b.x, b.y);
                Place("Park_Bench", prop, g, Quaternion.Euler(0f, 90f, 0f), 1f, true, true, 250f, asRenderer: true);
                _benchSeat = g + Vector3.up * 4.7f;
                for (int i = -2; i <= 2; i++) Occupy(b + new Vector2(0f, i * 4f), 3f);
            }

            // 水飲み場
            {
                Vector2 f = ParkLayout.Fountain;
                Vector3 g = ParkLayout.Ground(f.x, f.y);
                // よこのじゃぐち（メッシュの -X）を、水たまりへ向ける
                Vector2 sd = ParkLayout.SpoutDir;
                Place("Park_Fountain", prop, g, Quaternion.FromToRotation(Vector3.left, new Vector3(sd.x, 0f, sd.y)), 1f, true, true, 250f, asRenderer: true);
                _fountainTop = g + Vector3.up * 7.2f;
                _parkTops.Add(_fountainTop);
                Occupy(f, 3f);
                Occupy(ParkLayout.Puddle, ParkLayout.PuddleRadius + 1f);
            }

            // 花だん（れんがのふち）
            {
                Vector2 c = ParkLayout.FlowerBed;
                float edgeH = ParkLayout.Height(c.x + ParkLayout.BedSize.x * 0.5f + 1.5f, c.y);
                Place("Park_FlowerBed", prop, new Vector3(c.x, edgeH, c.y), Quaternion.identity, 1f, true, true, 250f, asRenderer: true);
                for (int i = 0; i <= 4; i++) Occupy(c + new Vector2(-10f + i * 5f, 4.5f), 1.2f);
                for (int i = 0; i <= 4; i++) Occupy(c + new Vector2(-10f + i * 5f, -4.5f), 1.2f);
            }

            // タイヤ（半分うまった、ペンキの 3 色）
            {
                string[] tires = { "Park_Tire_Red", "Park_Tire_Yellow", "Park_Tire_Blue", "Park_Tire_Red", "Park_Tire_Yellow" };
                for (int i = 0; i < tires.Length; i++)
                {
                    float a = Mathf.Lerp(-0.8f, 0.8f, i / (tires.Length - 1f));
                    Vector2 p = ParkLayout.Tires + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * 9f;
                    Vector3 g = ParkLayout.Ground(p.x, p.y);
                    Quaternion rot = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                    Place(tires[i], prop, g + Vector3.up * 1.6f, rot, 1f, true, true, 250f);
                    _parkTops.Add(g + Vector3.up * 5.8f);
                    Occupy(p, 4.2f);
                }
            }

            // 街灯
            Place("Park_Lamp", prop, ParkLayout.Ground(ParkLayout.Lamp.x, ParkLayout.Lamp.y), Quaternion.Euler(0f, 150f, 0f), 1f, true, true, 400f, asRenderer: true);
            Occupy(ParkLayout.Lamp, 2f);

            // さく（公園のまわり。トンネルの所はあけておく）
            // クヌギの幹にかかる所は、さくを幹の手前で止めて、両がわから幹へつなぐ（さくが幹をつきぬけない）
            const float fenceHalf = 4.1f, trunkR = 3.6f;
            int firstBlocked = -1, lastBlocked = -1;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 61f;
                if (Vector2.Distance(p, ParkLayout.Gate) < 10f) continue;
                Vector2 t = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                if (ShakuMath.DistToSegment(ParkLayout.Kunugi, p - t * fenceHalf, p + t * fenceHalf) < trunkR)
                {
                    if (firstBlocked < 0) firstBlocked = i;
                    lastBlocked = i;
                    continue;
                }
                Vector3 g = ParkLayout.Ground(p.x, p.y);
                Quaternion rot = Quaternion.LookRotation(new Vector3(-p.x, 0f, -p.y), Vector3.up);
                Place("Park_Fence", prop, g + Vector3.down * 0.2f, rot, 1f, true, true, 200f);
            }
            if (firstBlocked >= 0)
            {
                for (int side = 0; side < 2; side++)
                {
                    int j = side == 0 ? firstBlocked - 1 : lastBlocked + 1;
                    float a = j / 48f * Mathf.PI * 2f;
                    Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 61f;
                    Vector2 t = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                    Vector2 end = c + t * (side == 0 ? fenceHalf : -fenceHalf);   // 幹がわのはし
                    Vector2 d = (ParkLayout.Kunugi - end).normalized;
                    Vector2 mid = end + d * fenceHalf;   // 先は、幹の中にかくれる
                    Vector3 f = new Vector3(-d.y, 0f, d.x);
                    if (Vector3.Dot(f, new Vector3(-mid.x, 0f, -mid.y)) < 0f) f = -f;
                    Place("Park_Fence", prop, ParkLayout.Ground(mid.x, mid.y) + Vector3.down * 0.2f, Quaternion.LookRotation(f, Vector3.up), 1f, true, true, 200f);
                }
            }

            // ボール（押すと転がる）
            {
                Vector2 b = new Vector2(10f, -18f);
                Vector3 g = ParkLayout.Ground(b.x, b.y);
                var ball = Place("Park_Ball", shiny, g + Vector3.up * 2.4f, Quaternion.Euler(R(0, 360), R(0, 360), 0f), 1f, true, true, 200f, asRenderer: true);
                if (ball != null) RollingProp.Make(ball, assets.Get("Park_Ball"), 1.4f, 2.4f, Area, 0.02f);   // ゴムのボールは中が空っぽ
                Occupy(b, 3f);
            }
        }

        // ------------------------------------------------------------------
        // 小物（コライダーあり）
        // ------------------------------------------------------------------
        void ScatterParkProps()
        {
            Material prop = assets.prop;
            Occupy(ParkLayout.Gate, 4.5f);
            Occupy(ParkLayout.Spawn, 3f);
            // クヌギの下：落ち葉・どんぐり・小枝
            Vector2 k = ParkLayout.Kunugi;
            for (int i = 0; i < 24; i++)
            {
                Vector2 p = RandomInCircle(k, 16f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p)) continue;
                float s = R(0.6f, 1.1f);
                PlaceLoose(R01() < 0.6f ? "Leaf_Oak_Brown" : "Leaf_Oak_Orange", prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.04f, GroundRotation(p, R(0, 360), 1f, 3f), s, LooseProps.Shape.BigLeaf, true, 120f);
            }
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = RandomInCircle(k, 13f);
                if (!IsFree(p, 1f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.2f);
                bool lying = R01() < 0.6f;
                Quaternion rot = lying ? Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(0f, 0f, 88f) : GroundRotation(p, R(0, 360), 0.5f, 8f);
                Vector3 pos = ParkLayout.Ground(p.x, p.y) + (lying ? Vector3.up * 0.42f * s : Vector3.down * 0.03f);
                Occupy(p, 0.7f * s);
                if (p.magnitude > ParkFenceRadius - 2.2f) continue;   // さくにめりこむ所には置かない（乱数の使い方は変えない）
                var acorn = Place("Acorn", assets.propGlossy, pos, rot, s, true, true, 150f, true);
                if (acorn != null) RollingProp.Make(acorn, assets.Get("Acorn"), s, 0.47f * s, Area);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector2 p = RandomInCircle(k, 14f);
                if (!IsFree(p, 3f) || !IsLand(p)) continue;
                Place(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.8f, 2f), R(0.6f, 0.9f), true, true, 150f);
                Occupy(p, 3f);
            }
            // 植えこみの石（外がわ）
            for (int i = 0; i < 22; i++)
            {
                float a = i / 22f * Mathf.PI * 2f + R(-0.08f, 0.08f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 72f);
                if (Vector2.Distance(p, ParkLayout.Gate) < 9f) continue;
                float s = R(1.6f, 3.2f);
                Place(Pick(Rocks), prop, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.3f * s, Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10)), s, true, true, 260f);
            }
            // アリの行列（砂場からベンチの下へ）
            AddAntLine(new Vector2(14f, -30f), new Vector2(32f, -4f), 10);
        }

        // ------------------------------------------------------------------
        // 水たまり（水飲み場のそば）
        // ------------------------------------------------------------------
        void BuildParkWater()
        {
            int seg = 48;
            float r = ParkLayout.PuddleRadius + 1.4f;
            Vector2 c = ParkLayout.Puddle;
            var v = new List<Vector3> { new Vector3(c.x, ParkLayout.WaterLevel, c.y) };
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(c.x + Mathf.Cos(a) * r, ParkLayout.WaterLevel, c.y + Mathf.Sin(a) * r));
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
                uv.Add(new Vector2(p.x / 7f, p.z / 5f));   // 小さな水たまりは、さざ波も細かく
                col.Add(new Color(0f, 0f, 0f, 1f));
            }
            var mesh = Own(new Mesh { name = "Puddle" });
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
            // 水飲み場のじゃぐちの先から、ぽたぽた落ちる所に、波紋が広がる
            Vector2 drip = ParkLayout.FountainDrip;
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_Drip", new Vector4(drip.x, drip.y, 2.6f, 0.8f));
            mpb.SetFloat("_FoamDepth", 0.012f);   // とても浅い水たまり：岸ぎわの泡は、ふちだけ（全部が白くならない）
            mr.SetPropertyBlock(mpb);
            WaterView.Register(mr);   // 水が映るときだけ、深さと色の写しを作る
        }

        // ------------------------------------------------------------------
        // しばふと草花
        // ------------------------------------------------------------------
        bool OnPlayground(Vector2 p) =>
            ParkLayout.SandMask(p.x, p.y) > 0.3f || ParkLayout.TrailMask(p.x, p.y) > 0.35f
            || Vector2.Distance(p, ParkLayout.Puddle) < ParkLayout.PuddleRadius + 1.5f;

        bool InBed(Vector2 p, float margin) =>
            Mathf.Abs(p.x - ParkLayout.FlowerBed.x) < ParkLayout.BedSize.x * 0.5f - margin && Mathf.Abs(p.y - ParkLayout.FlowerBed.y) < ParkLayout.BedSize.y * 0.5f - margin;

        void BuildParkFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;
            // 刈りこまれたしばふ（低い草）。外がわほど、のびている
            int lawn = Mathf.RoundToInt(4200 * dens);
            for (int i = 0; i < lawn; i++)
            {
                Vector2 p = RandomInRing(2f, 72f);
                if (!IsLand(p, 0.2f) || InsideOccupied(p) || OnPlayground(p) || InBed(p, -0.5f)) continue;
                if (Vector2.Distance(p, ParkLayout.Spawn) < 2.5f) continue;
                float r = p.magnitude;
                float tall = ShakuMath.SmoothStep(48f, 62f, r);
                float s = Mathf.Lerp(R(0.28f, 0.45f), R(0.6f, 1.1f), tall);
                Place(tall > 0.5f ? Pick(Grass) : "Grass_B", fol, ParkLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.5f, 5f), s, false, false, 55f);
            }
            int clover = Mathf.RoundToInt(900 * dens);
            for (int i = 0; i < clover; i++)
            {
                Vector2 p = RandomInRing(2f, 62f);
                if (Mathf.PerlinNoise(p.x * 0.06f + 70f, p.y * 0.06f + 10f) < 0.55f) continue;
                if (!IsLand(p, 0.3f) || InsideOccupied(p) || OnPlayground(p) || InBed(p, -0.5f)) continue;
                Place(R01() < 0.5f ? "Clover_A" : "Clover_B", fol, ParkLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.7f, 4f), R(0.6f, 1.0f), false, false, 50f);
            }
            // しばふの花（タンポポ・デイジー）
            void Flowers(string name, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInRing(6f, 58f);
                    if (!IsLand(p, 0.3f) || InsideOccupied(p) || OnPlayground(p) || InBed(p, -1f)) continue;
                    Place(name, flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Flowers("Dandelion", 26, 0.6f, 0.9f);
            Flowers("DandelionPuff", 8, 0.7f, 1.0f);
            Flowers("Daisy", 18, 0.6f, 0.95f);
            // 花だん：チューリップの列と、キャベツ
            Vector2 c = ParkLayout.FlowerBed;
            float bedTop = ParkLayout.Height(c.x, c.y);
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < 9; i++)
                {
                    Vector2 p = c + new Vector2(-8.6f + i * 2.15f + R(-0.3f, 0.3f), -2.4f + row * 2.4f + R(-0.3f, 0.3f));
                    string tulip = Tulips[(i + row) % Tulips.Length];
                    if (row == 1 && (i == 2 || i == 6))
                    {
                        Place("Park_Cabbage", flw, new Vector3(p.x, bedTop - 0.1f, p.y), Quaternion.Euler(0f, R(0, 360), 0f), R(0.9f, 1.1f), false, true, 120f);
                        continue;
                    }
                    Place(tulip, flw, new Vector3(p.x, bedTop - 0.05f, p.y), Quaternion.Euler(R(-4, 4), R(0, 360), R(-4, 4)), R(0.85f, 1.1f), false, true, 120f);
                }
            }
            // 植えこみ（外がわのシダ）
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = RandomInRing(56f, 76f);
                if (!IsLand(p, 0.3f) || InsideOccupied(p)) continue;
                if (Vector2.Distance(p, ParkLayout.Gate) < 7f) continue;
                string fern = R01() < 0.5f ? "Fern_A" : "Fern_B";
                Quaternion frot = GroundRotation(p, R(0, 360), 0.3f, 6f);
                float fs = R(0.7f, 1.2f);
                // さくの板のそば：葉がさくをつきぬけてしまうので、植えない（乱数の使い方は変えない）
                if (Mathf.Abs(p.magnitude - ParkFenceRadius) < 2.6f * fs) { Count("fenceFern", fern + "|Park_Fence", ParkLayout.Ground(p.x, p.y)); continue; }
                Place(fern, flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, frot, fs, false, true, 200f);
            }
            // 落ち葉と小石（押すと動く）
            int litter = Mathf.RoundToInt(500 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = R01() < 0.5f ? RandomInCircle(ParkLayout.Kunugi, 18f) : RandomInRing(4f, 64f);
                if (!IsLand(p, 0.2f) || InsideOccupied(p)) continue;   // 遊具の下には置かない（棒に引っかかる）
                string leaf = Pick(BigLeaves);
                Quaternion lrot = GroundRotation(p, R(0, 360), 1f, 6f);
                float ls = R(0.07f, 0.14f);
                // さくの下にはさまる所には置かない（乱数の使い方は変えない）
                if (Mathf.Abs(p.magnitude - ParkFenceRadius) < 0.9f) { Count("fenceLeaf", leaf + "|Park_Fence", ParkLayout.Ground(p.x, p.y)); continue; }
                PlaceLoose(leaf, assets.prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, lrot, ls, LooseProps.Shape.Leaf, false, 40f);
            }
            int pebbles = Mathf.RoundToInt(400 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(2f, 64f);
                if (ParkLayout.TrailMask(p.x, p.y) < 0.4f && ParkLayout.SandMask(p.x, p.y) < 0.4f) continue;
                if (!IsLand(p, 0.0f) || InsideOccupied(p)) continue;
                PlaceLoose(Pick(Rocks), assets.prop, ParkLayout.Ground(p.x, p.y), Quaternion.Euler(R(-12f, 12f), R(0, 360), R(-12f, 12f)), R(0.05f, 0.14f), LooseProps.Shape.Pebble, false, 40f);
            }
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceParkDewdrops()
        {
            DewdropPoints.Clear();
            Vector2 s = ParkLayout.Spawn;
            AddDewFromAbove(s + new Vector2(-3.5f, 2.5f));
            AddDewFromAbove(s + new Vector2(-6f, -3f));
            foreach (var t in _parkTops)
            {
                // 水飲み場は、まん中の飲み口の丸い頭のてっぺん（前と同じ場所。上からの光線は、頭のてっぺんから始まってしまうので、じかに置く）
                if (t == _fountainTop) AddDew(new Vector3(t.x, t.y - 7.2f + ParkLayout.BubblerTop, t.z));
                else AddDewFromAbove(new Vector2(t.x, t.z), t.y + 1f);
            }
            if (_benchSeat != Vector3.zero) AddDewFromAbove(new Vector2(_benchSeat.x, _benchSeat.z));
            if (_sandTop != Vector3.zero) AddDewFromAbove(new Vector2(_sandTop.x, _sandTop.z));
            if (_dokanCenter != Vector3.zero)
            {
                // 土管の中（上からではとどかないので、中の床に置く）
                if (CastDown(_dokanCenter + Vector3.up * 0.5f, 4f, out var hit)) AddDew(hit.point);
            }
            Vector2 c = ParkLayout.FlowerBed;
            AddDewFromAbove(c + new Vector2(ParkLayout.BedSize.x * 0.5f - 0.3f, 0f));
            AddDewFromAbove(c + new Vector2(-ParkLayout.BedSize.x * 0.5f + 0.3f, 2f));
            // クヌギの根もとと、枝や葉のしげみの上（上の樹冠より下から、下へさがす）
            float kh = ParkLayout.Height(ParkLayout.Kunugi.x, ParkLayout.Kunugi.y);
            AddDewFromAbove(ParkLayout.Kunugi + new Vector2(6f, -2f), kh + 28f);
            AddDewFromAbove(ParkLayout.Kunugi + new Vector2(-3f, 6f), kh + 28f);
            Vector2 inward = (-ParkLayout.Kunugi).normalized;   // 公園のまん中の方の枝（遊べる場所の中）
            for (int i = 0; i < 3; i++)
            {
                Vector2 d = (Vector2)(Quaternion.Euler(0f, 0f, -50f + 50f * i) * inward);
                AddDewFromAbove(ParkLayout.Kunugi + d * 13f, kh + 28f);
            }
            AddDewFromAbove(ParkLayout.Seesaw + new Vector2(0f, 3f));
            AddDewFromAbove(ParkLayout.Swing + new Vector2(-10f, 4f));
            FillDewdrops(8f, 58f);
        }

        void PlaceParkCreatures()
        {
            Vector2 bed = ParkLayout.FlowerBed;
            float bedTop = ParkLayout.Height(bed.x, bed.y);
            // モンシロチョウ（花だんのキャベツとチューリップ）とルリシジミ
            AddMob("monshiro", new Vector3(bed.x, bedTop, bed.y), 3, 9f, 3f);
            AddMob("monshiro", ParkLayout.Ground(18f, 8f), 2, 12f, 3.4f);
            AddMob("butterfly", ParkLayout.Ground(-20f, 30f), 1, 9f, 3.2f);
            // クヌギの木：クワガタとカミキリムシ
            Vector2 k = ParkLayout.Kunugi;
            AddMob("kuwagata", ParkLayout.Ground(k.x + 5.5f, k.y - 2f), 1, 2.5f);
            AddMob("kuwagata", ParkLayout.Ground(k.x - 2f, k.y + 6f), 1, 2.5f);
            AddMob("kamikiri", ParkLayout.Ground(k.x + 4.5f, k.y + 4f), 1, 2.5f);
            if (_benchSeat != Vector3.zero) AddMob("kamikiri", TopSurface(new Vector2(_benchSeat.x, _benchSeat.z + 3f)), 1, 2f);
            // カメムシ：花だんと、さくのそば
            AddMob("kamemushi", new Vector3(bed.x - 6f, bedTop, bed.y - 1f), 1, 3f);
            AddMob("kamemushi", new Vector3(bed.x + 5f, bedTop, bed.y + 1f), 1, 3f);
            AddMob("kamemushi", ParkLayout.Ground(-30f, 48f), 1, 3f);
            // トカゲ：日なたの砂場とタイヤ
            Vector2 sb = ParkLayout.Sandbox;
            AddMob("tokage", ParkLayout.Ground(sb.x + 9f, sb.y + 4f), 1, 6f);
            AddMob("tokage", ParkLayout.Ground(ParkLayout.Tires.x, ParkLayout.Tires.y - 4f), 1, 6f);
            // そのほか
            AddMob("ladybug", new Vector3(bed.x + 2f, bedTop, bed.y), 1, 3f);
            AddMob("grasshopper", ParkLayout.Ground(30f, 30f), 2, 8f);
            AddMob("grasshopper", ParkLayout.Ground(-18f, -40f), 1, 8f);
            AddMob("pillbug", ParkLayout.Ground(ParkLayout.Bench.x + 1f, ParkLayout.Bench.y), 3, 3f);
            AddMob("spider", ParkLayout.Ground(ParkLayout.JungleGym.x + 6f, ParkLayout.JungleGym.y + 3f), 1, 3f);
            AddMob("dragonfly", new Vector3(ParkLayout.Puddle.x, ParkLayout.WaterLevel, ParkLayout.Puddle.y), 1, 6f, 3f);
            // しばふのモグラ塚・水たまりのそばのケラ・花だんのハナカマキリ
            AddMob("mogura", ParkLayout.Ground(30f, 18f), 1, 0.3f);
            AddMob("mogura", ParkLayout.Ground(-20f, -5f), 1, 0.3f);
            AddMob("okera", ParkLayout.Ground(ParkLayout.Puddle.x + 6.5f, ParkLayout.Puddle.y - 1f), 1, 2f);
            AddMob("hanakamakiri", new Vector3(bed.x + 3f, bedTop, bed.y - 1f), 1, 2.5f);
            {
                // ひろばのハト（首をふりながら歩き、近づくと群れで飛ぶ）
                var g = AddMob("hato", ParkLayout.Ground(6f, -12f), 4, 4f);
                Vector2[] spots = { new Vector2(6f, -12f), new Vector2(-8f, 6f), new Vector2(24f, 2f), new Vector2(-2f, -26f) };
                foreach (var p in spots) g.path.Add(ParkLayout.Ground(p.x, p.y));
            }
            {
                var g = AddMob("sparrow", ParkLayout.Ground(4f, -10f), 3, 3f);
                Vector2[] spots = { new Vector2(4f, -10f), new Vector2(30f, 14f), new Vector2(-14f, -30f), new Vector2(-20f, 20f) };
                foreach (var p in spots) g.path.Add(ParkLayout.Ground(p.x, p.y));
            }
            {
                var g = AddMob("crow", ParkLayout.Ground(34f, 34f), 1, 2f);
                Vector2[] spots = { new Vector2(34f, 34f), new Vector2(-36f, -34f) };
                foreach (var p in spots) g.path.Add(ParkLayout.Ground(p.x, p.y));
                if (_parkTops.Count > 0) g.path.Add(_parkTops[0]);
            }
        }

        List<Vector2> ParkShaftSpots()
        {
            return new List<Vector2>
            {
                ParkLayout.Spawn + new Vector2(-3f, 1f), ParkLayout.FlowerBed, ParkLayout.Sandbox, ParkLayout.Swing + new Vector2(0f, -6f),
                ParkLayout.Kunugi + new Vector2(6f, -4f), new Vector2(0f, -4f),
            };
        }

        void DrawParkMap(Color32[] px, int size)
        {
            var wood = new Color32(160, 120, 76, 255);
            var red = new Color32(226, 72, 58, 255);
            var blue = new Color32(58, 127, 208, 255);
            var yellow = new Color32(242, 194, 50, 255);
            var steel = new Color32(206, 214, 220, 255);
            // 86. 小道・87. 水たまり・92. さく・93. 街灯と水飲み場
            MapTrails(px, size, ParkLayout.Trails, new Color32(222, 204, 160, 255));
            for (int i = 0; i < 96; i++)
            {
                float a = i / 96f * Mathf.PI * 2f;
                if (Vector2.Distance(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 61f, ParkLayout.Gate) < 10f) continue;
                MapDot(px, size, new Vector3(Mathf.Cos(a) * 61f, 0f, Mathf.Sin(a) * 61f), 0.8f, new Color32(240, 236, 226, 255));
            }
            MapDot(px, size, new Vector3(ParkLayout.Puddle.x, 0f, ParkLayout.Puddle.y), ParkLayout.PuddleRadius * 1.6f, new Color32(110, 170, 210, 255));
            MapDot(px, size, new Vector3(ParkLayout.Fountain.x, 0f, ParkLayout.Fountain.y), 2.2f, new Color32(190, 190, 186, 255));
            MapDot(px, size, new Vector3(ParkLayout.Lamp.x, 0f, ParkLayout.Lamp.y), 1.6f, new Color32(250, 220, 120, 255));
            // 91. タイヤ
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Lerp(-0.8f, 0.8f, i / 4f);
                MapDot(px, size, new Vector3(ParkLayout.Tires.x + Mathf.Sin(a) * 9f, 0f, ParkLayout.Tires.y - Mathf.Cos(a) * 9f), 2f, i % 3 == 0 ? new Color32(226, 72, 58, 255) : i % 3 == 1 ? new Color32(242, 194, 50, 255) : new Color32(58, 127, 208, 255));
            }
            // 88. 砂の城・89. ビー玉・90. シロツメクサの群れ
            foreach (var p in ExtraSpots("castle")) MapDot(px, size, p, 2.4f, new Color32(226, 205, 150, 255));
            foreach (var p in ExtraSpots("marble")) MapDot(px, size, p, 1.2f, new Color32(80, 160, 230, 255));
            foreach (var p in ExtraSpots("clover")) MapDot(px, size, p, 2.6f, new Color32(236, 240, 228, 255));
            // すべり台（台と坂）
            Vector2 s = ParkLayout.Slide;
            for (int i = 0; i <= 12; i++) MapDot(px, size, new Vector3(s.x + i * 1.8f, 0f, s.y), 2.2f, steel);
            MapDot(px, size, new Vector3(s.x, 0f, s.y), 3.2f, red);
            // ブランコ（わく）・シーソー・ジャングルジム
            for (int i = -10; i <= 10; i++) MapDot(px, size, new Vector3(ParkLayout.Swing.x + i, 0f, ParkLayout.Swing.y), 1.2f, yellow);
            for (int i = -12; i <= 12; i += 2) MapDot(px, size, new Vector3(ParkLayout.Seesaw.x + i, 0f, ParkLayout.Seesaw.y), 1.4f, wood);
            for (int i = -6; i <= 6; i += 3)
                for (int j = -6; j <= 6; j += 3) MapDot(px, size, new Vector3(ParkLayout.JungleGym.x + i, 0f, ParkLayout.JungleGym.y + j), 1.2f, blue);
            // 砂場のわく・花だん
            Vector2 c = ParkLayout.Sandbox, h = ParkLayout.SandboxSize * 0.5f;
            for (float x = -h.x; x <= h.x; x += 1f) { MapDot(px, size, new Vector3(c.x + x, 0f, c.y + h.y), 1f, wood); MapDot(px, size, new Vector3(c.x + x, 0f, c.y - h.y), 1f, wood); }
            for (float z = -h.y; z <= h.y; z += 1f) { MapDot(px, size, new Vector3(c.x + h.x, 0f, c.y + z), 1f, wood); MapDot(px, size, new Vector3(c.x - h.x, 0f, c.y + z), 1f, wood); }
            Vector2 b = ParkLayout.FlowerBed;
            for (int i = -4; i <= 4; i++) MapDot(px, size, new Vector3(b.x + i * 2.15f, 0f, b.y), 1.6f, new Color32(240, 120, 150, 255));
            // 土管・クヌギ・ベンチ
            for (int i = -5; i <= 5; i++) MapDot(px, size, new Vector3(ParkLayout.Dokan.x + i, 0f, ParkLayout.Dokan.y), 2.4f, new Color32(180, 178, 172, 255));
            MapDot(px, size, new Vector3(ParkLayout.Kunugi.x, 0f, ParkLayout.Kunugi.y), 4.5f, new Color32(98, 70, 46, 255));
            for (int i = -2; i <= 2; i++) MapDot(px, size, new Vector3(ParkLayout.Bench.x, 0f, ParkLayout.Bench.y + i * 2f), 1.6f, wood);
        }
    }
}
