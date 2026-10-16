using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shakutori
{
    /// <summary>
    /// タイトル画面のひみつの操作：ファストトラベルを、ぜんぶ使えるようにする（どのエリアへも・どの名所へも、地図から行ける）。
    /// ・タイトルの「しゃくとりの森」の文字を、すばやく 7 回タップ（クリック）
    /// ・キーボードの矢印キー、またはゲームパッドの十字キーで ↑↑↓↓←→←→
    /// </summary>
    public class TitleSecret
    {
        public const int Taps = 7;
        public const float TapWindow = 3f;
        static readonly Vector2Int[] Code =
        {
            Vector2Int.up, Vector2Int.up, Vector2Int.down, Vector2Int.down,
            Vector2Int.left, Vector2Int.right, Vector2Int.left, Vector2Int.right,
        };

        readonly float[] _tapTimes = new float[Taps];
        int _tapCount;
        int _codeAt;
        Vector2Int _lastDir;

        /// <summary>ひみつの操作が終わったとき。</summary>
        public event Action Unlocked;

        /// <summary>文字をタップした（時刻）。7 回目が 3 秒のうちなら、ひらく。</summary>
        public void Tap(float now)
        {
            _tapTimes[_tapCount % Taps] = now;
            _tapCount++;
            if (_tapCount >= Taps && now - _tapTimes[_tapCount % Taps] <= TapWindow)
            {
                _tapCount = 0;
                Unlocked?.Invoke();
            }
        }

        /// <summary>方向の入力（1 回ずつ）。順番どおりなら進み、まちがえたら最初から。</summary>
        public void Direction(Vector2Int d)
        {
            if (d == Vector2Int.zero) return;
            if (d == Code[_codeAt]) _codeAt++;
            else _codeAt = d == Code[0] ? 1 : 0;
            if (_codeAt >= Code.Length)
            {
                _codeAt = 0;
                Unlocked?.Invoke();
            }
        }

        /// <summary>毎フレーム：キーボードの矢印キーと、ゲームパッドの十字キー（押した瞬間だけ）。</summary>
        public void Poll()
        {
            Vector2Int d = Vector2Int.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame) d = Vector2Int.up;
                else if (kb.downArrowKey.wasPressedThisFrame) d = Vector2Int.down;
                else if (kb.leftArrowKey.wasPressedThisFrame) d = Vector2Int.left;
                else if (kb.rightArrowKey.wasPressedThisFrame) d = Vector2Int.right;
            }
            var pad = Gamepad.current;
            if (d == Vector2Int.zero && pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) d = Vector2Int.up;
                else if (pad.dpad.down.wasPressedThisFrame) d = Vector2Int.down;
                else if (pad.dpad.left.wasPressedThisFrame) d = Vector2Int.left;
                else if (pad.dpad.right.wasPressedThisFrame) d = Vector2Int.right;
            }
            Direction(d);
        }

        /// <summary>
        /// ファストトラベルを、ぜんぶ使えるようにする：どのエリアにも行ったことにして、どの名所も見つけたことにする。
        /// 新しくひらいた数を返す。
        /// </summary>
        public static int UnlockAll()
        {
            var d = SaveSystem.Data;
            int n = 0;
            foreach (var a in Areas.All)
            {
                if (!d.visited.Contains(a.Id)) { d.visited.Add(a.Id); n++; }
                foreach (var lm in a.Landmarks)
                    if (!d.places.Contains(lm.id)) { d.places.Add(lm.id); n++; }
            }
            return n;
        }
    }
}
