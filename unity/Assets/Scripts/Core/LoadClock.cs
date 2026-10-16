using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Shakutori
{
    /// <summary>
    /// 読み込みの時間をはかる（エリアを作る、だんかいごと）。おわったら 1 行のログに出す（Web 版でも、ブラウザのコンソールで見られる）。
    /// </summary>
    public static class LoadClock
    {
        static readonly Stopwatch Watch = new Stopwatch();
        static readonly List<(string name, double ms)> Laps = new List<(string, double)>();
        static double _last;

        /// <summary>前の回の、だんかいごとの時間（ミリ秒）。</summary>
        public static IReadOnlyList<(string name, double ms)> LastLaps => Laps;
        public static double TotalMs { get; private set; }

        public static void Start()
        {
            Laps.Clear();
            Watch.Restart();
            _last = 0;
        }

        public static void Lap(string name)
        {
            if (!Watch.IsRunning) return;
            double now = Watch.Elapsed.TotalMilliseconds;
            Laps.Add((name, now - _last));
            _last = now;
        }

        public static string Stop(string title)
        {
            if (!Watch.IsRunning) return "";
            TotalMs = Watch.Elapsed.TotalMilliseconds;
            Watch.Stop();
            var sb = new StringBuilder();
            sb.Append($"[load] {title} {TotalMs:0} ms:");
            foreach (var (name, ms) in Laps) sb.Append($" {name}={ms:0}");
            return sb.ToString();
        }
    }
}
