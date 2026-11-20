using System.Diagnostics.CodeAnalysis;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

internal static class Il2CppUi
{
    public static bool IsAlive([NotNullWhen(true)] UnityEngine.Object? value) =>
        value != null && !value.WasCollected;

    public static void SetWordWrapping(TMP_Text label, bool wrap) =>
        label.enableWordWrapping = wrap;

    public static void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener((UnityAction)onTap);

    public static void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener((UnityAction<bool>)onChanged);

    public static void OnTextChanged(TMP_InputField field, Action<string> onChanged) =>
        field.onValueChanged.AddListener((UnityAction<string>)onChanged);
}
