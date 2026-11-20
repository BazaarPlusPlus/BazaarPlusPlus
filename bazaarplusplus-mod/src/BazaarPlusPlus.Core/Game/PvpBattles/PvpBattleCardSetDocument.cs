#nullable enable
using BazaarPlusPlus.Storage;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.PvpBattles;

// The installer History reader consumes this battle_snapshots JSON shape, including
// snake_case keys and game enum member names.
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

// The game and Bundle use 0 for a card with no size, so the database stores it as "0".
public sealed class PvpBattleCardDocument
{
    public const string NoSize = "0";

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
