using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>
    /// クリスマス村エリアの組み立て：大きなクリスマスツリー（電球と星）とプレゼントの山・雪だるま・そり・こおった池・
    /// あかりのともる丸太の家・サンタさんへのポストと手ぶくろ・ガーデンランプ・電球のさく・キャンディケイン・
    /// 雪のモミの木・ヒイラギ・雪の吹きだまり・村のまわりのモミの森。
    /// </summary>
    public partial class WorldGenerator
    {
        readonly List<Vector3> _xmasTops = new List<Vector3>();      // しずくを置く、物の上（上からさがす）
        readonly List<Vector3> _xmasPerches = new List<Vector3>();   // コマドリがとまる所（さくのくい・ポスト・雪だるまのぼうし・ランプの屋根）
        readonly List<Vector3> _xmasLamps = new List<Vector3>();     // ランプのあかり（フユシャクが回る）
        readonly List<Vector2> _xmasFirs = new List<Vector2>();
        Vector3 _mittenMouth, _mittenFace;

        /// <summary>コマドリがとまる所（テスト用）。</summary>
        public IReadOnlyList<Vector3> XmasPerches => _xmasPerches;
        /// <summary>ガーデンランプのあかり（フユシャク・光の演出）。</summary>
        public IReadOnlyList<Vector3> XmasLamps => _xmasLamps;
        /// <summary>手ぶくろの口（ヤマネのいる所）。</summary>
        public Vector3 MittenMouth => _mittenMouth;

        void ClearXmas()
        {
            _xmasTops.Clear();
            _xmasPerches.Clear();
            _xmasLamps.Clear();
            _xmasFirs.Clear();
            _mittenMouth = _mittenFace = Vector3.zero;
        }

        static Vector3 XG(Vector2 p) => XmasLayout.Ground(p.x, p.y);

        /// <summary>電球・窓・ランプのあかり（色とりどりに光る材質）。</summary>
        Material XmasLight => assets.lightGlow != null ? assets.lightGlow : assets.warmGlow != null ? assets.warmGlow : assets.glow;

        // build_xmas_kit.py の寸法
        const float LampGlowY = 15.3f;        // ランプのガラスのまん中の高さ
        const float FenceHalf = 10f;
        const float FencePostTop = 11.4f;     // さくのくいの雪のぼうしの上

        /// <summary>真上から物の上の点（なければ null）。名前が prefix で始まる物だけ。</summary>
        Vector3? TopOf(Vector3 from, string prefix, float down = 30f)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(from, Vector3.down, out var hit, down, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)
                && hit.collider.name.StartsWith(prefix) && hit.normal.y > 0.5f)
                return hit.point;
            return null;
        }

        // ------------------------------------------------------------------
        // 大きな物
        // ------------------------------------------------------------------
        void BuildXmasSolids()
        {
            BuildXmasBackdrop();
            Material prop = assets.prop;
            Material big = assets.bark;
            Material light = XmasLight;

            // 大きなクリスマスツリー（電球と、てっぺんの星）
            {
                Vector3 o = XG(XmasLayout.Tree) + Vector3.down * 0.2f;
                Quaternion rot = Quaternion.Euler(0f, 15f, 0f);
                Place("Xmas_Tree", big, o, rot, 1f, true, true, 900f, asRenderer: true);
                Place("Xmas_TreeLights", light, o, rot, 1f, false, false, 900f, asRenderer: true);
                Place("Xmas_TreeStar", light, o, rot, 1f, false, false, 900f, asRenderer: true);
                Occupy(XmasLayout.Tree, XmasLayout.TreeSkirt);
                // 段の上（南・西・東）と、てっぺんの星のとなり
                foreach (var (r, a) in new[] { (14f, -100f), (10.5f, 160f), (7f, 20f) })
                {
                    float ar = a * Mathf.Deg2Rad;
                    _xmasTops.Add(new Vector3(XmasLayout.Tree.x + Mathf.Cos(ar) * r, 70f, XmasLayout.Tree.y + Mathf.Sin(ar) * r));
                }
                _xmasTops.Add(new Vector3(XmasLayout.Tree.x + 0.6f, 70f, XmasLayout.Tree.y - 1.1f));
            }

            // プレゼントの山（ツリーの前。上は平らで、ツリーの枝へのふみ台）
            foreach (var (mesh, p, yaw, s, top) in new[]
                     {
                         ("Xmas_Present_A", new Vector2(-8f, -11f), 18f, 1f, true), ("Xmas_Present_B", new Vector2(5f, -12.5f), -12f, 1f, true),
                         ("Xmas_Present_C", new Vector2(15f, -6f), 32f, 1f, true), ("Xmas_Present_A", new Vector2(-15f, -4.5f), 50f, 0.5f, false),
                         ("Xmas_Present_C", new Vector2(10.5f, -19f), -20f, 0.6f, false),
                     })
            {
                Vector3 g = XG(p);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Place(mesh, prop, g + Vector3.down * 0.15f, rot, s, true, true, 400f);
                Occupy(p, 5.5f * s);
                if (top) _xmasTops.Add(g + rot * new Vector3(2.8f * s, 0f, 2.6f * s) + Vector3.up * 30f);   // ちょう結びの、となり
            }

            // 雪だるま（にんじんの鼻を、広場の南へ）と、そり（トナカイのそり置き場）
            {
                Vector3 g = XG(XmasLayout.Snowman);
                Place("Xmas_Snowman", prop, g + Vector3.down * 0.3f, Quaternion.Euler(0f, -68f, 0f), 1f, true, true, 500f, asRenderer: true);
                Occupy(XmasLayout.Snowman, 9f);
                _xmasTops.Add(g + new Vector3(0.6f, 40f, 0.4f));   // バケツのぼうしの上
                var hat = TopOf(g + new Vector3(0.3f, 40f, 0f), "Xmas_Snowman", 45f);
                if (hat.HasValue) _xmasPerches.Add(hat.Value);
            }
            {
                Vector3 g = XG(XmasLayout.Sled);
                Quaternion rot = Quaternion.Euler(0f, XmasLayout.SledYaw, 0f);
                Place("Xmas_Sled", prop, g + Vector3.down * 0.1f, rot, 1f, true, true, 400f);
                // そりは長い（すべり木の前のまがりから、ひもまで 29）：長さにそって、物を置かない所にする
                Vector3 along = rot * Vector3.forward;
                foreach (float s in new[] { -9f, -2f, 5f, 12f })
                    Occupy(XmasLayout.Sled + new Vector2(along.x, along.z) * s, 7f);
                _xmasTops.Add(g + new Vector3(0f, 20f, 0f));
            }

            // こおった池の氷（水ではない。上を歩ける）
            {
                Vector2 c = XmasLayout.Pond;
                Place("Xmas_Ice", assets.propGlossy != null ? assets.propGlossy : prop, new Vector3(c.x, XmasLayout.IceTop, c.y), Quaternion.identity, 1f, true, false, 400f, asRenderer: true);
                Occupy(c, Mathf.Max(XmasLayout.PondRadii.x, XmasLayout.PondRadii.y) * 0.8f);
                _xmasTops.Add(new Vector3(c.x - 6f, 20f, c.y + 2f));
                _xmasTops.Add(new Vector3(c.x + 7f, 20f, c.y - 3f));
            }

            // あかりのともる丸太の家（村のはし。窓とのきの電球が光る）
            {
                Vector2 c = XmasLayout.Cottage;
                Quaternion rot = Quaternion.Euler(0f, XmasLayout.CottageYaw, 0f);
                Vector3 o = new Vector3(c.x, 0f, c.y);
                Place("Xmas_Cottage", big, o, rot, 1f, true, true, 900f, asRenderer: true);
                Place("Xmas_CottageWindow", light, o, rot, 1f, false, false, 900f, asRenderer: true);
                for (float u = -30f; u <= 30f; u += 7.5f)
                    for (float v = -18f; v <= 14f; v += 8f)
                        Occupy(XmasLayout.CottageLocal(u, v), 5f);
                Vector2 sill = XmasLayout.CottageLocal(XmasLayout.WindowX - 5.5f, XmasLayout.CottageFront - 2.4f);
                _xmasTops.Add(new Vector3(sill.x, XmasLayout.SillY + 3f, sill.y));                 // 窓台
                Vector2 step = XmasLayout.CottageLocal(XmasLayout.DoorX + 4f, XmasLayout.CottageFront - 3.2f);
                _xmasTops.Add(new Vector3(step.x, 6f, step.y));                                     // とびらの前の、ふみ石
                Vector2 ridge = XmasLayout.CottageLocal(-18f, -3f);
                _xmasTops.Add(new Vector3(ridge.x, 75f, ridge.y));                                  // 屋根の上（雪）
                // ヒイラギ（家の前の、すみ）
                foreach (float u in new[] { -27f, 26f })
                {
                    Vector2 p = XmasLayout.CottageLocal(u, XmasLayout.CottageFront - 4.5f);
                    Place("Xmas_Holly", prop, XG(p) + Vector3.down * 0.3f, Quaternion.Euler(0f, R(0f, 360f), 0f), R(1f, 1.2f), true, true, 300f);
                }
            }

            // サンタさんへのポストと、落ちた手ぶくろ（口に、冬眠中のヤマネ）
            {
                Vector3 g = XG(XmasLayout.Mailbox);
                Place("Xmas_Mailbox", prop, g + Vector3.down * 0.4f, Quaternion.Euler(0f, XmasLayout.MailboxYaw, 0f), 1f, true, true, 400f, asRenderer: true);
                Occupy(XmasLayout.Mailbox, 4f);
                var top = TopOf(g + Vector3.up * 30f, "Xmas_Mailbox", 35f);
                if (top.HasValue)
                {
                    _xmasPerches.Add(top.Value);
                    _xmasTops.Add(top.Value + Vector3.up * 2f);
                }
                Vector3 m = XG(XmasLayout.Mitten);
                Quaternion mr = Quaternion.Euler(0f, XmasLayout.MittenYaw, 0f);
                Place("Xmas_Mitten", prop, m + Vector3.down * 0.1f, mr, 1f, true, true, 300f, asRenderer: true);
                Occupy(XmasLayout.Mitten, 5f);
                _mittenFace = mr * Vector3.forward;
                Vector3 mouth = m + mr * new Vector3(0f, 0f, XmasLayout.MittenCuff.z + 0.5f);
                mouth.y = XmasLayout.Height(mouth.x, mouth.z) - 0.05f;
                _mittenMouth = mouth;
                Place("Xmas_Holly", prop, XG(XmasLayout.Mailbox + new Vector2(-5f, 3f)) + Vector3.down * 0.3f, Quaternion.Euler(0f, 40f, 0f), 1.1f, true, true, 300f);
            }

            // ガーデンランプ（小道ぞいと、ツリーの広場のまわり。ガラスが光る）
            Vector2[] lamps =
            {
                new Vector2(-5.5f, -47f), new Vector2(5.5f, -35f), new Vector2(-5.5f, -23f),
                new Vector2(29.5f, 8f), new Vector2(14.5f, 33.5f), new Vector2(-14.5f, 33.5f), new Vector2(-29.5f, 8f), new Vector2(20f, -21f),
            };
            for (int i = 0; i < lamps.Length; i++)
            {
                Vector2 p = lamps[i];
                Vector3 g = XG(p);
                Quaternion rot = Quaternion.Euler(0f, 45f + 17f * i, 0f);
                Place("Xmas_Lamp", prop, g, rot, 1f, true, true, 400f);
                Place("Xmas_LampGlow", light, g, rot, 1f, false, false, 400f);
                Occupy(p, 2.5f);
                _xmasLamps.Add(g + Vector3.up * LampGlowY);
                if (i == 1 || i == 5)
                {
                    var roof = TopOf(g + Vector3.up * 30f, "Xmas_Lamp", 35f);
                    if (roof.HasValue) { _xmasPerches.Add(roof.Value); _xmasTops.Add(roof.Value + Vector3.up * 2f); }
                }
            }

            // 電球のさく（小道の両がわと、家の前庭、村の西のはし）
            var fences = new List<(Vector2 p, float yaw)>
            {
                (new Vector2(-10.5f, -38f), 90f), (new Vector2(10.5f, -38f), 90f),
                (XmasLayout.CottageLocal(-24f, XmasLayout.CottageFront - 15f), XmasLayout.CottageYaw),
                (XmasLayout.CottageLocal(31f, XmasLayout.CottageFront - 15f), XmasLayout.CottageYaw),   // とびらの前は、あけておく
                (new Vector2(-46f, 18f), 80f),
            };
            for (int i = 0; i < fences.Count; i++)
            {
                var (p, yaw) = fences[i];
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 g = XG(p);
                Place("Xmas_Fence", prop, g + Vector3.down * 0.4f, rot, 1f, true, true, 400f);
                Place("Xmas_FenceLights", light, g + Vector3.down * 0.4f, rot, 1f, false, false, 400f);
                Vector3 along = rot * Vector3.right;
                for (float s = -FenceHalf; s <= FenceHalf; s += 4f) Occupy(new Vector2(p.x + along.x * s, p.y + along.z * s), 1.6f);
                foreach (float sx in new[] { -1f, 1f })
                {
                    Vector3 post = g + along * (sx * (FenceHalf - 0.7f)) + rot * new Vector3(0f, 0f, 0.25f);
                    var top = TopOf(post + Vector3.up * 20f, "Xmas_Fence", 25f);
                    if (top.HasValue && (i < 2 ? sx < 0f : sx > 0f)) _xmasPerches.Add(top.Value);
                    if (top.HasValue && i == 0 && sx > 0f) _xmasTops.Add(top.Value + Vector3.up * 2f);
                }
            }

            // キャンディケイン（光の輪からの小道の両がわ）
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Vector2 p = new Vector2(side * 4.6f, -43f + i * 4.6f);
                if (NearArrival(p, 2.5f)) continue;
                float yaw = side < 0f ? 90f : -90f;   // 曲がった先は、小道の方へ
                float s = R(0.55f, 0.65f);
                Place("Xmas_CandyCane", prop, XG(p), Quaternion.Euler(0f, yaw + R(-12f, 12f), 0f), s, true, true, 300f);
                Occupy(p, 1f);
                if (i == 3) _xmasTops.Add(XG(p) + new Vector3(0f, 20f, 0f) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 1.6f * s));
            }

            // 雪のモミの木（村のまわりと、ところどころ）
            Vector2[] firs =
            {
                new Vector2(-52f, -30f), new Vector2(-57f, -8f), new Vector2(-52f, 30f), new Vector2(-38f, 50f), new Vector2(-14f, 58f),
                new Vector2(54f, -36f), new Vector2(57f, 14f), new Vector2(46f, 36f), new Vector2(-22f, -50f), new Vector2(26f, -50f),
                new Vector2(-44f, 38f), new Vector2(40f, -46f),
            };
            for (int i = 0; i < firs.Length; i++)
            {
                Vector2 p = firs[i];
                if (!IsFree(p, 5f)) continue;
                float s = R(0.8f, 1.3f);
                Place("Xmas_Fir", big, XG(p) + Vector3.down * 0.3f, Quaternion.Euler(0f, R(0f, 360f), 0f), s, true, true, 600f, asRenderer: true);
                Occupy(p, 7f * s);
                _xmasFirs.Add(p);
                if (i == 1 || i == 6) _xmasTops.Add(XG(p) + new Vector3(4f * s, 60f, 0f));
            }
        }

        /// <summary>村のまわりの、大きな雪のモミの森（遊べる場所の外）。とても高いので、影は落とさない（村じゅうが、月かげになってしまう）。</summary>
        void BuildXmasBackdrop()
        {
            for (int i = 0; i < 30; i++)
            {
                float a = i / 30f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(80f, 102f);
                if (Vector2.Distance(p, XmasLayout.Gate * 1.5f) < 14f) continue;
                if (XmasLayout.CottageK(p.x, p.y) > 0.01f) continue;
                Place("Xmas_Fir", assets.bark, new Vector3(p.x, XmasLayout.Height(p.x * 0.96f, p.y * 0.96f) - 2f, p.y),
                    Quaternion.Euler(0f, R(0f, 360f), 0f), R(2.2f, 3.4f), false, false, 900f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------
        void ScatterXmasProps()
        {
            Material prop = assets.prop;
            Occupy(XmasLayout.Gate, 4.5f);
            Occupy(XmasLayout.Spawn, 3f);
            // 雪の吹きだまり（村のはし。登れる）
            Vector2[] drifts =
            {
                new Vector2(-40f, -42f), new Vector2(38f, -40f), new Vector2(-12f, 44f), new Vector2(52f, 26f), new Vector2(-58f, 6f),
                new Vector2(-28f, 48f), new Vector2(-50f, -18f), new Vector2(50f, -12f),
            };
            for (int i = 0; i < drifts.Length; i++)
            {
                Vector2 p = drifts[i];
                if (!IsFree(p, 4f) || XmasLayout.DistToTrail(p) < 5f || NearArrival(p, 5f)) continue;
                float s = R(0.8f, 1.25f);
                Place("Xmas_SnowDrift", prop, XG(p) + Vector3.down * 0.2f, Quaternion.Euler(0f, R(0f, 360f), 0f), s, true, true, 300f);
                Occupy(p, 6f * s);
                if (i == 0 || i == 3) _xmasTops.Add(XG(p) + new Vector3(0f, 20f, 0f));
            }
            // ヒイラギ（ツリーの広場のまわり）
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f + 0.4f;
                Vector2 p = XmasLayout.Tree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 30.5f;
                if (!IsFree(p, 2.5f) || XmasLayout.DistToTrail(p) < 3f || NearArrival(p, 4f)) continue;
                Place("Xmas_Holly", prop, XG(p) + Vector3.down * 0.3f, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.2f), true, true, 300f);
                Occupy(p, 3f);
            }
            // 雪から頭を出した、小石
            for (int i = 0; i < 20; i++)
            {
                Vector2 p = RandomInRing(30f, 62f);
                if (!IsFree(p, 1.2f) || XmasLayout.DistToTrail(p) < 2f || XmasLayout.OnIce(p.x, p.y, 1.5f)) continue;
                float s = R(0.35f, 0.8f);
                Place(Pick(Rocks), prop, XG(p) + Vector3.down * 0.45f * s, Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)), s, true, true, 200f);
                Occupy(p, 1.4f * s);
            }
        }

        // ------------------------------------------------------------------
        // 草花のかわり：雪の上の、まつぼっくりと小枝
        // ------------------------------------------------------------------
        void BuildXmasFoliage()
        {
            float dens = foliageDensity;
            // モミの木の下の、まつぼっくり
            foreach (var f in _xmasFirs)
            {
                int n = Mathf.RoundToInt(3 * dens);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = f + Random2(R(5f, 8f));
                    if (InsideOccupied(p) || XmasLayout.DistToTrail(p) < 1.5f || !FreeAt(XG(p), 0.6f, 1.2f) || NearArrival(p, 4f)) continue;
                    float s = R(0.8f, 1.1f);
                    Quaternion rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(R(70f, 95f), 0f, 0f);
                    PlaceLoose("Pinecone", assets.prop, XG(p) + Vector3.up * 0.45f * s, rot, s, LooseProps.Shape.Pinecone, true, 120f);
                }
            }
            // 雪から出た、かれ草（村のはしだけ。ひかえめに）
            int grass = Mathf.RoundToInt(160 * dens);
            for (int i = 0; i < grass; i++)
            {
                Vector2 p = RandomInRing(42f, 70f);
                if (InsideOccupied(p) || XmasLayout.TrailMask(p.x, p.y) > 0.2f || XmasLayout.OnIce(p.x, p.y, 2f) || XmasLayout.CottageK(p.x, p.y) > 0.3f) continue;
                Place(Pick(Grass), assets.foliage, XG(p) + Vector3.down * 0.15f, GroundRotation(p, R(0, 360), 0.5f, 5f), R(0.35f, 0.6f), false, false, 55f);
            }
        }

        Vector2 Random2(float r)
        {
            float a = R(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        // ------------------------------------------------------------------
        // しずく・いきもの
        // ------------------------------------------------------------------
        void PlaceXmasDewdrops()
        {
            DewdropPoints.Clear();
            AddDewFromAbove(XmasLayout.Spawn + new Vector2(3.5f, 3f));
            // 物の上（ツリーの段とてっぺん・プレゼント・雪だるまのぼうし・そり・氷・窓台・屋根・ポスト・ランプ・さく・キャンディケイン・モミ・吹きだまり）
            foreach (var top in _xmasTops) AddDewFromAbove(new Vector2(top.x, top.z), top.y);
            // のこりは、雪の原の、探索が楽しくなる所へ
            FillDewsToExplore(8f, 60f, p => !XmasLayout.OnIce(p.x, p.y, 1f) && XmasLayout.CottageK(p.x, p.y) < 0.5f);
        }

        void PlaceXmasCreatures()
        {
            // トナカイ：そり置き場のまわりの、ひらけた雪の原（2 頭。1 頭ずつ、大きな体が、そり・木・吹きだまりに重ならない所から）
            Vector2 first = new Vector2(999f, 999f);
            int deer = 0;
            foreach (var start in new[] { XmasLayout.Sled + new Vector2(14f, -4f), XmasLayout.Sled + new Vector2(8f, -12f), XmasLayout.Sled + new Vector2(-6f, 16f) })
            {
                if (deer >= 2) break;
                var probe = new MobGroup { species = "tonakai", count = 1, radius = 6f };
                Vector2 f0 = first;
                if (!Spiral(start, 22f, q => (q - f0).sqrMagnitude > 10f * 10f && GroundProblem(probe, q, false) == null, out var p, 400)) continue;
                AddMob("tonakai", XG(p), 1, 6f);
                first = p;
                deer++;
            }
            // ユキウサギ：村のはしの、吹きだまりとモミのかげ
            AddMob("yukiusagi", XG(new Vector2(-46f, 4f)), 1, 5f);
            AddMob("yukiusagi", XG(new Vector2(46f, 22f)), 1, 5f);
            // ヨーロッパコマドリ：さくのくい・ポスト・雪だるまのぼうし・ランプの屋根を、とびうつる
            if (_xmasPerches.Count > 0)
            {
                var g = AddMob("robin", _xmasPerches[0], 1, 1.5f);
                foreach (var p in _xmasPerches) g.path.Add(p);
                g.showcase = true;
            }
            // フユシャク：ガーデンランプのあかりのまわり（3 つのランプ）
            foreach (int i in new[] { 1, 4, 6 })
            {
                if (i >= _xmasLamps.Count) continue;
                var g = AddMob("fuyushaku", _xmasLamps[i], 1, 3f, 1.5f);
                foreach (var l in _xmasLamps) g.path.Add(l);
                g.showcase = true;
            }
            // ヤマネ：手ぶくろの口で、冬眠中
            if (_mittenMouth != Vector3.zero)
            {
                var g = AddMob("yamane", _mittenMouth, 1, 0f);
                g.face = _mittenFace;
                g.showcase = true;
            }
            // ほかのエリアからも：雪の上のけものと、冬の小鳥
            // ホンドギツネ：西の雪の原（雪の下のネズミをねらって、ジャンプ）
            AddMob("kitsune", XG(new Vector2(-46f, -6f)), 1, 8f);
            // シマエナガ：雪のモミの木の、段の上をとびうつる
            {
                var spots = FirPerches(5);
                if (spots.Count > 1)
                {
                    var g = AddMob("shimaenaga", spots[0], 1, 1.5f);
                    foreach (var p in spots) g.path.Add(p);
                    g.showcase = true;
                }
            }
            // スズメ：ふくらすずめ。ひらけた雪の上を、とびまわる
            {
                var g = AddMob("sparrow", XG(new Vector2(-12f, -30f)), 3, 3f);
                Vector2[] spots = { new Vector2(-12f, -30f), new Vector2(-24f, 18f), new Vector2(22f, 22f), new Vector2(-30f, -16f), new Vector2(26f, -32f) };
                foreach (var p in spots) g.path.Add(XG(p));
            }
            // ニホンリス：北西のモミの木の根もと（まつぼっくりが落ちている所）
            AddMob("risu", XG(new Vector2(-32f, 45f)), 1, 5f);
        }

        /// <summary>雪のモミの木の、段の上（小鳥がとまる所）。上からのレイで、さがす。</summary>
        List<Vector3> FirPerches(int max)
        {
            var list = new List<Vector3>();
            Physics.SyncTransforms();
            foreach (var f in _xmasFirs)
            {
                foreach (var a in new[] { 0.6f, 2.6f, 4.4f })
                {
                    Vector3 from = XG(f) + new Vector3(Mathf.Cos(a) * 3.2f, 70f, Mathf.Sin(a) * 3.2f);
                    if (!Physics.Raycast(from, Vector3.down, out var hit, 80f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!hit.collider.name.StartsWith("Xmas_Fir") || hit.normal.y < 0.5f) continue;
                    if (hit.point.y - XmasLayout.Height(hit.point.x, hit.point.z) < 10f) continue;
                    list.Add(hit.point);
                    break;
                }
                if (list.Count >= max) break;
            }
            return list;
        }

        // ------------------------------------------------------------------
        // 地図
        // ------------------------------------------------------------------
        void DrawXmasMap(Color32[] px, int size)
        {
            MapTrails(px, size, XmasLayout.Trails, new Color32(168, 160, 152, 255));
            // こおった池
            Vector2 c = XmasLayout.Pond, r = XmasLayout.PondRadii;
            for (int i = 0; i < 40; i++)
            {
                float a = i * 2.39996f, k = Mathf.Sqrt((i + 0.5f) / 40f);
                MapDot(px, size, new Vector3(c.x + Mathf.Cos(a) * r.x * k, 0f, c.y + Mathf.Sin(a) * r.y * k), 2.6f, new Color32(150, 196, 226, 255));
            }
            // 家
            for (float u = -30f; u <= 30f; u += 3f)
                for (float v = -14f; v <= 14f; v += 3f)
                {
                    Vector2 p = XmasLayout.CottageLocal(u, v);
                    MapDot(px, size, new Vector3(p.x, 0f, p.y), 1.8f, new Color32(150, 96, 64, 255));
                }
            // ツリー・雪だるま・ポスト・そり
            MapDot(px, size, new Vector3(XmasLayout.Tree.x, 0f, XmasLayout.Tree.y), 9f, new Color32(64, 132, 84, 255));
            MapDot(px, size, new Vector3(XmasLayout.Tree.x, 0f, XmasLayout.Tree.y), 2.2f, new Color32(250, 214, 96, 255));
            MapDot(px, size, new Vector3(XmasLayout.Snowman.x, 0f, XmasLayout.Snowman.y), 4f, new Color32(246, 248, 255, 255));
            MapDot(px, size, new Vector3(XmasLayout.Mailbox.x, 0f, XmasLayout.Mailbox.y), 2.5f, new Color32(212, 60, 70, 255));
            MapDot(px, size, new Vector3(XmasLayout.Sled.x, 0f, XmasLayout.Sled.y), 3f, new Color32(186, 70, 60, 255));
            foreach (var l in _xmasLamps) MapDot(px, size, l, 1.2f, new Color32(255, 220, 120, 255));
        }
    }
}
