using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shakutori.Tests
{
    /// <summary>
    /// 総合テスト用の操作ハーネス。実際のシーン（Forest）を読み込み、
    /// 画面上のスティック／ボタンと同じ経路（GameInput の仮想入力）でしゃくとりむしを動かす。
    /// </summary>
    public static class GameHarness
    {
        public static GameManager GM { get; private set; }
        public static InchwormController Worm => GM.worm;
        public static GameUI UI => GM.ui;
        public static Collectibles Col => GM.collectibles;
        public static FollowCamera Cam => GM.followCamera;

        public static IEnumerator Boot(bool keepSave = false)
        {
            if (!keepSave) SaveSystem.ResetAll();
            GameManager.Assists = false;   // ヒントや画質の自動調整は、テストごとに結果が変わらないよう切る
            Time.timeScale = 1f;
            ResetInput();
            SceneManager.LoadScene("Forest", LoadSceneMode.Single);
            yield return null;
            yield return null;
            float t = 0f;
            while (true)
            {
                GM = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                if (GM != null && GM.State != GameManager.GameState.Loading) break;
                t += Time.unscaledDeltaTime;
                if (t > 120f) Assert.Fail("森の生成がタイムアウトしました");
                yield return null;
            }
        }

        public static IEnumerator StartNewGame()
        {
            GM.StartGame(false);
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 10f, "ゲーム開始");
        }

        public static IEnumerator ContinueGame()
        {
            GM.StartGame(true);
            yield return WaitUntil(() => GM.State == GameManager.GameState.Playing && Worm.InputEnabled, 10f, "つづきから");
        }

        public static IEnumerator WaitUntil(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (!cond())
            {
                t += Time.unscaledDeltaTime;
                if (t > timeout) Assert.Fail($"待ち時間切れ: {what}");
                yield return null;
            }
        }

        public static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        public static IEnumerator Seconds(float s)
        {
            float t = 0f;
            while (t < s)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>スティックを一定時間倒し続ける。perFrame は毎フレームの検証用。</summary>
        public static IEnumerator Hold(Vector2 move, float seconds, Action perFrame = null, bool sprint = false)
        {
            GameInput.VirtualMove = move;
            GameInput.VirtualSprint = sprint;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                perFrame?.Invoke();
                yield return null;
            }
            GameInput.VirtualMove = Vector2.zero;
            GameInput.VirtualSprint = false;
        }

        public static void ResetInput()
        {
            GameInput.VirtualMove = Vector2.zero;
            GameInput.VirtualSprint = false;
            GameInput.VirtualStand = false;
            GameInput.SetVirtualSilk(false);
            GameInput.VirtualAim = false;
        }

        /// <summary>カメラを指定の向き（水平）へ向ける。</summary>
        public static IEnumerator FaceCamera(Vector3 dir)
        {
            Cam.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Cam.pitch = 20f;
            yield return null;
            yield return null;
        }

        /// <summary>XZ の位置の一番上の面にしゃくとりむしを置く。</summary>
        public static Vector3 Place(Vector2 xz, Vector3 forward)
        {
            Vector3 p = TopSurface(xz);
            Worm.Spawn(p, forward);
            return p;
        }

        public static Vector3 TopSurface(Vector2 xz)
        {
            Assert.IsTrue(Physics.Raycast(new Vector3(xz.x, 200f, xz.y), Vector3.down, out var hit, 400f, ShakuConst.SurfaceMask), $"{xz} に面がない");
            return hit.point;
        }

        public static bool NearSurface(Vector3 p, float maxGap)
        {
            return Physics.CheckSphere(p, maxGap, ShakuConst.SurfaceMask, QueryTriggerInteraction.Ignore);
        }

        public static float MaxSegment(BodyCurve c)
        {
            float m = 0f;
            for (int i = 1; i < c.Count; i++) m = Mathf.Max(m, Vector3.Distance(c.pos[i - 1], c.pos[i]));
            return m;
        }

        /// <summary>尾と頭を結ぶ線から体がどれだけ持ち上がっているか（Ω 字の高さ）。</summary>
        public static float LoopHeight(BodyCurve c, Vector3 up)
        {
            Vector3 a = c.Tail, b = c.Head;
            float best = 0f;
            for (int i = 0; i < c.Count; i++)
            {
                Vector3 p = c.pos[i];
                float t = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 1e-6f));
                best = Mathf.Max(best, Vector3.Dot(p - Vector3.Lerp(a, b, t), up));
            }
            return best;
        }
    }
}
