using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>森の生成（地形・配置・しずく・ミニマップ）を実際に組み立てて検証する。</summary>
    public class WorldGeneratorTests
    {
        static WorldGenerator _gen;

        [OneTimeSetUp]
        public void Generate()
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            var go = new GameObject("World");
            _gen = go.AddComponent<WorldGenerator>();
            _gen.assets = TestUtil.LoadWorldAssets();
            _gen.instanced = go.AddComponent<InstancedRenderer>();
            _gen.sun = sun;
            Areas.Current = Areas.Forest;
            _gen.GenerateNow();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_gen != null) _gen.Clear();
        }

        [Test]
        public void IsGeneratedWithRoot()
        {
            Assert.IsTrue(_gen.IsGenerated);
            Assert.IsNotNull(_gen.Root);
            Assert.AreSame(_gen, WorldGenerator.Instance);
        }

        [Test]
        public void TerrainHasCollidersOnSurfaceLayer()
        {
            for (float x = -50f; x <= 50f; x += 25f)
            for (float z = -50f; z <= 50f; z += 25f)
            {
                bool hit = Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var h, 400f, ShakuConst.SurfaceMask);
                Assert.IsTrue(hit, $"({x},{z}) に地面がない");
                Assert.AreEqual(ShakuConst.SurfaceLayer, h.collider.gameObject.layer);
            }
        }

        [Test]
        public void TerrainMatchesHeightFunction()
        {
            var p = new Vector2(12.3f, -7.7f);
            Assert.IsTrue(Physics.Raycast(new Vector3(p.x, 50f, p.y), Vector3.down, out var h, 100f, ShakuConst.SurfaceMask));
            if (h.collider.name.StartsWith("Terrain"))
                Assert.AreEqual(ForestLayout.Height(p.x, p.y), h.point.y, 0.15f);
        }

        [Test]
        public void FortyFiveDewdrops_AllReachable()
        {
            var pts = _gen.DewdropPoints;
            Assert.AreEqual(45, pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Assert.IsTrue(ForestLayout.InPlayArea(pts[i]), $"#{i} {pts[i]} がプレイ範囲外");
                Assert.IsFalse(ForestLayout.IsUnderwater(pts[i]), $"#{i} {pts[i]} が水中");
                for (int j = i + 1; j < pts.Count; j++)
                    Assert.Greater(Vector3.Distance(pts[i], pts[j]), 0.5f, $"#{i} と #{j} が重なっている");
                // しずくの真下には這える面がある
                Assert.IsTrue(Physics.Raycast(pts[i] + Vector3.up * 0.3f, Vector3.down, 1f, ShakuConst.SurfaceMask), $"#{i} の下に面がない");
            }
        }

        [Test]
        public void SomeDewdropsAreOnHighPlaces()
        {
            int high = 0;
            foreach (var p in _gen.DewdropPoints)
                if (p.y > ForestLayout.Height(p.x, p.z) + 2f) high++;
            Assert.GreaterOrEqual(high, 5, "切り株やキノコの上など、登らないと取れないしずくがある");
        }

        [Test]
        public void SpawnIsOnGroundNearDesignatedPoint()
        {
            var s = _gen.SpawnPoint;
            Assert.Less(Vector2.Distance(new Vector2(s.x, s.z), ForestLayout.Spawn), 0.5f);
            Assert.AreEqual(ForestLayout.Height(s.x, s.z), s.y, 1.0f);
        }

        [Test]
        public void LandmarkObjectsExist()
        {
            // 切り株のてっぺん
            Assert.IsTrue(Physics.Raycast(new Vector3(ForestLayout.Stump.x, 100f, ForestLayout.Stump.y), Vector3.down, out var stump, 200f, ShakuConst.SurfaceMask));
            Assert.Greater(stump.point.y, ForestLayout.Height(ForestLayout.Stump.x, ForestLayout.Stump.y) + 7f, "切り株の上面");
            // 丸太の中は空洞（中心から下に床がある）
            var lm = ForestLayout.Landmarks[8];
            Vector3 mid = (lm.capsuleA + lm.capsuleB) * 0.5f;
            Assert.IsTrue(Physics.Raycast(mid + Vector3.up * 1.5f, Vector3.down, out var floor, 5f, ShakuConst.SurfaceMask), "丸太の中の床");
            Assert.Less(floor.point.y, mid.y);
            Assert.IsTrue(Physics.Raycast(mid + Vector3.up * 1.5f, Vector3.up, 6f, ShakuConst.SurfaceMask), "丸太の天井");
            // 大樹
            Assert.IsTrue(Physics.Raycast(new Vector3(ForestLayout.GreatTree.x, 60f, ForestLayout.GreatTree.y - 40f), Vector3.forward, 60f, ShakuConst.SurfaceMask), "大樹の幹");
            // 水面
            Assert.IsNotNull(GameObject.Find("Water"));
        }

        [Test]
        public void ArchRootFormsHollowAboveGlowcaps()
        {
            var lm = ForestLayout.Landmarks[2];
            float g = ForestLayout.Height(lm.position.x, lm.position.y);
            bool roof = false;
            for (int i = -2; i <= 2 && !roof; i++)
            for (int j = -2; j <= 2 && !roof; j++)
            {
                var p = new Vector3(lm.position.x + i * 2f, g + 1.5f, lm.position.y + j * 2f);
                if (Physics.Raycast(p, Vector3.up, 20f, ShakuConst.SurfaceMask)) roof = true;
            }
            Assert.IsTrue(roof, "光るキノコの洞の上には大樹の根のアーチがある");
        }

        [Test]
        public void LilyPadsAreAboveWaterAndWalkable()
        {
            Vector2 p = ForestLayout.Pond;
            int pads = 0;
            for (float x = -10f; x <= 10f; x += 1f)
            for (float z = -10f; z <= 14f; z += 1f)
            {
                if (Physics.Raycast(new Vector3(p.x + x, 5f, p.y + z), Vector3.down, out var h, 10f, ShakuConst.SurfaceMask)
                    && h.collider.name.StartsWith("LilyPad"))
                {
                    pads++;
                    Assert.IsFalse(ForestLayout.IsUnderwater(h.point), "葉の舟の上は水中扱いにならない");
                }
            }
            Assert.Greater(pads, 20);
        }

        [Test]
        public void Pond_IsDrawnLikeTheRiver()
        {
            var water = GameObject.Find("Water");
            Assert.IsNotNull(water, "水たまり");
            var mr = water.GetComponent<MeshRenderer>();
            Assert.AreSame(_gen.assets.pond, mr.sharedMaterial, "川と同じ水の描き方");
            Assert.AreEqual("Shakutori/ToonRiver", mr.sharedMaterial.shader.name);
            Assert.AreEqual(0f, mr.sharedMaterial.GetFloat("_StreakStrength"), "流れの筋はない");
            var mesh = water.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(mesh.vertexCount, mesh.uv.Length, "さざ波の模様の UV");
            Assert.AreEqual(mesh.vertexCount, mesh.colors.Length);
            foreach (var c in mesh.colors) Assert.AreEqual(0f, c.g, "白くあわ立つ所はない");
        }

        [Test]
        public void ManyInstancesAreRendered()
        {
            Assert.Greater(_gen.instanced.InstanceCount + _gen.loose.Count, 3000);
        }

        [Test]
        public void SmallThings_ArePushableNotFixed()
        {
            Assert.Greater(_gen.loose.Count, 1000, "落ち葉・小石・松ぼっくりは、押すと動く物");
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                Assert.AreNotEqual("Pinecone", mc.name, "松ぼっくりは、動かない当たり判定ではない");
            int cones = 0;
            for (int i = 0; i < _gen.loose.Count; i++) if (_gen.loose.MeshNameOf(i) == "Pinecone") cones++;
            Assert.GreaterOrEqual(cones, 2, "松ぼっくりは押すと転がる");
            Assert.AreEqual(0, _gen.loose.BodyCount, "はじめは絵だけ（体は、しゃくとりむしが近づいてから）");
        }

        [Test]
        public void MapTextureIsBuilt()
        {
            Assert.IsNotNull(_gen.MapTexture);
            Assert.AreEqual(256, _gen.MapTexture.width);
            Assert.AreEqual(256, _gen.MapTexture.height);
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            var first = new List<Vector3>(_gen.DewdropPoints);
            _gen.GenerateNow();
            Assert.AreEqual(first.Count, _gen.DewdropPoints.Count);
            for (int i = 0; i < first.Count; i++)
                TestUtil.AssertVector(first[i], _gen.DewdropPoints[i], 1e-3f, $"#{i}");
        }

        [Test]
        public void ClearRemovesEverything()
        {
            var go = new GameObject("World2");
            var g2 = go.AddComponent<WorldGenerator>();
            g2.assets = _gen.assets;
            g2.instanced = go.AddComponent<InstancedRenderer>();
            g2.GenerateNow();
            g2.Clear();
            Assert.IsNull(g2.Root);
            Assert.IsFalse(g2.IsGenerated);
            Assert.AreEqual(0, g2.instanced.InstanceCount);
            Object.DestroyImmediate(go);
            _gen.GenerateNow();   // 他のテストのために元に戻す
        }
    }
}
