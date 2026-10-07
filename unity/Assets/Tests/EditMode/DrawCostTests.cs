using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>描く量をへらす工夫の単体テスト：見えない物・消えてしまう物は描かず、影だけの物は影にだけ描く。</summary>
    public class DrawCostTests
    {
        [SetUp]
        public void SetUp() => TestUtil.NewEmptyScene();

        static Camera FrontCamera()
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.LookRotation(Vector3.forward));
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.aspect = 16f / 9f;
            return cam;
        }

        static Matrix4x4 At(float z) => Matrix4x4.TRS(new Vector3(0f, 0f, z), Quaternion.identity, Vector3.one);

        [Test]
        public void Instances_OutsideViewOrBeyondFade_AreNotDrawn()
        {
            var assets = TestUtil.LoadWorldAssets();
            var mesh = assets.Get("Rock_A");
            var r = new GameObject("ir").AddComponent<InstancedRenderer>();
            // 草：52 m で完全に消える。影は落とさない
            float fadeEnd = assets.foliage.GetFloat("_FarFadeEnd");
            r.Add(mesh, assets.foliage, At(10f), false, 200f);              // 前・近い → 描く
            r.Add(mesh, assets.foliage, At(fadeEnd + 15f), false, 200f);    // 前・消える距離より遠い → 描かない
            r.Add(mesh, assets.foliage, At(-10f), false, 200f);             // うしろ → 描かない
            // 小物：影を落とす
            r.Add(mesh, assets.prop, At(10f), true, 300f);                  // 前・近い → 描いて、影も
            r.Add(mesh, assets.prop, At(-10f), true, 300f);                 // うしろ・近い → 影だけ（視野の外から影が落ちる）
            r.Add(mesh, assets.prop, At(-100f), true, 300f);                // うしろ・遠い → 何もしない
            r.Add(mesh, assets.prop, At(150f), true, 300f);                 // 前・影の届かない遠く → 描くが、影は描かない
            r.Build();
            r.Render(FrontCamera());
            Assert.AreEqual(3, r.DrawnCount, "画面に描くのは、前の草 1 つと、前の小物 2 つ");
            Assert.AreEqual(1, r.ShadowedCount, "影も落とすのは、近くの小物だけ");
            Assert.AreEqual(1, r.ShadowOnlyCount, "うしろの近くの小物は、影だけ");
        }

        [Test]
        public void DensePlants_HaveLighterShapes_ForDistance()
        {
            var assets = TestUtil.LoadWorldAssets();
            foreach (var name in new[] { "Clover_A", "Clover_B", "Fern_A", "Moss", "Daisy", "Rock_A", "Mushroom_Red", "Pinecone", "Leaf_Maple_Red", "Leaf_Oak_Green", "Ant", "Frog" })
            {
                var m = assets.Get(name);
                Assert.IsNotNull(m, name);
                Assert.Greater(m.lodCount, 1, $"{name} は遠くで使う三角形の少ない形を持つ");
                int prev = int.MaxValue;
                for (int lod = 0; lod < m.lodCount; lod++)
                {
                    int n = m.GetTriangles(0, lod, true).Length;
                    Assert.Less(n, prev, $"{name}：段が上がるほど三角形が少ない");
                    prev = n;
                }
                // いちばん細かい形は、もとの形そのまま
                Assert.AreEqual(m.GetTriangles(0, 0, true).Length, m.triangles.Length, $"{name}：ふつうに読むと、いちばん細かい形");
            }
            // 軽い物・形が変わる物・当たり判定用のメッシュは、そのまま
            foreach (var name in new[] { "Grass_A", "Grass_B", "Dewdrop", "Mushroom_Red_Col", "Inchworm" })
                Assert.LessOrEqual(assets.TryGet(name)?.lodCount ?? 1, 1, $"{name} は形を減らさない");
        }

        [Test]
        public void LighterShapes_AreMadeOnceAndKeepTheOutline()
        {
            var assets = TestUtil.LoadWorldAssets();
            var src = assets.Get("Rock_A");
            Assert.AreSame(src, DetailMeshes.Get(src, 0), "いちばん細かい段は、もとのメッシュ");
            var l1 = DetailMeshes.Get(src, 1);
            Assert.AreNotSame(src, l1);
            Assert.AreSame(l1, DetailMeshes.Get(src, 1), "一度作ったら使いまわす");
            Assert.Less(l1.GetIndexCount(0), src.GetIndexCount(0, 0), "三角形が少ない");
            Assert.AreEqual(src.GetIndexCount(0, 1), l1.GetIndexCount(0), "取り込み時に作った段そのもの");
            Assert.AreEqual(src.vertexCount, l1.vertexCount, "頂点（色・UV）はもとのまま");
            Assert.AreEqual(src.bounds.center, l1.bounds.center);
            Assert.AreEqual(src.bounds.size, l1.bounds.size, "見た目の範囲は同じ");
            var grass = assets.Get("Grass_A");
            Assert.AreSame(grass, DetailMeshes.Get(grass, 2), "段を持たないメッシュは、そのまま");
        }

        [Test]
        public void Collision_AlwaysUsesTheFullShape()
        {
            var assets = TestUtil.LoadWorldAssets();
            foreach (var name in new[] { "Leaf_Maple_Red", "LilyPad", "Acorn", "Twig_A" })
            {
                var src = assets.Get(name);
                Assert.Greater(src.lodCount, 1, name);
                var col = DetailMeshes.ForCollision(src);
                Assert.AreSame(col, DetailMeshes.ForCollision(src), "一度作ったら使いまわす");
                Assert.AreEqual(1, col.lodCount, $"{name}：当たり判定には、少ない形はまざらない");
                CollectionAssert.AreEqual(src.triangles, col.triangles, $"{name}：三角形も順番も、いちばん細かい形と同じ");
                CollectionAssert.AreEqual(src.vertices, col.vertices);
                Assert.IsTrue(col.isReadable, "すべりの計算で法線を読めるように");
            }
            var grass = assets.Get("Grass_A");
            Assert.AreSame(grass, DetailMeshes.ForCollision(grass), "段を持たないメッシュは、そのまま");
        }

        [Test]
        public void LodBands_FollowScreenSize()
        {
            Assert.AreEqual(0, InstancedRenderer.LodFor(5f, 1f, 4), "近くは、いちばん細かい形");
            Assert.AreEqual(1, InstancedRenderer.LodFor(15f, 1f, 4));
            Assert.AreEqual(2, InstancedRenderer.LodFor(30f, 1f, 4));
            Assert.AreEqual(3, InstancedRenderer.LodFor(200f, 1f, 4));
            Assert.AreEqual(1, InstancedRenderer.LodFor(200f, 1f, 2), "持っている段より上は使わない");
            Assert.AreEqual(0, InstancedRenderer.LodFor(30f, 3f, 4), "大きい物は、遠くまで細かい形");
        }

        [Test]
        public void TurningAround_SwapsWhatIsDrawn()
        {
            var assets = TestUtil.LoadWorldAssets();
            var mesh = assets.Get("Rock_A");
            var r = new GameObject("ir").AddComponent<InstancedRenderer>();
            for (int i = 0; i < 20; i++)
            {
                r.Add(mesh, assets.prop, At(5f + i), false, 300f);
                r.Add(mesh, assets.prop, At(-5f - i), false, 300f);
            }
            r.Build();
            var cam = FrontCamera();
            r.Render(cam);
            Assert.AreEqual(20, r.DrawnCount, "前の 20 個だけ");
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            r.Render(cam);
            Assert.AreEqual(20, r.DrawnCount, "うしろを向くと、うしろの 20 個だけ");
            Assert.AreEqual(0, r.ShadowOnlyCount, "影を落とさない物は、影にも描かない");
        }
    }
}
