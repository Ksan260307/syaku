using UnityEngine;

namespace Shakutori
{
    /// <summary>しゃくとりむしが乗ったまま一緒に動く足場（葉っぱの渡し舟など）の目印。</summary>
    public class MovingPlatform : MonoBehaviour
    {
    }

    /// <summary>
    /// 葉っぱの渡し舟。岸と中州のあいだを、待っては進むをくり返す。
    /// しゃくとりむしより先に動かすため実行順を早めている。
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class RiverFerry : MonoBehaviour
    {
        public Vector3 dockA;
        public Vector3 dockB;
        public Quaternion baseRotation = Quaternion.identity;
        public float waitTime = 8f;   // 乗り降りに十分な時間
        public float travelTime = 7f;

        float _clock;

        /// <summary>0..1 = A で待つ / A→B / B で待つ / B→A の位置。</summary>
        public float Phase => Mathf.Repeat(_clock, Cycle) / Cycle;
        public float Cycle => 2f * (waitTime + travelTime);
        public bool AtA => Mathf.Repeat(_clock, Cycle) < waitTime;
        public bool AtB
        {
            get
            {
                float t = Mathf.Repeat(_clock, Cycle);
                return t >= waitTime + travelTime && t < 2f * waitTime + travelTime;
            }
        }

        public Vector3 Evaluate(float clock, out Quaternion rot)
        {
            float t = Mathf.Repeat(clock, Cycle);
            float k;
            if (t < waitTime) k = 0f;
            else if (t < waitTime + travelTime) k = ShakuMath.Smoother01((t - waitTime) / travelTime);
            else if (t < 2f * waitTime + travelTime) k = 1f;
            else k = 1f - ShakuMath.Smoother01((t - 2f * waitTime - travelTime) / travelTime);
            Vector3 p = Vector3.Lerp(dockA, dockB, k);
            p.y += Mathf.Sin(clock * 1.7f) * 0.025f;
            rot = baseRotation * Quaternion.Euler(Mathf.Sin(clock * 1.3f) * 1.2f, Mathf.Sin(clock * 0.4f) * 3f * (k > 0f && k < 1f ? 1f : 0.3f), Mathf.Sin(clock * 1.1f) * 1.5f);
            return p;
        }

        void Start() => Apply();

        void Update()
        {
            _clock += Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            Vector3 p = Evaluate(_clock, out var r);
            transform.SetPositionAndRotation(p, r);
            Physics.SyncTransforms();
        }

        /// <summary>テストや演出用：指定の時刻へ進める。</summary>
        public void SetClock(float t)
        {
            _clock = t;
            Apply();
        }
    }
}
