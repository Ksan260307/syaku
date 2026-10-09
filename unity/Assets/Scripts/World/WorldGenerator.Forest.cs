using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shakutori
{
    /// <summary>森エリアの組み立て（大樹・切り株・丸太・キノコ・水たまり・草花・しずく・いきもの）。</summary>
    public partial class WorldGenerator
    {
        Vector3 _ladybugLeaf;

        // ------------------------------------------------------------------
        // 大きな目印（大樹・切り株・丸太・背景の幹）
        // ------------------------------------------------------------------
        void BuildForestSolids()
        {
            // 大樹: アーチ状の根が「光るキノコの洞」を向くように回転
            Mesh tree = assets.Get("GreatTree");
            if (tree != null)
            {
                Vector3 archLocal = Vector3.zero;
                var v = tree.vertices;
                foreach (var p in v)
                {
                    float rh = new Vector2(p.x, p.z).magnitude;
                    if (rh > 24f && p.y > 6f) archLocal += new Vector3(p.x, 0f, p.z);
                }
                if (archLocal.sqrMagnitude < 1e-3f) archLocal = Vector3.forward;
                Vector2 want = (ForestLayout.ArchTarget - ForestLayout.GreatTree).normalized;
                float yaw = Vector3.SignedAngle(archLocal.normalized, new Vector3(want.x, 0f, want.y), Vector3.up);
                Vector3 tp = ForestLayout.Ground(ForestLayout.GreatTree.x, ForestLayout.GreatTree.y) + Vector3.down * 1.2f;
                Place("GreatTree", assets.bark, tp, Quaternion.Euler(0f, yaw, 0f), 1f, true, true, 400f, asRenderer: true);
                Occupy(ForestLayout.GreatTree, 24f);
            }

            // 古い切り株
            Vector3 sp = ForestLayout.Ground(ForestLayout.Stump.x, ForestLayout.Stump.y) + Vector3.down * 0.6f;
            Place("Stump", assets.bark, sp, Quaternion.Euler(0f, 37f, 0f), 1f, true, true, 300f, asRenderer: true);
            Occupy(ForestLayout.Stump, 10f);

            // 中が空洞の丸太
            Vector2 la = ForestLayout.LogCenter - ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector2 lb = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector3 A = ForestLayout.Ground(la.x, la.y) + Vector3.up * 3.35f;
            Vector3 B = ForestLayout.Ground(lb.x, lb.y) + Vector3.up * 3.35f;
            Mesh logMesh = assets.Get("HollowLog");
            Vector3 logAxis = logMesh != null && logMesh.bounds.center.x < 0f ? Vector3.left : Vector3.right;
            Quaternion logRot = Quaternion.FromToRotation(logAxis, (B - A).normalized);
            // メッシュは x=0..48 に伸びているので端 A に置く
            Place("HollowLog", assets.bark, A, logRot, 1f, true, true, 300f, asRenderer: true);
            for (int i = 0; i <= 8; i++)
                Occupy(Vector2.Lerp(la, lb, i / 8f), 5.5f);

            // 背景の大きな幹（森の奥行き）
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * Mathf.PI * 2f + R(-0.12f, 0.12f);
                float d = R(100f, 150f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 45f) continue;
                Vector3 g = ForestLayout.Ground(p.x, p.y) + Vector3.down * 2f;
                Place(i % 2 == 0 ? "BgTrunk_A" : "BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.6f), false, true, 500f, asRenderer: true);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (i + 0.5f) / 6f * Mathf.PI * 2f + 0.4f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(80f, 88f);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 40f) continue;
                Vector3 g = ForestLayout.Ground(p.x, p.y) + Vector3.down * 1.5f;
                Place("BgTrunk_B", assets.bark, g, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.55f, 0.75f), false, true, 500f, asRenderer: true);
            }
        }

        // ------------------------------------------------------------------
        // 小物（コライダーあり）
        // ------------------------------------------------------------------
        void ScatterForestProps()
        {
            Material prop = assets.prop;

            // 川辺へのトンネルと、スタート地点のまわりはあけておく
            Occupy(ForestLayout.Gate, 4.5f);
            Occupy(ForestLayout.ParkGate, 4.5f);
            Occupy(ForestLayout.Spawn, 3.5f);
            // てんとう虫のいる葉っぱ
            {
                Vector2 lp = ForestLayout.Meadow + new Vector2(4f, -3f);
                Quaternion lr = Quaternion.Euler(0f, 35f, 0f);
                Vector3 lpos = ForestLayout.Ground(lp.x, lp.y) + Vector3.up * 0.05f;
                Place("Leaf_Oak_Green", prop, lpos, lr, 1.15f, true, true, 120f);
                Mesh lm = assets.Get("Leaf_Oak_Green");
                _ladybugLeaf = lpos + lr * ((lm != null ? lm.bounds.center : Vector3.zero) * 1.15f);
                Occupy(lp, 2.5f);
            }

            // --- 赤キノコの森 ---
            Vector2 g = ForestLayout.MushroomGrove;
            for (int i = 0; i < BigReds.Length; i++)
            {
                Vector2 p = g + BigReds[i];
                float s = BigRedScales[i];
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.2f;
                Place("Mushroom_Red", prop, pos, GroundRotation(p, R(0, 360), 0.2f, 4f), s, true);
                Occupy(p, 1.2f * s);
                _redCaps.Add((pos, s));
            }
            for (int i = 0; i < 7; i++)
            {
                Vector2 p = RandomInCircle(g, 13f);
                if (!IsFree(p, 2f) || ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                float s = R(0.7f, 1.3f);
                Place("Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.15f, GroundRotation(p, R(0, 360), 0.3f, 5f), s, true);
                Occupy(p, 1.3f * s);
            }
            for (int i = 0; i < 9; i++)
            {
                Vector2 p = RandomInCircle(g, 14f);
                if (!IsFree(p, 1f)) continue;
                float s = R(0.8f, 1.3f);
                Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                Occupy(p, 0.8f * s);
            }
            // 妖精の輪
            Vector2 ring = g + new Vector2(8f, -9f);
            for (int i = 0; i < 13; i++)
            {
                float a = i / 13f * Mathf.PI * 2f;
                Vector2 p = ring + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5.5f;
                float s = R(0.35f, 0.5f);
                Place(i % 3 == 0 ? "Mushroom_Cluster" : "Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.5f, 6f), s, true);
            }

            // --- 光るキノコの洞 ---
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = RandomInCircle(ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f), 6.5f);
                float s = R(0.45f, 1.2f);
                Place("Mushroom_Glow", assets.glow, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, GroundRotation(p, R(0, 360), 0.5f, 10f), s, true, true, 150f);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 la = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f + R(1.5f, 4f)) + new Vector2(R(-4f, 4f), R(-4f, 4f));
                Place("Mushroom_Glow", assets.glow, ForestLayout.Ground(la.x, la.y), GroundRotation(la, R(0, 360), 0.5f, 10f), R(0.3f, 0.6f), true);
            }

            // --- あちこちのキノコ ---
            for (int i = 0; i < 26; i++)
            {
                Vector2 p = RandomInRing(8f, 62f);
                if (!IsFree(p, 1.5f) || !IsLand(p) || ForestLayout.TrailMask(p.x, p.y) > 0.25f) continue;
                int k = RI(0, 10);
                if (k < 2)
                {
                    float s = R(0.45f, 0.8f);
                    Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.15f;
                    Place("Mushroom_Red", prop, pos, GroundRotation(p, R(0, 360), 0.3f, 5f), s, true);
                    Occupy(p, 1.1f * s);
                }
                else if (k < 5)
                {
                    float s = R(0.5f, 1.0f);
                    Place("Mushroom_Brown", prop, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.1f, GroundRotation(p, R(0, 360), 0.3f, 6f), s, true);
                    Occupy(p, 1.2f * s);
                }
                else
                {
                    float s = R(0.6f, 1.1f);
                    Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 6f), s, true);
                    Occupy(p, 0.7f * s);
                }
            }
            // 切り株のまわり
            for (int i = 0; i < 5; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Stump + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(9.5f, 12f);
                Place("Mushroom_Cluster", prop, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.4f, 8f), R(0.6f, 1.1f), true);
            }

            // --- 岩（小石） ---
            for (int i = 0; i < 60; i++)
            {
                Vector2 p = RandomInRing(6f, 62f);
                float s = R(0.35f, 1.7f);
                if (!IsFree(p, 1.4f * s) || !IsLand(p, 0.0f) || ForestLayout.TrailMask(p.x, p.y) > 0.2f) continue;
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.25f * s;
                Quaternion rot = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
                Place(Pick(Rocks), prop, pos, rot, s, true);
                Occupy(p, 1.3f * s);
                if (s > 1.1f) _bigRocks.Add((pos, s));
            }
            // 外周の大岩（自然な境界）
            for (int i = 0; i < 34; i++)
            {
                float a = i / 34f * Mathf.PI * 2f + R(-0.06f, 0.06f);
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(64f, 74f);
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 22f) continue;
                float s = R(2.6f, 5.2f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.35f * s;
                Place(Pick(Rocks), prop, pos, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), s, true, true, 260f);
                Occupy(p, 1.4f * s);
            }

            // --- 大きな落ち葉（足場） ---
            for (int i = 0; i < 130; i++)
            {
                Vector2 p;
                int zone = RI(0, 10);
                if (zone < 2) p = RandomInCircle(ForestLayout.AcornPlaza, 14f);
                else if (zone < 4) p = RandomInCircle(ForestLayout.GreatTree + new Vector2(0f, -28f), 20f);
                else p = RandomInRing(4f, 63f);
                if (!IsLand(p, 0.1f) || InsideOccupied(p)) continue;
                if (ForestLayout.TrailMask(p.x, p.y) > 0.6f && R01() < 0.7f) continue;
                string leaf = Pick(BigLeaves);
                if (Vector2.Distance(p, ForestLayout.Meadow) < 14f) leaf = R01() < 0.7f ? "Leaf_Oak_Green" : leaf;
                float s = R(0.7f, 1.25f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.04f;
                Quaternion rot = GroundRotation(p, R(0, 360), 1f, 3f);
                PlaceLoose(leaf, prop, pos, rot, s, LooseProps.Shape.BigLeaf, true, 120f);   // 乗れる足場で、強い風ですべる
                if (_bigLeaves.Count < 40) _bigLeaves.Add(pos);
            }

            // --- 小枝 ---
            for (int i = 0; i < 18; i++)
            {
                Vector2 p = RandomInRing(6f, 62f);
                if (!IsFree(p, 3f) || !IsLand(p)) continue;
                float s = R(0.8f, 1.2f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f;
                Place(R01() < 0.5f ? "Twig_A" : "Twig_B", prop, pos, GroundRotation(p, R(0, 360), 0.8f, 2f), s, true, true, 150f);
                _twigs.Add(pos);
            }

            // --- どんぐり広場 ---
            Vector2 ap = ForestLayout.AcornPlaza;
            for (int i = 0; i < 22; i++)
            {
                Vector2 p = i < 14 ? RandomInCircle(ap, 12f) : RandomInRing(8f, 60f);
                if (!IsFree(p, 1f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.25f);
                bool lying = R01() < 0.45f;
                Quaternion rot = lying
                    ? Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(0f, 0f, 88f)
                    : GroundRotation(p, R(0, 360), 0.5f, 8f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + (lying ? Vector3.up * 0.42f * s : Vector3.down * 0.03f);
                // どんぐりは、押すと転がる
                var acorn = Place("Acorn", assets.propGlossy, pos, rot, s, true, true, 150f, true);
                if (acorn != null) RollingProp.Make(acorn, assets.Get("Acorn"), s, 0.47f * s, Area);
                Occupy(p, 0.7f * s);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = RandomInCircle(ap, 11f);
                if (!IsFree(p, 1f)) continue;
                float s = R(1.0f, 1.3f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.02f;
                // しずくが入ったぼうし（ひとつ目）は、その場に置いたまま。ほかは押すと動く
                bool special = i == 0 || _specialCap == Vector3.zero;
                if (special)
                {
                    Place("AcornCap", prop, pos, GroundRotation(p, R(0, 360), 0.6f, 5f), s, true);
                    _specialCap = pos;
                }
                else PlaceLoose("AcornCap", prop, pos, GroundRotation(p, R(0, 360), 0.6f, 5f), s, LooseProps.Shape.Cap, true, 150f);
                Occupy(p, 0.8f * s);
            }
            for (int i = 0; i < 10; i++)
            {
                Vector2 p = i < 6 ? RandomInCircle(ap, 12f) : RandomInCircle(ForestLayout.GreatTree + new Vector2(0, -26f), 16f);
                if (!IsFree(p, 1.6f) || !IsLand(p)) continue;
                float s = R(0.9f, 1.3f);
                Quaternion rot = Quaternion.Euler(0f, R(0, 360), 0f) * Quaternion.Euler(R(70f, 95f), 0f, 0f);
                // 松ぼっくりは、押すと転がる
                PlaceLoose("Pinecone", prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.45f * s, rot, s, LooseProps.Shape.Pinecone, true, 150f);
                Occupy(p, 1.4f * s);
            }

            // --- 水たまり（葉っぱの舟と橋） ---
            Vector2 pc = ForestLayout.Pond;
            Vector2[] pads =
            {
                new Vector2(-1.5f, 12.5f), new Vector2(1.5f, 8.6f), new Vector2(-2f, 4.6f), new Vector2(2.2f, 1.2f),
                new Vector2(-1f, -2.6f), new Vector2(3.5f, -5.4f), new Vector2(-4.5f, -1.2f), new Vector2(7.2f, -2.2f),
                new Vector2(-6f, 6f), new Vector2(6.5f, 6.5f),
            };
            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 p = pc + pads[i];
                float s = i < 8 ? R(0.85f, 1.05f) : R(0.6f, 0.8f);
                // 葉の舟は厚みが下向きにあるので、上面が水面より少し上に来るよう浮かせる（重なりのちらつき防止に高さも少しずらす）
                Vector3 pos = new Vector3(p.x, ForestLayout.WaterLevel + 0.14f + i * 0.012f, p.y);
                Place("LilyPad", prop, pos, Quaternion.Euler(0f, R(0, 360), 0f), s, true, false, 150f);
                _lilyPads.Add(pos);
            }
            Place("WaterLily", assets.flowers, new Vector3(pc.x + 3.2f, ForestLayout.WaterLevel + 0.1f, pc.y - 5.0f), Quaternion.Euler(0f, 30f, 0f), 1.0f, false);
            Place("WaterLily", assets.flowers, new Vector3(pc.x - 5.8f, ForestLayout.WaterLevel + 0.1f, pc.y + 6.2f), Quaternion.Euler(0f, 80f, 0f), 0.8f, false);
            // 岸から最初の葉っぱへ渡る小枝の橋
            {
                Vector3 shore = ForestLayout.Ground(pc.x - 1.2f, pc.y + 19.5f);
                Vector3 pad0 = _lilyPads[0];
                Vector3 mid = (shore + pad0) * 0.5f + Vector3.up * 0.05f;
                Vector3 dir = pad0 - shore;
                Quaternion rot = Quaternion.FromToRotation(Vector3.right, dir.normalized);
                Place("Twig_A", prop, mid + Vector3.down * 0.2f, rot, dir.magnitude / 13f, true);
            }
            // 飛び石
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = pc + new Vector2(R(-9f, 9f), R(-9f, 9f));
                float s = R(0.7f, 1.1f);
                string rock = Pick(Rocks);
                Vector3 pos = new Vector3(p.x, ForestLayout.WaterLevel - 0.3f, p.y);
                Place(rock, prop, pos, Quaternion.Euler(0, R(0, 360), 0), s, true);
                StoneFooting(pos, rock, s);   // 水たまりの底までとどく石（水の中で宙にうかない）
            }
        }

        void BuildForestWater()
        {
            int seg = 96;
            float r = ForestLayout.PondRadius * 1.3f;
            var v = new List<Vector3> { new Vector3(ForestLayout.Pond.x, ForestLayout.WaterLevel, ForestLayout.Pond.y) };
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(ForestLayout.Pond.x + Mathf.Cos(a) * r, ForestLayout.WaterLevel, ForestLayout.Pond.y + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < seg; i++)
            {
                t.Add(0);
                t.Add(1 + (i + 1) % seg);
                t.Add(1 + i);
            }
            // 川と同じ水の描き方：UV は水面の位置（さざ波の模様）、頂点色は流れなし・白い泡なし
            var uv = new List<Vector2>(v.Count);
            var col = new List<Color>(v.Count);
            foreach (var p in v)
            {
                uv.Add(new Vector2(p.x / 20f, p.z / 8f));
                col.Add(new Color(0f, 0f, 0f, 1f));
            }
            var mesh = Own(new Mesh { name = "Water" });
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
            // 21. ときどき雨だれのような波紋が広がる（葉っぱの舟のあいだ）
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_Drip", new Vector4(ForestLayout.Pond.x - 3f, ForestLayout.Pond.y + 3f, 2.4f, 0.55f));
            mr.SetPropertyBlock(mpb);
            WaterView.Register(mr);   // 水が映るときだけ、深さと色の写しを作る
        }

        // ------------------------------------------------------------------
        // 草花（コライダーなし）
        // ------------------------------------------------------------------
        void BuildForestFoliage()
        {
            float dens = foliageDensity;
            Material fol = assets.foliage;
            Material flw = assets.flowers;

            // 草（草原は密、ほかはまばら）
            int grassCount = Mathf.RoundToInt(5200 * dens);
            for (int i = 0; i < grassCount; i++)
            {
                Vector2 p;
                float roll = R01();
                if (roll < 0.35f) p = RandomInCircle(ForestLayout.Meadow, 19f);
                else if (roll < 0.47f) p = RandomInCircle(Vector2.zero, 14f);
                else if (roll < 0.57f) p = RandomInCircle(ForestLayout.Pond, ForestLayout.PondRadius * 1.35f);
                else p = RandomInRing(3f, 72f);
                float patch = Mathf.PerlinNoise(p.x * 0.08f + 100f, p.y * 0.08f);
                bool meadow = Vector2.Distance(p, ForestLayout.Meadow) < 18f;
                if (!meadow && patch < 0.52f) continue;
                if (!IsLand(p, 0.12f) || InsideOccupied(p)) continue;
                if (ForestLayout.TrailMask(p.x, p.y) > 0.35f) continue;
                if (Vector2.Distance(p, ForestLayout.Spawn) < 2.5f) continue;
                float s = R(0.55f, 1.15f) * (meadow ? 1.05f : 0.85f);
                Vector3 pos = ForestLayout.Ground(p.x, p.y);
                Place(Pick(Grass), fol, pos, GroundRotation(p, R(0, 360), 0.5f, 5f), s, false, false, 55f);
            }
            // 水辺の葦
            for (int i = 0; i < 16; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = ForestLayout.Pond + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(ForestLayout.PondRadius * 0.82f, ForestLayout.PondRadius * 1.05f);
                if (ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                Place("Reed", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.2f, Quaternion.Euler(0, R(0, 360), 0), R(0.7f, 1.1f), false, true, 120f);
            }
            // クローバー
            int clover = Mathf.RoundToInt(1100 * dens);
            for (int i = 0; i < clover; i++)
            {
                Vector2 p = RandomInRing(2f, 66f);
                float patch = Mathf.PerlinNoise(p.x * 0.06f + 300f, p.y * 0.06f + 40f);
                if (patch < 0.6f) continue;
                if (!IsLand(p, 0.15f) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.4f) continue;
                Place(R01() < 0.5f ? "Clover_A" : "Clover_B", fol, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.7f, 4f), R(0.7f, 1.2f), false, false, 50f);
            }
            // 苔のかたまり
            int moss = Mathf.RoundToInt(1400 * dens);
            for (int i = 0; i < moss; i++)
            {
                Vector2 p = R01() < 0.3f ? RandomInCircle(ForestLayout.MossHill, 16f) : RandomInRing(1f, 70f);
                float patch = Mathf.PerlinNoise(p.x * 0.07f + 500f, p.y * 0.07f + 80f);
                if (patch < 0.5f && Vector2.Distance(p, ForestLayout.MossHill) > 14f) continue;
                if (!IsLand(p, 0.1f) || ForestLayout.TrailMask(p.x, p.y) > 0.5f) continue;
                Place("Moss", fol, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 1f, 3f), R(0.5f, 1.4f), false, false, 60f);
            }
            // 落ち葉のかけら
            int litter = Mathf.RoundToInt(2400 * dens);
            for (int i = 0; i < litter; i++)
            {
                Vector2 p = R01() < 0.25f ? RandomInCircle(ForestLayout.AcornPlaza, 18f) : RandomInRing(1f, 72f);
                float patch = Mathf.PerlinNoise(p.x * 0.045f + 50f, p.y * 0.045f + 20f);
                if (patch < 0.5f && Vector2.Distance(p, ForestLayout.AcornPlaza) > 16f) continue;
                if (!IsLand(p, 0.05f)) continue;
                PlaceLoose(Pick(BigLeaves), assets.prop, ForestLayout.Ground(p.x, p.y) + Vector3.up * 0.01f, GroundRotation(p, R(0, 360), 1f, 6f), R(0.07f, 0.16f), LooseProps.Shape.Leaf, false, 40f);
            }
            // 小さな小石
            int pebbles = Mathf.RoundToInt(600 * dens);
            for (int i = 0; i < pebbles; i++)
            {
                Vector2 p = RandomInRing(1f, 70f);
                if (!IsLand(p, -0.5f)) continue;
                PlaceLoose(Pick(Rocks), assets.prop, ForestLayout.Ground(p.x, p.y), Quaternion.Euler(R(-12f, 12f), R(0, 360), R(-12f, 12f)), R(0.06f, 0.2f), LooseProps.Shape.Pebble, false, 40f);
            }
            // 芽生え
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = i < 10 ? RandomInCircle(Vector2.zero, 9f) : RandomInRing(4f, 64f);
                if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.4f) continue;
                if (Vector2.Distance(p, ForestLayout.Spawn) < 2.5f) continue;
                Place("Sprout", fol, ForestLayout.Ground(p.x, p.y), GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.2f), false, true, 70f);
            }
            // 花
            void Flowers(string name, Vector2 center, float radius, int count, float smin, float smax)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = RandomInCircle(center, radius);
                    if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.3f) continue;
                    if (Vector2.Distance(p, ForestLayout.Spawn) < 3f) continue;
                    Place(name, flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.05f, GroundRotation(p, R(0, 360), 0.2f, 5f), R(smin, smax), false, true, 120f);
                }
            }
            Flowers("Daisy", ForestLayout.Meadow, 16f, 22, 0.8f, 1.2f);
            Flowers("Bellflower", ForestLayout.Meadow, 16f, 12, 0.8f, 1.2f);
            Flowers("Dandelion", ForestLayout.Meadow, 17f, 14, 0.8f, 1.2f);
            Flowers("DandelionPuff", ForestLayout.Meadow, 15f, 7, 0.9f, 1.2f);
            Flowers("Strawberry", ForestLayout.Meadow, 15f, 6, 0.9f, 1.3f);
            Flowers("Daisy", Vector2.zero, 60f, 14, 0.7f, 1.1f);
            Flowers("Dandelion", Vector2.zero, 60f, 8, 0.7f, 1.0f);
            Flowers("Bellflower", ForestLayout.MossHill, 14f, 6, 0.8f, 1.1f);
            Flowers("Strawberry", Vector2.zero, 50f, 5, 0.8f, 1.2f);
            Flowers("Daisy", Vector2.zero, 10f, 5, 0.7f, 1.0f);
            // シダ（外周と大樹のまわり）
            for (int i = 0; i < 60; i++)
            {
                Vector2 p;
                if (i < 38) p = RandomInRing(56f, 76f);
                else if (i < 50) p = RandomInCircle(ForestLayout.GreatTree + new Vector2(0f, -22f), 26f);
                else p = RandomInCircle(ForestLayout.LogCenter, 16f);
                if (!IsLand(p) || InsideOccupied(p) || ForestLayout.TrailMask(p.x, p.y) > 0.2f) continue;
                if (p.magnitude < 50f && Vector2.Distance(p, ForestLayout.Spawn) < 12f) continue;
                Place(R01() < 0.5f ? "Fern_A" : "Fern_B", flw, ForestLayout.Ground(p.x, p.y) + Vector3.down * 0.3f, GroundRotation(p, R(0, 360), 0.3f, 6f), R(0.7f, 1.3f), false, true, 200f);
            }
        }

        void PlaceForestDewdrops()
        {
            DewdropPoints.Clear();
            // はじまりの場所（チュートリアル）
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(0.5f, 4.5f));
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(-4.5f, 1.5f));
            AddDewFromAbove(ForestLayout.Spawn + new Vector2(3.5f, -4.5f));
            // 切り株のてっぺん
            AddDewFromAbove(ForestLayout.Stump);
            AddDewFromAbove(ForestLayout.Stump + new Vector2(4.2f, -2.5f));
            // 丸太の中
            for (int i = -1; i <= 1; i += 2)
            {
                Vector2 c = ForestLayout.LogCenter + ForestLayout.LogDir * (i * 11f);
                Vector3 inside = ForestLayout.Ground(c.x, c.y) + Vector3.up * 3.4f;
                if (CastDown(inside, 6f, out var hit)) AddDew(hit.point);
            }
            // 丸太の上
            AddDewFromAbove(ForestLayout.LogCenter + ForestLayout.LogDir * 4f);
            // 葉っぱの舟
            if (_lilyPads.Count > 5)
            {
                AddDewFromAbove(new Vector2(_lilyPads[4].x, _lilyPads[4].z));
                AddDewFromAbove(new Vector2(_lilyPads[7].x, _lilyPads[7].z));
            }
            // どんぐりの帽子の中
            if (_specialCap != Vector3.zero)
            {
                if (CastDown(_specialCap + Vector3.up * 3f, 5f, out var hit)) DewdropPoints.Add(hit.point);
            }
            // 赤キノコのかさの上
            _redCaps.Sort((a, b) => b.scale.CompareTo(a.scale));
            for (int i = 0; i < Mathf.Min(3, _redCaps.Count); i++)
                AddDewFromAbove(new Vector2(_redCaps[i].pos.x + 0.6f, _redCaps[i].pos.z + 0.4f));
            // 大樹の根の上
            int roots = 0;
            for (int k = 0; k < 80 && roots < 4; k++)
            {
                float a = R(Mathf.PI * 1.05f, Mathf.PI * 1.95f);
                Vector2 p = ForestLayout.GreatTree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(20f, 36f);
                if (p.magnitude > ForestLayout.PlayRadius - 3f) continue;
                if (CastDown(new Vector3(p.x, 150f, p.y), 200f, out var hit) && hit.point.y > ForestLayout.Height(p.x, p.y) + 1.5f)
                {
                    int before = DewdropPoints.Count;
                    AddDew(hit.point);
                    if (DewdropPoints.Count > before) roots++;
                }
            }
            // 光るキノコの洞
            AddDewFromAbove(ForestLayout.ArchTarget + new Vector2(3f, 4f), 5f + ForestLayout.Height(ForestLayout.ArchTarget.x, ForestLayout.ArchTarget.y));
            AddDewFromAbove(ForestLayout.ArchTarget + new Vector2(-1f, -1f), 5f + ForestLayout.Height(ForestLayout.ArchTarget.x, ForestLayout.ArchTarget.y));
            // 大きな岩の上
            for (int i = 0; i < Mathf.Min(5, _bigRocks.Count); i++)
                AddDewFromAbove(new Vector2(_bigRocks[i].pos.x, _bigRocks[i].pos.z));
            // 小枝・落ち葉の上
            for (int i = 0; i < Mathf.Min(2, _twigs.Count); i++)
                AddDewFromAbove(new Vector2(_twigs[i].x, _twigs[i].z));
            for (int i = 0; i < Mathf.Min(4, _bigLeaves.Count); i++)
                AddDewFromAbove(new Vector2(_bigLeaves[i * 7 % _bigLeaves.Count].x, _bigLeaves[i * 7 % _bigLeaves.Count].z));
            // 草原
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(2f, 1f));
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(-6f, 5f));
            AddDewFromAbove(ForestLayout.Meadow + new Vector2(5f, -7f));
            // 残りは方角ごとに均等に散らす
            int guard = 0;
            int target = Area.DropCount;
            while (DewdropPoints.Count < target && guard++ < 2000)
            {
                int sector = DewdropPoints.Count % 8;
                float a = (sector + R01()) / 8f * Mathf.PI * 2f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(6f, 60f);
                if (!IsLand(p, 0.2f)) continue;
                AddDewFromAbove(p);
            }
        }

        // ------------------------------------------------------------------
        // いきもの（森）
        // ------------------------------------------------------------------
        static readonly Vector2 AntNest1 = new Vector2(8f, -1.5f);
        static readonly Vector2 AntFood1 = new Vector2(3.5f, 7.5f);

        void PlaceForestMobProps()
        {
            // アリの巣と行列
            AddAntLine(AntNest1, AntFood1, 12);
            AddAntLine(ForestLayout.AcornPlaza + new Vector2(8f, -6f), ForestLayout.AcornPlaza + new Vector2(-2f, 0.5f), 14);
            // オトシブミのゆりかご
            Vector2[] centers = { ForestLayout.MossHill + new Vector2(3f, -2f), ForestLayout.Meadow + new Vector2(-12f, -6f) };
            int[] counts = { 4, 3 };
            for (int k = 0; k < centers.Length; k++)
            {
                for (int i = 0; i < counts[k]; i++)
                {
                    Vector2 p = RandomInCircle(centers[k], 3f);
                    if (!IsLand(p)) continue;
                    Place("Cradle", assets.prop, ForestLayout.Ground(p.x, p.y), Quaternion.Euler(R(-6f, 6f), R(0, 360), R(-6f, 6f)), R(0.9f, 1.3f), true, true, 100f);
                }
            }
        }

        void PlaceForestCreatures()
        {
            Vector2 pond = ForestLayout.Pond;
            Vector2 meadow = ForestLayout.Meadow;
            Vector2 moss = ForestLayout.MossHill;
            AddMob("ant", ForestLayout.Ground(meadow.x - 4f, meadow.y), 5, 6f);
            // てんとう虫：花の草原の葉っぱの上と、まわりの花
            if (CastDown(_ladybugLeaf + Vector3.up * 6f, 10f, out var leafHit)) AddMob("ladybug", leafHit.point, 1, 0.8f);
            AddMob("ladybug", ForestLayout.Ground(meadow.x + 6f, meadow.y + 4f), 2, 4f);
            AddMob("ladybug", ForestLayout.Ground(moss.x, moss.y), 1, 4f);
            // かたつむり：いちばん大きな赤キノコの根元をぐるり
            {
                Vector2 c = ForestLayout.MushroomGrove;
                var g = AddMob("snail", ForestLayout.Ground(c.x, c.y), 1, 2.2f);
                g.path.Add(ForestLayout.Ground(c.x, c.y));
                AddMob("snail", ForestLayout.Ground(moss.x - 4f, moss.y + 3f), 1, 3f);
                Vector2 lg = ForestLayout.LogCenter + ForestLayout.LogDir * 27f + new Vector2(3f, 0f);
                AddMob("snail", ForestLayout.Ground(lg.x, lg.y), 1, 2.5f);
            }
            // ちょうちょ
            AddMob("butterfly", ForestLayout.Ground(meadow.x, meadow.y), 4, 9f, 3.5f);
            AddMob("butterfly", ForestLayout.Ground(3f, 3f), 2, 6f, 3f);
            AddMob("butterfly", ForestLayout.Ground(pond.x, pond.y + 16f), 1, 5f, 3f);
            // オトシブミ（ゆりかごのそば）
            AddMob("otoshibumi", ForestLayout.Ground(moss.x + 3f, moss.y - 2f), 2, 3f);
            AddMob("otoshibumi", ForestLayout.Ground(meadow.x - 12f, meadow.y - 6f), 1, 3f);
            // バッタ
            AddMob("grasshopper", ForestLayout.Ground(meadow.x, meadow.y), 4, 12f);
            AddMob("grasshopper", ForestLayout.Ground(9f, 8f), 2, 6f);
            // アマガエル・アメンボ・トンボ（水たまり）
            AddMob("frog", ForestLayout.Ground(pond.x - 2f, pond.y + 15.5f), 1, 2f);
            AddMob("frog", ForestLayout.Ground(pond.x + 14f, pond.y + 2f), 1, 2f);
            AddMob("waterstrider", new Vector3(pond.x + 4f, ForestLayout.WaterLevel, pond.y + 2f), 2, 5f);
            AddMob("dragonfly", new Vector3(pond.x, ForestLayout.WaterLevel, pond.y), 1, 8f, 2.5f);
            // だんごむし：丸太のトンネルの奥と、落ち葉のたまり場
            {
                Vector2 c = ForestLayout.LogCenter - ForestLayout.LogDir * 17f;
                Vector3 inside = ForestLayout.Ground(c.x, c.y) + Vector3.up * 3.4f;
                if (CastDown(inside, 6f, out var hit)) AddMob("pillbug", hit.point, 1, 1.5f);
                AddMob("pillbug", ForestLayout.Ground(ForestLayout.AcornPlaza.x + 3f, ForestLayout.AcornPlaza.y + 3f), 2, 4f);
            }
            // カブトムシ：大樹の根の上
            for (int k = 0; k < 120; k++)
            {
                float a = Mathf.Lerp(Mathf.PI * 1.15f, Mathf.PI * 1.85f, k / 120f);
                Vector2 p = ForestLayout.GreatTree + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (24f + (k % 5) * 2f);
                if (p.magnitude > ForestLayout.PlayRadius - 4f) continue;
                if (CastDown(new Vector3(p.x, 150f, p.y), 200f, out var hit) && hit.point.y > ForestLayout.Height(p.x, p.y) + 2f && hit.normal.y > 0.6f)
                {
                    AddMob("beetle", hit.point, 1, 1.2f);
                    break;
                }
            }
            AddMob("beetle", ForestLayout.Ground(ForestLayout.Stump.x - 11f, ForestLayout.Stump.y + 4f), 1, 2f);
            // スズメ（ひらけた場所を飛びまわる）
            {
                var g = AddMob("sparrow", ForestLayout.Ground(meadow.x - 2f, meadow.y - 4f), 3, 3f);
                Vector2[] spots = { meadow + new Vector2(-2f, -4f), new Vector2(6f, 13f), new Vector2(-2f, -16f), ForestLayout.AcornPlaza + new Vector2(2f, 4f), new Vector2(20f, 12f) };
                foreach (var p in spots) g.path.Add(ForestLayout.Ground(p.x, p.y));
            }
            // ハエトリグモ（切り株の上・苔の丘・はじまりの苔原の近く）
            AddMob("spider", TopSurface(ForestLayout.Stump + new Vector2(1.5f, -1f)), 2, 2.5f);
            AddMob("spider", ForestLayout.Ground(moss.x + 5f, moss.y + 4f), 1, 2.5f);
            AddMob("spider", ForestLayout.Ground(-7f, 4f), 1, 2f);
            AddMob("spider", ForestLayout.Ground(ForestLayout.AcornPlaza.x - 4f, ForestLayout.AcornPlaza.y + 5f), 1, 2f);
            // オオカマキリ（花の草原の草むら・大樹の根もと）
            AddMob("mantis", ForestLayout.Ground(meadow.x + 7f, meadow.y - 7f), 1, 3f);
            AddMob("mantis", ForestLayout.Ground(meadow.x - 9f, meadow.y + 5f), 1, 3f);
            AddMob("mantis", ForestLayout.Ground(ForestLayout.GreatTree.x + 10f, ForestLayout.GreatTree.y - 20f), 1, 3f);
            // 光るキノコの洞のまわりを飛ぶコウモリ
            Vector2 cave = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
            AddMob("koumori", ForestLayout.Ground(cave.x, cave.y), 3, 6f, 3.5f);
            // 草むらの小枝にまぎれるナナフシ
            AddMob("nanafushi", ForestLayout.Ground(ForestLayout.MossHill.x + 4f, ForestLayout.MossHill.y - 3f), 1, 2.5f);
            AddMob("nanafushi", ForestLayout.Ground(ForestLayout.GreatTree.x + 12f, ForestLayout.GreatTree.y - 18f), 1, 3f);
            // 水たまりの中を泳ぐゲンゴロウ
            AddMob("gengorou", new Vector3(pond.x - 4f, ForestLayout.WaterLevel, pond.y + 3f), 2, 6f);
            // 花の草原の花にまぎれるハナカマキリ
            AddMob("hanakamakiri", ForestLayout.Ground(meadow.x + 5f, meadow.y + 3f), 1, 3f);
            // 草原のはしのモグラ塚
            AddMob("mogura", ForestLayout.Ground(meadow.x - 14f, meadow.y - 10f), 1, 0.3f);
            // カラス（高い場所に下りて見張る）
            {
                var g = AddMob("crow", TopSurface(ForestLayout.Stump), 1, 2f);
                g.path.Add(TopSurface(ForestLayout.Stump));
                g.path.Add(ForestLayout.Ground(-12f, -30f));
                g.path.Add(ForestLayout.Ground(22f, 50f));
                g.path.Add(ForestLayout.Ground(-30f, 10f));
            }
        }

        /// <summary>赤キノコの森の、大きな赤キノコ（林のまん中からの場所と大きさ）。</summary>
        static readonly Vector2[] BigReds = { new Vector2(0, 0), new Vector2(-7, 5), new Vector2(6, 6), new Vector2(4, -7), new Vector2(-6, -6) };
        static readonly float[] BigRedScales = { 1.6f, 1.2f, 1.0f, 1.35f, 0.9f };

        static readonly Vector2[] ForestShaftSpots =
        {
            new Vector2(2f, 3f), ForestLayout.Meadow, ForestLayout.Meadow + new Vector2(-8f, 6f), ForestLayout.Pond + new Vector2(2f, 2f),
            ForestLayout.Stump + new Vector2(-2f, 2f), ForestLayout.MushroomGrove + new Vector2(-3f, 2f), ForestLayout.AcornPlaza,
            new Vector2(-20f, -8f), new Vector2(18f, 22f), ForestLayout.Gate + new Vector2(-4f, 1f),
        };

        void DrawForestMap(Color32[] px, int size, bool generated)
        {
            ColorUtility.TryParseHtmlString("#6b4a32", out var bark);
            ColorUtility.TryParseHtmlString("#c9a46a", out var wood);
            Vector2 la = ForestLayout.LogCenter - ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            Vector2 lb = ForestLayout.LogCenter + ForestLayout.LogDir * (ForestLayout.LogLength * 0.5f);
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                Vector2 p = MapToWorld(i, j, size);
                Color c = px[j * size + i];
                if (Vector2.Distance(p, ForestLayout.GreatTree) < 16f) c = bark;
                if (Vector2.Distance(p, ForestLayout.Stump) < 8f) c = Vector2.Distance(p, ForestLayout.Stump) < 6.8f ? wood : bark;
                if (ShakuMath.DistToSegment(p, la, lb) < 5f) c = bark * 1.15f;
                px[j * size + i] = c;
            }
            // 86. 小道・90. 花の草原の花畑・91. 苔の丘・93. 大樹の根
            MapTrails(px, size, ForestLayout.Trails, new Color32(214, 194, 150, 255));
            for (int k = 0; k < 60; k++)
            {
                float a = k * 2.39996f, r = Mathf.Sqrt(k / 60f) * 12f;
                Vector2 p = ForestLayout.Meadow + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var fc = k % 3 == 0 ? new Color32(255, 240, 120, 255) : k % 3 == 1 ? new Color32(255, 255, 255, 255) : new Color32(180, 150, 230, 255);
                MapDot(px, size, new Vector3(p.x, 0f, p.y), 0.6f, fc);
            }
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                Vector2 p = MapToWorld(i, j, size);
                float m = ShakuMath.SmoothStep(14f, 9f, Vector2.Distance(p, ForestLayout.MossHill));
                if (m > 0f) px[j * size + i] = Color32.Lerp(px[j * size + i], new Color32(96, 170, 70, 255), m * 0.45f);
            }
            for (int k = 0; k < 9; k++)
            {
                float a = Mathf.Lerp(Mathf.PI * 1.1f, Mathf.PI * 1.9f, k / 8f);
                for (float d = 15f; d < 34f; d += 1.2f)
                    MapDot(px, size, new Vector3(ForestLayout.GreatTree.x + Mathf.Cos(a) * d, 0f, ForestLayout.GreatTree.y + Mathf.Sin(a) * d + Mathf.Sin(d * 0.4f + k) * 0.8f), 1f, new Color32(120, 86, 58, 255));
            }
            if (generated)
                foreach (var (pos, s) in _redCaps) MapDot(px, size, pos, Mathf.Max(2f, 2.5f * s), new Color32(230, 70, 60, 255));
            if (generated)
            {
                // 87. 葉っぱの舟・88. どんぐり・89. 光るキノコと道しるべ・92. 外周の大岩
                foreach (var p in _lilyPads) MapDot(px, size, p, 2.2f, new Color32(84, 160, 80, 255));
                for (int k = 0; k < 18; k++)
                {
                    float a = k * 2.39996f, r = Mathf.Sqrt(k / 18f) * 11f;
                    MapDot(px, size, new Vector3(ForestLayout.AcornPlaza.x + Mathf.Cos(a) * r, 0f, ForestLayout.AcornPlaza.y + Mathf.Sin(a) * r), 1f, new Color32(150, 98, 52, 255));
                }
                Vector2 h = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
                for (int k = 0; k < 10; k++)
                    MapDot(px, size, new Vector3(h.x + Mathf.Cos(k * 2.4f) * 4f, 0f, h.y + Mathf.Sin(k * 2.4f) * 4f), 1f, new Color32(110, 220, 255, 255));
                foreach (var p in ExtraSpots("glowtrail")) MapDot(px, size, p, 0.8f, new Color32(110, 220, 255, 255));
                foreach (var p in ExtraSpots("boundary")) MapDot(px, size, p, 0.9f, new Color32(150, 150, 140, 255));
            }
            else
                for (int i = 0; i < BigReds.Length; i++)
                {
                    Vector2 p = ForestLayout.MushroomGrove + BigReds[i];
                    MapDot(px, size, new Vector3(p.x, 0f, p.y), Mathf.Max(2f, 2.5f * BigRedScales[i]), new Color32(230, 70, 60, 255));
                }
        }
    }
}
