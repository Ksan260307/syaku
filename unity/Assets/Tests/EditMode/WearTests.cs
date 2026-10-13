using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>きせかえ：いろ・ぼうし・めがねの定義と、解放の条件・絵・小物のメッシュ・セーブ。</summary>
    public class WearTests
    {
        static ProgressStats Stats(int d, int p, int s, int obs = 0, int friends = 0) =>
            new ProgressStats { drops = d, places = p, species = s, observed = obs, bestFriends = friends };

        [Test]
        public void ThereAre30Skins_16Colors10Hats4Glasses()
        {
            Assert.AreEqual(30, Skins.All.Count);
            Assert.AreEqual(16, Skins.OfKind(WearKind.Color).Count, "いろ");
            Assert.AreEqual(10, Skins.OfKind(WearKind.Hat).Count, "ぼうし");
            Assert.AreEqual(4, Skins.OfKind(WearKind.Glasses).Count, "めがね");
            foreach (var id in new[] { "cap", "straw", "hair", "sun", "rainbow" }) Assert.IsNotNull(Skins.Find(id), id);
            Assert.AreEqual(WearKind.Hat, Skins.Find("hair").kind, "ぴょこんげは、ぼうしのなかま");
            Assert.IsTrue(Skins.Find("hair").bouncy, "ぴょこんげはゆれる");
            Assert.IsNull(Skins.Find(""));
            Assert.IsNull(Skins.Find("no-such"));
        }

        [Test]
        public void EverySkin_HasAPictureAndHatsAndGlassesHaveMeshes()
        {
            var assets = TestUtil.LoadWorldAssets();
            Assert.IsNotNull(assets.wear, "ぼうし・めがねの材質");
            foreach (var s in Skins.All)
            {
                Assert.IsNotNull(assets.Portrait(s.Icon), $"{s.id} の絵");
                if (s.kind == WearKind.Color) Assert.IsNull(s.mesh, s.id);
                else
                {
                    var m = assets.TryGet(s.mesh);
                    Assert.IsNotNull(m, $"{s.id} のメッシュ {s.mesh}");
                    // 頭のまん中のまわり（しゃくとりむしの頭の大きさは、およそ 0.065）
                    Assert.Less(m.bounds.max.magnitude, 0.25f, s.id);
                    if (s.kind == WearKind.Hat) Assert.Greater(m.bounds.max.y, 0.05f, $"{s.id} は頭の上");
                }
            }
        }

        [Test]
        public void NewSkins_UnlockAsYouPlay()
        {
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("hair"), Stats(0, 0, 0)), "ぴょこんげは、はじめから");
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("cap"), Stats(5, 0, 0)));
            Assert.IsFalse(Skins.IsUnlocked(Skins.Find("cap"), Stats(4, 0, 0)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("round"), Stats(0, 3, 0)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("rainbow"), Stats(0, 0, 35)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("flower"), Stats(0, 0, 0, obs: 15)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("beret"), Stats(0, 0, 0, friends: 2)));
            Assert.IsFalse(Skins.IsUnlocked(Skins.Find("crown"), Stats(999, Areas.TotalLandmarks - 1, 99)));
            Assert.IsTrue(Skins.IsUnlocked(Skins.Find("crown"), Stats(0, Areas.TotalLandmarks, 0)), "おうかんは、名所をぜんぶ");
            // どの条件も、ゲームの中でとどく数
            foreach (var s in Skins.All)
            {
                Assert.LessOrEqual(s.needDrops, Areas.TotalDrops, s.id);
                Assert.LessOrEqual(s.needPlaces, Areas.TotalLandmarks, s.id);
                Assert.LessOrEqual(s.needSpecies, SpeciesCatalog.Count, s.id);
            }
        }

        [Test]
        public void Wearing_EachKindIsSeparate_AndBrokenSavesAreRepaired()
        {
            try
            {
                SaveSystem.ResetAllInMemoryForTests();
                var d = SaveSystem.Data;
                d.skin = "mint";
                d.hat = "cap";
                d.glasses = "sun";
                Assert.AreEqual("mint", Skins.Worn(WearKind.Color).id);
                Assert.AreEqual("cap", Skins.Worn(WearKind.Hat).id);
                Assert.AreEqual("sun", Skins.Worn(WearKind.Glasses).id);
                d.hat = "sun";   // めがねを、ぼうしの所に書いても、かぶらない
                Assert.IsNull(Skins.Worn(WearKind.Hat));
                d.hat = "";
                Assert.IsNull(Skins.Worn(WearKind.Hat), "かぶっていない");
                var old = new SaveData { hat = null, glasses = null };
                SaveSystem.Migrate(old);
                Assert.AreEqual("", old.hat);
                Assert.AreEqual("", old.glasses);
            }
            finally
            {
                SaveSystem.ResetAllInMemoryForTests();
            }
        }

        [Test]
        public void Apply_PatternsGoToTheWormShader_AndHatsDoNotRecolor()
        {
            TestUtil.NewEmptyScene();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var r = go.GetComponent<Renderer>();
            var mpb = new MaterialPropertyBlock();
            Skins.Apply(r, Skins.Find("shima"), 0f);
            r.GetPropertyBlock(mpb);
            Assert.AreEqual(13f, mpb.GetFloat("_Stripes"), 1e-4f, "しましま");
            Assert.Greater(mpb.GetFloat("_StripeDark"), 0.5f);
            Skins.Apply(r, Skins.Find("ichigo"), 0f);
            r.GetPropertyBlock(mpb);
            Assert.Greater(mpb.GetFloat("_Dots"), 0.5f, "水玉");
            Assert.AreEqual(0f, mpb.GetFloat("_Stripes"), "前のもようは消える");
            Skins.Apply(r, Skins.Find("rainbow"), 0f);
            r.GetPropertyBlock(mpb);
            Assert.AreEqual(360f, mpb.GetFloat("_HueAlong"), 1e-3f, "体にそって、にじの色");
            float h0 = mpb.GetFloat("_HueShift");
            Skins.Apply(r, Skins.Find("rainbow"), 2f);
            r.GetPropertyBlock(mpb);
            Assert.AreNotEqual(h0, mpb.GetFloat("_HueShift"), "レインボーのしまは、ゆっくり流れる");
            Skins.Apply(r, Skins.Find("cap"), 0f);   // ぼうしは、体のいろを変えない
            r.GetPropertyBlock(mpb);
            Assert.AreEqual(360f, mpb.GetFloat("_HueAlong"), 1e-3f);
            Object.DestroyImmediate(go);
            // しゃくとりむしの材質は、もようを持っている
            var worm = TestUtil.LoadWorldAssets().worm;
            foreach (var p in new[] { "_HueAlong", "_Stripes", "_StripeDark", "_Dots" }) Assert.IsTrue(worm.HasProperty(p), p);
        }
    }
}
