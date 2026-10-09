using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>公園の改善：砂の城・ビー玉・積み木の階段・じょうろ・紙ひこうき・シロツメクサ・水たまりの飛び石など。</summary>
    public partial class WorldGenerator
    {
        /// <summary>公園の光の筋を足す場所（すべり台・ジャングルジム・ベンチのそば）。</summary>
        public static readonly Vector2[] ParkExtraShafts =
        {
            ParkLayout.Slide + new Vector2(8f, 5f), ParkLayout.JungleGym + new Vector2(4f, 9f), ParkLayout.Bench + new Vector2(-6f, -4f),
        };

        void BuildParkExtras()
        {
            Material prop = assets.prop, flw = assets.flowers, fol = assets.foliage;
            Material shiny = assets.propGlossy != null ? assets.propGlossy : prop;
            Vector2 sb = ParkLayout.Sandbox;

            // 5. 光の筋を足す
            BuildLightShafts(ParkExtraShafts);

            // 31. 砂場の砂の城（71. 登れる）・67. まわりの小さな砂山・78. 砂場のふちから城への飛び石
            Vector2 castle = Vector2.zero;
            Vector2 half = ParkLayout.SandboxSize * 0.5f - new Vector2(3f, 3f);
            System.Func<Vector2, bool> inside = q => Mathf.Abs(q.x - sb.x) < half.x && Mathf.Abs(q.y - sb.y) < half.y;
            // 砂山とバケツのあいだはせまいので、あいている所（砂場の南東・南のすみ）から、さがす
            if (FindSpot(sb + new Vector2(9.5f, -6f), 3f, 1.8f, true, out var cp, inside)
                || FindSpot(sb + new Vector2(2.5f, -6f), 3f, 1.8f, true, out cp, inside)
                || FindSpot(sb + new Vector2(-10f, 6f), 3f, 1.8f, true, out cp, inside))
            {
                PutSolid("Park_SandCastle", prop, ParkLayout.Ground(cp.x, cp.y) + Vector3.down * 0.1f, Quaternion.Euler(0f, XR(0f, 360f), 0f), 1f, 3f);
                Mark("castle", ParkLayout.Ground(cp.x, cp.y));
                castle = cp;
            }
            if (castle != Vector2.zero)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + 0.5f;
                    Vector2 p = castle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.6f;
                    if (InsideOccupied(p) || NearDew(p, 1.5f)) continue;
                    PutDeco("Park_SandMound", prop, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.12f, 0.18f), true, 120f);
                }
                // 砂場のわくのふちから、城へ
                Vector2 edge = new Vector2(castle.x, sb.y + Mathf.Sign(castle.y - sb.y) * (ParkLayout.SandboxSize.y * 0.5f - 0.6f));
                for (float t = 0.3f; t < 0.8f; t += 0.25f)
                {
                    Vector2 p = Vector2.Lerp(edge, castle, t);
                    if (NearDew(p, 1.6f) || !IsFree(p, 0.6f)) continue;
                    Mesh m = assets.Get("RiverStone_C");
                    float h = m != null ? m.bounds.max.y * 0.7f : 0.3f;
                    PutSolid("RiverStone_C", prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * (0.22f - h), Quaternion.Euler(0f, XR(0f, 360f), 0f), 0.7f, 0.8f);
                    Mark("castlepath", ParkLayout.Ground(p.x, p.y));
                }
            }

            // 32. 砂場のビー玉・33. 芝生のビー玉・62. ベンチのわきのビー玉（75. 押すと転がる）
            Marbles(sb, 9f, 3);
            Marbles(new Vector2(14f, -24f), 12f, 2);
            Marbles(new Vector2(-14f, 28f), 10f, 1);
            Marbles(ParkLayout.Bench + new Vector2(-4.5f, -9.5f), 2f, 1);

            // 34. ジャングルジムのそばの積み木の山（72. 登れる）・80. ジャングルジムの下の段へ登りやすい積み木
            foreach (var off in new[] { new Vector2(-2f, 12.5f), new Vector2(12.5f, -2f), new Vector2(12f, 9f) })
                if (BlockStack(ParkLayout.JungleGym + off, true)) break;
            {
                Vector2 dir = (-ParkLayout.JungleGym).normalized;
                Block(ParkLayout.JungleGym + dir * 7.2f, 0.75f, "gymstep");
            }
            // 35. ベンチのそばの積み木・73. 積み木の階段でベンチの座面へ
            Block(ParkLayout.Bench + new Vector2(-4.6f, 4.2f), 0.62f, "benchstep");
            Block(ParkLayout.Bench + new Vector2(-3.2f, 4.2f), 1.0f, "benchstep");
            // 53. シーソーのそばの積み木・81. シーソーの両はしのそばの、乗り降りしやすい積み木
            Block(ParkLayout.Seesaw + new Vector2(0f, 5.2f), 0.8f, "block");
            Block(ParkLayout.Seesaw + new Vector2(-9.5f, 3.6f), 0.6f, "seesawstep");
            Block(ParkLayout.Seesaw + new Vector2(9.5f, 3.6f), 0.6f, "seesawstep");
            // 68. タイヤのそばの積み木・74. 積み木の階段でタイヤの上へ
            Block(ParkLayout.Tires + new Vector2(0f, -4.3f), 0.9f, "tirestep");

            // 36. 花だんのそばのじょうろ（76. 登れる）
            foreach (var off in new[] { new Vector2(13.5f, -3f), new Vector2(-13.5f, 2.5f), new Vector2(12f, 6.5f) })
            {
                Vector2 p = ParkLayout.FlowerBed + off;
                if (!ExtraOk(p, 2f, true)) continue;
                PutSolid("Park_WateringCan", prop, ParkLayout.Ground(p.x, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), 1f, 2.4f);
                Mark("can", ParkLayout.Ground(p.x, p.y));
                break;
            }

            // 37. 芝生の紙ひこうき・38. すべり台の出口のそばの紙ひこうき（77. 上に乗れる）
            int planes = 0;
            for (int i = 0; i < 30 && planes < 2; i++)
            {
                Vector2 p = RandomXInRing(12f, 50f);
                if (OnPlayground(p) || !IsLand(p, 0.2f) || !ExtraOk(p, 2.2f, true)) continue;
                PutSolid("Park_PaperPlane", prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.02f, GroundRotation(p, XR(0f, 360f), 1f, 3f), 1f, 2.2f);
                Mark("plane", ParkLayout.Ground(p.x, p.y));
                planes++;
            }
            {
                Vector2 p = ParkLayout.Slide + new Vector2(25f, 3.5f);
                if (ExtraOk(p, 2.2f, true))
                {
                    PutSolid("Park_PaperPlane", prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.02f, Quaternion.Euler(0f, 70f, 0f), 0.9f, 2.2f);
                    Mark("plane", ParkLayout.Ground(p.x, p.y));
                }
            }

            // 39. 芝生のシロツメクサの群れ・40. さくのそばのシロツメクサとタンポポ・59. キャベツのそばのシロツメクサ
            foreach (var c in new[] { new Vector2(22f, 18f), new Vector2(-18f, -4f), new Vector2(-38f, 6f), new Vector2(30f, -38f), new Vector2(-6f, 52f) })
            {
                if (ScatterDeco(c, 3.5f, 9, 0.7f, 1.0f, flw, 0.05f, "Park_WhiteClover") > 0) Mark("clover", ParkLayout.Ground(c.x, c.y));
            }
            for (int i = 0; i < 30; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(56.5f, 59.5f);
                if (!IsLand(p, 0.2f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco(XR01() < 0.5f ? "Park_WhiteClover" : "Dandelion", flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 0.2f, 5f), XR(0.7f, 1f), true, 110f);
            }
            {
                Vector2 bed = ParkLayout.FlowerBed;
                float bedTop = ParkLayout.Height(bed.x, bed.y);
                foreach (float dx in new[] { -4.3f, 4.3f })
                    PutDeco("Park_WhiteClover", flw, new Vector3(bed.x + dx + 1.1f, bedTop - 0.05f, bed.y + 1.2f), Quaternion.Euler(0f, XR(0f, 360f), 0f), 0.75f, true, 120f);
            }

            // 41. クヌギの根もとのどんぐり（押すと転がる）・58. クヌギのまわりの小枝
            Vector2 k = ParkLayout.Kunugi;
            int acorns = 0;
            for (int i = 0; i < 30 && acorns < 6; i++)
            {
                Vector2 p = RandomXInCircle(k, 13f);
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.6f, true)) continue;
                float s = XR(0.9f, 1.2f);
                Vector3 pos = ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.42f * s;
                var acorn = Place("Acorn", assets.propGlossy, pos, Quaternion.Euler(0f, XR(0f, 360f), 0f) * Quaternion.Euler(0f, 0f, 88f), s, true, true, 150f, true);
                if (acorn != null) RollingProp.Make(acorn, assets.Get("Acorn"), s, 0.47f * s, Area);
                Occupy(p, 0.7f * s);
                acorns++;
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = RandomXInCircle(k, 15f);
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 1f, false)) continue;
                PutDeco(XPick("Twig_A", "Twig_B"), prop, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.08f, GroundRotation(p, XR(0f, 360f), 0.8f, 2f), XR(0.3f, 0.5f), true, 120f);
            }

            // 42. ベンチの下の落ち葉・60. 公園のすみの落ち葉の山・61. 土管の上の落ち葉
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = ParkLayout.Bench + new Vector2(XR(-3f, 3f), XR(-9f, 9f));
                if (!IsLand(p, 0.05f) || NearDew(p, 0.6f)) continue;
                PlaceLoose(XPick("Leaf_Oak_Brown", "Leaf_Oak_Orange"), prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, XR(0f, 360f), 1f, 6f), XR(0.08f, 0.15f), LooseProps.Shape.Leaf, false, 40f);
            }
            {
                Vector2 c = new Vector2(-46f, -30f);
                for (int i = 0; i < 60; i++)
                {
                    Vector2 p = RandomXInCircle(c, 3.5f);
                    if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.1f, false)) continue;
                    Vector3 g = ParkLayout.Ground(p.x, p.y) + Vector3.up * (0.01f + 0.25f * Mathf.Max(0f, 1f - Vector2.Distance(p, c) / 3.5f));
                    PlaceLoose(XPick(BigLeaves), prop, g, GroundRotation(p, XR(0f, 360f), 1f, 10f), XR(0.1f, 0.18f), LooseProps.Shape.Leaf, false, 50f);
                }
                Mark("leafpile", ParkLayout.Ground(c.x, c.y));
            }
            for (int i = -2; i <= 2; i++)
            {
                Vector2 p = ParkLayout.Dokan + new Vector2(i * 1.9f + XR(-0.4f, 0.4f), XR(-0.6f, 0.6f));
                if (CastDown(new Vector3(p.x, 30f, p.y), 40f, out var top) && top.collider.name.StartsWith("Park_Dokan"))
                    PutDeco(XPick("Leaf_Oak_Brown", "Leaf_Maple_Yellow"), prop, top.point + Vector3.up * 0.02f, Quaternion.FromToRotation(Vector3.up, top.normal) * Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.12f, 0.2f), false, 80f);
            }

            // 43. 街灯の根もとの小さな花とクローバー・66. 水飲み場の根もとの苔
            ScatterDeco(ParkLayout.Lamp, 2.6f, 8, 0.6f, 0.9f, flw, 0.05f, "Daisy", "Bellflower");
            ScatterDeco(ParkLayout.Lamp, 2.6f, 6, 0.6f, 0.9f, fol, 0f, "Clover_A", "Clover_B");
            for (int i = 0; i < 14; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = ParkLayout.Fountain + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(1.6f, 3.2f);
                if (!IsLand(p, 0.02f) || NearDew(p, 0.6f)) continue;
                PutDeco("Moss", fol, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 1f, 3f), XR(0.4f, 0.8f), false, 70f);
            }
            // 44. 水飲み場のまわりの小石
            ScatterLoose(ParkLayout.Fountain + new Vector2(0f, -1f), 4.5f, 14, 0.07f, 0.16f, LooseProps.Shape.Pebble, Rocks);
            // 45. 水たまりのふちの水草
            for (int i = 0; i < 6; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                WaterGrassAt(ParkLayout.Puddle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(3.2f, 4.4f));
            }
            // 46. 土管のまわりの背の高い草・47. タイヤのまわりのタンポポ・69. すべり台のはしごの下のタンポポ
            for (int i = 0; i < 24; i++)
            {
                Vector2 p = ParkLayout.Dokan + new Vector2(XR(-9f, 9f), XR(-1f, 1f) * XR(3.4f, 5f));
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco("Grass_C", fol, ParkLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.5f, 5f), XR(0.8f, 1.2f), false, 70f);
            }
            ScatterDeco(ParkLayout.Tires, 11f, 12, 0.6f, 0.9f, flw, 0.05f, "Dandelion", "DandelionPuff");
            ScatterDeco(ParkLayout.Slide + new Vector2(-5.5f, 0f), 2.2f, 6, 0.6f, 0.85f, flw, 0.05f, "Dandelion");

            // 48. 小道のふちの小石・49. 小道の分かれ道の目じるし（82. 登れる）
            foreach (var trail in ParkLayout.Trails)
                for (int i = 0; i < trail.Length - 1; i++)
                {
                    Vector2 a = trail[i], b = trail[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
                    for (float s = 1f; s < len; s += XR(2.5f, 4f))
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / len) + n * (XR01() < 0.5f ? -1f : 1f) * XR(2.4f, 3f);
                        if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.2f, false)) continue;
                        PlaceLoose(XPick(Rocks), prop, ParkLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f)), XR(0.08f, 0.18f), LooseProps.Shape.Pebble, false, 50f);
                    }
                }
            Cairn(new Vector2(4f, -6f) + new Vector2(-3f, -3.2f));
            Cairn(new Vector2(-12f, 14f) + new Vector2(-3f, -3f));
            Cairn(new Vector2(36f, -4f) + new Vector2(-1f, -3.6f));

            // 50. 遠景の森・51. 遠くの大きな木の幹
            DistantCanopy(10, 105f, 140f, p => false);
            MoreBackgroundTrunks(8, 88f, 118f, p => false);

            // 52. 花だんのふちの外の、こぼれ種から咲いたチューリップ
            for (int i = 0; i < 10; i++)
            {
                Vector2 bed = ParkLayout.FlowerBed;
                float sx = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = bed + (XR01() < 0.5f
                    ? new Vector2(sx * (ParkLayout.BedSize.x * 0.5f + XR(1.4f, 2.6f)), XR(-4f, 4f))
                    : new Vector2(XR(-10f, 10f), sx * (ParkLayout.BedSize.y * 0.5f + XR(1.4f, 2.4f))));
                if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco(Tulips[_xr.Next(Tulips.Length)], flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, Quaternion.Euler(XR(-5f, 5f), XR(0f, 360f), XR(-5f, 5f)), XR(0.7f, 0.9f), true, 120f);
            }

            // 54. ブランコの下の小石・85. ブランコの座板の下の平たい石・63. ジャングルジムの下の小石
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 c = ParkLayout.Swing + new Vector2(s * 4.6f, 0f);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 p = c + new Vector2(XR(-2.2f, 2.2f), XR(-1.6f, 1.6f));
                    if (NearDew(p, 0.6f)) continue;
                    PlaceLoose(XPick(Rocks), prop, ParkLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f)), XR(0.06f, 0.12f), LooseProps.Shape.Pebble, false, 40f);
                }
                ForcedFlatRock(c + new Vector2(0f, 1.6f), 0.75f, "swingstep");
            }
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = ParkLayout.JungleGym + new Vector2(XR(-6f, 6f), XR(-6f, 6f));
                if (NearDew(p, 0.6f)) continue;
                PlaceLoose(XPick(Rocks), prop, ParkLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f)), XR(0.06f, 0.12f), LooseProps.Shape.Pebble, false, 40f);
            }

            // 55. 砂場のふちのスコップ
            {
                Vector2 p = sb + new Vector2(-6.5f, 7.2f);
                if (ExtraOk(p, 1.4f, true))
                {
                    PutSolid("Park_Shovel", shiny, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.15f, Quaternion.Euler(0f, 20f, 0f), 0.9f, 1.8f);
                    Mark("shovel", ParkLayout.Ground(p.x, p.y));
                }
            }

            // 56. 森へのトンネルのまわりのシダと苔・57. 芝生のデイジー・64. さくの内がわのシダ・65. 芝生のクローバーの群れ
            Vector2 gin = ParkLayout.Gate + (-ParkLayout.Gate).normalized * 9f;
            ScatterDeco(gin + new Vector2(0f, 5f), 3f, 6, 0.6f, 1f, flw, 0.3f, "Fern_A", "Fern_B");
            ScatterDeco(gin + new Vector2(0f, -5f), 3f, 10, 0.5f, 1.1f, fol, 0.05f, "Moss");
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = RandomXInRing(6f, 55f);
                if (OnPlayground(p) || InBed(p, -1f) || !IsLand(p, 0.2f) || !ExtraOk(p, 0.2f, false)) continue;
                PutDeco("Daisy", flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 0.2f, 5f), XR(0.55f, 0.85f), true, 110f);
            }
            for (int i = 0; i < 26; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(57f, 60.5f);
                if (Vector2.Distance(p, ParkLayout.Gate) < 8f || !IsLand(p, 0.2f) || !ExtraOk(p, 0.4f, false)) continue;
                PutDeco(XPick("Fern_A", "Fern_B"), flw, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.6f, 1f), true, 180f);
            }
            foreach (var c in new[] { new Vector2(-30f, -36f), new Vector2(40f, 28f), new Vector2(-40f, 46f) })
                ScatterDeco(c, 4f, 22, 0.7f, 1.1f, fol, 0f, "Clover_A", "Clover_B");

            // 70. 公園の入り口のそばのデイジーの花畑
            ScatterDeco(ParkLayout.Spawn + new Vector2(-8f, 6f), 3.5f, 18, 0.6f, 0.9f, flw, 0.05f, "Daisy");

            // 79. 水たまりの中の、平たい石の飛び石
            {
                Vector2 a = ParkLayout.Puddle + new Vector2(-ParkLayout.PuddleRadius - 0.5f, -1f);
                Vector2 b = ParkLayout.Puddle + new Vector2(ParkLayout.PuddleRadius + 0.5f, 1f);
                for (int i = 1; i <= 4; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / 5f);
                    if (NearDew(p, 1.4f)) continue;   // 水たまりの中（水たまり全体は、ほかの物を置かないように使ったことにしてある）
                    Mesh m = assets.Get("RiverStone_C");
                    float h = m != null ? m.bounds.max.y * 0.75f : 0.3f;
                    PutSolid("RiverStone_C", prop, new Vector3(p.x, ParkLayout.WaterLevel + 0.18f - h, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), 0.75f, 0.9f);
                    Mark("puddlestone", new Vector3(p.x, ParkLayout.WaterLevel, p.y));
                }
            }

            // 83. 土管の入り口の、平たい石の段
            for (int s = -1; s <= 1; s += 2)
                ForcedFlatRock(ParkLayout.Dokan + new Vector2(s * 6.3f, 1.3f), 0.8f, "dokanstep");

            // 84. クヌギの根もとの、根っこのような小枝（幹へ取りつきやすい）
            {
                Vector2 dir = (-ParkLayout.Kunugi).normalized;
                Vector3 kg = ParkLayout.Ground(k.x, k.y);
                Vector3 from = kg + new Vector3(dir.x, 0f, dir.y) * 14f + Vector3.up * 2.4f;
                if (Physics.Raycast(from, -new Vector3(dir.x, 0f, dir.y), out var hit, 16f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.name.StartsWith("Park_Kunugi"))
                {
                    Vector2 foot = new Vector2(hit.point.x, hit.point.z) + dir * 5.5f;
                    if (!NearDew(foot, 2f))
                        TwigSpan(ParkLayout.Ground(foot.x, foot.y) + Vector3.up * 0.15f, hit.point + new Vector3(dir.x, 0f, dir.y) * 0.3f, "kunugiroot");
                }
            }
        }

        /// <summary>ビー玉（押すと転がる）を、中心のまわりに置く。</summary>
        void Marbles(Vector2 center, float radius, int count)
        {
            int n = 0;
            for (int i = 0; i < count * 14 && n < count; i++)
            {
                Vector2 p = RandomXInCircle(center, radius);
                if (!IsLand(p, 0.05f) || !ExtraOk(p, 0.6f, true)) continue;
                var go = Place("Park_Marble", assets.propGlossy, ParkLayout.Ground(p.x, p.y) + Vector3.up * 0.9f, Quaternion.Euler(XR(0f, 360f), XR(0f, 360f), 0f), 1f, true, true, 140f, true);
                if (go != null) RollingProp.Make(go, assets.Get("Park_Marble"), 1f, 0.9f, Area, 1.3f);   // ガラス（2.5 g/cm³）の玉は、箱の体積の半分ほど
                Occupy(p, 1f);
                Mark("marble", ParkLayout.Ground(p.x, p.y));
                n++;
            }
        }

        /// <summary>積み木（立方体）。scale 1 で、高さ 2.6。しずくのそばには置かない。</summary>
        bool Block(Vector2 p, float scale, string kind)
        {
            if (NearDew(p, 2.2f + scale) || !IsLand(p, 0.1f)) return false;
            PutSolid("Park_Block_Cube", assets.prop, ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, Quaternion.Euler(0f, XR(-12f, 12f), 0f), scale, 1.9f * scale);
            Mark(kind, ParkLayout.Ground(p.x, p.y));
            Mark("blocks", ParkLayout.Ground(p.x, p.y));
            return true;
        }

        /// <summary>積み木の山（立方体 2 つと、上に三角の屋根）。</summary>
        bool BlockStack(Vector2 p, bool roof)
        {
            if (!ExtraOk(p, 3f, true) || !IsLand(p, 0.1f)) return false;
            Vector3 g = ParkLayout.Ground(p.x, p.y) + Vector3.down * 0.05f;
            float yaw = XR(0f, 90f);
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Place("Park_Block_Cube", assets.prop, g, r, 1f, true);
            Place("Park_Block_Cube", assets.prop, g + r * new Vector3(2.65f, 0f, 0f), r, 1f, true);
            if (roof) Place("Park_Block_Roof", assets.prop, g + Vector3.up * 2.6f, r, 1f, true);
            Occupy(p, 3.6f);
            Mark("stack", g);
            Mark("blocks", g);
            return true;
        }

        /// <summary>遊具のすぐそばの平たい石（遊具の場所にも置く。しずくのそばはさける）。</summary>
        bool ForcedFlatRock(Vector2 p, float scale, string kind)
        {
            if (NearDew(p, 2f + scale) || !IsLand(p, 0.05f)) return false;
            Mesh m = assets.Get("RiverStone_C");
            float h = m != null ? m.bounds.max.y * scale : 0.3f;
            PutSolid("RiverStone_C", assets.prop, ParkLayout.Ground(p.x, p.y) + Vector3.up * (0.2f - h), Quaternion.Euler(0f, XR(0f, 360f), 0f), scale, 1f * scale);
            Mark(kind, ParkLayout.Ground(p.x, p.y));
            return true;
        }
    }
}
