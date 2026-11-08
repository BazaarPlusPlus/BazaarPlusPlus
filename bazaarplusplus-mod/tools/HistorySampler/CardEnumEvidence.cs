using System.Text.Json;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.HistorySampler;

/// <summary>
/// The game-typed card reads, from the card enum table both hosts are held to. The tool never
/// verifies a replay: the game does that when it plays one.
/// </summary>
internal sealed class CardEnumEvidence : IPvpBattleEvidenceBinding
{
    public static CardEnumEvidence Instance { get; } = new();

    private readonly Dictionary<PvpCardEnum, Dictionary<string, int>> _values;

    private CardEnumEvidence()
    {
        using var stream =
            typeof(CardEnumEvidence).Assembly.GetManifestResourceStream(
                "HistorySampler.pvp-card-enums.json"
            ) ?? throw new InvalidOperationException("The card enum table is not embedded.");
        using var table = JsonDocument.Parse(stream);
        _values = Enum.GetValues<PvpCardEnum>()
            .ToDictionary(
                kind => kind,
                kind =>
                    table
                        .RootElement.GetProperty(kind.ToString())
                        .EnumerateObject()
                        .ToDictionary(member => member.Name, member => member.Value.GetInt32())
            );
    }

    public int CardEnumValue(PvpCardEnum kind, string name) =>
        _values[kind].TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"{kind} has no member '{name}'.");

    public bool TryCardEnumName(PvpCardEnum kind, int value, out string name)
    {
        name = _values[kind].FirstOrDefault(entry => entry.Value == value).Key ?? string.Empty;
        return name.Length > 0;
    }

    public void VerifyReplay(PvpReplayPayload payload) { }
}
