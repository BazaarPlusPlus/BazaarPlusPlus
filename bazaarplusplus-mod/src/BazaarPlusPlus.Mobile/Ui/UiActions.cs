using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

/// <summary>
/// The one way a Mobile UI control reaches feature code: the handler runs as the host runs its
/// own controls' (<see cref="IMobilePlatformPort.RunUiAction"/>), inside the main-thread scope, so
/// every <c>await</c> it starts resumes on the main thread, and an exception is reported instead
/// of escaping into uGUI's event loop.
/// </summary>
internal static class UiActions
{
    public static void OnTap(Button button, string control, Action handler)
    {
        if (button == null)
            throw new ArgumentNullException(nameof(button));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        MobilePlatform.Port.OnClick(button, () => Run(control, handler));
    }

    /// <summary><paramref name="handler"/> gets the field's new text after each edit.</summary>
    public static void OnChanged(TMP_InputField field, string control, Action<string> handler)
    {
        if (field == null)
            throw new ArgumentNullException(nameof(field));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        MobilePlatform.Port.OnTextChanged(
            field,
            value => Run(control, () => handler(value ?? string.Empty))
        );
    }

    public static void Run(string control, Action handler) =>
        MobilePlatform.Port.RunUiAction(control, handler);
}
