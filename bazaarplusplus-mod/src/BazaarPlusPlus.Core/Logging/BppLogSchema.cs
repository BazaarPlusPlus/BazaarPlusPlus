#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

// Declare scopes centrally to keep event cardinality independent of runtime data.
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
    public static BppLogFeatureScope EncounterPreview { get; } =
        new("EventPreview", "event_preview");
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
    public static BppLogFeatureScope Touch { get; } = new("Touch", "touch");
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
        EncounterPreview,
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
        Touch,
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

// A null storm key disables suppression. An empty key groups by event ID alone.
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

// The field policy defines which correlation identifiers may appear verbatim in logs.
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
