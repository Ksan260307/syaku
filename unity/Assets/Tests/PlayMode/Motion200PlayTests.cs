using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：いきもののモーション 200（Creatures.Motion2.cs）。足した動きで、形がくずれないこと。</summary>
    public class Motion200PlayTests
    {
        Creatures C => GM.creatures;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown()
        {
            Creatures.s_NoMoreMotion = false;
            ResetInput();
        }

        IEnumerator GoTo(AreaLayout area)
        {
            SaveSystem.Data.visited.Add(area.Id);
            GM.TravelTo(area.Id);
            yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 180f, area.Id + "へ");
            yield return Seconds(0.5f);
        }

        /// <summary>数秒のあいだ、どのいきものも、足した動きで大きくかたむいたり、のびちぢみしすぎたりしない。</summary>
        IEnumerator CheckPoses(string where)
        {
            float moved = 0f;
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                // ときどき、近くのいきものをおどろかせる（びくっ・警戒の動きも見る）
                if (Mathf.Repeat(t, 1.3f) < Time.deltaTime) C.Disturb(Worm.HeadPosition, 4f);
                yield return null;
                Assert.IsTrue(C.MorePosesNatural(40f, out string bad, out float biggest), $"{where}：{bad}");
                moved = Mathf.Max(moved, biggest);
            }
            Assert.Greater(moved, 1f, where + "：足した動きが、見てわかるくらい体をかたむける");
        }

        [UnityTest]
        public IEnumerator AddedMotions_KeepEveryCreatureNatural()
        {
            yield return CheckPoses("森");
            yield return GoTo(Areas.Coast);
            yield return CheckPoses("海岸");
            yield return GoTo(Areas.Night);
            yield return CheckPoses("夜の森");
            yield return GoTo(Areas.Xmas);
            yield return CheckPoses("クリスマス村");
        }
    }
}
