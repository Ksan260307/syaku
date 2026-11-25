using UnityEngine;
using UnityEngine.InputSystem;

namespace Shakutori
{
    /// <summary>
    /// キーボード・マウス・ゲームパッド・タッチ（画面上のスティック）をまとめた入力。
    /// マウスドラッグ／タッチでのカメラ操作は UI の「視点エリア」から AddLook で渡される。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameInput : MonoBehaviour
    {
        public static Vector2 Move { get; private set; }
        public static Vector2 Look { get; private set; }
        public static float Zoom { get; private set; }
        /// <summary>地図・図鑑・ポーズなどの画面が開いているか（GameManager が毎フレーム入れる）。開いている間のホイールやピンチは、画面のスクロールのためのもの。</summary>
        public static bool OverlayOpen { get; set; }
        /// <summary>カメラの拡大・縮小に使う量（画面が開いている間は 0：地図や図鑑のスクロールで、カメラが動かない）。</summary>
        public static float CameraZoom => OverlayOpen ? 0f : SaveSystem.Settings.invertZoom ? -Zoom : Zoom;   // 設定で、ズームの向きを逆に
        public static bool Sprint { get; private set; }
        public static bool SilkPressed { get; private set; }
        public static bool SilkHeld { get; private set; }
        public static bool StandHeld { get; private set; }
        public static bool PausePressed { get; private set; }
        public static bool MapPressed { get; private set; }

        /// <summary>つないでいるゲームパッドの種類（ボタンの名前の出し方）："xbox"・"ps"・"switch"。</summary>
        public static string PadStyle { get; private set; } = "xbox";

        /// <summary>ゲームパッドの名前から、ボタンの名前の出し方を決める（PlayStation・Switch・そのほか）。</summary>
        public static string StyleOf(string typeName, string displayName)
        {
            string n = ((typeName ?? "") + " " + (displayName ?? "")).ToLowerInvariant();
            if (n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("playstation") || n.Contains("wireless controller") || n.Contains("sony")) return "ps";
            if (n.Contains("switch") || n.Contains("pro controller") || n.Contains("joy-con") || n.Contains("nintendo")) return "switch";
            return "xbox";
        }

        static readonly System.Text.RegularExpressions.Regex PadToken = new System.Text.RegularExpressions.Regex(@"\b(LB|RB|LT|RT|View|Menu|A|B|X|Y)\b");

        /// <summary>
        /// Xbox の名前で書いたボタンを、いまのゲームパッドの名前にする（PlayStation は ×○□△・L1/R1/L2/R2、
        /// Switch は B/A/Y/X・L/R/ZL/ZR。ボタンの場所は同じ）。
        /// </summary>
        public static string PadLabel(string xbox) => PadLabel(xbox, PadStyle);

        public static string PadLabel(string xbox, string style)
        {
            if (string.IsNullOrEmpty(xbox) || style == "xbox") return xbox;
            return PadToken.Replace(xbox, m =>
            {
                string k = m.Value;
                if (style == "ps")
                    switch (k)
                    {
                        case "A": return "×";
                        case "B": return "○";
                        case "X": return "□";
                        case "Y": return "△";
                        case "LB": return "L1";
                        case "RB": return "R1";
                        case "LT": return "L2";
                        case "RT": return "R2";
                        case "View": return "SHARE";
                        case "Menu": return "OPTIONS";
                    }
                else
                    switch (k)
                    {
                        case "A": return "B";
                        case "B": return "A";
                        case "X": return "Y";
                        case "Y": return "X";
                        case "LB": return "L";
                        case "RB": return "R";
                        case "LT": return "ZL";
                        case "RT": return "ZR";
                        case "View": return "－";
                        case "Menu": return "＋";
                    }
                return k;
            });
        }
        /// <summary>地図・図鑑で、前／次のページ（エリアの地図・図鑑のタブ）へ（PageUp / PageDown・LB / RB）。</summary>
        public static bool PrevTabPressed { get; private set; }
        /// <summary>背伸びのボタンを押した瞬間（写真モードで、しゃくとりむしをかくすのに使う）。</summary>
        public static bool StandPressed { get; private set; }
        /// <summary>地図の拡大（＋）・縮小（－）のキーを押した（+1・-1。押していなければ 0）。</summary>
        public static int MapZoomStep { get; private set; }
        /// <summary>もどる（ゲームパッドの B・マウスの「もどる」ボタン・Backspace）：開いている画面をとじる。</summary>
        public static bool BackPressed { get; private set; }
        /// <summary>「はやく」「ねらう」を切りかえで入れている・ゆっくり歩きを L3 で入れている（画面に印を出す）。</summary>
        public static bool SprintLatched => _sprintLatch && SaveSystem.Settings.sprintToggle;
        public static bool AimLatched => _aimLatch && SaveSystem.Settings.aimToggle;
        public static bool SlowLatched => _slowLatch;
        static Gamepad _stylePad;
        public static bool NextTabPressed { get; private set; }
        public static bool HelpPressed { get; private set; }
        public static bool ConfirmPressed { get; private set; }
        public static bool AnyKeyPressed { get; private set; }
        public static bool UsingGamepad { get; private set; }
        /// <summary>最後に使われたのがタッチか（タッチ用の画面ボタンを出すかどうか）。</summary>
        public static bool TouchDetected { get; private set; }
        /// <summary>タッチ操作ができる端末か（スマホ・タブレット・タッチ対応 PC）。</summary>
        public static bool TouchCapable => Touchscreen.current != null || Application.isMobilePlatform;
        public static bool CollectionPressed { get; private set; }
        /// <summary>糸をねらっている（F / 右クリック / LT / 画面の「ねらう」を押している間）。</summary>
        public static bool AimHeld { get; private set; }
        public static bool VirtualAim;
        /// <summary>カメラを後ろに戻す（中ボタン / X キー / R3）。</summary>
        public static bool RecenterPressed { get; private set; }
        /// <summary>写真モード（F2）。</summary>
        public static bool PhotoPressed { get; private set; }
        public static bool VirtualRecenter;

        /// <summary>自分で倒したスティックの向き（おまかせ歩きの向きを入れる前）。</summary>
        public static Vector2 PlayerMove { get; private set; }
        /// <summary>おまかせ歩き（AutoWalk）が倒すスティック。自分で倒していないときだけ使う。</summary>
        public static Vector2 AutoMove;
        /// <summary>ゆっくり歩き（Ctrl を押している間 / L3 で切りかえ）。せまい足場で、小さく動ける。</summary>
        public static bool Slow { get; private set; }
        public static bool VirtualSlow;
        static bool _slowLatch;
        /// <summary>ゆっくり歩きのときの、スティックの倒しぐあいの上限。</summary>
        public const float SlowMagnitude = 0.4f;

        // 画面上のボタン・スティック（UI から設定）
        public static Vector2 VirtualMove;
        public static bool VirtualSprint;
        /// <summary>「はやく」の強さ（0〜1）。キーやボタンは 1、ゲームパッドのトリガーは押しぐあい。</summary>
        public static float SprintAmount { get; private set; }
        public static bool VirtualStand;
        static bool _virtualSilkPressed;
        static bool _virtualSilkHeld;
        static Vector2 _lookAccum;
        static bool _sprintLatch, _aimLatch, _aimWasDown;

        /// <summary>「はやく」の切りかえを解除する（設定を切ったとき）。</summary>
        public static void ClearSprintLatch() => _sprintLatch = false;
        static float _zoomAccum;

        public static float LookSensitivity = 1f;
        public static bool InvertY;
        public static bool InvertX;

        public static void AddLook(Vector2 pixels) => _lookAccum += pixels;

        /// <summary>UI が受けたポインターの種類から、タッチ操作中かマウス操作中かを切り替える。</summary>
        public static void MarkTouch()
        {
            TouchDetected = true;
            UsingGamepad = false;
        }

        public static void MarkMouse()
        {
            if (!Application.isMobilePlatform) TouchDetected = false;
        }
        public static void AddZoom(float amount) => _zoomAccum += amount;

        public static void SetVirtualSilk(bool held)
        {
            if (held && !_virtualSilkHeld) _virtualSilkPressed = true;
            _virtualSilkHeld = held;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            Vector2 move = Vector2.zero;
            bool sprint = false, silkP = false, silkH = false, stand = false, pause = false, map = false, help = false, confirm = false, any = false, book = false, aim = false, recenter = false, photo = false, slow = false;
            bool prevTab = false, nextTab = false, standP = false;
            int zoomStep = 0;
            bool back = false;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                // 設定で「切りかえ」にすると、Shift を押すたびに「はやく」が入ったり切れたりする
                if (SaveSystem.Settings.sprintToggle)
                {
                    if (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame) _sprintLatch = !_sprintLatch;
                    sprint |= _sprintLatch;
                }
                else sprint |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                silkP |= kb.spaceKey.wasPressedThisFrame;
                silkH |= kb.spaceKey.isPressed;
                stand |= kb.eKey.isPressed;
                standP |= kb.eKey.wasPressedThisFrame;
                aim |= kb.fKey.isPressed;
                recenter |= kb.xKey.wasPressedThisFrame;
                photo |= kb.f2Key.wasPressedThisFrame;
                pause |= kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                map |= kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                prevTab |= kb.pageUpKey.wasPressedThisFrame;
                back |= kb.backspaceKey.wasPressedThisFrame;
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame || kb.semicolonKey.wasPressedThisFrame) zoomStep = 1;
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) zoomStep = -1;
                nextTab |= kb.pageDownKey.wasPressedThisFrame;
                help |= kb.hKey.wasPressedThisFrame;
                book |= kb.bKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame;
                confirm |= kb.enterKey.wasPressedThisFrame;
                slow |= kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                any |= kb.anyKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame) UsingGamepad = false;
                if (kb.qKey.isPressed) _lookAccum.x -= 600f * Time.unscaledDeltaTime;
                if (kb.rKey.isPressed) _lookAccum.x += 600f * Time.unscaledDeltaTime;
            }
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) _zoomAccum += Mathf.Sign(wheel) * 0.6f;
                // 右ボタンを押している間は、ねらいながらマウスで見まわせる
                recenter |= mouse.middleButton.wasPressedThisFrame;
                back |= mouse.backButton.wasPressedThisFrame;
                if (mouse.rightButton.isPressed)
                {
                    aim = true;
                    _lookAccum += mouse.delta.ReadValue();
                }
            }
            Vector2 padLook = Vector2.zero;
            if (pad != null)
            {
                if (pad != _stylePad)
                {
                    _stylePad = pad;
                    PadStyle = StyleOf(pad.GetType().Name, pad.displayName + " " + pad.description.product + " " + pad.description.manufacturer);
                }
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude > 0.18f) { move += ls; UsingGamepad = true; }
                // 十字キーでも進める
                Vector2 dp = pad.dpad.ReadValue();
                if (dp.sqrMagnitude > 0.1f) { move += dp; UsingGamepad = true; }
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.magnitude > 0.15f) { padLook = rs * 520f * Time.unscaledDeltaTime; UsingGamepad = true; }
                sprint |= pad.rightTrigger.isPressed || pad.buttonEast.isPressed;
                silkP |= pad.buttonSouth.wasPressedThisFrame;
                silkH |= pad.buttonSouth.isPressed;
                stand |= pad.buttonNorth.isPressed;
                standP |= pad.buttonNorth.wasPressedThisFrame;
                aim |= pad.leftTrigger.isPressed;
                recenter |= pad.rightStickButton.wasPressedThisFrame;
                if (pad.leftStickButton.wasPressedThisFrame) _slowLatch = !_slowLatch;   // L3 で、ゆっくり歩きを切りかえ
                pause |= pad.startButton.wasPressedThisFrame;
                map |= pad.selectButton.wasPressedThisFrame;
                prevTab |= pad.leftShoulder.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
                nextTab |= pad.rightShoulder.wasPressedThisFrame;
                book |= pad.buttonWest.wasPressedThisFrame;
                confirm |= pad.buttonSouth.wasPressedThisFrame;
                any |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                if (pad.leftShoulder.isPressed) _zoomAccum += 2f * Time.unscaledDeltaTime;
                if (pad.rightShoulder.isPressed) _zoomAccum -= 2f * Time.unscaledDeltaTime;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) MarkTouch();
            // ボタンを押さずにマウスが動いた（＝本物のマウスがある）ときだけマウス操作に戻す
            if (TouchDetected && mouse != null && mouse.delta.ReadValue().sqrMagnitude > 9f && !mouse.leftButton.isPressed
                && (Touchscreen.current == null || !Touchscreen.current.primaryTouch.press.isPressed)) MarkMouse();

            move += VirtualMove;
            if (move.sqrMagnitude > 1f) move.Normalize();
            // ゆっくり歩き：スティックを少しだけ倒したのと同じ（歩幅が小さく、ゆっくり、頭で足場をさぐる）
            Slow = slow || _slowLatch || VirtualSlow;
            if (Slow && move.sqrMagnitude > 0.01f) move = move.normalized * Mathf.Min(move.magnitude, SlowMagnitude);
            PlayerMove = move;
            if (move.sqrMagnitude < 0.01f && AutoMove.sqrMagnitude > 0.01f) move = AutoMove;
            Move = move;
            Sprint = sprint || VirtualSprint;
            // 「はやく」の強さ（ゲームパッドのトリガーは押しぐあいで 0〜1）
            float amt = sprint || VirtualSprint ? 1f : 0f;
            if (pad != null) amt = Mathf.Max(amt, pad.rightTrigger.ReadValue());
            SprintAmount = amt;
            SilkPressed = silkP || _virtualSilkPressed;
            SilkHeld = silkH || _virtualSilkHeld;
            StandHeld = stand || VirtualStand;
            // 設定で「切りかえ」にすると、押すたびにねらう／やめる（長押しが苦手な人向け）
            if (SaveSystem.Settings.aimToggle)
            {
                if (aim && !_aimWasDown) _aimLatch = !_aimLatch;
                _aimWasDown = aim;
                AimHeld = _aimLatch || VirtualAim;
            }
            else
            {
                _aimLatch = false;
                AimHeld = aim || VirtualAim;
            }
            RecenterPressed = recenter || VirtualRecenter;
            VirtualRecenter = false;
            PhotoPressed = photo;
            PausePressed = pause;
            MapPressed = map;
            PrevTabPressed = prevTab;
            StandPressed = standP;
            MapZoomStep = zoomStep;
            BackPressed = back;
            NextTabPressed = nextTab;
            HelpPressed = help;
            CollectionPressed = book;
            ConfirmPressed = confirm;
            AnyKeyPressed = any;

            // ドラッグ 1 ピクセル ≒ 0.2 度（指でもマウスでも回しやすい速さ）
            Vector2 look = _lookAccum * 0.8f + padLook;
            look *= LookSensitivity;
            if (InvertY) look.y = -look.y;
            if (InvertX) look.x = -look.x;
            Look = look;
            Zoom = _zoomAccum;
            _lookAccum = Vector2.zero;
            _zoomAccum = 0f;
            _virtualSilkPressed = false;
        }
    }
}
