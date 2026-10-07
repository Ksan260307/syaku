using UnityEngine;

namespace Shakutori
{
    /// <summary>BGM・環境音・効果音。音素材は tools/make_audio.py で合成したもの。</summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        public AudioClip music;
        public AudioClip ambience;
        public AudioClip[] steps;
        public AudioClip collect;
        public AudioClip discover;
        public AudioClip click;
        public AudioClip silk;
        public AudioClip land;
        public AudioClip complete;
        public AudioClip riverAmbience;
        public AudioClip creature;
        public AudioClip travel;
        public AudioClip unlock;
        public AudioClip caw;
        public AudioClip fall;
        public AudioClip splash;
        public AudioClip rare;
        public AudioClip chirp;
        public AudioClip croak;

        /// <summary>足もとの種類（足音の高さや大きさが変わる）。</summary>
        public enum Surface { Ground, Wood, Stone, Leaf, Creature }

        float _waterNear = 1f;
        float _duckUntil;

        AudioSource _music, _amb, _amb2;
        float _ambMix;   // 0 = 森, 1 = 川
        float _ambMixTarget;
        AudioSource[] _sfx;
        int _next;
        float _musicTarget, _ambTarget;
        bool _started;

        void Awake()
        {
            Instance = this;
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.volume = 0f;
            _amb = gameObject.AddComponent<AudioSource>();
            _amb.loop = true;
            _amb.playOnAwake = false;
            _amb.volume = 0f;
            _amb2 = gameObject.AddComponent<AudioSource>();
            _amb2.loop = true;
            _amb2.playOnAwake = false;
            _amb2.volume = 0f;
            _sfx = new AudioSource[8];
            for (int i = 0; i < _sfx.Length; i++)
            {
                _sfx[i] = gameObject.AddComponent<AudioSource>();
                _sfx[i].playOnAwake = false;
            }
        }

        /// <summary>ブラウザはユーザー操作のあとでないと音を出せないので、最初のクリックで呼ぶ。</summary>
        public void StartMusic()
        {
            if (_started) return;
            _started = true;
            if (music != null) { _music.clip = music; _music.Play(); }
            if (ambience != null) { _amb.clip = ambience; _amb.Play(); }
            if (riverAmbience != null) { _amb2.clip = riverAmbience; _amb2.Play(); }
        }

        /// <summary>エリアに合わせて環境音を切り替える（ゆっくりクロスフェード）。</summary>
        public void SetArea(string areaId)
        {
            _ambMixTarget = areaId == "river" ? 1f : 0f;
        }

        void Update()
        {
            var s = SaveSystem.Settings;
            _musicTarget = s.music * 0.55f;
            _ambTarget = Mathf.Clamp01(s.ambience) * 0.5f;
            float k = ShakuMath.DampFactor(1.2f, Time.unscaledDeltaTime);
            // ファンファーレの間は BGM を少し下げる
            float duck = Time.unscaledTime < _duckUntil ? 0.35f : 1f;
            _music.volume = Mathf.Lerp(_music.volume, _started ? _musicTarget * duck : 0f, k);
            _ambMix = Mathf.MoveTowards(_ambMix, _ambMixTarget, Time.unscaledDeltaTime * 0.5f);
            _amb.volume = Mathf.Lerp(_amb.volume, _started ? _ambTarget * (1f - _ambMix) : 0f, k);
            _amb2.volume = Mathf.Lerp(_amb2.volume, _started ? _ambTarget * 1.1f * _ambMix * Mathf.Lerp(0.5f, 1.25f, _waterNear) : 0f, k);
        }

        public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            var src = _sfx[_next];
            _next = (_next + 1) % _sfx.Length;
            src.pitch = pitch;
            src.volume = volume * SaveSystem.Settings.sfx;
            src.PlayOneShot(clip);
        }

        public void Step(bool head) => Step(head, Surface.Ground);

        /// <summary>足もとの物の名前とレイヤーから、足音の種類を決める。</summary>
        public static Surface Classify(string objectName, int layer)
        {
            if (layer == ShakuConst.CreatureLayer) return Surface.Creature;
            if (string.IsNullOrEmpty(objectName)) return Surface.Ground;
            string n = objectName;
            if (n.Contains("Rock") || n.Contains("Stone") || n.Contains("Pebble")) return Surface.Stone;
            if (n.Contains("Leaf") || n.Contains("Lily") || n.Contains("Mushroom") || n.Contains("Fern") || n.Contains("Moss")) return Surface.Leaf;
            if (n.Contains("Trunk") || n.Contains("Stump") || n.Contains("Log") || n.Contains("Root") || n.Contains("Branch")
                || n.Contains("Twig") || n.Contains("Pine") || n.Contains("Acorn") || n.Contains("Bark") || n.Contains("Wood")) return Surface.Wood;
            return Surface.Ground;
        }

        /// <summary>足音。木は低くこもり、石は高く硬く、葉っぱは軽く、いきものの上はやわらかく。</summary>
        public void Step(bool head, Surface surface) => Step(head, surface, 1f);

        /// <summary>loudness：はやくで大きく、ゆっくり・いきものの近くでは小さく。</summary>
        public void Step(bool head, Surface surface, float loudness)
        {
            if (steps == null || steps.Length == 0) return;
            float vol = (head ? 0.35f : 0.25f) * Mathf.Clamp(loudness, 0.3f, 1.6f);
            float pitch = Random.Range(0.9f, 1.15f) * (head ? 1.08f : 0.95f);
            switch (surface)
            {
                case Surface.Wood: pitch *= 0.78f; vol *= 1.1f; break;
                case Surface.Stone: pitch *= 1.3f; vol *= 0.9f; break;
                case Surface.Leaf: pitch *= 1.12f; vol *= 1.25f; break;
                case Surface.Creature: pitch *= 0.85f; vol *= 0.6f; break;
            }
            Play(steps[Random.Range(0, steps.Length)], vol, pitch);
        }

        /// <summary>川の音の近さ（0 = 遠い、1 = すぐそば）。川辺では水に近いほど水音が大きい。</summary>
        public void SetWaterNearness(float k) => _waterNear = Mathf.Clamp01(k);

        /// <summary>しばらく BGM を下げる。</summary>
        public void Duck(float seconds) => _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + seconds);

        public void Chirp(float volume) => Play(chirp, volume, Random.Range(0.92f, 1.12f));
        public void Croak(float volume) => Play(croak, volume, Random.Range(0.9f, 1.08f));

        public void Collect(int count) => Play(collect, 0.8f, 1f + Mathf.Min(count % 8, 7) * 0.025f);
        public void Discover() => Play(discover, 0.9f);
        public void Click() => Play(click, 0.5f, Random.Range(0.97f, 1.03f));
        public void Silk() => Play(silk, 0.55f);
        public void Land() => Play(land, 0.5f);
        public void Complete()
        {
            Duck(6f);
            Play(complete, 0.9f);
        }
        public void Creature() => Play(creature, 0.85f);
        public void Travel() => Play(travel, 0.8f);
        public void Unlock() => Play(unlock, 0.8f);
        public void Caw(float volume) => Play(caw, volume, Random.Range(0.92f, 1.05f));
        public void Fall() => Play(fall, 0.55f, Random.Range(0.95f, 1.08f));
        public void Splash() => Play(splash, 0.85f);
        public void Rare()
        {
            Duck(3.2f);
            Play(rare, 0.95f);
        }
        /// <summary>落ちて着地した音（強く落ちたほど低く大きく）。</summary>
        public void Thud(float strength) => Play(land, Mathf.Lerp(0.45f, 1f, strength), Mathf.Lerp(1.05f, 0.75f, strength));
    }
}
