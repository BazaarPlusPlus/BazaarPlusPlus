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
    internal static BppLogFeatureScope BilingualItemNames { get; } =
        new("BilingualItemNames", "bilingual_item_names");
    internal static BppLogFeatureScope NameOverride { get; } = new("NameOverride", "name_override");
    internal static BppLogFeatureScope Lobby { get; } = new("Lobby", "lobby");
    internal static BppLogFeatureScope Settings { get; } = new("Settings", "settings");
    internal static BppLogFeatureScope Supporters { get; } = new("Supporters", "supporters");

    private static readonly BppLogFeatureScope[] DeclaredScopes =
    [
        Logger,
        Plugin,
        RunLifecycle,
        RunLogging,
        Upload,
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
        BilingualItemNames,
        NameOverride,
        Lobby,
        Settings,
        Supporters,
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

internal enum BppLogFieldPrivacy
{
    Public,
    UntrustedText,
    Sensitive,
    LocalPath,
    RemoteUri,
}

internal enum BppLogCorrelationPolicy
{
    None,
    Full,
    Short,
    Hash,
}

internal enum BppLogCardinality
{
    Low,
    High,
}

/// <summary>
/// Defines one ordered field. The definition token owns its name and governance metadata; runtime
/// values bind to this exact token so callers cannot override privacy or correlation policy.
/// </summary>
internal sealed class BppLogFieldDefinition
{
    internal BppLogFieldDefinition(
        int order,
        string name,
        BppLogFieldPrivacy privacy,
        BppLogCorrelationPolicy correlation,
        BppLogCardinality cardinality
    )
    {
        Order = order;
        Name = name;
        Privacy = privacy;
        Correlation = correlation;
        Cardinality = cardinality;
    }

    internal int Order { get; }

    internal string Name { get; }

    internal BppLogFieldPrivacy Privacy { get; }

    internal BppLogCorrelationPolicy Correlation { get; }

    internal BppLogCardinality Cardinality { get; }

    internal BppLogFieldValue Bind(object? value) => new(this, value);
}

internal sealed class BppLogStormPolicy
{
    private readonly BppLogFieldDefinition[] _keyFields;

    internal BppLogStormPolicy(IReadOnlyList<BppLogFieldDefinition>? keyFields)
    {
        _keyFields = Snapshot(keyFields);
    }

    internal IReadOnlyList<BppLogFieldDefinition> KeyFields => _keyFields;

    private static BppLogFieldDefinition[] Snapshot(IReadOnlyList<BppLogFieldDefinition>? fields)
    {
        if (fields == null || fields.Count == 0)
            return Array.Empty<BppLogFieldDefinition>();

        var snapshot = new BppLogFieldDefinition[fields.Count];
        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = fields[index];
        return snapshot;
    }
}

/// <summary>
/// Stable event vocabulary entry. Definitions live beside their owning feature and declare scope,
/// event ID, ordered field schema, privacy, correlation, cardinality, and optional storm keys.
/// Each feature publishes definitions from a local static <c>*LogEvents</c> class marked with
/// <see cref="BppLogEventSourceAttribute"/>; every definition is a static readonly field so catalog
/// discovery can enumerate it. Implementation, patch, and helper names belong in the event ID
/// after the fixed feature prefix; they must never become scopes. Scope, ID, and field metadata are
/// authored constants, while runtime data is supplied only through bound values.
/// </summary>
internal sealed class BppLogEventDefinition
{
    private readonly BppLogFieldDefinition[] _fields;

    internal BppLogEventDefinition(
        BppLogFeatureScope scope,
        string eventId,
        IReadOnlyList<BppLogFieldDefinition>? fields,
        BppLogStormPolicy? stormPolicy = null
    )
    {
        Scope = scope;
        EventId = eventId;
        _fields = Snapshot(fields);
        StormPolicy = stormPolicy;
    }

    internal BppLogFeatureScope Scope { get; }

    internal string EventId { get; }

    internal IReadOnlyList<BppLogFieldDefinition> Fields => _fields;

    internal BppLogStormPolicy? StormPolicy { get; }

    private static BppLogFieldDefinition[] Snapshot(IReadOnlyList<BppLogFieldDefinition>? fields)
    {
        if (fields == null || fields.Count == 0)
            return Array.Empty<BppLogFieldDefinition>();

        var snapshot = new BppLogFieldDefinition[fields.Count];
        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = fields[index];
        return snapshot;
    }
}

internal readonly struct BppLogFieldValue
{
    internal BppLogFieldValue(BppLogFieldDefinition field, object? value)
    {
        Field = field;
        Value = value;
    }

    internal BppLogFieldDefinition Field { get; }

    internal object? Value { get; }
}
