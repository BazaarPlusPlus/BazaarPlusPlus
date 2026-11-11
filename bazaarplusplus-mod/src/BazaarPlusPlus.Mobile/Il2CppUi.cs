using System.Diagnostics.CodeAnalysis;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

/// <summary>
/// The Il2CppInterop form of the Unity calls the Mobile UI makes (ADR-0025 decision 3):
/// object liveness, text wrapping, and listeners converted to Unity's delegate types.
/// </summary>
internal static class Il2CppUi
{
    /// <summary>
    /// Whether <paramref name="value"/> is a live Unity object: neither destroyed nor collected
    /// under its IL2CPP proxy.
    /// </summary>
    public static bool IsAlive([NotNullWhen(true)] UnityEngine.Object? value) =>
        value != null && !value.WasCollected;

    /// <summary>Lets <paramref name="label"/> wrap at its rect's width, or keeps it on one line.</summary>
    public static void SetWordWrapping(TMP_Text label, bool wrap) =>
        label.enableWordWrapping = wrap;

    /// <summary>Runs <paramref name="onTap"/> each time <paramref name="button"/> is clicked.</summary>
    public static void OnClick(Button button, Action onTap) =>
        button.onClick.AddListener((UnityAction)onTap);

    /// <summary>Runs <paramref name="onChanged"/> with each new value of <paramref name="toggle"/>.</summary>
    public static void OnValueChanged(Toggle toggle, Action<bool> onChanged) =>
        toggle.onValueChanged.AddListener((UnityAction<bool>)onChanged);

    /// <summary>Runs <paramref name="onChanged"/> with each new text of <paramref name="field"/>.</summary>
    public static void OnTextChanged(TMP_InputField field, Action<string> onChanged) =>
        field.onValueChanged.AddListener((UnityAction<string>)onChanged);
}
