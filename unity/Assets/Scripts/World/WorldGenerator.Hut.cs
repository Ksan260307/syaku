using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// 山小屋の中：部屋（丸太のかべ・屋根うら・はり・あいたとびら・窓の外の景色）と家具・小物・しずく・いきもの・地図・窓からの光。
    /// 部屋は人の大きさなので、しゃくとりむしには、テーブルもベッドも大きな岩山のよう。水はない（白く見える水たまりを作らない）。
    /// </summary>
    public partial class WorldGenerator
    {
        /// <summary>家具の上の、しずくをのせる点など（テストで見る）。</summary>
        public readonly List<Vector3> HutTops = new List<Vector3>();

        void ClearHut() => HutTops.Clear();

        static Vector3 HG(float x, float z) => new Vector3(x, 0f, z);

        /// <summary>部屋の大きな物（かべ・屋根・はり）か。</summary>
        static bool HutRoomPiece(string mesh) => mesh == "Hut_Walls" || mesh == "Hut_Roof" || mesh == "Hut_Beams" || mesh == "Hut_Door" || mesh == "Hut_View";

        Material HutMaterial(string mesh)
        {
            if (mesh == "Hut_View") return assets.backdrop != null ? assets.backdrop : assets.bark;
            if (HutRoomPiece(mesh) || mesh == "Hut_Bunk" || mesh == "Hut_Table" || mesh == "Hut_Bench" || mesh == "Hut_Shelf" || mesh == "Hut_Stove" || mesh == "Hut_Firewood")
                return assets.bark;
            return assets.prop;
        }

        // ------------------------------------------------------------------
        // 部屋と家具
        // ------------------------------------------------------------------
        void BuildHutSolids()
        {
            foreach (var it in HutLayout.Items)
            {
                Quaternion rot = Quaternion.Euler(0f, it.yaw, 0f);
                Place(it.mesh, HutMaterial(it.mesh), it.pos, rot, 1f, it.collider, it.shadows, 600f, asRenderer: true);
                if (!HutRoomPiece(it.mesh)) Occupy(new Vector2(it.pos.x, it.pos.z), 3f);
            }
            // 光る物：まきストーブの火と、つるしたランプ
            Material warm = assets.warmGlow != null ? assets.warmGlow : assets.glow;
            Place("Hut_StoveFire", warm, HG(HutLayout.Stove.x, HutLayout.Stove.y), Quaternion.Euler(0f, 90f, 0f), 1f, false, false, 600f, asRenderer: true);
            Place("Hut_LampGlow", warm, new Vector3(36f, HutLayout.BeamY, -6f), Quaternion.identity, 1f, false, false, 600f, asRenderer: true);
            // 家具のまわり（小物を置かない）
            Occupy(HutLayout.Table, 20f);
            Occupy(HutLayout.Bunk, 22f);
            Occupy(HutLayout.Stove, 14f);
            BuildWindowLight();
        }

        /// <summary>
        /// 窓ととびらからさしこむ日ざし（光の筋）。日ざしの向きにそって、窓の穴から床へ。
        /// 部屋の中は、かべと屋根のかげになるので、日だまりは窓の形に明るい。
        /// </summary>
        void BuildWindowLight()
        {
            if (assets.lightShaft == null) return;
            Vector3 dir = sun != null ? sun.transform.forward : new Vector3(-0.38f, -0.79f, 0.48f).normalized;
            if (dir.y > -0.2f) return;
            var parent = new GameObject("WindowLight").transform;
            parent.SetParent(Root, false);
            // 南の窓・東の窓・とびら（それぞれの穴のまん中と、大きさ）
            var holes = new (Vector3 c, float w)[]
            {
                (new Vector3(34f, 52f, -HutLayout.HalfZ), 26f),
                (new Vector3(HutLayout.HalfX, 52f, -6f), 28f),
                (new Vector3((HutLayout.DoorA + HutLayout.DoorB) * 0.5f, 38f, -HutLayout.HalfZ), 22f),
            };
            foreach (var (c, w) in holes)
            {
                // 穴から床までの長さ（光の筋は、床の点から日ざしの来る方へのびる）
                float down = c.y / -dir.y;
                Vector3 ground = c + dir * down;
                float len = down + 40f;
                var go = new GameObject("Shaft");
                go.transform.SetParent(parent, false);
                go.transform.position = ground - dir * len;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir);
                go.transform.localScale = new Vector3(w * 0.5f, len, w * 0.5f);
                go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = assets.lightShaft;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------
        // 押すと動く小さな物：パンくず・ひまわりのたね・マッチ・ボタン
        // ------------------------------------------------------------------
        void ScatterHutProps()
        {
            Material prop = assets.prop;
            // テーブルの下と上のパンくず（ネズミとハエの好物）
            for (int i = 0; i < 9; i++)
            {
                Vector2 p = HutLayout.Table + new Vector2(R(-14f, 14f), R(-24f, 22f));
                PlaceLoose("Hut_Crumb", prop, HG(p.x, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.4f), LooseProps.Shape.Pebble, true, 80f);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector2 p = new Vector2(58f, -18f) + new Vector2(R(-9f, 9f), R(-7f, 7f));
                if (CastDown(new Vector3(p.x, 40f, p.y), 20f, out var hit) && hit.normal.y > 0.8f && hit.point.y < HutLayout.TableHeight + 0.5f)
                    PlaceLoose("Hut_Crumb", prop, hit.point, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.3f), LooseProps.Shape.Pebble, true, 80f);
            }
            // ネズミの穴のまわりの、ひまわりのたね
            for (int i = 0; i < 8; i++)
            {
                Vector2 p = HutLayout.MouseHole + new Vector2(R(-12f, -3f), R(-7f, 7f));
                PlaceLoose("Hut_Seed", prop, HG(p.x, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.15f), LooseProps.Shape.Pebble, true, 80f);
            }
            // ストーブのそばのマッチ
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = HutLayout.Stove + new Vector2(R(16f, 22f), R(-12f, 12f));
                PlaceLoose("Hut_Match", prop, HG(p.x, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), 1f, LooseProps.Shape.Pebble, true, 80f);
            }
            // ラグの上と、ベッドの下のボタン
            foreach (var p in new[] { new Vector2(-12f, -12f), new Vector2(8f, 6f), new Vector2(-20f, 30f), new Vector2(18f, 40f) })
                PlaceLoose("Hut_Button", prop, HG(p.x + R(-1f, 1f), p.y + R(-1f, 1f)), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.2f), LooseProps.Shape.Cap, true, 80f);
        }

        // ------------------------------------------------------------------
        // しずく（30 こ）：床・家具の上・窓台・はりの上
        // ------------------------------------------------------------------
        void PlaceHutDewdrops()
        {
            DewdropPoints.Clear();
            HutTops.Clear();
            // 入り口のまわり
            AddDewFromAbove(new Vector2(-40f, -44f), 20f);    // 玄関マット
            AddDewFromAbove(new Vector2(-50f, -34f), 20f);
            AddDewFromAbove(new Vector2(-21.5f, -44f), 20f);  // 登山ぐつの中
            AddDewFromAbove(new Vector2(-4f, -45f), 40f);     // リュックの上
            // ラグ
            AddDewFromAbove(new Vector2(0f, -6f), 20f);
            AddDewFromAbove(new Vector2(-14f, 6f), 20f);
            // まきストーブ（やかんのふた）・灰受けの板・まきの山
            AddDewFromAbove(new Vector2(-54.25f, -6.2f), 50f);
            AddDewFromAbove(new Vector2(-40f, -12f), 20f);
            AddDewFromAbove(HutLayout.Firewood, 20f);
            AddDewFromAbove(new Vector2(-48f, 36f), 20f);
            // かべの棚の 3 段
            AddDewFromAbove(new Vector2(-66f, 47f), 40f);
            AddDewFromAbove(new Vector2(-62f, 47f), 58f);
            AddDewFromAbove(new Vector2(-42.5f, 47f), 76f);
            // 二段ベッド：下の段・まくら、上の段・まくら、柱のてっぺん
            AddDewFromAbove(new Vector2(-10f, 35f), 30f);
            AddDewFromAbove(new Vector2(35f, 35f), 30f);
            AddDewFromAbove(new Vector2(0f, 35f), 70f);
            AddDewFromAbove(new Vector2(35f, 35f), 70f);
            AddDewFromAbove(new Vector2(42f, 20f), 70f);
            // テーブル（天板・マグカップの中・パン）とベンチ・テーブルの下
            AddDewFromAbove(new Vector2(41f, -26f), 40f);
            AddDewFromAbove(new Vector2(46f, 10f), 40f);
            AddDewFromAbove(new Vector2(58f, -18f), 40f);
            AddDewFromAbove(new Vector2(24f, -20f), 25f);
            AddDewFromAbove(new Vector2(52f, -6f), 20f);
            // 窓台
            AddDewFromAbove(new Vector2(68f, -16f), 45f);
            AddDewFromAbove(new Vector2(24f, -48f), 45f);
            AddDewFromAbove(new Vector2(44f, -48f), 45f);
            // 天井のはり
            AddDewFromAbove(new Vector2(0f, -20f), 100f);
            AddDewFromAbove(new Vector2(-36f, 6f), 100f);
            AddDewFromAbove(new Vector2(36f, 30f), 100f);
            // ネズミの穴の前
            AddDewFromAbove(new Vector2(64f, 40f), 20f);
            // とどかなかった分は、床に（数をそろえる）
            int guard = 0;
            while (DewdropPoints.Count < Area.DropCount && guard++ < 400)
            {
                Vector2 p = new Vector2(R(-62f, 62f), R(-44f, 44f));
                if (!IsFree(p, 2f)) continue;
                AddDewFromAbove(p, 20f);
            }
            foreach (var d in DewdropPoints)
                if (d.y > 1f) HutTops.Add(d);
        }

        // ------------------------------------------------------------------
        // いきもの
        // ------------------------------------------------------------------
        void PlaceHutCreatures()
        {
            float face = HutLayout.HalfX - HutLayout.WallFace;
            // ニホンヤモリ：東のかべ（窓の南）と、西のかべ（かけ時計の下）
            AddMob("yamori", new Vector3(face, 44f, -42f), 1, 3f).up = Vector3.left;
            AddMob("yamori", new Vector3(-face, 46f, 34f), 1, 3f).up = Vector3.right;
            // トビズムカデ：まきの山のかげと、ベッドの下
            AddMob("mukade", HG(-48f, 22f), 1, 4f);
            AddMob("mukade", HG(14f, 42f), 1, 5f);
            // ハサミムシ：玄関マットのはしと、ほうきのかげ
            AddMob("hasamimushi", HG(-56f, -40f), 2, 3f);
            AddMob("hasamimushi", HG(-26f, -36f), 1, 2.5f);
            // イエバエ：テーブルの上（パンのにおい）と、明るい南の窓
            AddMob("hae", new Vector3(HutLayout.Table.x, HutLayout.TableHeight, HutLayout.Table.y), 2, 12f, HutLayout.TableHeight + 5f);
            AddMob("hae", new Vector3(34f, HutLayout.WindowSillS, -46f), 1, 8f, HutLayout.WindowSillS + 6f);
            // ツバメ：はりの上の巣。巣のふち・はりの上・窓台に下りる（見どころなので、そのまま）
            {
                var g = AddMob("tsubame", new Vector3(HutLayout.Nest.x, HutLayout.BeamTop + 2f, HutLayout.Nest.y), 1, 2f);
                g.showcase = true;
                Vector3[] spots =
                {
                    new Vector3(HutLayout.Nest.x, HutLayout.BeamTop + 2f, HutLayout.Nest.y),
                    new Vector3(0f, HutLayout.BeamTop, -6f),
                    new Vector3(-36f, HutLayout.BeamTop, -24f),
                    new Vector3(36f, HutLayout.BeamTop, 22f),
                    new Vector3(40f, HutLayout.WindowSillS, -48f),
                };
                foreach (var p in spots)
                    g.path.Add(CastDown(p + Vector3.up * 3f, 8f, out var hit) ? hit.point : p);
                g.center = g.path[0];
            }
            // ハツカネズミ：ネズミの穴の前と、テーブルの下（パンくず）
            AddMob("nezumi", HG(HutLayout.MouseHole.x - 7f, HutLayout.MouseHole.y), 1, 6f);
            AddMob("nezumi", HG(HutLayout.Table.x - 4f, HutLayout.Table.y + 4f), 1, 8f);
            // ハエトリグモ：窓台と、テーブルの上
            AddMob("spider", new Vector3(44f, HutLayout.WindowSillS, -48f), 1, 2f);
            AddMob("spider", new Vector3(42f, HutLayout.TableHeight, -20f), 1, 3f);
            // テーブルの上の、ハエのとまる所（パン・地図・マグカップのふち）
            FlowerPoints.Add(new Vector3(58f, HutLayout.TableHeight + 6.2f, -18f));
            FlowerPoints.Add(new Vector3(48f, HutLayout.TableHeight + 0.3f, -2f));
            FlowerPoints.Add(new Vector3(49f, HutLayout.TableHeight + 4.7f, 10f));
            FlowerPoints.Add(new Vector3(62f, HutLayout.TableHeight + 2.5f, 16f));
        }

        // ------------------------------------------------------------------
        // 地図：床と家具の形
        // ------------------------------------------------------------------
        static void HutMapRect(Color32[] px, int size, Vector2 c, Vector2 half, float yawDeg, Color32 col)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            Vector2 ax = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)), az = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            for (float u = -half.x; u <= half.x; u += 1.2f)
                for (float v = -half.y; v <= half.y; v += 1.2f)
                {
                    Vector2 p = c + ax * u + az * v;
                    MapDot(px, size, new Vector3(p.x, 0f, p.y), 1.2f, col);
                }
        }

        void DrawHutMap(Color32[] px, int size)
        {
            var wall = new Color32(110, 70, 40, 255);
            float hx = HutLayout.HalfX, hz = HutLayout.HalfZ;
            // かべ（とびらと窓はあけて）
            for (float x = -hx; x <= hx; x += 1f)
            {
                MapDot(px, size, new Vector3(x, 0f, hz + 1.5f), 1.6f, wall);
                if (x < HutLayout.DoorA || x > HutLayout.DoorB) MapDot(px, size, new Vector3(x, 0f, -hz - 1.5f), 1.6f, x > 14f && x < 54f ? new Color32(150, 200, 230, 255) : wall);
            }
            for (float z = -hz; z <= hz; z += 1f)
            {
                MapDot(px, size, new Vector3(-hx - 1.5f, 0f, z), 1.6f, wall);
                MapDot(px, size, new Vector3(hx + 1.5f, 0f, z), 1.6f, z > -28f && z < 16f ? new Color32(150, 200, 230, 255) : wall);
            }
            // 家具
            HutMapRect(px, size, HutLayout.Rug, new Vector2(26f, 18f), 0f, new Color32(196, 104, 86, 255));
            HutMapRect(px, size, HutLayout.DoorMat, new Vector2(14f, 8f), 0f, new Color32(140, 104, 66, 255));
            HutMapRect(px, size, HutLayout.Table, new Vector2(17f, 29f), 0f, new Color32(214, 168, 108, 255));
            HutMapRect(px, size, HutLayout.Bench, new Vector2(4.5f, 23f), 0f, new Color32(176, 122, 72, 255));
            HutMapRect(px, size, HutLayout.Bunk, new Vector2(39f, 16f), 180f, new Color32(184, 76, 64, 255));
            HutMapRect(px, size, HutLayout.Stove, new Vector2(12f, 10f), 90f, new Color32(60, 56, 56, 255));
            HutMapRect(px, size, HutLayout.Firewood, new Vector2(9f, 11f), 90f, new Color32(130, 96, 66, 255));
            HutMapRect(px, size, HutLayout.Shelf, new Vector2(13f, 5f), 180f, new Color32(214, 168, 108, 255));
            MapDot(px, size, new Vector3(HutLayout.MouseHole.x - 1f, 0f, HutLayout.MouseHole.y), 2f, new Color32(40, 30, 26, 255));
            // はり（天井）
            foreach (float bx in HutLayout.BeamsX)
                for (float z = -hz; z <= hz; z += 2f) MapDot(px, size, new Vector3(bx, 0f, z), 0.7f, new Color32(120, 84, 52, 160));
        }
    }
}
