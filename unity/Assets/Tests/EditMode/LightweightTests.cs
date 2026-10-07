using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>軽量化の単体テスト：ぶつかり合いの結果が、計算する順番によらないこと。</summary>
    public class LightweightTests
    {
        [Test]
        public void ContactKick_IsOrderIndependent_AndKeepsMomentum()
        {
            var rng = new System.Random(3);
            float Rf() => (float)rng.NextDouble() * 2f - 1f;
            for (int i = 0; i < 200; i++)
            {
                Vector3 va = new Vector3(Rf(), 0f, Rf()) * 2f;
                Vector3 vb = new Vector3(Rf(), 0f, Rf()) * 2f;
                Vector3 nrm = new Vector3(Rf(), 0f, Rf()).normalized;   // b から a への向き
                if (nrm.sqrMagnitude < 0.5f) continue;
                float e = 0.3f + 0.6f * (float)rng.NextDouble();

                // a が先：どちらも、ぶつかる前の速さを読む
                Vector3 a1 = va + ShakuPhysics.ContactKick(va, vb, nrm, e);
                Vector3 b1 = vb + ShakuPhysics.ContactKick(vb, va, -nrm, e);
                // b が先
                Vector3 b2 = vb + ShakuPhysics.ContactKick(vb, va, -nrm, e);
                Vector3 a2 = va + ShakuPhysics.ContactKick(va, vb, nrm, e);
                Assert.Less((a1 - a2).magnitude, 1e-6f, "どちらを先に計算しても同じ");
                Assert.Less((b1 - b2).magnitude, 1e-6f);
                Assert.Less(((a1 + b1) - (va + vb)).magnitude, 1e-4f, "運動量は保たれる");
                float closing = Vector3.Dot(va - vb, nrm);
                if (closing < 0f)
                    Assert.AreEqual(-e * closing, Vector3.Dot(a1 - b1, nrm), 1e-4f, "反発係数の分だけ、はね返る");
                else
                    Assert.AreEqual(va, a1, "はなれていくときは、何もしない");
            }
        }
    }
}
