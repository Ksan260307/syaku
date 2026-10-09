using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    /// <summary>
    /// 場所で聞こえる音（エリアの改善 300）：近づくほど大きくなるループ（滝・浅瀬・カエルの合唱・ほら穴のしずく）と、
    /// 場所から鳴る効果音（どんぐり・ホコリタケ・ブランコ・シーソー・魚など）、森の葉ずれ（風が強いほど大きい）、遠くのキツツキ。
    /// 音の大きさは「環境音の音量」「効果音の音量」の設定にしたがう。ブラウザでは、最初のクリックのあとから鳴らす。
    /// </summary>
    public class AreaSounds : MonoBehaviour
    {
        public static AreaSounds Instance { get; private set; }

        class Loop { public AudioSource src; public float vol; public bool global; }

        readonly List<Loop> _loops = new List<Loop>();
        readonly AudioSource[] _pool = new AudioSource[6];
        int _next;
        Transform _root;
        Loop _canopy;
        Loop _ridge;
        string _area = "forest";
        float _woodNext;
        readonly System.Random _rng = new System.Random(77);
        float R(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        /// <summary>いま鳴らしているループの数と、名前（テスト用）。</summary>
        public int LoopCount => _loops.Count;
        public bool HasLoop(string clip) => _loops.Exists(l => l.src != null && l.src.clip != null && l.src.clip.name == clip);
        /// <summary>最後に場所から鳴らした効果音（テスト用）。</summary>
        public string LastPlayed { get; private set; } = "";
        public int Played { get; private set; }
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        /// <summary>その効果音を、場所から鳴らした回数（テスト用）。</summary>
        public int CountOf(string clip) => _counts.TryGetValue(clip, out var n) ? n : 0;

        void OnEnable() => Instance = this;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < _pool.Length; i++)
            {
                var go = new GameObject("AreaSfx");
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                Spatial(s, 2f, 32f);
                _pool[i] = s;
            }
        }

        static void Spatial(AudioSource s, float min, float max)
        {
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = min;
            s.maxDistance = max;
            s.dopplerLevel = 0f;
        }

        static AudioClip Clip(string name) => AudioManager.Instance != null ? AudioManager.Instance.AreaClip(name) : null;

        /// <summary>エリアの場所の音を作り直す。</summary>
        public void Build(WorldGenerator world)
        {
            Instance = this;
            _area = world.Area.Id;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("AreaLoops").transform;
            _root.SetParent(transform, false);
            _loops.Clear();
            _canopy = null;
            _ridge = null;
            _woodNext = Time.time + R(15f, 30f);
            if (_area == "forest")
            {
                Vector2 h = ForestLayout.ArchTarget + new Vector2(1.5f, 2.5f);
                AddLoop("loop_cave_drip", ForestLayout.Ground(h.x, h.y) + Vector3.up * 2f, 2f, 20f, 0.8f);              // ほら穴のしずく
                AddLoop("loop_frogs", new Vector3(ForestLayout.Pond.x, ForestLayout.WaterLevel, ForestLayout.Pond.y), 6f, 30f, 0.45f);   // 水たまりのカエル
                _canopy = AddLoop("loop_canopy", Vector3.zero, 0f, 0f, 0.35f, global: true);                            // 森の葉ずれ
            }
            else if (_area == "river")
            {
                float fz = RiverLayout.FallZ;
                AddLoop("loop_waterfall", new Vector3(RiverLayout.CenterX(fz - 2f), RiverLayout.WaterLevel(fz - 2f) + 1f, fz - 2f), 3f, 44f, 0.85f);   // 滝
                AddLoop("loop_shallows", new Vector3(RiverLayout.CenterX(RiverLayout.StonesZ), RiverLayout.WaterLevel(RiverLayout.StonesZ), RiverLayout.StonesZ), 2f, 18f, 0.6f);   // とびいしの瀬
                float pz = RiverLayout.PoolZ;
                AddLoop("loop_frogs", new Vector3(RiverLayout.CenterX(pz), RiverLayout.WaterLevel(pz), pz), 6f, 32f, 0.55f);   // よどみのカエル
            }
            else if (_area == "mountain")
            {
                Vector2 sp = MountainLayout.Spring;
                AddLoop("loop_spring", new Vector3(sp.x, MountainLayout.SpringLevel + 1f, sp.y + MountainLayout.SpringRadius * 0.6f), 2f, 22f, 0.7f);   // 湧き水
                _ridge = AddLoop("loop_ridge_wind", Vector3.zero, 0f, 0f, 0.4f, global: true);                                                  // 尾根の風（高い所ほど大きい）
            }
        }

        Loop AddLoop(string clip, Vector3 pos, float min, float max, float vol, bool global = false)
        {
            var c = Clip(clip);
            if (c == null) return null;
            var go = new GameObject("Loop_" + clip);
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = c;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            if (global) s.spatialBlend = 0f;
            else Spatial(s, min, max);
            var l = new Loop { src = s, vol = vol, global = global };
            _loops.Add(l);
            return l;
        }

        /// <summary>場所から効果音を鳴らす（遠いほど小さい）。</summary>
        public void PlayAt(string clip, Vector3 pos, float volume, float pitch = 1f)
        {
            LastPlayed = clip;
            Played++;
            _counts[clip] = CountOf(clip) + 1;
            var c = Clip(clip);
            var am = AudioManager.Instance;
            if (c == null || am == null || !am.Started) return;
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            s.transform.position = pos;
            s.clip = c;
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume) * SaveSystem.Settings.sfx;
            s.Play();
        }

        void Update()
        {
            var am = AudioManager.Instance;
            bool started = am != null && am.Started;
            float amb = Mathf.Clamp01(SaveSystem.Settings.ambience) * 0.6f;
            float gust = Wind.Gust(Time.time);
            float k = ShakuMath.DampFactor(1.5f, Time.unscaledDeltaTime);
            foreach (var l in _loops)
            {
                if (l.src == null) continue;
                if (started && !l.src.isPlaying) l.src.Play();
                float target = started ? amb * l.vol : 0f;
                if (l == _canopy) target *= 0.15f + 0.85f * gust * gust;   // 風が強いほど、葉ずれが大きい
                if (l == _ridge)
                {
                    // 山の尾根の風：高い所ほど、風が強いほど大きい
                    var w = InchwormController.Instance;
                    float high = w != null ? ShakuMath.SmoothStep(6f, 26f, w.CenterPosition.y) : 0f;
                    target *= (0.1f + 0.9f * high) * (0.5f + 0.5f * gust);
                }
                l.src.volume = Mathf.Lerp(l.src.volume, target, k);
            }
            // 森：遠くで、ときどきキツツキが木をたたく
            if (_area == "forest" && started && Time.time > _woodNext)
            {
                _woodNext = Time.time + R(25f, 50f);
                var worm = InchwormController.Instance;
                Vector3 c = worm != null ? worm.CenterPosition : Vector3.zero;
                float a = R(0f, Mathf.PI * 2f);
                PlayAt("woodpecker", c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 22f + Vector3.up * 14f, 0.8f, R(0.95f, 1.05f));
            }
        }
    }
}
