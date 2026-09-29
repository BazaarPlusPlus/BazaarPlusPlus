#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.GameInterop.TagTypography;

// BPP-authored replacements for native tag labels, free of game types so they resolve without
// the game runtime.
internal static class NativeTagLabelText
{
    internal static string Quest(string nativeLabel) =>
        L.Resolve(new LocalizedTextSet(nativeLabel, "任务"));
}
