using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// お社の鈴：鈴緒（紅白のひも）に、しゃくとりむしの頭がさわると、鈴が「しゃらん」と鳴る。
    /// つづけては鳴らない（少しのあいだ、まつ）。
    /// </summary>
    public class ShrineBell : MonoBehaviour
    {
        public Vector3 ropeLow, ropeHigh, bell;
        public float reach = 1.1f;
        float _next;

        public static ShrineBell Instance { get; private set; }
        /// <summary>鳴らした回数（テスト用）。</summary>
        public int Rings { get; private set; }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>頭（の場所 p）が、鈴緒にさわっているか。</summary>
        public bool Touches(Vector3 p)
        {
            Vector3 ab = ropeHigh - ropeLow;
            float t = Mathf.Clamp01(Vector3.Dot(p - ropeLow, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return (ropeLow + ab * t - p).sqrMagnitude < reach * reach;
        }

        public void Ring()
        {
            Rings++;
            _next = Time.time + 2.5f;
            AreaSounds.Instance?.PlayAt("suzu", bell, 0.9f);
        }

        void Update()
        {
            var w = InchwormController.Instance;
            if (w == null || Time.time < _next) return;
            if (Touches(w.HeadPosition)) Ring();
        }
    }
}
