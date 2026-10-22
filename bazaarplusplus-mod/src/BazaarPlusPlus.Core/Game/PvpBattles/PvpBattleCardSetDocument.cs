#nullable enable
using BazaarPlusPlus.Storage;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One board capture as the history database stores it in <c>battle_snapshots</c>: snake_case
/// JSON whose card enums are the game's member names. The desktop host serializes its game-typed
/// capture into this shape; the Android host writes this type directly. Readers that hold no game
/// types, such as the Bundle composer, read it through this type.
/// </summary>
public sealed class PvpBattleCardSetDocument
{
    private static readonly JsonSerializerSettings SerializerSettings =
        SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: true);

    public IList<PvpBattleCardDocument> Items { get; set; } = new List<PvpBattleCardDocument>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }

    public static string Serialize(PvpBattleCardSetDocument document) =>
        JsonConvert.SerializeObject(
            document ?? throw new ArgumentNullException(nameof(document)),
            SerializerSettings
        );

    public static PvpBattleCardSetDocument Deserialize(string json) =>
        JsonConvert.DeserializeObject<PvpBattleCardSetDocument>(json, SerializerSettings)
        ?? new PvpBattleCardSetDocument();
}

/// <summary>One captured card; the four enum fields hold the game's member names.</summary>
public sealed class PvpBattleCardDocument
{
    public string InstanceId { get; set; } = string.Empty;

    public string TemplateId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    public string? Section { get; set; }

    public string? Socket { get; set; }

    public string? Name { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public IDictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>();
}
