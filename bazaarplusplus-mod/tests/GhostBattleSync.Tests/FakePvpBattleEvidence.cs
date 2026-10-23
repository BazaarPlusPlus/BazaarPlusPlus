#nullable enable
using System.Text.Json;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.TestSupport;

// The game-typed PvP evidence reads, over the card enum table both hosts are held to. A replay
// whose spawn message starts with RejectedReplayMarker fails verification, as bytes the game's
// serializer cannot decode do.
internal sealed class FakePvpBattleEvidence : IPvpBattleEvidenceBinding
{
    public const byte RejectedReplayMarker = 0xFF;

    public static FakePvpBattleEvidence Instance { get; } = new();

    private readonly Dictionary<PvpCardEnum, Dictionary<string, int>> _values;

    private FakePvpBattleEvidence()
    {
        using var table = JsonDocument.Parse(
            TestInputs.Fixture("tests/Shared/fixtures/pvp-card-enums.json")
        );
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

    public void VerifyReplay(PvpReplayPayload payload)
    {
        if (payload.SpawnMessageBytes is [RejectedReplayMarker, ..])
            throw new InvalidDataException("The spawn message does not decode.");
    }
}
