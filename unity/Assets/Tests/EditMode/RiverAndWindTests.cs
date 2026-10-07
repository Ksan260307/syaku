using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>川の流れの見た目と、草花のゆれ（風の位相）。</summary>
    public class RiverAndWindTests
    {
        [Test]
        public void SwayPhase_NeverSpeedsUpOverTime()
        {
            // 「時間 × 速さ」では、遊んだ時間が長いほど、突風で速さが変わったときに草花が高速でふるえた。
            // 位相を積み上げれば、何分たっても、ゆれる速さはいつも 0.9〜1.5
            Wind.ResetPhase();
            float t = 1800f;
            Wind.Publish(t);
            float prev = Wind.Phase;
            for (int i = 0; i < 60 * 60; i++)
            {
                t += 1f / 60f;
                Wind.Publish(t);
                float rate = (Wind.Phase - prev) * 60f;
                prev = Wind.Phase;
                if (rate < 0f) continue;   // ひとまわりして、もどったとき
                Assert.That(rate, Is.InRange(0.85f, 1.55f), $"t={t}");
            }
            Wind.ResetPhase();
        }

        [Test]
        public void RiverFlow_IsFastInTheMiddleAndSlowInThePool()
        {
            float z = 20f;
            float cx = RiverLayout.CenterX(z), w = RiverLayout.HalfWidth(z);
            Color mid = WorldGenerator.RiverFlowColor(cx, z);
            Color edge = WorldGenerator.RiverFlowColor(cx + w * 0.9f, z);
            Assert.Greater(mid.r, edge.r + 0.3f, "まん中は速く、岸ぎわはゆっくり");
            Color pool = WorldGenerator.RiverFlowColor(RiverLayout.CenterX(RiverLayout.PoolZ), RiverLayout.PoolZ);
            Assert.Less(pool.r, mid.r * 0.5f, "よどみは、ほとんど流れない");
            Color below = WorldGenerator.RiverFlowColor(RiverLayout.CenterX(RiverLayout.FallZ - 1f), RiverLayout.FallZ - 1f);
            Color far = WorldGenerator.RiverFlowColor(RiverLayout.CenterX(RiverLayout.FallZ - 12f), RiverLayout.FallZ - 12f);
            Assert.Greater(below.g, 0.6f, "滝の下は、白くあわ立つ");
            Assert.Less(far.g, 0.1f, "はなれると、おちつく");
        }

        [Test]
        public void Habitats_AreRecordedWithNearbyLandmarks()
        {
            SaveSystem.ResetAllInMemoryForTests();
            Habitats.ClearSession();
            var forest = Areas.Forest;
            var lm = forest.Landmarks[3];
            var groups = new System.Collections.Generic.List<MobGroup>
            {
                new MobGroup { species = "ant", center = new Vector3(lm.position.x + 1f, 0f, lm.position.y), radius = 4f },
                new MobGroup { species = "ant", center = new Vector3(500f, 0f, 500f), radius = 2f },   // 名所から遠い
                new MobGroup { species = "snail", center = new Vector3(lm.position.x, 0f, lm.position.y - 2f), radius = 2f },
            };
            Habitats.Record(forest, groups);
            var ants = Habitats.Of("ant");
            Assert.AreEqual(2, ants.Count);
            Assert.AreEqual(lm.name, ants[0].landmark, "そばの名所");
            Assert.AreEqual("", ants[1].landmark);
            StringAssert.StartsWith("森：" + lm.name, Habitats.Describe("ant"));
            StringAssert.EndsWith("のまわり", Habitats.Describe("ant"));
            Assert.AreEqual(Habitats.Of("ant").Count, Habitats.Of("ant_helmet").Count, "レアないきものは、もとのいきものと同じすみか");
            // 同じエリアを作り直すと、記録は入れかわる（ふえない）
            Habitats.Record(forest, groups);
            Assert.AreEqual(2, Habitats.Of("ant").Count);
            Assert.AreEqual(SpeciesCatalog.Get("crab").areaLabel, Habitats.Describe("crab"), "記録がなければ、エリアの名前");
            // 「最初から」でセーブを消しても、この起動で作ったエリアのすみかは残る
            SaveSystem.ResetAllInMemoryForTests();
            Assert.AreEqual(2, Habitats.Of("ant").Count);
            Habitats.ClearSession();
            SaveSystem.ResetAllInMemoryForTests();
        }

        [Test]
        public void RiverShader_HasNoWhiteDefaults()
        {
            var sh = Shader.Find("Shakutori/ToonRiver");
            Assert.IsNotNull(sh);
            var m = new Material(sh);
            Color deep = m.GetColor("_DeepColor");
            Assert.Less(deep.r + deep.g + deep.b, 1.0f, "深い所は、こい色");
            Assert.Greater(deep.b, deep.r, "青みがかった色");
            Assert.Less(m.GetFloat("_FoamDepth"), 0.2f, "泡は、とても浅い所だけ");
            Object.DestroyImmediate(m);
            var mat = TestUtil.LoadWorldAssets().river;
            Assert.IsNotNull(mat);
            Assert.AreEqual(sh, mat.shader);
            Assert.Less(mat.GetFloat("_FoamDepth"), 0.2f, "川のマテリアルにも反映");
        }
    }
}
