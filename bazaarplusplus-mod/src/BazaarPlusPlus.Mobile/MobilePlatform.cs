using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

/// <summary>
/// The Mobile Platform Port (the mod's GLOSSARY.md; ADR-0025 decision 3): what a host gives the
/// Mobile UI to reach its runtime. Code in this tree reaches the host only through it, never
/// through <c>#if</c>. Each host implements it once and installs it through
/// <see cref="MobilePlatform.Install"/> before any Mobile UI is built.
/// </summary>
internal interface IMobilePlatformPort
{
    /// <summary>
    /// Makes an <c>await</c> started in the scope resume on the main thread, until the scope is
    /// disposed. A host whose main thread already carries that context returns an empty scope.
    /// </summary>
    IDisposable EnterMainThread();

    /// <summary>
    /// Whether <paramref name="value"/> is a live Unity object: neither destroyed nor, on an
    /// IL2CPP host, collected under its managed proxy.
    /// </summary>
    bool IsAlive(UnityEngine.Object? value);

    /// <summary><paramref name="value"/> as <typeparamref name="T"/>, which it must be.</summary>
    T Cast<T>(UnityEngine.Object value)
        where T : UnityEngine.Object;

    /// <summary>Lets <paramref name="label"/> wrap at its rect's width, or keeps it on one line.</summary>
    void SetWordWrapping(TMP_Text label, bool wrap);

    /// <summary>Runs <paramref name="onTap"/> each time <paramref name="button"/> is clicked.</summary>
    void OnClick(Button button, Action onTap);

    /// <summary>Runs <paramref name="onChanged"/> with each new value of <paramref name="toggle"/>.</summary>
    void OnValueChanged(Toggle toggle, Action<bool> onChanged);

    /// <summary>
    /// The game sprites the native skin borrows, from a host read of the game's own popups and
    /// tooltips; a sprite the game has not loaded is null.
    /// </summary>
    NativeSkinArt ReadNativeSkinArt();
}

/// <summary>
/// The game's popup button at rest and clicked (<see cref="Button"/>, <see cref="Selected"/>), and
/// its tooltip frame and tag frame (<see cref="Frame"/>, <see cref="TagFrame"/>).
/// </summary>
internal sealed record NativeSkinArt(
    Sprite? Button,
    Sprite? Selected,
    Sprite? Frame,
    Sprite? TagFrame
);

/// <summary>The host's <see cref="IMobilePlatformPort"/>, installed once at startup.</summary>
internal static class MobilePlatform
{
    private static IMobilePlatformPort? _port;

    public static IMobilePlatformPort Port =>
        _port
        ?? throw new InvalidOperationException(
            "The host built Mobile UI before it installed its Mobile Platform Port."
        );

    public static void Install(IMobilePlatformPort port) =>
        _port = port ?? throw new ArgumentNullException(nameof(port));
}
