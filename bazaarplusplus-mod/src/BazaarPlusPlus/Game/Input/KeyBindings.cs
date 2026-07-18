#nullable enable
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.Input;

internal static class KeyBindings
{
    internal static class Modifiers
    {
        public static bool IsCtrlPressed(Keyboard? keyboard)
        {
            return keyboard != null
                && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        }

        public static bool IsAltPressed(Keyboard? keyboard)
        {
            return keyboard != null
                && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        }

        public static bool IsShiftPressed(Keyboard? keyboard)
        {
            return keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }
}
