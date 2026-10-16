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
    /// </summary>
    public class TitleDemo : MonoBehaviour
    {
        public const string Folder = "Demo";
        public const int Width = 960, Height = 540;

        VideoPlayer _player;
        RenderTexture _rt;
        VisualElement _view;
        string _current;
        bool _want;
        int _fails;
        readonly System.Random _rng = new System.Random(System.Environment.TickCount);

        /// <summary>いま流している（流そうとしている）エリア。</summary>
        public string Current => _current;
        /// <summary>映像が画面に出ている。</summary>
        public bool Showing => _view != null && _view.ClassListContains("title-demo--on");

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
                if (_player != null) _player.Stop();
            }
        }

        void Ensure()
        {
            if (_player != null) return;
            _rt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "TitleDemo" };
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
                _view?.AddToClassList("title-demo--on");
            };
            _player.loopPointReached += p => { if (_want) PlayNext(); };
            _player.errorReceived += (p, msg) =>
            {
                Debug.LogWarning($"[TitleDemo] {_current}: {msg}");
                _view?.RemoveFromClassList("title-demo--on");
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
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
