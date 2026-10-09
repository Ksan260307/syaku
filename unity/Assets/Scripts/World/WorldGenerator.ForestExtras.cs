using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>森の改善：サルノコシカケの階段・クモの巣・ホコリタケ・光るキノコの道しるべ・小道の目じるし・水たまりのまわりなど。</summary>
    public partial class WorldGenerator
    {
        /// <summary>森の光の筋を足す場所（苔の丘・丸太のトンネルの出口・切り株の頂のそば）。</summary>
        public static readonly Vector2[] ForestExtraShafts =
        {
            ForestLayout.MossHill + new Vector2(1f, -1f), ForestLayout.LogCenter + ForestLayout.LogDir * 27f, ForestLayout.Stump + new Vector2(6f, 9f),
        };

        void BuildForestExtras()
        {
            Material prop = assets.prop, flw = assets.flowers, fol = assets.foliage;
            Vector2 pond = ForestLayout.Pond;
            float R = ForestLayout.PondRadius;
            Vector2 perp = new Vector2(ForestLayout.LogDir.y, -ForestLayout.LogDir.x);

            // 5. 光の筋を足す
            BuildLightShafts(ForestExtraShafts);

            // 31. 大樹の幹のサルノコシカケ（遊べる南がわ）
            Vector3 tree = ForestLayout.Ground(ForestLayout.GreatTree.x, ForestLayout.GreatTree.y);
            ShelfStairs(tree, 46f, 9f, 27f, 2.6f, 64f, 8f, 1.6f, "GreatTree", "shelf_tree");
            // 32. 古い切り株の側面のサルノコシカケの階段（東がわ。休みながら頂まで）
            Vector3 stump = ForestLayout.Ground(ForestLayout.Stump.x, ForestLayout.Stump.y);
            ShelfStairs(stump, 15f, 1.0f, 7.0f, 0.95f, -88f, 27f, 1.0f, "Stump", "shelf_stump");
            // 33. 丸太の側面のサルノコシカケ
            foreach (float t in new[] { -15f, -5f, 7f, 16f })
            {
                Vector2 c = ForestLayout.LogCenter + ForestLayout.LogDir * t;
                Vector3 dir = new Vector3(perp.x, 0f, perp.y) * (t > 0f ? 1f : -1f);
                Vector3 from = ForestLayout.Ground(c.x, c.y) + Vector3.up * XR(2.4f, 4.6f) + dir * 12f;
                if (Physics.Raycast(from, -dir, out var hit, 14f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) && hit.collider.name.StartsWith("HollowLog"))
                {
                    Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                    Place("ShelfFungus", prop, hit.point - n * 0.2f, Quaternion.LookRotation(n, Vector3.up), XR(0.8f, 1.0f), true, true, 160f);
                    Mark("shelf_log", hit.point);
                }
            }

            // 34. クモの巣（小枝・赤キノコの柄・切り株・大樹の根・岩と地面のすきま）・35. 丸太のトンネルの入り口のわき
            // どれも、まん中から面にそって糸をのばして、とどいた所を支えにして張る（宙にうかない）
            {
                var wr = new System.Random(3407);
                Vector2 la = ForestLayout.LogCenter - ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
                Vector2 lb = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
                Vector3 axis = new Vector3(ForestLayout.LogDir.x, 0f, ForestLayout.LogDir.y);
                // 丸太のふくらみの下（地面とのすきま）。入り口のわきで、口の面にそって
                StringWebNear(wr, la + ForestLayout.LogDir * 3.5f + perp * 4.4f, 0.8f, 0.35f, 1.1f, 2.0f, axis, 20f, "logweb");
                StringWebNear(wr, lb - ForestLayout.LogDir * 3.5f - perp * 4.4f, 0.8f, 0.35f, 1.1f, 2.0f, axis, 20f, "logweb");
                // 赤キノコの森：柄と柄のあいだ・かさの下
                StringWebNear(wr, ForestLayout.MushroomGrove + new Vector2(-3.5f, 2.5f), 2.5f, 0.6f, 2.2f, 2.6f, Vector3.zero, 0f, "groveweb");
                StringWebNear(wr, ForestLayout.MushroomGrove + new Vector2(2f, -3.5f), 2.5f, 0.6f, 2.2f, 2.6f, Vector3.zero, 0f, "groveweb");
                // 切り株の根元（北がわ。西は壁登り、東はサルノコシカケの階段）
                StringWebNear(wr, ForestLayout.Stump + new Vector2(0f, 9.5f), 2.5f, 0.4f, 1.6f, 2.4f, Vector3.zero, 0f, "stumpweb");
                // 大樹の根もと（根と根のあいだ・幹と地面のすみ）
                StringWebNear(wr, ForestLayout.GreatTree + new Vector2(-6f, -9f), 6f, 0.6f, 2.2f, 2.6f, Vector3.zero, 0f, "rootweb");
                // 光るキノコの洞の、アーチの根のわき
                StringWebNear(wr, ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f), 6f, 0.5f, 2f, 2.6f, Vector3.zero, 0f, "hollowweb");
                // 草むらの岩と地面のすきま（小道のわきの見わたせる岩・苔の丘）
                StringWebNear(wr, new Vector2(5.5f, 17f), 3f, 0.4f, 1.4f, 2.2f, Vector3.zero, 0f, "rockweb");
                StringWebNear(wr, ForestLayout.MossHill + new Vector2(4f, -6f), 7f, 0.4f, 1.4f, 2.2f, Vector3.zero, 0f, "rockweb");
            }

            // 36. ホコリタケ（苔の丘・赤キノコの森）。73. ふむと弾む（キノコの材質）
            Puffballs(ForestLayout.MossHill, 11f, 6);
            Puffballs(ForestLayout.MushroomGrove, 13f, 6);

            // 37. 目覚めの苔原のそばの、小さなキノコの輪
            {
                Vector2 ring = ForestLayout.Spawn + new Vector2(-7f, 6f);
                for (int i = 0; i < 11; i++)
                {
                    float a = i / 11f * Mathf.PI * 2f;
                    Vector2 p = ring + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.6f;
                    if (!ExtraOk(p, 0.3f, true)) continue;
                    PutSolid(i % 3 == 0 ? "Mushroom_Cluster" : "Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.5f, 6f), XR(0.22f, 0.32f), 0.35f);
                    Mark("fairy", ForestLayout.Ground(p.x, p.y));
                }
            }

            // 38. 小道のふちのデイジーとクローバー
            foreach (var trail in ForestLayout.Trails)
                for (int i = 0; i < trail.Length - 1; i++)
                {
                    Vector2 a = trail[i], b = trail[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
                    for (float s = 2f; s < len; s += XR(3f, 5f))
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / len) + n * (XR01() < 0.5f ? -1f : 1f) * XR(1.9f, 2.6f);
                        if (!IsLand(p, 0.15f) || !ExtraOk(p, 0.2f, false)) continue;
                        bool daisy = XR01() < 0.55f;
                        PutDeco(daisy ? "Daisy" : XPick("Clover_A", "Clover_B"), daisy ? flw : fol, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f,
                            GroundRotation(p, XR(0f, 360f), 0.3f, 5f), XR(0.6f, 0.9f), daisy);
                    }
                }

            // 39. 小道の分かれ道の、小石を積んだ目じるし（82. 登れる）
            Cairn(new Vector2(14f, 4f) + new Vector2(1.8f, -2.6f));
            Cairn(new Vector2(-1f, 26f) + new Vector2(2.8f, 1.2f));
            Cairn(new Vector2(16f, -18f) + new Vector2(0.6f, 3f));

            // 40. 丸太のトンネルから光るキノコの洞まで、光るキノコの道しるべ
            {
                Vector2 a = ForestLayout.LogCenter + ForestLayout.LogDir * 27f;
                Vector2 b = ForestLayout.ArchTarget + new Vector2(1.5f, -4f);
                float len = Vector2.Distance(a, b);
                Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
                for (float s = 0f; s <= len; s += 3f)
                {
                    Vector2 c = Vector2.Lerp(a, b, s / len) + n * (Mathf.Sin(s * 0.5f) * 1.6f + 1.4f);
                    if (!FindSpot(c, 1.5f, 0.3f, false, out var p, q => IsLand(q, 0.1f))) continue;
                    PutDeco("Mushroom_Glow", assets.glow, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 0.5f, 8f), XR(0.22f, 0.32f), false, 140f);
                    Mark("glowtrail", ForestLayout.Ground(p.x, p.y));
                }
            }

            // 41. 大きな岩の上の苔・42. 岩のかげのシダ
            Vector3 sunDir = sun != null ? sun.transform.forward : new Vector3(0.3f, -0.85f, 0.4f).normalized;
            Vector2 shade = new Vector2(sunDir.x, sunDir.z).normalized;
            foreach (var (pos, s) in _bigRocks)
            {
                if (CastDown(pos + Vector3.up * 8f, 12f, out var top) && top.collider.name.StartsWith("Rock") && top.point.y > pos.y + 0.4f)
                    PutDeco("Moss", fol, top.point + Vector3.down * 0.06f, Quaternion.FromToRotation(Vector3.up, top.normal) * Quaternion.Euler(0f, XR(0f, 360f), 0f), 0.55f * s);
                Vector2 f = new Vector2(pos.x, pos.z) + shade * (1.7f * s);
                if (IsLand(f, 0.1f) && ExtraOk(f, 0.4f, false))
                    PutDeco(XPick("Fern_A", "Fern_B"), flw, ForestLayout.Ground(f.x, f.y) + Vector3.down * 0.3f, GroundRotation(f, XR(0f, 360f), 0.3f, 6f), XR(0.5f, 0.8f), true, 150f);
            }

            // 43. 丸太の上の苔とキノコ・59. 丸太のトンネルの中の床の苔（どちらも当たり判定なし）
            for (float t = -18f; t <= 18f; t += 6f)
            {
                Vector2 c = ForestLayout.LogCenter + ForestLayout.LogDir * t;
                Vector3 g = ForestLayout.Ground(c.x, c.y);
                if (CastDown(g + Vector3.up * 12f, 14f, out var top) && top.collider.name.StartsWith("HollowLog") && top.point.y > g.y + 4f)
                {
                    Vector3 side = new Vector3(perp.x, 0f, perp.y) * XR(-1.2f, 1.2f);
                    PutDeco(XR01() < 0.5f ? "Moss" : "Mushroom_Cluster", XR01() < 0.5f ? fol : prop, top.point + side + Vector3.down * 0.05f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.35f, 0.55f));
                    Mark("logtop", top.point);
                }
                if (CastDown(g + Vector3.up * 3.4f, 6f, out var floor) && floor.collider.name.StartsWith("HollowLog"))
                    PutDeco("Moss", fol, floor.point + Vector3.down * 0.04f, Quaternion.FromToRotation(Vector3.up, floor.normal) * Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.3f, 0.45f));
            }

            // 44. 古い切り株の頂のふち（東がわ）の小さなキノコ
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Lerp(-70f, 70f, i / 4f) * Mathf.Deg2Rad;
                Vector2 p = ForestLayout.Stump + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6.0f;
                if (CastDown(new Vector3(p.x, stump.y + 20f, p.y), 30f, out var top) && top.collider.name.StartsWith("Stump") && top.point.y > stump.y + 5f)
                    PutDeco("Mushroom_Cluster", prop, top.point, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.3f, 0.42f), true, 160f);
            }

            // 45. 鏡の水たまりの岸を、押すと動く小石がふちどる
            for (int i = 0; i < 46; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * XR(0.96f, 1.12f);
                if (!IsLand(p, 0.02f) || !ExtraOk(p, 0.2f, false)) continue;
                PlaceLoose(XPick("Rock_A", "Rock_B", "Rock_C"), prop, ForestLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f)), XR(0.08f, 0.22f), LooseProps.Shape.Pebble, false, 60f);
            }
            // 46. 奥（南）の岸のガマの群れ
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.Deg2Rad * XR(-125f, -55f);
                Vector2 p = pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * XR(0.86f, 1.02f);
                if (new Vector2(p.x, p.y).magnitude > ForestLayout.PlayRadius - 2f || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco("Reed", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.2f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.7f, 1.05f), true, 140f);
                Mark("reed", ForestLayout.Ground(p.x, p.y));
            }
            // 47. 睡蓮の花をもう 2 つ
            PutDeco("WaterLily", flw, new Vector3(pond.x - 2.5f, ForestLayout.WaterLevel + 0.1f, pond.y - 8f), Quaternion.Euler(0f, 140f, 0f), 0.9f, false, 150f);
            PutDeco("WaterLily", flw, new Vector3(pond.x + 7.5f, ForestLayout.WaterLevel + 0.1f, pond.y + 3.5f), Quaternion.Euler(0f, 210f, 0f), 0.75f, false, 150f);
            // 48. 浅い所の水草のしげみ
            for (int i = 0; i < 6; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * XR(0.7f, 0.86f);
                if (ForestLayout.Height(p.x, p.y) > ForestLayout.WaterLevel - 0.05f) continue;
                PutDeco("WaterGrass", flw, new Vector3(p.x, ForestLayout.WaterLevel + 0.02f + i * 0.004f, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.8f, 1.2f), false, 110f);
                Mark("watergrass", new Vector3(p.x, ForestLayout.WaterLevel, p.y));
            }

            // 49. どんぐりのぼうしを積んだ山（78. 登れる）
            {
                Vector2 c = ForestLayout.AcornPlaza + new Vector2(4.5f, -5.5f);
                if (ExtraOk(c, 2.4f, true))
                {
                    Vector3 g = ForestLayout.Ground(c.x, c.y);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * 2.094f;
                        Place("AcornCap", prop, g + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.25f, Quaternion.Euler(0f, XR(0f, 360f), 0f), 1.25f, true);
                    }
                    Place("AcornCap", prop, g + Vector3.up * 0.75f, Quaternion.Euler(XR(-8f, 8f), XR(0f, 360f), XR(-8f, 8f)), 1.3f, true);
                    Occupy(c, 2.6f);
                    Mark("cappile", g);
                }
            }
            // 50. どんぐり広場のまわりの松ぼっくりのかたまり（押すと転がる）
            ScatterLoose(ForestLayout.AcornPlaza + new Vector2(-7f, 6f), 3f, 4, 0.8f, 1.1f, LooseProps.Shape.Pinecone, "Pinecone");
            ScatterLoose(ForestLayout.AcornPlaza + new Vector2(9f, 5f), 3f, 3, 0.8f, 1.1f, LooseProps.Shape.Pinecone, "Pinecone");

            // 51. 大樹の根元の落ち葉の吹きだまり
            for (int i = 0; i < 140; i++)
            {
                Vector2 p = RandomXInCircle(ForestLayout.GreatTree + new Vector2(XR(-14f, 14f), -27f), 6f);
                if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.1f, false)) continue;
                PlaceLoose(XPick(BigLeaves), prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, XR(0f, 360f), 1f, 6f), XR(0.08f, 0.16f), LooseProps.Shape.Leaf, false, 40f);
            }

            // 52. 花の草原のイチゴとツリガネソウ・53. 草原のはしの背の高い草
            ScatterDeco(ForestLayout.Meadow, 12f, 12, 0.85f, 1.2f, flw, 0.05f, "Strawberry", "Bellflower");
            for (int i = 0; i < 44; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Meadow + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(14f, 17.5f);
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.3f, false) || ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                PutDeco("Grass_C", fol, ForestLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.5f, 5f), XR(1.1f, 1.45f), false, 70f);
            }

            // 54. 生えたばかりの小さな赤キノコ・55. 大きなかさの下の小さな光るキノコ
            ScatterDeco(ForestLayout.MushroomGrove, 12f, 10, 0.16f, 0.26f, prop, 0.02f, "Mushroom_Red");
            for (int i = 0; i < _redCaps.Count && i < 5; i++)
            {
                var (pos, s) = _redCaps[i];
                for (int k = 0; k < 2; k++)
                {
                    float a = XR(0f, Mathf.PI * 2f);
                    Vector2 p = new Vector2(pos.x, pos.z) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1.6f * s);
                    if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.2f, false)) continue;
                    PutDeco("Mushroom_Glow", assets.glow, ForestLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.5f, 8f), XR(0.2f, 0.28f), false, 120f);
                }
            }

            // 56. 苔の丘のてっぺんの芽生え・57. 外周の大岩のすきまのシダ
            ScatterDeco(ForestLayout.MossHill, 4.5f, 12, 0.7f, 1.1f, fol, 0f, "Sprout");
            for (int i = 0; i < 30; i++)
            {
                float a = i / 30f * Mathf.PI * 2f + XR(-0.05f, 0.05f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(61f, 64f);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 20f || !IsLand(p, 0.1f) || !ExtraOk(p, 0.4f, false)) continue;
                PutDeco(XPick("Fern_A", "Fern_B"), flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.6f, 1f), true, 200f);
            }

            // 58. 小枝の山（見た目だけ）
            for (int i = 0; i < 6; i++)
            {
                Vector2 c = RandomXInRing(10f, 58f);
                if (!IsLand(c, 0.2f) || !ExtraOk(c, 1.6f, false) || ForestLayout.TrailMask(c.x, c.y) > 0.3f) continue;
                Vector3 g = ForestLayout.Ground(c.x, c.y);
                // 交差して積み重なる：下の枝の上に、のせる（宙にうかない）。見た目だけなので、下の枝の高さは自分で数える
                float top = g.y;
                for (int k = 0; k < 4; k++)
                {
                    float s = XR(0.22f, 0.32f);
                    float y = k == 0 ? g.y - 0.02f : top - 0.1f * s;
                    PutDeco(XPick("Twig_A", "Twig_B"), prop, new Vector3(g.x, y, g.z), Quaternion.Euler(XR(-6f, 6f), XR(0f, 360f), XR(-6f, 6f)), s, true, 120f);
                    top = y + 0.42f * s;   // 小枝のまん中の、上の面
                }
                Mark("twigpile", g);
            }

            // 60. 公園へのトンネルのまわりのタンポポ・61. 川辺へのトンネルのまわりのスギナとカキツバタ
            ScatterDeco(ForestLayout.ParkGate + (-ForestLayout.ParkGate).normalized * 9f, 5f, 14, 0.7f, 1.0f, flw, 0.05f, "Dandelion", "DandelionPuff");
            ScatterDeco(ForestLayout.Gate + (-ForestLayout.Gate).normalized * 9f, 5f, 12, 0.7f, 1.0f, flw, 0.1f, "Horsetail", "Iris");

            // 62. 目覚めの苔原のまわりの、ふかふかの苔
            for (int i = 0; i < 70; i++)
            {
                Vector2 p = RandomXInCircle(Vector2.zero, 9f);
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.2f, false) || ForestLayout.TrailMask(p.x, p.y) > 0.6f) continue;
                PutDeco("Moss", fol, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 1f, 3f), XR(0.5f, 1.1f), false, 60f);
            }

            // 63. 光るキノコの洞のまわりの、根っこのような小枝
            {
                Vector2 c = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2f + XR(-0.2f, 0.2f);
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(7.5f, 9.5f);
                    if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.6f, false)) continue;
                    LayTwig("Twig_B", prop, p, -a * Mathf.Rad2Deg, XR(-4f, 4f), XR(0.45f, 0.6f), 140f);
                }
            }

            // 64. 古い切り株のまわりの茶色い落ち葉
            for (int i = 0; i < 70; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Stump + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(9.5f, 14f);
                if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.1f, false)) continue;
                PlaceLoose(XPick("Leaf_Oak_Brown", "Leaf_Oak_Orange"), prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, XR(0f, 360f), 1f, 6f), XR(0.08f, 0.15f), LooseProps.Shape.Leaf, false, 40f);
            }

            // 65. 大樹の根のあいだのどんぐりと松ぼっくり
            ScatterLoose(ForestLayout.GreatTree + new Vector2(0f, -27f), 12f, 5, 0.8f, 1.1f, LooseProps.Shape.Pinecone, "Pinecone");
            ScatterDeco(ForestLayout.GreatTree + new Vector2(0f, -27f), 12f, 8, 0.9f, 1.2f, assets.propGlossy, 0.03f, "Acorn");

            // 66. 遠くの大きな木の幹・67. 遠景の森
            MoreBackgroundTrunks(10, 88f, 118f, p => Vector2.Distance(p, ForestLayout.GreatTree) < 45f);
            DistantCanopy(9, 108f, 140f, p => Vector2.Distance(p, ForestLayout.GreatTree) < 55f);

            // 68. 花の草原の、ひとやすみできる平たい石・69. 水たまりのそばの、カエルがすわれる平たい石
            FlatRock(ForestLayout.Meadow + new Vector2(-4f, 6f), 1.3f, "rest");
            int frogRocks = 0;
            for (int i = 0; i < 16 && frogRocks < 2; i++)
            {
                float a = i / 16f * Mathf.PI * 2f + 0.3f;
                if (FlatRock(pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * 1.12f, 1.0f, "froggy")) frogRocks++;
            }

            // 70. 目覚めの苔原のしずくのそばの、小さな花
            for (int i = 0; i < 3 && i < DewdropPoints.Count; i++)
            {
                Vector3 d = DewdropPoints[i];
                for (int k = 0; k < 2; k++)
                {
                    float a = (i * 2 + k) * 2.4f;
                    Vector2 p = new Vector2(d.x, d.z) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.95f;
                    if (!IsLand(p, 0.05f)) continue;
                    PutDeco(k == 0 ? "Daisy" : "Bellflower", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 0.2f, 4f), XR(0.55f, 0.7f), true, 80f);
                }
            }

            // 71. 丸太の上への小枝のスロープ
            {
                Vector2 top2 = ForestLayout.LogCenter - ForestLayout.LogDir * 11f;
                Vector3 g = ForestLayout.Ground(top2.x, top2.y);
                if (CastDown(g + Vector3.up * 12f, 14f, out var top) && top.collider.name.StartsWith("HollowLog"))
                {
                    Vector2 foot = top2 - ForestLayout.LogDir * 3f - perp * 8.5f;
                    TwigSpan(ForestLayout.Ground(foot.x, foot.y) + Vector3.up * 0.15f, top.point + Vector3.up * 0.05f - new Vector3(perp.x, 0f, perp.y) * 0.8f, "ramp_log");
                }
            }

            // 72. 水たまりの南の岸から、葉っぱの舟の方への飛び石
            if (_lilyPads.Count > 5)
            {
                Vector3 pad = _lilyPads[5];
                Vector2 shore = pond + new Vector2(2.5f, -R * 0.97f);
                Vector2 to = new Vector2(pad.x, pad.z);
                float len = Vector2.Distance(shore, to);
                for (float s = 1.6f; s < len - 2.2f; s += 2.2f)
                {
                    Vector2 p = Vector2.Lerp(shore, to, s / len);
                    if (ForestLayout.Height(p.x, p.y) > ForestLayout.WaterLevel - 0.05f) continue;
                    bool nearDew = false;
                    foreach (var d in DewdropPoints) if (Vector2.Distance(new Vector2(d.x, d.z), p) < 2.2f) nearDew = true;
                    if (nearDew || !IsFree(p, 0.9f)) continue;
                    Mesh m = assets.Get("RiverStone_C");
                    float h = m != null ? m.bounds.max.y : 0.4f;
                    Vector3 sp = new Vector3(p.x, ForestLayout.WaterLevel + 0.22f - h, p.y);
                    PutSolid("RiverStone_C", prop, sp, Quaternion.Euler(0f, XR(0f, 360f), 0f), 1f, 1f);
                    StoneFooting(sp, "RiverStone_C", 1f);   // 水たまりの底までとどく石
                    Mark("pondstone", new Vector3(p.x, ForestLayout.WaterLevel, p.y));
                }
            }

            // 74. 苔の丘のてっぺんの、見晴らしのよい平たい岩
            FlatRock(ForestLayout.MossHill + new Vector2(1.5f, -1.5f), 1.6f, "view");

            // 75. 大樹の根の上への小枝のはしご
            for (int k = 0; k < 60; k++)
            {
                float a = Mathf.Lerp(Mathf.PI * 1.3f, Mathf.PI * 1.7f, k / 60f);
                Vector2 p = ForestLayout.GreatTree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (25f + (k % 4) * 2f);
                if (p.magnitude > ForestLayout.PlayRadius - 6f) continue;
                if (!CastDown(new Vector3(p.x, 150f, p.y), 200f, out var hit) || !hit.collider.name.StartsWith("GreatTree")) continue;
                if (hit.point.y < ForestLayout.Height(p.x, p.y) + 2.5f || hit.normal.y < 0.6f) continue;
                Vector2 away = (p - ForestLayout.GreatTree).normalized;
                Vector2 foot = p + away * 7f;
                if (!IsLand(foot, 0.1f) || !ExtraOk(foot, 1f, true)) continue;
                TwigSpan(ForestLayout.Ground(foot.x, foot.y) + Vector3.up * 0.15f, hit.point + Vector3.up * 0.05f, "ladder_tree");
                break;
            }

            // 76. 赤キノコのかさとかさのあいだの小枝の橋
            if (_redCaps.Count >= 4)
            {
                Vector3 a = _redCaps[0].pos, b = _redCaps[3].pos;
                if (CastDown(a + Vector3.up * 30f, 40f, out var ta) && CastDown(b + Vector3.up * 30f, 40f, out var tb)
                    && ta.collider.name.StartsWith("Mushroom") && tb.collider.name.StartsWith("Mushroom") && Mathf.Abs(ta.point.y - tb.point.y) < 3f)
                    TwigSpan(ta.point + Vector3.up * 0.1f, tb.point + Vector3.up * 0.1f, "bridge_caps");
            }

            // 77. 花の草原のまん中の、大きな葉っぱの足場
            {
                Vector2 p = ForestLayout.Meadow + new Vector2(1.5f, -2.5f);
                if (ExtraOk(p, 1.8f, true))
                {
                    PlaceLoose("Leaf_Oak_Green", prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.04f, GroundRotation(p, XR(0f, 360f), 1f, 2f), 1.45f, LooseProps.Shape.BigLeaf, true, 120f);
                    Occupy(p, 2f);
                    Mark("meadowleaf", ForestLayout.Ground(p.x, p.y));
                }
            }

            // 80. 水たまりの岸の、水面へつき出した平たい石
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Deg2Rad * (200f + i * 12f);
                if (FlatRock(pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * 0.9f, 1.4f, "peek", true)) break;
            }

            // 81. 外周の大岩の手前の、低い石の列
            BoundaryStones(61.5f, 64, p => Vector2.Distance(p, ForestLayout.GreatTree) < 22f);

            // 83. 大樹へ向かう小道のそばの、見わたせる岩
            {
                Vector2 p = new Vector2(5.5f, 17f);
                if (ExtraOk(p, 1.6f, true))
                {
                    PutSolid("Rock_C", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, Quaternion.Euler(0f, 30f, 0f), 1.5f, 2f);
                    Mark("view", ForestLayout.Ground(p.x, p.y));
                }
            }

            // 85. 苔原のそばの、押して転がせるどんぐり
            {
                // 景色の通り道（大樹を見上げる向き）をよけて、苔原の西がわ
                if (FindSpot(ForestLayout.Spawn + new Vector2(-5.2f, 2.5f), 3f, 0.6f, true, out var p, q => IsLand(q, 0.1f) && ForestLayout.TrailMask(q.x, q.y) < 0.3f))
                {
                    Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.48f;
                    var acorn = Place("Acorn", assets.propGlossy, pos, Quaternion.Euler(0f, 30f, 0f) * Quaternion.Euler(0f, 0f, 88f), 1.1f, true, true, 150f, true);
                    if (acorn != null) RollingProp.Make(acorn, assets.Get("Acorn"), 1.1f, 0.47f * 1.1f, Area);
                    Occupy(p, 0.8f);
                    Mark("toyacorn", pos);
                }
            }

            // 99. 外周の大岩の上の苔
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(65f, 71f);
                if (CastDown(new Vector3(p.x, 80f, p.y), 120f, out var top) && top.collider.name.StartsWith("Rock") && top.point.y > ForestLayout.Height(p.x, p.y) + 1.5f)
                    PutDeco("Moss", fol, top.point + Vector3.down * 0.08f, Quaternion.FromToRotation(Vector3.up, top.normal) * Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(1.2f, 2f), false, 260f);
            }
        }

        Vector2 RandomXInCircle(Vector2 c, float r)
        {
            float a = XR(0f, Mathf.PI * 2f);
            return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Mathf.Sqrt(XR01()) * r);
        }

        Vector2 RandomXInRing(float r0, float r1)
        {
            float a = XR(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Sqrt(Mathf.Lerp(r0 * r0, r1 * r1, XR01()));
        }

        /// <summary>
        /// クモの巣を張る：c のまわりをさがして、まん中から面にそって糸をのばし、とどいた所（地面・キノコの柄・岩・幹）を支えにする。
        /// 地面と物、物と物のように支えが 2 つ以上あって、まわりをかこむように支えがあるときだけ張る。見た目だけで、引っかからない。
        /// </summary>
        bool StringWebNear(System.Random wr, Vector2 c, float spread, float h0, float h1, float maxR, Vector3 normal, float yawJitter, string kind)
        {
            float Rnd(float a, float b) => a + (float)wr.NextDouble() * (b - a);
            WebPlan best = null;
            for (int t = 0; t < 120; t++)
            {
                float ang = Rnd(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rnd(0f, 1f)) * spread;
                Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d;
                if (!WebSpotOk(p)) continue;
                Vector3 n = normal.sqrMagnitude > 0.5f ? Quaternion.Euler(0f, Rnd(-yawJitter, yawJitter), 0f) * normal
                    : Quaternion.Euler(0f, Rnd(0f, 180f), 0f) * Vector3.forward;
                n = Quaternion.AngleAxis(Rnd(-8f, 8f), Vector3.Cross(n, Vector3.up)) * n;   // 少しだけ、前後へかたむく
                Vector3 hub = Area.Ground(p.x, p.y) + Vector3.up * Rnd(h0, h1);
                var plan = PlanWeb(hub, n, maxR);
                if (plan != null && (best == null || plan.score > best.score)) best = plan;
            }
            if (best == null) return false;
            BuildWeb(best, wr);
            Mark("web", best.hub);
            Mark(kind, best.hub);
            return true;
        }

        bool WebSpotOk(Vector2 p)
        {
            if (p.magnitude > Area.PlayRadius - 2f) return false;
            foreach (var d in DewdropPoints)
                if ((new Vector2(d.x, d.z) - p).sqrMagnitude < 1.6f * 1.6f) return false;
            if (Area.TrailMask(p.x, p.y) > 0.35f) return false;   // 小道の上には張らない（通ると、くぐりぬけてしまう）
            foreach (var w in ExtraSpots("web"))
                if ((new Vector2(w.x, w.z) - p).sqrMagnitude < 6f * 6f) return false;
            return true;
        }

        class WebPlan
        {
            public Vector3 hub, u, r, n;
            public readonly List<Vector2> frame = new List<Vector2>();   // 面の上の、支えにとどいた所（角度の順）
            public readonly List<float> angles = new List<float>();
            public float score;
        }

        /// <summary>まん中から 28 方向へ糸をのばして、支えをさがす。張れないときは null。</summary>
        static WebPlan PlanWeb(Vector3 hub, Vector3 normal, float maxR)
        {
            if (Physics.CheckSphere(hub, 0.18f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) return null;
            Vector3 u = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (u.sqrMagnitude < 0.25f) return null;
            u.Normalize();
            Vector3 r = Vector3.Cross(normal, u).normalized;
            var plan = new WebPlan { hub = hub, u = u, r = r, n = normal.normalized };
            const int N = 28;
            bool ground = false, other = false;
            Collider first = null;
            bool two = false;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 d = r * Mathf.Cos(a) + u * Mathf.Sin(a);
                if (!Physics.Raycast(hub, d, out var hit, maxR, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.rigidbody != null) continue;   // ゆれる物・動く物には張らない
                if (hit.collider.name.StartsWith("Terrain_")) ground = true; else other = true;
                if (first == null) first = hit.collider;
                else if (hit.collider != first) two = true;
                plan.frame.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Max(0.05f, hit.distance - 0.015f));
                plan.angles.Add(a);
            }
            int k = plan.frame.Count;
            if (k < 8 || !other || !two) return null;
            float maxGap = 0f;
            for (int i = 0; i < k; i++)
            {
                float g = (i + 1 < k ? plan.angles[i + 1] : plan.angles[0] + Mathf.PI * 2f) - plan.angles[i];
                maxGap = Mathf.Max(maxGap, g);
            }
            if (maxGap > 150f * Mathf.Deg2Rad) return null;
            // まん中から枠の糸までの近さ（近すぎると、つぶれた形になる）
            float margin = float.MaxValue, mean = 0f;
            for (int i = 0; i < k; i++)
            {
                Vector2 a = plan.frame[i], b = plan.frame[(i + 1) % k];
                margin = Mathf.Min(margin, SegmentDistance(Vector2.zero, a, b));
                mean += a.magnitude / k;
            }
            if (margin < 0.3f || mean < 0.55f) return null;
            // 細長い帯のような形は、張らない（たて・よこの広がりが近い形だけ）
            Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
            foreach (var q in plan.frame) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            Vector2 ext = hi - lo;
            if (Mathf.Min(ext.x, ext.y) < 0.45f * Mathf.Max(ext.x, ext.y)) return null;
            plan.score = margin + (ground ? 0.3f : 0f) - maxGap * 0.15f + Mathf.Min(mean, 1.6f) * 0.3f;
            return plan;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>枠の糸・まん中から放射状の糸・うずまきの横糸・つゆの玉を、細い糸のメッシュにする。</summary>
        void BuildWeb(WebPlan w, System.Random wr)
        {
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var col = new List<Color>();
            var tri = new List<int>();
            Vector3 P(Vector2 q) => w.r * q.x + w.u * q.y;
            Color Silk(float sway) => new Color(0.93f, 0.96f, 0.98f, sway);
            void Thread(Vector2 a2, Vector2 b2, float width, float swayA, float swayB)
            {
                Vector3 a = P(a2), b = P(b2);
                Vector3 t = b - a;
                if (t.sqrMagnitude < 1e-6f) return;
                t.Normalize();
                Vector3 side = Vector3.Cross(t, w.n).normalized;
                // 十字に組んだ 2 枚の細い帯（どこから見ても糸が見える）
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector3 off = pass == 0 ? w.n * width : side * width;
                    Vector3 nn = pass == 0 ? side : w.n;
                    int i0 = v.Count;
                    v.Add(a - off); v.Add(a + off); v.Add(b + off); v.Add(b - off);
                    for (int j = 0; j < 4; j++) nrm.Add(nn);
                    col.Add(Silk(swayA)); col.Add(Silk(swayA)); col.Add(Silk(swayB)); col.Add(Silk(swayB));
                    tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2); tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 3);
                }
            }
            void Bead(Vector2 q, float size, float sway)
            {
                Vector3 c = P(q);
                int i0 = v.Count;
                Vector3[] dirs = { w.r, -w.r, w.u, -w.u, w.n, -w.n };
                foreach (var d in dirs) { v.Add(c + d * size); nrm.Add(d); col.Add(new Color(0.86f, 0.95f, 1f, sway)); }
                int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                foreach (int x in f) tri.Add(i0 + x);
                Mark("webdew", w.hub + c);
            }
            var fr = w.frame;
            int k = fr.Count;
            // 枠の糸（支えにとどいた所をつなぐ。支えにふれているので、ゆれない）
            for (int i = 0; i < k; i++) Thread(fr[i], fr[(i + 1) % k], 0.0075f, 0f, 0f);
            // 放射状の糸：まん中から枠の糸まで
            const int spokes = 13;
            float a0 = (float)wr.NextDouble() * Mathf.PI * 2f;
            var ends = new List<Vector2>();
            for (int s = 0; s < spokes; s++)
            {
                float a = a0 + s * Mathf.PI * 2f / spokes + ((float)wr.NextDouble() - 0.5f) * 0.12f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float hitT = float.MaxValue;
                for (int i = 0; i < k; i++)
                {
                    Vector2 p = fr[i], e = fr[(i + 1) % k] - p;
                    float den = d.x * e.y - d.y * e.x;
                    if (Mathf.Abs(den) < 1e-6f) continue;
                    float tt = (p.x * e.y - p.y * e.x) / den;
                    float uu = (p.x * d.y - p.y * d.x) / den;
                    if (tt > 0f && uu >= -0.001f && uu <= 1.001f) hitT = Mathf.Min(hitT, tt);
                }
                if (hitT == float.MaxValue) continue;
                ends.Add(d * hitT);
                Thread(Vector2.zero, d * hitT, 0.0055f, 0.16f, 0f);
            }
            // うずまきの横糸（まん中のすぐまわりは、あけておく）
            int m = ends.Count;
            for (int ring = 0; ring < 9; ring++)
            {
                for (int s = 0; s < m; s++)
                {
                    float fa = 0.2f + (ring + (float)s / m) * 0.085f, fb = 0.2f + (ring + (float)(s + 1) / m) * 0.085f;
                    if (fb > 0.97f) continue;
                    Vector2 a = ends[s] * fa, b = ends[(s + 1) % m] * fb;
                    Thread(a, b, 0.004f, 0.16f * (1f - fa), 0.16f * (1f - fb));
                    if (wr.NextDouble() < 0.12)
                        Bead(Vector2.Lerp(a, b, 0.3f + 0.4f * (float)wr.NextDouble()), 0.022f + 0.015f * (float)wr.NextDouble(), 0.16f * (1f - fa));
                }
            }
            // まん中の小さな台
            for (int s = 0; s < m; s++) Thread(ends[s] * 0.07f, ends[(s + 1) % m] * 0.07f, 0.005f, 0.16f, 0.16f);

            var mesh = Own(new Mesh { name = "SpiderWeb" });
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            instanced.Add(mesh, assets.foliage, Matrix4x4.TRS(w.hub, Quaternion.identity, Vector3.one), false, 70f);
        }

        /// <summary>ホコリタケ（ふむと弾んで、胞子が出る）。</summary>
        void Puffballs(Vector2 center, float radius, int count)
        {
            int n = 0;
            for (int i = 0; i < count * 12 && n < count; i++)
            {
                Vector2 p = RandomXInCircle(center, radius);
                float s = XR(0.8f, 1.3f);
                if (!IsLand(p, 0.15f) || !ExtraOk(p, 1f * s, true)) continue;
                Vector3 g = Area.Ground(p.x, p.y);
                PutSolid("Mushroom_Puffball", assets.prop, g + Vector3.down * 0.05f, Quaternion.Euler(0f, XR(0f, 360f), 0f), s, 1.1f * s);
                Mark("puffball", g + Vector3.up * (1.55f * s));
                n++;
            }
        }
    }
}
