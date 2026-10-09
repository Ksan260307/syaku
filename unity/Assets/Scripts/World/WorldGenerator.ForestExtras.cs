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

            // 34. 草むらの小枝のあいだのクモの巣・35. 丸太のトンネルの入り口のクモの巣
            for (int i = 0; i < _twigs.Count && i < 7; i++)
            {
                Vector3 tw = _twigs[i];
                Vector2 p = new Vector2(tw.x, tw.z) + new Vector2(XR(-1.5f, 1.5f), XR(-1.5f, 1.5f));
                if (!ExtraOk(p, 0.5f, false)) continue;
                Web(Area.Ground(p.x, p.y) + Vector3.up * 1.3f, XR(0f, 360f), XR(0.5f, 0.65f));
            }
            for (int end = -1; end <= 1; end += 2)
            {
                Vector2 c = ForestLayout.LogCenter + ForestLayout.LogDir * (end * 25.5f) + perp * 4.6f;
                Web(ForestLayout.Ground(c.x, c.y) + Vector3.up * 2.4f, Mathf.Atan2(ForestLayout.LogDir.x, ForestLayout.LogDir.y) * Mathf.Rad2Deg, 0.8f);
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
                for (int k = 0; k < 4; k++)
                    PutDeco(XPick("Twig_A", "Twig_B"), prop, g + Vector3.up * (0.12f * k), Quaternion.Euler(XR(-6f, 6f), XR(0f, 360f), XR(-6f, 6f)), XR(0.22f, 0.32f), true, 120f);
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
                    PutDeco("Twig_B", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.08f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, XR(-4f, 4f)), XR(0.45f, 0.6f), true, 140f);
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
                    PutSolid("RiverStone_C", prop, new Vector3(p.x, ForestLayout.WaterLevel + 0.22f - h, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), 1f, 1f);
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
                if (FindSpot(ForestLayout.Spawn + new Vector2(-5.2f, 2.5f), 3f, 0.6f, true, out var p, q => IsLand(q, 0.1f)))
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

        /// <summary>クモの巣（たての面。見た目だけで、引っかからない）。</summary>
        void Web(Vector3 center, float yawDeg, float scale)
        {
            Place("SpiderWeb", assets.foliage, center, Quaternion.Euler(0f, yawDeg, XR(-8f, 8f)), scale, false, false, 70f);
            Mark("web", center);
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
