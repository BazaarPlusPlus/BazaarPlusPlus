#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

/// <summary>
/// Closed, low-cardinality owner of an operational event. Feature code selects a declared scope;
/// it never manufactures a component name from runtime data.
/// </summary>
internal sealed class BppLogFeatureScope
{
    internal static BppLogFeatureScope Logger { get; } = new("Logger", "logging");
    internal static BppLogFeatureScope Plugin { get; } = new("Plugin", "plugin");
    internal static BppLogFeatureScope RunLifecycle { get; } = new("RunLifecycle", "run_lifecycle");
    internal static BppLogFeatureScope RunLogging { get; } = new("RunLogging", "run_logging");
    internal static BppLogFeatureScope Upload { get; } = new("Upload", "upload");
    internal static BppLogFeatureScope BundlePipeline { get; } =
        new("BundlePipeline", "bundle_pipeline");
    internal static BppLogFeatureScope PvpBattles { get; } = new("PvpBattles", "pvp_battles");
    internal static BppLogFeatureScope CombatReplay { get; } = new("CombatReplay", "combat_replay");
    internal static BppLogFeatureScope Screenshots { get; } = new("Screenshots", "screenshots");
    internal static BppLogFeatureScope VoiceSubtitles { get; } =
        new("VoiceSubtitles", "voice_subtitles");
    internal static BppLogFeatureScope OverlayPanels { get; } =
        new("OverlayPanels", "overlay_panels");
    internal static BppLogFeatureScope HistoryPanel { get; } = new("HistoryPanel", "history_panel");
    internal static BppLogFeatureScope CollectionPanel { get; } =
        new("CollectionPanel", "collection_panel");
    internal static BppLogFeatureScope LiveBuildPanel { get; } =
        new("LiveBuildPanel", "live_build_panel");
    internal static BppLogFeatureScope Tooltips { get; } = new("Tooltips", "tooltips");
    internal static BppLogFeatureScope EventPreview { get; } = new("EventPreview", "event_preview");
    internal static BppLogFeatureScope ItemEnchantPreview { get; } =
        new("ItemEnchantPreview", "item_enchant_preview");
    internal static BppLogFeatureScope CombatStatusBar { get; } =
        new("CombatStatusBar", "combat_status_bar");
    internal static BppLogFeatureScope PostCombatImpact { get; } =
        new("PostCombatImpact", "post_combat_impact");
    internal static BppLogFeatureScope BilingualItemNames { get; } =
        new("BilingualItemNames", "bilingual_item_names");
    internal static BppLogFeatureScope NameOverride { get; } = new("NameOverride", "name_override");
    internal static BppLogFeatureScope CosmeticNames { get; } =
        new("CosmeticNames", "cosmetic_names");
    internal static BppLogFeatureScope Lobby { get; } = new("Lobby", "lobby");
    internal static BppLogFeatureScope Settings { get; } = new("Settings", "settings");
    internal static BppLogFeatureScope StaticCards { get; } = new("StaticCards", "static_cards");
    internal static BppLogFeatureScope Supporters { get; } = new("Supporters", "supporters");
    internal static BppLogFeatureScope GraphicsUpscaling { get; } =
        new("GraphicsUpscaling", "graphics_upscaling");

    private static readonly BppLogFeatureScope[] DeclaredScopes =
    [
        Logger,
        Plugin,
        RunLifecycle,
        RunLogging,
        Upload,
        BundlePipeline,
        PvpBattles,
        CombatReplay,
        Screenshots,
        VoiceSubtitles,
        OverlayPanels,
        HistoryPanel,
        CollectionPanel,
        LiveBuildPanel,
        Tooltips,
        EventPreview,
        ItemEnchantPreview,
        CombatStatusBar,
        PostCombatImpact,
        BilingualItemNames,
        CosmeticNames,
        NameOverride,
        Lobby,
        Settings,
        StaticCards,
        Supporters,
        GraphicsUpscaling,
    ];

    private BppLogFeatureScope(string prefixName, string eventIdPrefix)
    {
        PrefixName = prefixName;
        EventIdPrefix = eventIdPrefix;
    }

    internal string PrefixName { get; }

    internal string EventIdPrefix { get; }

    internal static IReadOnlyList<BppLogFeatureScope> All { get; } =
        Array.AsReadOnly(DeclaredScopes);

    internal static bool IsDeclared(BppLogFeatureScope? scope)
    {
        for (var index = 0; index < DeclaredScopes.Length; index++)
        {
            if (ReferenceEquals(DeclaredScopes[index], scope))
                return true;
        }
        return false;
    }
}

internal enum BppLogCorrelationPolicy
{
    None,
    Full,
    Short,
    Hash,
}

/// <summary>
/// One operational event, written at its call site: a closed scope, a literal dotted-snake id
/// under the scope's prefix, and the storm key. <c>Storm == null</c> never suppresses;
/// <c>storm: []</c> suppresses repeats of the id; named keys suppress repeats whose named
/// fields carry equal values. Errors key on correlation fields and the exception type instead.
/// </summary>
internal readonly struct BppLogEvent
{
    internal BppLogEvent(BppLogFeatureScope scope, string id, string[]? storm = null)
    {
        Scope = scope;
        Id = id;
        Storm = storm;
    }

    internal BppLogFeatureScope Scope { get; }

    internal string Id { get; }

    internal IReadOnlyList<string>? Storm { get; }
}

/// <summary>
/// One rendered field, written at its call site as <c>("name", value)</c> or
/// <c>("name", value, policy)</c>. The policy is the privacy contract: <c>Short</c> renders the
/// first 8 characters, <c>Hash</c> the first 12 hex digits of SHA-256, <c>Full</c> marks a
/// correlation id rendered verbatim, and <c>None</c> renders the value verbatim.
/// </summary>
internal readonly struct BppLogField
{
    internal BppLogField(
        string name,
        object? value,
        BppLogCorrelationPolicy policy = BppLogCorrelationPolicy.None
    )
    {
        Name = name;
        Value = value;
        Policy = policy;
    }

    internal string Name { get; }

    internal object? Value { get; }

    internal BppLogCorrelationPolicy Policy { get; }

    public static implicit operator BppLogField((string Name, object? Value) field) =>
        new(field.Name, field.Value);

    public static implicit operator BppLogField(
        (string Name, object? Value, BppLogCorrelationPolicy Policy) field
    ) => new(field.Name, field.Value, field.Policy);
}
