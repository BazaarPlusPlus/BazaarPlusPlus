#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
    private static readonly List<BppSettingsDockDefinition> _definitions = new();

    public static void Install(IBppConfig config, SettingsDockEntryRegistry registry)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        var ordered = new List<(int Order, BppSettingsDockDefinition Def)>(
            registry.MaterializeWithOrder(config)
        );
        ordered.Sort((a, b) => a.Order.CompareTo(b.Order));

        _definitions.Clear();
        foreach (var pair in ordered)
            _definitions.Add(pair.Def);
    }

    public static void Reset() => _definitions.Clear();

    internal static IReadOnlyList<BppSettingsDockDefinition> Definitions => _definitions;

    private static readonly LocalizedTextSet PreviewOffStatus = new("OFF", "按键显示");
    private static readonly LocalizedTextSet PreviewAutoStatus = new("AUTO", "智能切换");
    private static readonly LocalizedTextSet PreviewAlwaysStatus = new("ON", "常驻显示");

    internal static string ResolvePreviewVisibilityModeStatus(
        PreviewVisibilityMode mode,
        string languageCode
    )
    {
        var status = mode switch
        {
            PreviewVisibilityMode.Off => PreviewOffStatus,
            PreviewVisibilityMode.Always => PreviewAlwaysStatus,
            _ => PreviewAutoStatus,
        };
        return status.Resolve(languageCode, L.CurrentMode);
    }
}
