using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;

namespace Shakutori.Tests
{
    /// <summary>総合テスト：細かい凹凸のある物（松ぼっくり）に登っても、はさまって動けなくならない。</summary>
    public class ClimbablePropTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void TearDown() => ResetInput();

        /// <summary>seconds 秒だけ歩いて、進んだ道のりと、いちばん長く止まっていた時間をはかる。</summary>
        static IEnumerator Walk(float seconds, float[] result, System.Func<bool> done = null)
        {
            Vector3 last = Worm.CenterPosition;
            float path = 0f, still = 0f, maxStill = 0f, t = 0f;
            GameInput.VirtualMove = Vector2.up;
            while (t < seconds && (done == null || !done()))
            {
                yield return null;
                t += Time.deltaTime;
                float d = Vector3.Distance(Worm.CenterPosition, last);
                if (d > 0.05f)
                {
                    path += d;
                    last = Worm.CenterPosition;
                    still = 0f;
                }
                else still += Time.deltaTime;
                maxStill = Mathf.Max(maxStill, still);
            }
            GameInput.VirtualMove = Vector2.zero;
            result[0] = path;
            result[1] = maxStill;
        }

        [UnityTest]
        public IEnumerator Pinecone_CanBeWalkedOnWithoutGettingStuck()
        {
            MeshCollider cone = null;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                if (mc.name == "Pinecone") { cone = mc; break; }
            Assume.That(cone != null, "松ぼっくりがない");
            Assert.IsTrue(cone.sharedMesh.name.EndsWith("_Coarse"), "松ぼっくりは、凹凸をつつむ大まかな当たり判定");
            Bounds cb = cone.bounds;
            Vector3 ls = cone.sharedMesh.bounds.size;
            Vector3 localAxis = ls.y >= ls.x && ls.y >= ls.z ? Vector3.up : (ls.x >= ls.z ? Vector3.right : Vector3.forward);
            Vector3 along = Vector3.ProjectOnPlane(cone.transform.TransformDirection(localAxis), Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

            // 松ぼっくりのてっぺんに置いて、軸にそって歩く
            Vector3 top = TopSurface(new Vector2(cb.center.x, cb.center.z) - new Vector2(along.x, along.z) * 0.5f);
            Assert.AreSame(cone, Physics.Raycast(top + Vector3.up, Vector3.down, out var h0, 3f, ShakuConst.SurfaceMask) ? h0.collider : null, "松ぼっくりの上");
            Worm.Spawn(top, along);
            yield return FaceCamera(along);
            yield return Seconds(1.2f);
            var r = new float[2];
            yield return Walk(6f, r);
            Assert.Greater(r[0], 1.0f, "松ぼっくりの上を歩ける");
            Assert.Less(r[1], 2.5f, "凹凸にはさまって動けなくならない");

            // 横へ歩いて、松ぼっくりから下りる
            float ground = ForestLayout.Height(cb.center.x, cb.center.z);
            yield return FaceCamera(across);
            yield return Walk(14f, r, () => Worm.HeadPosition.y - ForestLayout.Height(Worm.HeadPosition.x, Worm.HeadPosition.z) < 0.25f && !Worm.IsFalling);
            Assert.Less(r[1], 3f, "下りる途中でも、はさまらない");
            Assert.Less(Worm.HeadPosition.y - ForestLayout.Height(Worm.HeadPosition.x, Worm.HeadPosition.z), 0.4f, "地面まで下りられる");
        }
    }
}
