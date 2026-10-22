using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト：歩き方の味つけ：歩幅のゆらぎ・調子が出てくる・はやくの弾み・U ターン前にふり返る・
    /// ゆっくりのときの探り歩き・伸びている途中で手をはなしたときのブレーキ。
    /// </summary>
    public class WormGaitTests
    {
        readonly List<GameObject> _temp = new List<GameObject>();

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
            foreach (var go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
        }

        /// <summary>空中に、長くて平らな台を置いて、その上の手前のはしにしゃくとりむしを置く。</summary>
        IEnumerator OnLongDeck(float length = 16f)
        {
            Vector3 top = TopSurface(ForestLayout.Spawn) + new Vector3(0f, 3.5f, 0f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestDeck";
            go.layer = ShakuConst.SurfaceLayer;
            go.transform.position = top + new Vector3(0f, -0.2f, length * 0.5f - 1f);
            go.transform.localScale = new Vector3(4f, 0.4f, length);
            go.GetComponent<Renderer>().enabled = false;
            _temp.Add(go);
            Physics.SyncTransforms();
            Worm.Spawn(top, Vector3.forward);
            yield return FaceCamera(Vector3.forward);
            yield return Seconds(1.2f);   // 目をさます伸びが終わるまで
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Steps_Vary_AndTheWormGetsIntoTheGroove()
        {
            yield return OnLongDeck();
            var strides = new Dictionary<int, float>();
            bool groove = false;
            yield return Hold(Vector2.up, 11f, () =>
            {
                if (Worm.State == InchwormController.Mode.Reach && !strides.ContainsKey(Worm.StepStreak))
                    strides[Worm.StepStreak] = Worm.LastReachLift;
                groove |= Worm.InGroove;
            });
            Assert.Greater(strides.Count, 8, "何歩も歩いた");
            float min = 9f, max = 0f;
            foreach (var kv in strides)
            {
                if (kv.Key < 3) continue;
                min = Mathf.Min(min, kv.Value);
                max = Mathf.Max(max, kv.Value);
            }
            Assert.Greater(max - min, 0.01f, $"頭を上げる高さは、一歩ごとに少しちがう min={min:0.000} max={max:0.000}");
            Assert.IsTrue(groove, "長く歩くと、調子が出てくる");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Sprinting_BouncesInRhythm()
        {
            yield return OnLongDeck();
            float bounce = 0f;
            yield return Hold(Vector2.up, 4f, () => bounce = Mathf.Max(bounce, Worm.BounceHeight), sprint: true);
            Assert.Greater(bounce, 0.004f, "はやくは、ぽんぽんと弾む");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Backward_LooksBackBeforeTheUTurn()
        {
            yield return OnLongDeck();
            Vector3 start = Worm.Heading;
            GameInput.VirtualMove = Vector2.down;
            yield return WaitUntil(() => Worm.IsTurningBack, 1f, "ふり返る");
            Assert.AreEqual(InchwormController.Mode.Idle, Worm.State, "ふり返っている間は、まだ動かない");
            float look = 0f;
            yield return WaitUntil(() =>
            {
                look = Mathf.Max(look, Mathf.Abs(Worm.StandLook));
                return Worm.State == InchwormController.Mode.Reach;
            }, 1f, "U ターン");
            Assert.Greater(look, 0.5f, "頭を後ろへふる");
            // カメラも追って回るので、いちばん後ろを向いた所で調べる
            float least = 1f;
            yield return Hold(Vector2.down, 2f, () => least = Mathf.Min(least, Vector3.Dot(Worm.Heading, start)));
            Assert.Less(least, 0f, "後ろを向いた");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SlowStick_ProbesWithTheHead()
        {
            yield return OnLongDeck();
            float probe = 0f;
            yield return Hold(Vector2.up * 0.4f, 3f, () => probe = Mathf.Max(probe, Worm.ProbeSway));
            Assert.Greater(probe, 0.02f, "ゆっくりのときは、頭を左右にふって探る");
            probe = 0f;
            float t0 = Time.time;
            yield return Hold(Vector2.up, 2.5f, () => { if (Time.time - t0 > 0.8f) probe = Mathf.Max(probe, Worm.ProbeSway); });
            Assert.AreEqual(0f, probe, 1e-4f, "ふつうに歩くときは、探らない");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ReleasingMidReach_ShortensTheStep()
        {
            yield return OnLongDeck();
            int before = Worm.BrakeCount;
            GameInput.VirtualMove = Vector2.up;
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Pull, 3f, "引き寄せ");
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Reach, 3f, "伸び");
            ResetInput();
            // 0.1 秒より長くはなしていると、ブレーキ（すぐまた押すときは、ブレーキしない）
            yield return WaitUntil(() => Worm.BrakeCount > before || Worm.State != InchwormController.Mode.Reach, 1f, "ブレーキ");
            Assert.AreEqual(before + 1, Worm.BrakeCount, "手をはなすと、伸びかけの一歩を短くする");
            yield return WaitUntil(() => Worm.State == InchwormController.Mode.Idle, 2f, "止まる");
            Assert.Less(Worm.LastReachDistance, 1.01f);
        }
    }
}
