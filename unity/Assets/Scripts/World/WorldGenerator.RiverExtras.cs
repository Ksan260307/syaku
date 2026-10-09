using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>川辺の改善：流木・水草・小石の浜・滝の岩の階段・滝の上のとびいし・下流の石の列など。</summary>
    public partial class WorldGenerator
    {
        /// <summary>滝の上の川の、もうひとつのとびいしの場所（Z）。</summary>
        public const float UpperStonesZ = 53f;

        /// <summary>川辺の光の筋を足す場所（とびいしの瀬・滝の上の台地）。</summary>
        public static Vector2[] RiverExtraShafts() => new[]
        {
            new Vector2(RiverLayout.CenterX(RiverLayout.StonesZ) - 6f, RiverLayout.StonesZ + 1f), new Vector2(-26f, 48f), new Vector2(RiverLayout.CenterX(UpperStonesZ) + 12f, UpperStonesZ - 4f),
        };

        void BuildRiverExtras()
        {
            Material prop = assets.prop, flw = assets.flowers, fol = assets.foliage;
            float fz = RiverLayout.FallZ, pz = RiverLayout.PoolZ;
            Vector2 ic = RiverLayout.IslandCenter;

            // 5. 光の筋を足す
            BuildLightShafts(RiverExtraShafts());

            // 31. 岸の流木・33. 滝つぼの岸の流木（73. 登れる）
            int drifts = 0;
            for (int i = 0; i < 160 && drifts < 4; i++)   // 山へのトンネルの景色の通り道にはかからないよう、多めにさがす
            {
                // 4 本目は、滝つぼの岸
                float z = drifts == 3 ? XR(fz - 10f, fz - 5f) : XR(-50f, 50f);
                float side = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(2.8f, 4.5f), z);
                if (p.magnitude > 60f || !IsLand(p, 0.2f) || !ExtraOk(p, 2.6f, true)) continue;
                Vector3 g = RiverLayout.Ground(p.x, p.y);
                PutSolid("DriftLog", prop, g + Vector3.down * 0.15f, GroundRotation(p, 90f + XR(-30f, 30f), 0.6f, 2f), XR(0.85f, 1.05f), 3.4f);
                Mark("drift", g);
                drifts++;
            }
            // 32. 中州に流れついた流木（74. 上からまわりの川を見わたせる）
            foreach (var off in new[] { new Vector2(1.6f, 3.2f), new Vector2(-1.4f, -3.4f), new Vector2(1.5f, -2.5f) })
            {
                Vector2 p = ic + off;
                if (RiverLayout.IslandRadius01(p.x, p.y) > 0.8f || !ExtraOk(p, 0.6f, true, ignoreMobs: true)) continue;
                Vector3 g = RiverLayout.Ground(p.x, p.y);
                PutSolid("DriftLog", prop, g + Vector3.down * 0.12f, GroundRotation(p, 20f, 0.6f, 2f), 0.42f, 1.6f);
                Mark("drift", g);
                Mark("islanddrift", g);
                break;
            }

            // 34. 岸の浅い所の水草・59. 橋の下の水ぎわの水草
            for (int i = 0; i < 16; i++)
            {
                float z = XR(-60f, 60f);
                if (Mathf.Abs(z - fz) < 4f || Mathf.Abs(z - RiverLayout.StonesZ) < 4f || Mathf.Abs(z - RiverLayout.IslandZ) < 9f) continue;
                float side = XR01() < 0.5f ? -1f : 1f;
                WaterGrassAt(new Vector2(RiverBankEdgeX(z, side) - side * XR(0.8f, 2.4f), z));
            }
            for (int side = -1; side <= 1; side += 2)
                WaterGrassAt(new Vector2(RiverBankEdgeX(RiverLayout.BridgeZ + 2.5f, side) - side * 1.2f, RiverLayout.BridgeZ + 2.5f));
            // 35. よどみの水草・43. よどみの睡蓮の花
            for (int i = 0; i < 6; i++)
                WaterGrassAt(new Vector2(RiverLayout.CenterX(pz) + XR(-12f, 12f), pz + XR(-12f, 12f)));
            float pwl = RiverLayout.WaterLevel(pz);
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = new Vector2(RiverLayout.CenterX(pz) + XR(-9f, 9f), pz + XR(-9f, 9f));
                if (RiverLayout.Height(p.x, p.y) > pwl - 0.3f) continue;
                PutDeco("WaterLily", flw, new Vector3(p.x, pwl + 0.12f, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.8f, 1.1f), false, 150f);
            }

            // 36. 岸の小石の浜・37. 中州のまわりの小石の浜・60. 岸の水ぎわの、押すと動く小石
            foreach (var (z, side) in new[] { (-28f, -1f), (-2f, 1f), (24f, -1f), (-50f, 1f) })
            {
                Vector2 c = new Vector2(RiverBankEdgeX(z, side) + side * 1.5f, z);
                ScatterLoose(c, 2.6f, 16, 0.08f, 0.3f, LooseProps.Shape.Pebble, RiverStones);
                Mark("beach", RiverLayout.Ground(c.x, c.y));
            }
            for (int i = 0; i < 18; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = ic + new Vector2(Mathf.Cos(a) * 4.2f, Mathf.Sin(a) * 7f) * XR(0.85f, 1f);
                if (!IsLand(p, 0.02f) || !ExtraOk(p, 0.2f, false)) continue;
                PlaceLoose(XPick(RiverStones), prop, RiverLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-20f, 20f), XR(0f, 360f), XR(-20f, 20f)), XR(0.08f, 0.24f), LooseProps.Shape.Pebble, false, 50f);
            }
            Mark("islandbeach", RiverLayout.Ground(ic.x, ic.y));
            for (int i = 0; i < 70; i++)
            {
                float z = XR(-62f, 62f);
                float side = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(0.2f, 2f), z);
                if (p.magnitude > 63f || !IsLand(p, 0.02f) || !ExtraOk(p, 0.2f, false)) continue;
                PlaceLoose(XPick(RiverStones), prop, RiverLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-20f, 20f), XR(0f, 360f), XR(-20f, 20f)), XR(0.08f, 0.28f), LooseProps.Shape.Pebble, false, 45f);
            }

            // 38. 滝の両わきの大岩の苔・61. 滝つぼのまわりのぬれた苔
            foreach (var r in _fallRocks)
                if (CastDown(r + Vector3.up * 10f, 14f, out var top) && top.collider.name.Contains("Stone"))
                    PutDeco("Moss", fol, top.point + Vector3.down * 0.08f, Quaternion.FromToRotation(Vector3.up, top.normal) * Quaternion.Euler(0f, XR(0f, 360f), 0f), 1.4f, false, 160f);
            for (int i = 0; i < 26; i++)
            {
                float side = XR01() < 0.5f ? -1f : 1f;
                float z = fz - XR(1f, 8f);
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(0.3f, 3f), z);
                if (!IsLand(p, 0.02f) || !ExtraOk(p, 0.2f, false)) continue;
                PutDeco("Moss", fol, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 1f, 3f), XR(0.6f, 1.2f), false, 80f);
            }
            // 39. 滝の段差の岩のすきまのシダ
            for (float x = -56f; x <= 56f; x += XR(5f, 8f))
            {
                Vector2 p = new Vector2(x, fz - 1.8f + XR(-0.8f, 0.8f));
                if (RiverLayout.InChannel(p.x, p.y, 3f) || p.magnitude > 62f || !ExtraOk(p, 0.4f, false)) continue;
                PutDeco(XPick("Fern_A", "Fern_B"), flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.6f, 0.95f), true, 180f);
            }
            // 40. 橋のたもとのスギナとカキツバタ
            foreach (var end in new[] { _bridgeA, _bridgeB })
                ScatterDeco(new Vector2(end.x, end.z), 3.5f, 8, 0.6f, 0.95f, flw, 0.1f, "Horsetail", "Iris");
            // 41. とびいしの瀬の両岸のガマ・63. 下流（南）の岸のガマ
            for (int side = -1; side <= 1; side += 2)
                ScatterDeco(new Vector2(RiverBankEdgeX(RiverLayout.StonesZ + 4f, side) + side * 2f, RiverLayout.StonesZ + 4f), 2.5f, 7, 0.55f, 0.9f, flw, 0.2f, "Reed");
            for (int i = 0; i < 14; i++)
            {
                float z = XR(-58f, -40f);
                float side = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(0.3f, 2.5f), z);
                if (p.magnitude > 63f || !IsLand(p, 0f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco("Reed", flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.2f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.55f, 0.95f), true, 130f);
                Mark("reed", RiverLayout.Ground(p.x, p.y));
            }
            // 42. 中州のガマとカキツバタ
            for (int i = 0; i < 7; i++)
            {
                float a = XR(0f, Mathf.PI * 2f);
                Vector2 p = ic + new Vector2(Mathf.Cos(a) * 3.6f, Mathf.Sin(a) * 6.3f);
                if (!IsLand(p, 0f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco(XR01() < 0.6f ? "Reed" : "Iris", flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.15f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.45f, 0.75f), true, 130f);
            }

            // 44. 滝の上の台地のデイジーとタンポポ・45. 背の高い草むら・52. 滝の上の水ぎわのカキツバタ・64. 上流の岸の背の高い草とシダ
            for (int i = 0; i < 60; i++)
            {
                Vector2 p = new Vector2(XR(-58f, 58f), XR(fz + 4f, 62f));
                if (p.magnitude > 63f || RiverLayout.InChannel(p.x, p.y, 4f) || !IsLand(p, 0.3f) || !ExtraOk(p, 0.3f, false)) continue;
                int k = _xr.Next(10);
                if (k < 4) PutDeco(XPick("Daisy", "Dandelion"), flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, XR(0f, 360f), 0.2f, 5f), XR(0.7f, 1f), true, 110f);
                else if (k < 8) PutDeco("Grass_C", fol, RiverLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.5f, 5f), XR(1f, 1.4f), false, 70f);
                else PutDeco(XPick("Fern_A", "Fern_B"), flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.6f, 1f), true, 160f);
            }
            for (int i = 0; i < 10; i++)
            {
                float z = XR(fz + 3f, 60f);
                float side = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(0.3f, 1.8f), z);
                if (p.magnitude > 63f || !IsLand(p, 0f) || !ExtraOk(p, 0.3f, false)) continue;
                PutDeco("Iris", flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.6f, 0.95f), true, 120f);
            }

            // 46. 森へのトンネルのまわりのシダと苔
            Vector2 gin = RiverLayout.Gate + (-RiverLayout.Gate).normalized * 9f;
            ScatterDeco(gin, 5f, 8, 0.6f, 1f, flw, 0.3f, "Fern_A", "Fern_B");
            ScatterDeco(gin, 5f, 14, 0.5f, 1.1f, fol, 0.05f, "Moss");

            // 47. 岸の草むらの大きな落ち葉
            for (int i = 0; i < 16; i++)
            {
                Vector2 p = RandomXInRing(12f, 58f);
                if (!IsLand(p, 0.4f) || RiverLayout.InChannel(p.x, p.y, 3f) || !ExtraOk(p, 1.4f, true)) continue;
                PlaceLoose(XPick(BigLeaves), prop, RiverLayout.Ground(p.x, p.y) + Vector3.up * 0.04f, GroundRotation(p, XR(0f, 360f), 1f, 3f), XR(0.7f, 1.1f), LooseProps.Shape.BigLeaf, true, 120f);
                Occupy(p, 1.6f);
            }

            // 48. 下流の川の中の、水面から頭を出す石・69. とびいしの瀬の上流の、苔むした岩
            for (int i = 0; i < 5; i++)
            {
                float z = XR(-56f, -28f);
                Vector2 p = new Vector2(RiverLayout.CenterX(z) + XR(-5f, 5f), z);
                if (Mathf.Abs(z - pz) < 13f || !IsFree(p, 1.6f) || NearDew(p, 2.5f)) continue;
                float s = XR(0.9f, 1.4f);
                string mesh = XPick("RiverStone_A", "RiverStone_B");
                Vector3 sp = new Vector3(p.x, RiverLayout.WaterLevel(z) - 0.45f * s, p.y);
                PutSolid(mesh, prop, sp, Quaternion.Euler(XR(-8f, 8f), XR(0f, 360f), XR(-8f, 8f)), s, 1.4f * s);
                StoneFooting(sp, mesh, s);   // 川底までとどく石
                Mark("midstone", new Vector3(p.x, RiverLayout.WaterLevel(z), p.y));
            }
            {
                float z = RiverLayout.StonesZ + 6.5f;
                Vector2 p = new Vector2(RiverLayout.CenterX(z) + 2.5f, z);
                if (IsFree(p, 2f) && !NearDew(p, 3f))
                {
                    Vector3 pos = new Vector3(p.x, RiverLayout.WaterLevel(z) - 0.5f, p.y);
                    PutSolid("RiverStone_A", prop, pos, Quaternion.Euler(0f, XR(0f, 360f), 0f), 1.6f, 2.2f);
                    Physics.SyncTransforms();
                    if (CastDown(pos + Vector3.up * 6f, 8f, out var top))
                        PutDeco("Moss", fol, top.point + Vector3.down * 0.06f, Quaternion.FromToRotation(Vector3.up, top.normal), 1.1f, false, 150f);
                    Mark("mossrock", pos);
                }
            }

            // 49. 下流の浅瀬の、小さな石の列（わたれる）
            StoneRow(-58f, 2.1f, 0.9f, "weir");
            // 72. 滝の上の川の、もうひとつのとびいし
            StoneRow(UpperStonesZ, 2.4f, 1.05f, "upstones");

            // 50. 岸の砂地の、流れついた小枝
            for (int i = 0; i < 9; i++)
            {
                float z = XR(-60f, 26f);
                float side = XR01() < 0.5f ? -1f : 1f;
                Vector2 p = new Vector2(RiverBankEdgeX(z, side) + side * XR(0.5f, 2f), z);
                if (!IsLand(p, 0.02f) || !ExtraOk(p, 0.8f, false)) continue;
                float yaw = 90f + XR(-25f, 25f), roll = XR(-3f, 3f);
                LayTwig("Twig_B", prop, p, yaw, roll, XR(0.25f, 0.4f), 100f);
            }
            // 51. 岸ぞいの小道のふちの小石
            foreach (var trail in RiverLayout.Trails)
                for (int i = 0; i < trail.Length - 1; i++)
                {
                    Vector2 a = trail[i], b = trail[i + 1];
                    float len = Vector2.Distance(a, b);
                    Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
                    for (float s = 1f; s < len; s += XR(2f, 3.5f))
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / len) + n * (XR01() < 0.5f ? -1f : 1f) * XR(1.9f, 2.5f);
                        if (!IsLand(p, 0.1f) || !ExtraOk(p, 0.2f, false)) continue;
                        PlaceLoose(XPick(RiverStones), prop, RiverLayout.Ground(p.x, p.y), Quaternion.Euler(XR(-15f, 15f), XR(0f, 360f), XR(-15f, 15f)), XR(0.1f, 0.22f), LooseProps.Shape.Pebble, false, 50f);
                    }
                }

            // 53. 外周の大岩のすきまのシダ・54. 遠景の森・70. 遠くの大きな木の幹
            for (int i = 0; i < 28; i++)
            {
                float a = i / 28f * Mathf.PI * 2f + XR(-0.05f, 0.05f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * XR(61f, 64f);
                if (RiverLayout.InChannel(p.x, p.y, 4f) || !IsLand(p, 0.2f) || !ExtraOk(p, 0.4f, false)) continue;
                PutDeco(XPick("Fern_A", "Fern_B"), flw, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.6f, 1f), true, 200f);
            }
            DistantCanopy(9, 108f, 140f, p => RiverLayout.InChannel(p.x, p.y, 18f));
            MoreBackgroundTrunks(9, 88f, 118f, p => RiverLayout.InChannel(p.x, p.y, 14f));

            // 55. 岸の土手のキノコのかたまり・65. あちこちの芽生え
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = RandomXInRing(14f, 58f);
                if (RiverLayout.DistToRiver(p.x, p.y) < RiverLayout.HalfWidth(p.y) + 10f || !IsLand(p, 0.4f) || !ExtraOk(p, 0.3f, false) || RiverLayout.TrailMask(p.x, p.y) > 0.4f) continue;
                PutDeco("Mushroom_Cluster", prop, RiverLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.4f, 6f), XR(0.45f, 0.75f), true, 120f);
            }
            for (int i = 0; i < 26; i++)
            {
                Vector2 p = RandomXInRing(6f, 60f);
                if (RiverLayout.InChannel(p.x, p.y, 4f) || !IsLand(p, 0.4f) || !ExtraOk(p, 0.2f, false)) continue;
                PutDeco("Sprout", fol, RiverLayout.Ground(p.x, p.y), GroundRotation(p, XR(0f, 360f), 0.3f, 6f), XR(0.7f, 1.1f), true, 70f);
            }

            // 56. 小道の分かれ道の、小石を積んだ目じるし（82. 登れる）
            float wxW = RiverLayout.CenterX(-2f) - (RiverLayout.HalfWidth(-2f) + 6f);
            Cairn(new Vector2(wxW - 2.6f, 0.8f));
            float wxE = RiverLayout.CenterX(RiverLayout.StonesZ) + (RiverLayout.HalfWidth(RiverLayout.StonesZ) + 6f);
            Cairn(new Vector2(wxE + 2.8f, RiverLayout.StonesZ + 2.5f));

            // 57. 中州のまん中の、休める平たい石
            FlatRock(ic + new Vector2(2.2f, 1.2f), 0.6f, "rest", false, ignoreMobs: true);
            // 58. よどみの岸の、カエルがすわれる平たい石
            for (int i = 0; i < 6; i++)
            {
                float z = pz + XR(-9f, 9f);
                float side = XR01() < 0.5f ? -1f : 1f;
                if (FlatRock(new Vector2(RiverBankEdgeX(z, side) + side * 1.4f, z), 0.9f, "froggy")) break;
            }

            // 62. トンネルから川へ向かう小道のわきのタンポポとデイジー・68. 岸の草むらのデイジーの花畑
            foreach (var t in new[] { new Vector2(-46f, -10.5f), new Vector2(-36f, -7.5f), new Vector2(-28f, -4f) })
                ScatterDeco(t + new Vector2(0f, 3f), 2.2f, 4, 0.6f, 0.9f, flw, 0.05f, "Dandelion", "Daisy");
            ScatterDeco(new Vector2(-34f, 20f), 5f, 20, 0.6f, 0.95f, flw, 0.05f, "Daisy");
            Mark("daisies", RiverLayout.Ground(-34f, 20f));

            // 66. 流木のまわりのクローバー・67. 中州の南のはしの、流れついた笹舟
            foreach (var d in ExtraSpots("drift"))
                ScatterDeco(new Vector2(d.x, d.z), 3.5f, 6, 0.7f, 1.1f, fol, 0f, "Clover_A", "Clover_B");
            {
                Vector2 p = ic + new Vector2(0.4f, -6.4f);
                if (IsLand(p, 0f))
                {
                    PutDeco("SasaBune", flw, RiverLayout.Ground(p.x, p.y) + Vector3.up * 0.05f, Quaternion.Euler(4f, 70f, 6f), 0.9f, true, 90f);
                    Mark("stranded", RiverLayout.Ground(p.x, p.y));
                }
            }

            // 71. 滝の段差のわきの、岩の階段（下の岸から台地へ）
            {
                // 5 段ともあいている列をさがす（崖ぞいの岩の間）
                // 台地へ続く小道（RiverLayout の 5 本目）が通る列。崖ぞいの岩に重なっても、岩も登れる段になる
                float x = RiverLayout.CenterX(fz) - RiverLayout.HalfWidth(fz) - 9.5f;
                float baseY = RiverLayout.Height(x, fz - 5f);
                Mesh m = assets.Get("RiverStone_C");
                float h = m != null ? m.bounds.max.y : 0.4f;
                for (int i = 0; i < 5; i++)
                {
                    float z = fz - 4.2f + i * 1.25f;
                    Vector2 p = new Vector2(x + Mathf.Sin(i * 1.3f) * 0.5f, z);
                    float top = Mathf.Max(baseY + 0.8f * (i + 1), RiverLayout.Height(p.x, p.y) + 0.25f);
                    if (top > RiverLayout.Height(p.x, p.y + 2f) + 0.9f) continue;
                    if (NearDew(p, 2f)) continue;
                    PutSolid("RiverStone_C", prop, new Vector3(p.x, top - h * 1.1f, p.y), Quaternion.Euler(0f, 90f + XR(-15f, 15f), 0f), 1.1f, 1.2f);
                    Mark("stairs", new Vector3(p.x, top, p.y));
                }
            }

            // 75. 下流の岸の、川へつき出した平たい石・76. 橋のたもとの、乗りやすい平たい石・84. 舟を待つ間に休める平たい石
            for (int i = 0; i < 6; i++)
            {
                float z = XR(-40f, -28f);
                if (FlatRock(new Vector2(RiverBankEdgeX(z, -1f) + 0.6f, z), 1.3f, "peek", true)) break;
            }
            FlatRock(new Vector2(_bridgeA.x - 2.6f, _bridgeA.z + 2.2f), 1.0f, "bridgestep");
            FlatRock(new Vector2(RiverBankEdgeX(RiverLayout.IslandZ, 1f) + 3.6f, RiverLayout.IslandZ - 3.2f), 1.0f, "dockrest");

            // 77. よどみの葉の道の先に、葉っぱをもう 1 枚
            if (_poolPads.Count > 0)
            {
                Vector3 last = _poolPads[_poolPads.Count - 1];
                Vector2 p = new Vector2(last.x + 5.2f, pz + (_poolPads.Count % 2 == 0 ? 1.8f : -1.8f));
                if (RiverLayout.Height(p.x, p.y) < pwl - 0.4f && IsFree(p, 1.4f))
                {
                    Place("LilyPad", prop, new Vector3(p.x, pwl + 0.14f + _poolPads.Count * 0.012f, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), 1.35f, true, false, 150f);
                    Mark("extrapad", new Vector3(p.x, pwl, p.y));
                }
            }

            // 78. 岸の土手の上の、見晴らしのよい岩・79. 滝の上の台地のふちの、滝を見下ろせる岩
            foreach (var (p, kind) in new[] { (new Vector2(-24f, -14f), "view"), (new Vector2(RiverLayout.CenterX(fz + 2f) + RiverLayout.HalfWidth(fz + 2f) + 4.5f, fz + 2.5f), "fallview") })
            {
                if (!IsLand(p, 0.3f) || !ExtraOk(p, 1.6f, true)) continue;
                PutSolid("Rock_C", prop, RiverLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, Quaternion.Euler(0f, XR(0f, 360f), 0f), 1.5f, 2f);
                Mark(kind, RiverLayout.Ground(p.x, p.y));
            }

            // 81. 外周の大岩の手前の、低い石の列
            BoundaryStones(61.5f, 60, p => RiverLayout.InChannel(p.x, p.y, 4f));

            // 83. 葉っぱの舟の乗り場（岸）の、小枝の目じるし
            {
                Vector2 p = new Vector2(RiverBankEdgeX(RiverLayout.IslandZ, 1f) + 2.6f, RiverLayout.IslandZ + 2.6f);
                if (IsLand(p, 0.05f) && ExtraOk(p, 0.4f, false))
                {
                    PutDeco("Twig_B", prop, RiverLayout.Ground(p.x, p.y) + Vector3.up * 1.6f, Quaternion.Euler(0f, 30f, 90f), 0.24f, true, 120f);
                    Mark("dock", RiverLayout.Ground(p.x, p.y));
                }
            }
        }

        bool NearDew(Vector2 p, float r)
        {
            foreach (var d in DewdropPoints)
                if ((new Vector2(d.x, d.z) - p).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>岸の浅い所の水草（見た目だけ）。</summary>
        void WaterGrassAt(Vector2 p)
        {
            float wl = Area.WaterLevelAt(p.x, p.y);
            if (wl < -100f || Area.Height(p.x, p.y) > wl - 0.05f) return;
            if (InsideOccupied(p) || NearDew(p, 1f)) return;
            PutDeco("WaterGrass", assets.flowers, new Vector3(p.x, wl + 0.02f, p.y), Quaternion.Euler(0f, XR(0f, 360f), 0f), XR(0.7f, 1.1f), false, 110f);
            Mark("watergrass", new Vector3(p.x, wl, p.y));
        }

        /// <summary>川を横切る、平たい石の列（とびいし）。z の所で、岸から岸まで。</summary>
        void StoneRow(float z, float spacing, float scale, string kind)
        {
            float x0 = RiverBankEdgeX(z, -1f) - 0.6f;
            float x1 = RiverBankEdgeX(z, 1f) + 0.6f;
            if (new Vector2(x0, z).magnitude > 63f || new Vector2(x1, z).magnitude > 63f) return;
            Mesh stone = assets.Get("RiverStone_C");
            int n = Mathf.CeilToInt((x1 - x0) / spacing) + 1;
            float wl = RiverLayout.WaterLevel(z);
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)(n - 1));
                float zz = z + Mathf.Sin(i * 1.9f) * 0.5f;
                Vector2 p = new Vector2(x, zz);
                if (NearDew(p, 1.5f) || !IsFree(p, 0.6f)) continue;
                float top = stone != null ? stone.bounds.max.y * scale : 0.5f;
                // 岸の土の中にうまってしまう、はしの石は置かない
                if (RiverLayout.Height(x, zz) > wl + 0.2f) continue;
                Vector3 sp = new Vector3(x, wl + 0.28f - top, zz);
                PutSolid("RiverStone_C", assets.prop, sp, Quaternion.Euler(0f, XR(-25f, 25f) + 90f, 0f), scale, 0.9f);
                StoneFooting(sp, "RiverStone_C", scale);   // 川底までとどく石
                Mark(kind, new Vector3(x, wl + 0.28f, zz));
            }
        }
    }
}
