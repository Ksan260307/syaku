using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// いきものの大きさのつりあい。神社・公園・山の物は本物より小さく作ってあるので、虫より大きい動物は、大きいものほど強くちぢめて描く
    /// （前は本物の大きさのキツネが大鳥居の半分以上の高さで、半分にしても、まだ大きすぎた）。虫はそのまま。
    /// はかって図鑑にのせる大きさは、本物の大きさのまま。
    /// </summary>
    public class CreatureSizeTests
    {
        WorldAssets _assets;

        [OneTimeSetUp]
        public void Load() => _assets = TestUtil.LoadWorldAssets();

        Vector3 GameSize(SpeciesDef sp)
        {
            var mesh = _assets.TryGet(sp.body);
            Assert.IsNotNull(mesh, sp.id);
            return mesh.bounds.size * sp.scale;
        }

        float Length(string id)
        {
            var sp = SpeciesCatalog.Get(id);
            Vector3 s = GameSize(sp);
            return Mathf.Max(s.x, s.y, s.z);
        }

        [Test]
        public void Animals_AreDrawnSmallerToMatchTheWorld()
        {
            foreach (var sp in SpeciesCatalog.All)
            {
                if (sp.id == "manta") continue;   // 沖の海を泳ぐ（まわりに物がない）
                Vector3 s = GameSize(sp);
                Assert.LessOrEqual(Mathf.Max(s.x, s.y, s.z), 12f, $"{sp.id} が大きすぎる（{Mathf.Max(s.x, s.y, s.z):0.0}）");
                // 虫くらいの大きさのいきものは、ちぢめない
                Vector3 m = _assets.TryGet(sp.body).bounds.size;
                if (Mathf.Max(m.x, m.z) <= 1.9f) Assert.GreaterOrEqual(sp.scale, 1f, $"{sp.id} は虫くらいなので、そのまま");
            }
            float torii = _assets.TryGet("Shrine_Torii").bounds.size.y;
            foreach (var id in new[] { "kitsune", "tanuki", "fukurou" })
            {
                Vector3 s = GameSize(SpeciesCatalog.Get(id));
                Assert.Less(s.y, torii * 0.16f, $"{id} の高さは、大鳥居の 1/6 より低い");
            }
            // ちぢめても、大きい順はそのまま。小さな動物も、見えにくいほどは小さくしない
            string[] order = { "kitsune", "tanuki", "crow", "hato", "nezumi" };
            for (int k = 1; k < order.Length; k++)
                Assert.Greater(Length(order[k - 1]), Length(order[k]), $"{order[k - 1]} は {order[k]} より大きい");
            Assert.Greater(Length("risu"), Length("sparrow"), "リスはスズメより大きい");
            foreach (var id in new[] { "sparrow", "nezumi", "tokage", "yamori", "nakiusagi" })
                Assert.Greater(Length(id), 2.4f, $"{id} は小さくなりすぎない");
        }

        [Test]
        public void Insects_KeepTheirSize()
        {
            foreach (var id in new[] { "ant", "ladybug", "beetle", "herakuresu", "kuwagata", "grasshopper", "butterfly", "mantis", "dragonfly", "oniyanma", "suzumushi", "korogi", "kanabun", "frog" })
                Assert.GreaterOrEqual(SpeciesCatalog.Get(id).scale, 1f, $"{id} は、そのまま");
        }

        [Test]
        public void MeasuredSizes_StayRealForScaledDownAnimals()
        {
            var go = new GameObject("C");
            try
            {
                var c = go.AddComponent<Creatures>();
                c.assets = _assets;
                float fox = c.BodyMm(SpeciesCatalog.Get("kitsune"), SpeciesCatalog.Get("kitsune").scale);
                Assert.Greater(fox, 900f, "キツネは、しっぽまで 1m ほど");
                float crow = c.BodyMm(SpeciesCatalog.Get("crow"), SpeciesCatalog.Get("crow").scale);
                Assert.Greater(crow, 400f, "カラスは 50cm ほど");
                var ladybug = SpeciesCatalog.Get("ladybug");
                Vector3 lb = _assets.TryGet(ladybug.body).bounds.size;
                Assert.AreEqual(Mathf.Max(lb.x, lb.z) * Friends.MmPerUnit, c.BodyMm(ladybug, 1f), 0.5f, "小さな虫は、そのまま");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
