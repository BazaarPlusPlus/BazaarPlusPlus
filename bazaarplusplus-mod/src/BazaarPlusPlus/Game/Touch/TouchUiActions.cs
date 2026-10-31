#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The one way a touch control reaches feature code: an exception is logged instead of escaping
/// into uGUI's event loop. Unity's synchronization context already resumes awaits on the main
/// thread here, so unlike the Android host no pump scope is needed.
/// </summary>
internal static class TouchUiActions
{
    public static void Run(string control, Action handler)
    {
        try
        {
            handler();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.ui.action_failed",
                    storm: ["control"]
                ),
                exception,
                ("control", control)
            );
        }
    }
}

/// <summary>
/// The font every touch surface draws with: the game's own sans face through
/// <see cref="NativeGameTypography"/>, with its CJK fallbacks added when a label needs them.
/// </summary>
internal static class TouchFonts
{
    /// <summary>The game's body font, or null while the game has not loaded it yet.</summary>
    public static TMP_FontAsset? Body()
    {
        var outcome = NativeGameTypography.PrepareOwnedText(out var preparation);
        return outcome == NativeGameTypography.Outcome.Ready ? preparation?.Font : null;
    }

    /// <summary>Adds the game's CJK fallbacks when <paramref name="label"/>'s text needs them.</summary>
    public static void Cover(TMP_Text label)
    {
        if (label == null)
            return;
        try
        {
            NativeGameTypography.EnsureNativeTextCoverage(label, label.text);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.fonts.cover_failed"),
                exception
            );
        }
    }
}
