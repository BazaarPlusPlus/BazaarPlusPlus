using System.Text.RegularExpressions;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.Ui;

// Game keyword tags need the native tooltip's sprite asset. TMP's default asset
// renders a missing-sprite box instead.
internal static class KeywordSprites
{
    private static readonly Regex SpriteName = new(
        "<sprite\\s+name=\"?([^\">\\s]+)\"?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static TMP_SpriteAsset[] _assets = [];

    // Share the scan across labels in one frame. A later scan can find newly loaded assets.
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
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.card_text.sprites"),
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
        Il2CppUi.IsAlive(asset) && asset.GetSpriteIndexFromName(name) >= 0;

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
