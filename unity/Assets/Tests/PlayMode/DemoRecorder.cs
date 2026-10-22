using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Shakutori.Tests.GameHarness;
#if UNITY_EDITOR
using UnityEditor.Media;
#endif

namespace Shakutori.Tests
{
    /// <summary>
    /// タイトル画面のデモ映像を作る（エリアごとに 1 本。unity/Assets/StreamingAssets/Demo/エリア.mp4）。
    /// それぞれのエリアの、景色のよい名所 3 か所で、しゃくとりむしが歩くところを、カメラがゆっくり回りながら写す（場面のあいだは、ふわっと切りかえ）。
    /// 時間がかかり、映像のファイルを書きかえるので、ふだんのテストでは走らせない［Explicit］。
    /// <c>-testFilter Shakutori.Tests.DemoRecorder</c> で名前を指定したときだけ。<c>SHAKU_DEMO_STILLS</c> を入れると、映像のかわりに、場面ごとの絵を build-tmp/demo_stills に書き出す。
    /// <c>SHAKU_DEMO_AREAS</c>（例：forest,shrine）で、作るエリアをしぼれる。
    /// 1 秒に 30 こまで（60Hz の画面で、こまの出る間あいがそろう）、2 倍の細かさで写して半分にちぢめ（ぎざぎざが出ない）、
    /// H.264（High）の 4Mbps で書く（ビットレートが低いと、葉や草がブロックのようにくずれる）。
    /// </summary>
    [Explicit("タイトルのデモ映像を作りなおすときだけ")]
    public class DemoRecorder
    {
        const int Fps = 30;
        const int Supersample = 2;
        const uint BitRate = 4000000;
        const float ShotSeconds = 4.6f;
        const float FadeSeconds = 0.6f;

        /// <summary>場面：名所（の、移動したときに着く所）と、カメラの回り方。</summary>
        struct Shot
        {
            public int landmark;
            public float orbit;      // 場面のあいだに回る角度（度）
            public float pitch, distance;
            public bool walk;        // しゃくとりむしが歩く
            public Shot(int landmark, float orbit = 18f, float pitch = 10f, float distance = 4.6f, bool walk = true)
            {
                this.landmark = landmark;
                this.orbit = orbit;
                this.pitch = pitch;
                this.distance = distance;
                this.walk = walk;
            }
        }

        static readonly Dictionary<string, Shot[]> Shots = new Dictionary<string, Shot[]>
        {
            ["forest"] = new[] { new Shot(3, -20f, 11f, 4.6f), new Shot(4, 18f, 10f, 4.8f), new Shot(6, 22f, 14f, 5.2f) },
            ["river"] = new[] { new Shot(11, -16f, 10f, 5f), new Shot(13, 18f, 12f, 4.6f), new Shot(15, -18f, 11f, 4.6f) },
            ["park"] = new[] { new Shot(22, 18f, 11f, 5f), new Shot(25, -18f, 11f, 4.6f), new Shot(24, 20f, 14f, 5.2f) },
            ["mountain"] = new[] { new Shot(33, 16f, 10f, 4.8f), new Shot(32, -18f, 12f, 5f), new Shot(38, 20f, 8f, 5.2f) },
            ["hut"] = new[] { new Shot(45, 18f, 10f, 4.6f), new Shot(46, -18f, 16f, 5.2f), new Shot(44, 16f, 12f, 4.8f) },
            ["coast"] = new[] { new Shot(54, 18f, 9f, 5f), new Shot(53, -18f, 10f, 4.6f), new Shot(57, 16f, 8f, 5.2f) },
            ["shrine"] = new[] { new Shot(62, -18f, 12f, 5f), new Shot(68, 18f, 11f, 5f), new Shot(67, 18f, 9f, 4.6f) },
            ["night"] = new[] { new Shot(71, -18f, 10f, 5f), new Shot(75, 18f, 11f, 4.8f), new Shot(74, -16f, 12f, 5f) },
            ["xmas"] = new[] { new Shot(80, 14f, 5f, 8f), new Shot(83, -16f, 3f, 8f), new Shot(86, 16f, 8f, 6f) },
        };

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string OutDir => Path.Combine(Application.dataPath, "StreamingAssets", TitleDemo.Folder);
        static string StillDir => Path.GetFullPath(Path.Combine(Root, "..", "build-tmp", "demo_stills"));

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Boot();
            yield return StartNewGame();
            yield return Seconds(0.3f);
        }

        [UnityTest, Timeout(7200000)]
        public IEnumerator Record()
        {
            bool stills = System.Environment.GetEnvironmentVariable("SHAKU_DEMO_STILLS") != null;
            string only = System.Environment.GetEnvironmentVariable("SHAKU_DEMO_AREAS");
            var areas = Areas.All.Where(a => Shots.ContainsKey(a.Id) && (string.IsNullOrEmpty(only) || only.Split(',').Contains(a.Id))).ToList();
            GM.ApplyQuality(1);   // きれい
            bool autoCam = SaveSystem.Settings.autoCamera;
            SaveSystem.Settings.autoCamera = false;   // カメラは、この中で回す
            var rt = new RenderTexture(TitleDemo.Width * Supersample, TitleDemo.Height * Supersample, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var small = new RenderTexture(TitleDemo.Width, TitleDemo.Height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            var tex = new Texture2D(TitleDemo.Width, TitleDemo.Height, TextureFormat.RGBA32, false);
            Directory.CreateDirectory(stills ? StillDir : OutDir);
            Time.captureFramerate = Fps;
            try
            {
                foreach (var area in areas)
                {
                    if (Areas.Current != area)
                    {
                        SaveSystem.Data.visited.Add(area.Id);
                        GM.TravelTo(area.Id);
                        yield return WaitUntil(() => Areas.Current == area && GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 300f, area.Id);
                        yield return Seconds(1f);
                    }
                    var sink = new Sink(stills ? null : Path.Combine(OutDir, area.Id + ".mp4"), tex);
                    try { yield return RecordArea(area, rt, small, tex, sink, stills); }
                    finally { sink.Dispose(); }
                }
            }
            finally
            {
                Time.captureFramerate = 0;
                SaveSystem.Settings.autoCamera = autoCam;
                ResetInput();
                Object.Destroy(rt);
                Object.Destroy(small);
                Object.Destroy(tex);
            }
        }

        IEnumerator RecordArea(AreaLayout area, RenderTexture rt, RenderTexture small, Texture2D tex, Sink sink, bool stills)
        {
            var shots = Shots[area.Id];
            int shotFrames = Mathf.RoundToInt(ShotSeconds * Fps), fade = Mathf.RoundToInt(FadeSeconds * Fps);
            var tails = new List<Color32[]>();   // 前の場面の終わりの絵（つぎの場面のはじめと重ねて、ふわっと切りかえる）
            for (int s = 0; s < shots.Length; s++)
            {
                var shot = shots[s];
                var lm = area.Landmarks.First(l => l.id == shot.landmark);
                Vector3 p = GM.world.ArrivalPoint(lm, out var fwd);
                Worm.Spawn(p, fwd);
                float yaw0 = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg - shot.orbit * 0.5f;
                Cam.ShowView(yaw0, shot.pitch, shot.distance);
                Cam.SnapToTarget();
                // 着いてすぐの、体がおちつくまで待つ（写さない）
                for (int k = 0; k < Fps; k++) yield return null;
                bool last = s == shots.Length - 1;
                int total = shotFrames + (last ? 0 : fade);
                var next = new List<Color32[]>();
                for (int f = 0; f < total; f++)
                {
                    float t = f / (float)total;
                    GameInput.VirtualMove = shot.walk && t > 0.08f ? Vector2.up * 0.7f : Vector2.zero;
                    Cam.yaw = yaw0 + shot.orbit * Mathf.SmoothStep(0f, 1f, t);
                    Cam.pitch = shot.pitch;
                    Cam.distance = shot.distance;
                    yield return null;
                    var px = Grab(rt, small, tex);
                    if (stills)
                    {
                        if (f == 0 || f == total / 2 || f == total - 1) Save(px, tex, $"{area.Id}_{s}_{(f == 0 ? "a" : f == total - 1 ? "c" : "b")}");
                        continue;
                    }
                    if (s > 0 && f < tails.Count) sink.Add(Blend(tails[f], px, (f + 1) / (float)(tails.Count + 1)));
                    else if (f < shotFrames || last) sink.Add(px);
                    else next.Add(px);
                }
                ResetInput();
                tails = next;
            }
        }

        /// <summary>写した絵を、そのまま映像に書いていく（たくさんの絵をためこまない）。</summary>
        sealed class Sink : System.IDisposable
        {
            readonly Texture2D _tex;
            readonly string _path;
            int _count;
#if UNITY_EDITOR
            readonly MediaEncoder _enc;
#endif
            public Sink(string path, Texture2D tex)
            {
                _path = path;
                _tex = tex;
#if UNITY_EDITOR
                if (path == null) return;
                if (File.Exists(path)) File.Delete(path);
                var h264 = new H264EncoderAttributes { gopSize = Fps, numConsecutiveBFrames = 2, profile = UnityEditor.VideoEncodingProfile.H264High };
                var attrs = new VideoTrackEncoderAttributes(h264)
                {
                    frameRate = new MediaRational(Fps),
                    width = (uint)tex.width,
                    height = (uint)tex.height,
                    includeAlpha = false,
                    bitRateMode = UnityEditor.VideoBitrateMode.High,
                    targetBitRate = BitRate,
                };
                _enc = new MediaEncoder(path, attrs);
#endif
            }

            public void Add(Color32[] px)
            {
#if UNITY_EDITOR
                if (_enc == null) return;
                _tex.SetPixels32(px);
                _tex.Apply(false);
                _enc.AddFrame(_tex);
                _count++;
#endif
            }

            public void Dispose()
            {
#if UNITY_EDITOR
                if (_enc == null) return;
                _enc.Dispose();
                Debug.Log($"[demo] {_path} {_count} frames {new FileInfo(_path).Length / 1024} KB");
#endif
            }
        }

        /// <summary>2 倍の細かさで写して、半分にちぢめる（となりの 4 つの点をまぜる）。</summary>
        static Color32[] Grab(RenderTexture rt, RenderTexture small, Texture2D tex)
        {
            var cam = Camera.main;
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            Graphics.Blit(rt, small);
            var prev = RenderTexture.active;
            RenderTexture.active = small;
            tex.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0, false);
            RenderTexture.active = prev;
            return tex.GetPixels32();
        }

        static Color32[] Blend(Color32[] a, Color32[] b, float k)
        {
            var o = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = Color32.Lerp(a[i], b[i], k);
            return o;
        }

        static void Save(Color32[] px, Texture2D tex, string name)
        {
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(Path.Combine(StillDir, name + ".png"), tex.EncodeToPNG());
        }

    }
}
