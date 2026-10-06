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

        AudioSource _music, _amb;
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
        }

        void Update()
        {
            var s = SaveSystem.Settings;
            _musicTarget = s.music * 0.55f;
            _ambTarget = Mathf.Clamp01(s.sfx) * 0.5f;
            float k = ShakuMath.DampFactor(1.2f, Time.unscaledDeltaTime);
            _music.volume = Mathf.Lerp(_music.volume, _started ? _musicTarget : 0f, k);
            _amb.volume = Mathf.Lerp(_amb.volume, _started ? _ambTarget : 0f, k);
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

        public void Step(bool head)
        {
            if (steps == null || steps.Length == 0) return;
            Play(steps[Random.Range(0, steps.Length)], head ? 0.35f : 0.25f, Random.Range(0.9f, 1.15f) * (head ? 1.08f : 0.95f));
        }

        public void Collect(int count) => Play(collect, 0.8f, 1f + Mathf.Min(count % 8, 7) * 0.025f);
        public void Discover() => Play(discover, 0.9f);
        public void Click() => Play(click, 0.5f, Random.Range(0.97f, 1.03f));
        public void Silk() => Play(silk, 0.55f);
        public void Land() => Play(land, 0.5f);
        public void Complete() => Play(complete, 0.9f);
    }
}
