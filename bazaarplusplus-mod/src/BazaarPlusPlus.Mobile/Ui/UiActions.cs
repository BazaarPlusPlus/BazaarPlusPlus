using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

// Route control callbacks through RunUiAction so awaits resume on the main thread
// and exceptions are reported before they can escape into uGUI.
internal static class UiActions
{
    public static void OnTap(Button button, string control, Action handler)
    {
        if (button == null)
            throw new ArgumentNullException(nameof(button));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        Il2CppUi.OnClick(button, () => Run(control, handler));
    }

    public static void OnChanged(TMP_InputField field, string control, Action<string> handler)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        Il2CppUi.OnTextChanged(field, value => Run(control, () => handler(value ?? string.Empty)));
    }

    public static void Run(string control, Action handler) =>
        MobilePlatform.Port.RunUiAction(control, handler);
}
