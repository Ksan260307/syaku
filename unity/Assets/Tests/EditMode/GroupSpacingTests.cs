using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// 群れで出る、ある程度大きないきもの（ナキウサギ・スズムシ・コオロギ・バッタ・フナムシ）は、仲間と少しずつ間をあけて出る
    /// （前は、せまい輪の中に、重なるように出ることがあった）。鳥は、種類ごとに 1 羽。
    /// </summary>
    public class GroupSpacingTests
    {
        [TestCase("mountain", "nakiusagi")]
        [TestCase("shrine", "suzumushi")]
        [TestCase("shrine", "korogi")]
        [TestCase("forest", "grasshopper")]
        [TestCase("coast", "funamushi")]
        public void BigGroupMembers_StartApart(string areaId, string species)
        {
            TestUtil.NewEmptyScene();
            SurfaceProbe.ClearCache();
            var go = new GameObject("World");
            var gen = go.AddComponent<WorldGenerator>();
            gen.assets = TestUtil.LoadWorldAssets();
            gen.instanced = go.AddComponent<InstancedRenderer>();
            Areas.Current = Areas.Get(areaId);
            try
            {
                gen.GenerateNow();
                var c = go.AddComponent<Creatures>();
                c.assets = gen.assets;
                c.Build(gen);
                var sp = SpeciesCatalog.Get(species);
                Vector3 e = gen.assets.TryGet(sp.body).bounds.extents;
                float reach = Mathf.Max(e.x, e.z) * sp.scale;   // 体のまん中から、はしまで
                int n = c.CountOf(species);
                Assert.GreaterOrEqual(n, 2, $"{areaId} の {species} の群れ");
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        Vector3 a = c.PositionOf(species, i), b = c.PositionOf(species, j);
                        float d = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                        Assert.Greater(d, reach * 1.6f, $"{species} {i} と {j} が近すぎる（{d:0.0}）");
                    }
                c.Clear();
            }
            finally
            {
                gen.Clear();
                Object.DestroyImmediate(go);
                Areas.Current = Areas.Forest;
            }
        }
    }
}
