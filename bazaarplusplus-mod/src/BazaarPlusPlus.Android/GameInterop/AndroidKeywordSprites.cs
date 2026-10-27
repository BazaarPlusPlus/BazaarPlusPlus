using System.Text.RegularExpressions;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The game's card text names its keyword icons as <c>&lt;sprite name=…&gt;</c>, which resolve
/// only against the sprite asset its own tooltip text carries; a BPP label with TMP's default
/// asset draws TMP's missing-sprite box instead. <see cref="Apply"/> gives a label that shows card
/// text the loaded sprite asset that holds its first keyword icon, found the way the desktop's
/// <c>KeywordIconSpriteProvider</c> finds it. Main thread only.
/// </summary>
internal static class AndroidKeywordSprites
{
    private static readonly Regex SpriteName = new(
        "<sprite\\s+name=\"?([^\">\\s]+)\"?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static TMP_SpriteAsset[] _assets = [];

    // The frame of the last scan: labels built in one frame share it, so an icon no loaded asset
    // holds costs one scan per frame, not one per label. A later frame scans again, since an
    // asset loaded since then may hold it.
    private static int _scanFrame = -1;

    /// <summary>Call after setting <paramref name="label"/>'s text.</summary>
    public static void Apply(TMP_Text label)
    {
        if (label == null)
            return;
        var match = SpriteName.Match(label.text ?? string.Empty);
        if (!match.Success)
            return;
        var name = match.Groups[1].Value;
        if (Holds(label.spriteAsset ?? TMP_Settings.defaultSpriteAsset, name))
            return;
        var asset = Find(name) ?? (Rescan() ? Find(name) : null);
        if (asset != null)
            label.spriteAsset = asset;
        if (Reported.Add((asset == null ? "missing:" : "found:") + name))
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.card_text.sprites"),
                ("icon", name),
                ("asset", asset?.name)
            );
    }

    private static bool Rescan()
    {
        var frame = Time.frameCount;
        if (frame == _scanFrame)
            return false;
        _scanFrame = frame;
        _assets = Scan();
        return true;
    }

    private static TMP_SpriteAsset? Find(string name)
    {
        foreach (var asset in _assets)
            if (Holds(asset, name))
                return asset;
        return null;
    }

    private static bool Holds(TMP_SpriteAsset? asset, string name) =>
        asset != null && !asset.WasCollected && asset.GetSpriteIndexFromName(name) >= 0;

    // The game's own text components first, then any sprite asset loaded at all.
    private static TMP_SpriteAsset[] Scan()
    {
        var seen = new HashSet<int>();
        var assets = new List<TMP_SpriteAsset>();
        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            var asset = text != null ? text.spriteAsset : null;
            if (asset != null && seen.Add(asset.GetInstanceID()))
                assets.Add(asset);
        }
        foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
            if (asset != null && seen.Add(asset.GetInstanceID()))
                assets.Add(asset);
        return [.. assets];
    }
}
