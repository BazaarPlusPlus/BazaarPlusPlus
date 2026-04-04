using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.Input;

internal static class KeyBindings
{
    internal static class Modifiers
    {
        public static bool IsCtrlPressed(Keyboard keyboard)
        {
            return keyboard != null
                && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        }

        public static bool IsShiftPressed(Keyboard keyboard)
        {
            return keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }
}
