#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

/// <summary>
/// Closed, low-cardinality owner of an operational event. Feature code selects a declared scope;
/// it never manufactures a component name from runtime data.
/// </summary>
public sealed class BppLogFeatureScope
{
    public static BppLogFeatureScope Logger { get; } = new("Logger", "logging");
    public static BppLogFeatureScope Plugin { get; } = new("Plugin", "plugin");
    public static BppLogFeatureScope RunLifecycle { get; } = new("RunLifecycle", "run_lifecycle");
    public static BppLogFeatureScope RunLogging { get; } = new("RunLogging", "run_logging");
    public static BppLogFeatureScope Upload { get; } = new("Upload", "upload");
    public static BppLogFeatureScope BundlePipeline { get; } =
        new("BundlePipeline", "bundle_pipeline");
    public static BppLogFeatureScope PvpBattles { get; } = new("PvpBattles", "pvp_battles");
    public static BppLogFeatureScope CombatReplay { get; } = new("CombatReplay", "combat_replay");
    public static BppLogFeatureScope Screenshots { get; } = new("Screenshots", "screenshots");
    public static BppLogFeatureScope VoiceSubtitles { get; } =
        new("VoiceSubtitles", "voice_subtitles");
    public static BppLogFeatureScope OverlayPanels { get; } =
        new("OverlayPanels", "overlay_panels");
    public static BppLogFeatureScope HistoryPanel { get; } = new("HistoryPanel", "history_panel");
    public static BppLogFeatureScope CollectionPanel { get; } =
        new("CollectionPanel", "collection_panel");
    public static BppLogFeatureScope LiveBuildPanel { get; } =
        new("LiveBuildPanel", "live_build_panel");
    public static BppLogFeatureScope Tooltips { get; } = new("Tooltips", "tooltips");
    public static BppLogFeatureScope EventPreview { get; } = new("EventPreview", "event_preview");
    public static BppLogFeatureScope ItemEnchantPreview { get; } =
        new("ItemEnchantPreview", "item_enchant_preview");
    public static BppLogFeatureScope CombatStatusBar { get; } =
        new("CombatStatusBar", "combat_status_bar");
    public static BppLogFeatureScope PostCombatImpact { get; } =
        new("PostCombatImpact", "post_combat_impact");
    public static BppLogFeatureScope BilingualItemNames { get; } =
        new("BilingualItemNames", "bilingual_item_names");
    public static BppLogFeatureScope NameOverride { get; } = new("NameOverride", "name_override");
    public static BppLogFeatureScope CosmeticNames { get; } =
        new("CosmeticNames", "cosmetic_names");
    public static BppLogFeatureScope Lobby { get; } = new("Lobby", "lobby");
    public static BppLogFeatureScope Settings { get; } = new("Settings", "settings");
    public static BppLogFeatureScope StaticCards { get; } = new("StaticCards", "static_cards");
    public static BppLogFeatureScope Supporters { get; } = new("Supporters", "supporters");
    public static BppLogFeatureScope GraphicsUpscaling { get; } =
        new("GraphicsUpscaling", "graphics_upscaling");
    public static BppLogFeatureScope Android { get; } = new("Android", "android");
    public static BppLogFeatureScope SelfUpdate { get; } = new("SelfUpdate", "self_update");

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
        Android,
        SelfUpdate,
    ];

    private BppLogFeatureScope(string prefixName, string eventIdPrefix)
    {
        PrefixName = prefixName;
        EventIdPrefix = eventIdPrefix;
    }

    public string PrefixName { get; }

    public string EventIdPrefix { get; }

    public static IReadOnlyList<BppLogFeatureScope> All { get; } = Array.AsReadOnly(DeclaredScopes);

    public static bool IsDeclared(BppLogFeatureScope? scope)
    {
        for (var index = 0; index < DeclaredScopes.Length; index++)
        {
            if (ReferenceEquals(DeclaredScopes[index], scope))
                return true;
        }
        return false;
    }
}

public enum BppLogCorrelationPolicy
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
public readonly struct BppLogEvent
{
    public BppLogEvent(BppLogFeatureScope scope, string id, string[]? storm = null)
    {
        Scope = scope;
        Id = id;
        Storm = storm;
    }

    public BppLogFeatureScope Scope { get; }

    public string Id { get; }

    public IReadOnlyList<string>? Storm { get; }
}

/// <summary>
/// One rendered field, written at its call site as <c>("name", value)</c> or
/// <c>("name", value, policy)</c>. The policy is the privacy contract: <c>Short</c> renders the
/// first 8 characters, <c>Hash</c> the first 12 hex digits of SHA-256, <c>Full</c> marks a
/// correlation id rendered verbatim, and <c>None</c> renders the value verbatim.
/// </summary>
public readonly struct BppLogField
{
    public BppLogField(
        string name,
        object? value,
        BppLogCorrelationPolicy policy = BppLogCorrelationPolicy.None
    )
    {
        Name = name;
        Value = value;
        Policy = policy;
    }

    public string Name { get; }

    public object? Value { get; }

    public BppLogCorrelationPolicy Policy { get; }

    public static implicit operator BppLogField((string Name, object? Value) field) =>
        new(field.Name, field.Value);

    public static implicit operator BppLogField(
        (string Name, object? Value, BppLogCorrelationPolicy Policy) field
    ) => new(field.Name, field.Value, field.Policy);
}
