using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Shakutori
{
    /// <summary>
    /// タイトル画面のうしろに流すデモ映像。エリアごとに 1 本（StreamingAssets/Demo/エリア.mp4。テストの DemoRecorder で作る）。
    /// 行ったことのあるエリアの映像だけから、ランダムに 1 本ずつ流す（終わったら、なるべくちがうエリアの映像へ）。
    /// 映像が読めないとき（ファイルがない・ブラウザが再生できない）は出さない（うしろのエリアが、いままでどおり見える）。
    /// 映像がすっかり出ているあいだは、うしろのエリアを描かない（映像にかくれて見えないのに描くと、映像がかくかくする）。
    /// </summary>
    public class TitleDemo : MonoBehaviour
    {
        public const string Folder = "Demo";
        public const int Width = 1280, Height = 720;
        /// <summary>映像がふわっと出てくる時間（USS の .title-demo の transition と同じ）。</summary>
        public const float FadeIn = 1.2f;

        VideoPlayer _player;
        RenderTexture _rt;
        VisualElement _view;
        string _current;
        bool _want;
        int _fails;
        float _onAt = -1f;
        bool _worldHidden;
        int _mask;
        bool _post;
        readonly System.Random _rng = new System.Random(System.Environment.TickCount);

        /// <summary>いま流している（流そうとしている）エリア。</summary>
        public string Current => _current;
        /// <summary>映像が画面に出ている。</summary>
        public bool Showing => _view != null && _view.ClassListContains("title-demo--on");
        /// <summary>映像にかくれて、うしろのエリアを描くのを止めている。</summary>
        public bool WorldHidden => _worldHidden;

        public static string UrlFor(string areaId) => Application.streamingAssetsPath + "/" + Folder + "/" + areaId + ".mp4";

        /// <summary>流せる映像のエリア（行ったことのあるエリア。エディタとPCでは、ファイルがあるものだけ）。</summary>
        public static List<string> Choices()
        {
            var list = new List<string>();
            foreach (var a in Areas.All)
            {
                if (!SaveSystem.Data.visited.Contains(a.Id) && a != Areas.Forest) continue;
#if !UNITY_WEBGL || UNITY_EDITOR
                if (!System.IO.File.Exists(UrlFor(a.Id))) continue;
#endif
                list.Add(a.Id);
            }
            return list;
        }

        /// <summary>次に流すエリア：行ったことのあるエリアから、ランダムに（同じものが続かないように）。</summary>
        public static string Pick(List<string> choices, string last, System.Random rng)
        {
            if (choices.Count == 0) return null;
            if (choices.Count == 1) return choices[0];
            string id;
            do id = choices[rng.Next(choices.Count)]; while (id == last);
            return id;
        }

        public void Bind(VisualElement view)
        {
            _view = view;
            if (_view != null) _view.pickingMode = PickingMode.Ignore;
        }

        /// <summary>テストの実行中（バッチモード）でも映像を流す（ふだんは流さない：テストのじゃまにならないように）。</summary>
        public static bool AllowInBatch;

        /// <summary>タイトルを出す・しまうときに呼ぶ。</summary>
        public void Show(bool on)
        {
            if (on && Application.isBatchMode && !AllowInBatch) return;
            _want = on;
            if (on)
            {
                _fails = 0;
                PlayNext();
            }
            else
            {
                _view?.RemoveFromClassList("title-demo--on");
                _onAt = -1f;
                HideWorld(false);
                if (_player != null) _player.Stop();
            }
        }

        void Update()
        {
            // 映像がすっかり出たら、うしろのエリアを描かない（映像がしまわれるときは、すぐ描きはじめる）
            bool cover = _want && _onAt >= 0f && Showing && Time.unscaledTime - _onAt > FadeIn + 0.1f;
            if (cover != _worldHidden) HideWorld(cover);
        }

        /// <summary>うしろのエリア（地形・草花・小物・いきもの・光のしあげ）を描くのを、止める・もどす。</summary>
        void HideWorld(bool hide)
        {
            if (hide == _worldHidden) return;
            var cam = Camera.main;
            if (cam == null) return;
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var gm = GetComponent<GameManager>();
            if (hide)
            {
                _mask = cam.cullingMask;
                cam.cullingMask = 0;
                if (data != null) { _post = data.renderPostProcessing; data.renderPostProcessing = false; }
            }
            else
            {
                cam.cullingMask = _mask;
                if (data != null) data.renderPostProcessing = _post;
            }
            if (gm != null)
            {
                if (gm.world != null && gm.world.instanced != null) gm.world.instanced.enabled = !hide;
                if (gm.world != null && gm.world.loose != null) gm.world.loose.enabled = !hide;
                if (gm.creatures != null) gm.creatures.enabled = !hide;
            }
            _worldHidden = hide;
        }

        void Ensure()
        {
            if (_player != null) return;
            _rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "TitleDemo", filterMode = FilterMode.Bilinear };
            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _rt;
            _player.audioOutputMode = VideoAudioOutputMode.None;   // 音なし（ブラウザでも、さわる前から流せる）
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
            _player.prepareCompleted += p => { if (_want) p.Play(); };
            _player.started += p =>
            {
                if (!_want) { p.Stop(); return; }
                _fails = 0;
                if (_onAt < 0f) _onAt = Time.unscaledTime;
                _view?.AddToClassList("title-demo--on");
            };
            _player.loopPointReached += p => { if (_want) PlayNext(); };
            _player.errorReceived += (p, msg) =>
            {
                Debug.LogWarning($"[TitleDemo] {_current}: {msg}");
                _view?.RemoveFromClassList("title-demo--on");
                _onAt = -1f;
                if (_want && ++_fails < 3) PlayNext();
            };
            if (_view != null) _view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_rt));
        }

        void PlayNext()
        {
            var id = Pick(Choices(), _current, _rng);
            if (id == null) { _view?.RemoveFromClassList("title-demo--on"); return; }
            Ensure();
            _current = id;
            _player.Stop();
            _player.url = UrlFor(id);
            _player.Prepare();
        }

        void OnDestroy()
        {
            HideWorld(false);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
