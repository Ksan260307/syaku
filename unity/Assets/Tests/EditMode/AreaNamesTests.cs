using NUnit.Framework;

namespace Shakutori.Tests
{
    /// <summary>まだ行ったことがないエリアの名前は、画面に出さない（「？？？」）。</summary>
    public class AreaNamesTests
    {
        [SetUp]
        public void SetUp()
        {
            SaveSystem.ResetAllInMemoryForTests();
            Areas.Current = Areas.Forest;
        }

        [TearDown]
        public void TearDown() => SaveSystem.ForgetLoadedForTests();

        [Test]
        public void HabitatLabels_HideAreasNotYetVisited()
        {
            // はじめは森だけ
            Assert.AreEqual("森・？？？", AreaNames.Label("森・川辺・公園・山・神社"), "行ったことがないエリアは、まとめて ？？？ ひとつ");
            Assert.AreEqual("？？？", AreaNames.Label("海岸"));
            Assert.AreEqual("森", AreaNames.Label("森"));
            SaveSystem.Data.visited.Add("park");
            SaveSystem.Data.visited.Add("shrine");
            Assert.AreEqual("森・公園・神社・？？？", AreaNames.Label("森・川辺・公園・山・神社"));
            foreach (var a in Areas.All) SaveSystem.Data.visited.Add(a.Id);
            Assert.AreEqual("森・川辺・公園・山・神社", AreaNames.Label("森・川辺・公園・山・神社"), "ぜんぶ行ったら、そのまま");
        }

        [Test]
        public void Texts_HideAreaNamesNotYetVisited()
        {
            Assert.AreEqual("？？？の花だん。", AreaNames.Mask("公園の花だん。"));
            Assert.AreEqual("？？？のまわりや、はい松のしげみのそば。", AreaNames.Mask("山小屋のまわりや、はい松のしげみのそば。"), "山小屋（山ではなく）");
            Assert.AreEqual("まきの山のかげ。", AreaNames.Mask("まきの山のかげ。"), "1 文字の名前は、ほかのことばの中にもあるので、かえない");
            Assert.AreEqual(AreaNames.Unknown, AreaNames.Name(Areas.Shrine));
            Assert.AreEqual("森", AreaNames.Name(Areas.Forest));
            SaveSystem.Data.visited.Add("park");
            Assert.AreEqual("公園の花だん。", AreaNames.Mask("公園の花だん。"), "行ったことがあれば、そのまま");
        }

        [Test]
        public void HabitatWithoutRecords_HidesAreasNotYetVisited()
        {
            Habitats.ClearSession();
            Assert.AreEqual("？？？", Habitats.Describe("crab"), "サワガニ（川辺）：まだ川辺に行っていない");
            SaveSystem.Data.visited.Add("river");
            Assert.AreEqual("川辺", Habitats.Describe("crab"));
            Habitats.ClearSession();
        }
    }
}
