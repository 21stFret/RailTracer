using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RailTracer
{
    // The controller (Steam Deck, Xbox and anything Steam Input shows as one). Tick once a frame, before any reads.
    // Active: the last input came from a pad (the cursor is hidden and the prompts show pad buttons); any mouse
    // movement, click or key hands it back.
    public static class Pad
    {
        public static bool Active { get; private set; }
        static Gamepad G => Gamepad.current;

        // Key repeat for the stick and D-pad steps (real time: the menus run with the game paused).
        public static float repeatDelay = 0.35f, repeatEvery = 0.11f;
        static Vector2Int held;
        static float nextStep;
        public static Vector2Int Step { get; private set; } // this frame's step (x right, y up), else zero

        static readonly GamepadButton[] Buttons =
        {
            GamepadButton.South, GamepadButton.East, GamepadButton.West, GamepadButton.North,
            GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftTrigger, GamepadButton.RightTrigger,
            GamepadButton.Start, GamepadButton.Select, GamepadButton.LeftStick, GamepadButton.RightStick,
            GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadRight,
        };

        public static void Tick()
        {
            var g = G;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.scroll.ReadValue().y != 0f)) Active = false;
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) Active = false;
            if (g != null && (AnyDown || g.leftStick.ReadValue().sqrMagnitude > 0.25f || g.rightStick.ReadValue().sqrMagnitude > 0.25f)) Active = true;
            Cursor.visible = !Active;

            // One step per push, then repeats while held. The stronger axis wins (no diagonals).
            var dir = Vector2Int.zero;
            if (g != null)
            {
                var v = g.leftStick.ReadValue() + g.dpad.ReadValue();
                if (v.sqrMagnitude > 0.25f)
                    dir = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? new Vector2Int(v.x > 0 ? 1 : -1, 0) : new Vector2Int(0, v.y > 0 ? 1 : -1);
            }
            Step = Vector2Int.zero;
            if (dir != held) { held = dir; Step = dir; nextStep = Time.unscaledTime + repeatDelay; }
            else if (dir != Vector2Int.zero && Time.unscaledTime >= nextStep) { Step = dir; nextStep = Time.unscaledTime + repeatEvery; }
        }

        public static bool Down(GamepadButton b) => G != null && G[b].wasPressedThisFrame;
        public static bool Held(GamepadButton b) => G != null && G[b].isPressed;
        public static Vector2 RightStick => G != null ? G.rightStick.ReadValue() : Vector2.zero;
        public static float LeftStickY => G != null ? G.leftStick.ReadValue().y : 0f;

        public static bool AnyDown
        {
            get
            {
                if (G == null) return false;
                foreach (var b in Buttons) if (G[b].wasPressedThisFrame) return true;
                return false;
            }
        }

        // The game reads Keyboard.current everywhere and treats null as "input held back": with no keyboard at all
        // (a Steam Deck can have none) it would never take input, so a dummy one stands in.
        public static void EnsureKeyboard()
        {
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>("Pad Stand-in Keyboard");
        }
    }

    // The pointer the game reads in place of the mouse: the real mouse, or in pad mode a stand-in placed on the
    // focused menu line or the cursor tile, A as its button.
    public class Pointer
    {
        public Vector2 pos, delta;
        public float scroll;
        public bool click, held, rclick, rheld;

        public static Pointer FromMouse(Mouse m) => m == null ? null : new Pointer
        {
            pos = m.position.ReadValue(), delta = m.delta.ReadValue(), scroll = m.scroll.ReadValue().y,
            click = m.leftButton.wasPressedThisFrame, held = m.leftButton.isPressed,
            rclick = m.rightButton.wasPressedThisFrame, rheld = m.rightButton.isPressed,
        };
    }
}
