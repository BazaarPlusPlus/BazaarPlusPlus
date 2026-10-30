using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile;

/// <summary>
/// The Mobile Platform Port (root GLOSSARY.md; ADR-0025 decision 3): what a host gives the Mobile
/// UI to reach its runtime. Code in this tree reaches the host only through it, never through
/// <c>#if</c>. Each host implements it once and installs it through
/// <see cref="MobilePlatform.Install"/> before any Mobile UI is built.
/// </summary>
internal interface IMobilePlatformPort
{
    /// <summary>
    /// Makes an <c>await</c> started in the scope resume on the main thread, until the scope is
    /// disposed. A host whose main thread already carries that context returns an empty scope.
    /// </summary>
    IDisposable EnterMainThread();

    // The members below reach the host's uGUI kit and keyword sprites. They leave the port when
    // the kit moves into this tree (issue #701, step 3) and the keyword sprites follow it.

    /// <summary>A child rect anchored to a fraction of its parent, inset by pixel offsets.</summary>
    RectTransform Rect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    );

    /// <summary>A wrapping rich-text label in <paramref name="rect"/> that takes no touches.</summary>
    TextMeshProUGUI Text(
        RectTransform rect,
        TMP_FontAsset font,
        string text,
        float size,
        TextAlignmentOptions alignment,
        Color color
    );

    /// <summary>A vertical drag-scrolled list filling <paramref name="area"/>, with inertia.</summary>
    (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area);

    /// <summary>
    /// Gives <paramref name="label"/> the game's sprite asset that holds the keyword icons its
    /// text names; call after setting the text.
    /// </summary>
    void ApplyKeywordSprites(TMP_Text label);
}

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
