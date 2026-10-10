using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 体が物にうまらない：川辺の滝のわきの坂（岩がならぶ）を歩いても、体が岩の中に入らない。
    /// 読み込み中に、エリアの物をひととおり描いておく（はじめて画面に入ったときに止まらない）。
    /// </summary>
    public class GroundFitPlayTests
    {
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
            ResetInput();
            Physics.queriesHitBackfaces = false;
        }

        static readonly Vector3[] Dirs =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
            new Vector3(1, 1, 1).normalized, new Vector3(-1, 1, 1).normalized, new Vector3(1, 1, -1).normalized, new Vector3(-1, 1, -1).normalized,
            new Vector3(1, -1, 1).normalized, new Vector3(-1, -1, 1).normalized, new Vector3(1, -1, -1).normalized, new Vector3(-1, -1, -1).normalized,
        };

        /// <summary>
        /// 点が岩の中にあるか（上へのレイが、岩の内がわから面に当たる）。中なら、岩の外までのいちばん近い道のり（めりこんだ深さ）。
        /// </summary>
        static float InsideRock(Vector3 p, out string what)
        {
            what = "";
            Physics.queriesHitBackfaces = true;
            try
            {
                if (!Physics.Raycast(p, Vector3.up, out var h, 3f, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore) || h.normal.y <= 0.05f) return 0f;
                string n = h.collider.name;
                if (!(n.StartsWith("Rock_") || n.StartsWith("RiverStone_"))) return 0f;
                var rock = h.collider;
                float best = 3f;
                foreach (var d in Dirs)
                    if (rock.Raycast(new Ray(p, d), out var e, 3f)) best = Mathf.Min(best, e.distance);
                what = n;
                return best;
            }
            finally { Physics.queriesHitBackfaces = false; }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RiverFallSlope_BodyNeverGoesInsideRocks()
        {
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, "川辺へ");
            yield return Seconds(0.5f);
            float fz = RiverLayout.FallZ, fcx = RiverLayout.CenterX(fz), fw = RiverLayout.HalfWidth(fz);
            // 前に、岩の下のすき間から岩の中へ入りこんでいた所（滝の両わきと、東の坂）
            foreach (float dx in new[] { -(fw + 0.5f), fw + 8f, fw + 13f })
            {
                float x = fcx + dx;
                Vector3 from = GM.world.TopSurface(new Vector2(x, fz - 8f));
                Vector3 to = GM.world.TopSurface(new Vector2(x, fz + 6f));
                Vector3 dir = to - from;
                dir.y = 0f;
                dir.Normalize();
                Worm.Spawn(from, dir);
                yield return FaceCamera(dir);
                GameInput.VirtualMove = Vector2.up;
                float t = 0f, worst = 0f;
                string where = "";
                while (t < 20f)
                {
                    Vector3 c = Worm.CenterPosition;
                    if (new Vector2(to.x - c.x, to.z - c.z).magnitude < 0.8f) break;
                    Cam.yaw = Mathf.Atan2(to.x - c.x, to.z - c.z) * Mathf.Rad2Deg;
                    yield return null;
                    t += Time.deltaTime;
                    if (Worm.State == InchwormController.Mode.Fall || Worm.State == InchwormController.Mode.Hang) continue;
                    var curve = Worm.Curve;
                    for (int k = 1; k < curve.Count - 1; k++)
                    {
                        float d = InsideRock(curve.pos[k], out var w);
                        if (d > worst) { worst = d; where = $"{w} {curve.pos[k]} state={Worm.State}"; }
                    }
                }
                ResetInput();
                // 体の太さ（直径 0.1）より深くは、めりこまない（かすめるくらいは、よい）
                Assert.Less(worst, 0.1f, $"x={x:F1} の坂で、体が岩の中に入った：{where}");
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator RearingUpOnASlope_KeepsTheBodyAboveGround()
        {
            // 滝の段の坂で、坂の上を向いて背伸び（体のつけ根が地面にうまらない）
            SaveSystem.Data.visited.Add("river");
            GM.TravelTo("river");
            yield return WaitUntil(() => Areas.Current == Areas.River && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 120f, "川辺へ");
            float fz = RiverLayout.FallZ;
            Vector2 at = Vector2.zero;
            for (float x = -40f; x < -15f; x += 0.5f)
            {
                Vector3 n = RiverLayout.Normal(x, fz - 1.5f);
                if (n.y < 0.85f && n.y > 0.6f) { at = new Vector2(x, fz - 1.5f); break; }
            }
            Assert.AreNotEqual(Vector2.zero, at, "坂がある");
            Vector3 up = RiverLayout.Normal(at.x, at.y);
            Vector3 uphill = -new Vector3(up.x, 0f, up.z).normalized;
            Worm.Spawn(RiverLayout.Ground(at.x, at.y), uphill);
            yield return FaceCamera(uphill);
            yield return Seconds(0.5f);
            GameInput.VirtualStand = true;
            float worst = 0f, rear = 0f;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                yield return null;
                rear = Mathf.Max(rear, Worm.RearAmount);
                var c = Worm.Curve;
                for (int k = 2; k < c.Count - 2; k++)
                {
                    Vector3 p = c.pos[k];
                    worst = Mathf.Max(worst, RiverLayout.Height(p.x, p.z) - p.y);
                }
            }
            GameInput.VirtualStand = false;
            Assert.Greater(rear, 0.3f, "背伸びした");
            Assert.Less(worst, 0.03f, "体が地面の下に入らない");
        }

        [UnityTest]
        public IEnumerator Loading_DrawsEverythingOnceAhead()
        {
            // 読み込みのあいだに、エリアの物（草花・小物・いきもの・地形）をひととおり用意している
            Assert.Greater(RenderWarmup.LastCount, 30, "先に描いた形と材質の組");
            Assert.IsFalse(WaterView.ForceCopies, "下ごしらえが終わったら、水が見えないときは写しを作らない");
            yield return null;
        }
    }
}
