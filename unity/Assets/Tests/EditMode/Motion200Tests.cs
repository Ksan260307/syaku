using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Shakutori.Tests
{
    /// <summary>いきもののモーション 200：一覧（CREATURE_MOTIONS.md）とコードの印がそろっていて、新しいひとりの動きが選ばれる。</summary>
    public class Motion200Tests
    {
        static string Root => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ".."));

        [Test]
        public void EveryListedMotion_IsMarkedInTheCode()
        {
            string code = string.Join("\n", new[] { "Creatures.Motion2.cs", "Creatures.cs" }
                .Select(f => File.ReadAllText(Path.Combine(Root, "unity", "Assets", "Scripts", "Gameplay", f))));
            int marks = Regex.Matches(code, @"\[M\] ").Count;
            Assert.AreEqual(200, marks, "コードの [M] の印は 200");
            string doc = File.ReadAllText(Path.Combine(Root, "CREATURE_MOTIONS.md"));
            int rows = Regex.Matches(doc, @"^\| *\d+ *\|", RegexOptions.Multiline).Count;
            Assert.AreEqual(200, rows, "一覧の行は 200");
        }

        [TestCase("otoshibumi")]
        [TestCase("kamikiri")]
        [TestCase("kamemushi")]
        [TestCase("mantis")]
        [TestCase("nanafushi")]
        [TestCase("butterfly")]
        [TestCase("ga")]
        public void QuietBugs_NowHaveIdleActs(string id)
        {
            Assert.Greater(Creatures.NewActOf(id, 0.1f), 0, id + "：止まっていると、ひとりでする動きがある");
            Assert.AreEqual(0, Creatures.NewActOf(id, 0.99f), id + "：いつもではない");
        }

        [Test]
        public void SpeciesWithOwnActs_AreNotChanged()
        {
            foreach (var id in new[] { "ant", "frog", "sparrow", "hato", "tokage", "kitsune", "tonakai" })
                Assert.AreEqual(-1, Creatures.NewActOf(id, 0.1f), id + "：もとの動き方のまま");
        }
    }
}
