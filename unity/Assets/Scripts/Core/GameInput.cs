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
        public static bool Sprint { get; private set; }
        public static bool SilkPressed { get; private set; }
        public static bool SilkHeld { get; private set; }
        public static bool StandHeld { get; private set; }
        public static bool PausePressed { get; private set; }
        public static bool MapPressed { get; private set; }
        public static bool HelpPressed { get; private set; }
        public static bool ConfirmPressed { get; private set; }
        public static bool AnyKeyPressed { get; private set; }
        public static bool UsingGamepad { get; private set; }
        /// <summary>最後に使われたのがタッチか（タッチ用の画面ボタンを出すかどうか）。</summary>
        public static bool TouchDetected { get; private set; }
        /// <summary>タッチ操作ができる端末か（スマホ・タブレット・タッチ対応 PC）。</summary>
        public static bool TouchCapable => Touchscreen.current != null || Application.isMobilePlatform;
        public static bool CollectionPressed { get; private set; }

        // 画面上のボタン・スティック（UI から設定）
        public static Vector2 VirtualMove;
        public static bool VirtualSprint;
        public static bool VirtualStand;
        static bool _virtualSilkPressed;
        static bool _virtualSilkHeld;
        static Vector2 _lookAccum;
        static float _zoomAccum;

        public static float LookSensitivity = 1f;
        public static bool InvertY;

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
            bool sprint = false, silkP = false, silkH = false, stand = false, pause = false, map = false, help = false, confirm = false, any = false, book = false;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                sprint |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                silkP |= kb.spaceKey.wasPressedThisFrame;
                silkH |= kb.spaceKey.isPressed;
                stand |= kb.eKey.isPressed;
                pause |= kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                map |= kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                help |= kb.hKey.wasPressedThisFrame;
                book |= kb.bKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame;
                confirm |= kb.enterKey.wasPressedThisFrame;
                any |= kb.anyKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame) UsingGamepad = false;
                if (kb.qKey.isPressed) _lookAccum.x -= 600f * Time.unscaledDeltaTime;
                if (kb.rKey.isPressed) _lookAccum.x += 600f * Time.unscaledDeltaTime;
            }
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) _zoomAccum += Mathf.Sign(wheel) * 0.6f;
            }
            Vector2 padLook = Vector2.zero;
            if (pad != null)
            {
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude > 0.18f) { move += ls; UsingGamepad = true; }
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.magnitude > 0.15f) { padLook = rs * 520f * Time.unscaledDeltaTime; UsingGamepad = true; }
                sprint |= pad.rightTrigger.isPressed || pad.buttonEast.isPressed;
                silkP |= pad.buttonSouth.wasPressedThisFrame;
                silkH |= pad.buttonSouth.isPressed;
                stand |= pad.buttonNorth.isPressed || pad.leftTrigger.isPressed;
                pause |= pad.startButton.wasPressedThisFrame;
                map |= pad.selectButton.wasPressedThisFrame;
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
            Move = move;
            Sprint = sprint || VirtualSprint;
            SilkPressed = silkP || _virtualSilkPressed;
            SilkHeld = silkH || _virtualSilkHeld;
            StandHeld = stand || VirtualStand;
            PausePressed = pause;
            MapPressed = map;
            HelpPressed = help;
            CollectionPressed = book;
            ConfirmPressed = confirm;
            AnyKeyPressed = any;

            // ドラッグ 1 ピクセル ≒ 0.2 度（指でもマウスでも回しやすい速さ）
            Vector2 look = _lookAccum * 0.8f + padLook;
            look *= LookSensitivity;
            if (InvertY) look.y = -look.y;
            Look = look;
            Zoom = _zoomAccum;
            _lookAccum = Vector2.zero;
            _zoomAccum = 0f;
            _virtualSilkPressed = false;
        }
    }
}
