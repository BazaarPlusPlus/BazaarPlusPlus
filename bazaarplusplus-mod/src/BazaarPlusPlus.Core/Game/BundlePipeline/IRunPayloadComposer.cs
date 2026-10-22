#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Bundle;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// Builds one run's Bundle V6 inputs from the local history database and stored Replay
/// Payloads (<see cref="RunPayloadComposer"/>; tests substitute their own).
/// </summary>
public interface IRunPayloadComposer
{
    string ResolvePlayerAccountId(string runId, string? frozenAccountId);

    RunPayloadComposition Compose(string runId, string playerAccountId);

    void FitToBudget(RunPayloadComposition composition);
}

/// <summary>The card enums a board capture stores by the game's member name.</summary>
public enum PvpCardEnum
{
    CardType,
    CardSize,
    InventorySection,
    ContainerSocket,
}

/// <summary>
/// The two reads of PvP evidence only the host can make, because they need the game's types:
/// the numeric value of a card enum a capture stored by name, and whether a Replay Payload's
/// message bytes decode as the game's messages.
/// </summary>
public interface IPvpBattleEvidenceBinding
{
    /// <summary>The game's value of <paramref name="name"/>; throws for a name the game lacks.</summary>
    int CardEnumValue(PvpCardEnum kind, string name);

    /// <summary>Throws when a message does not decode with the game's serializer.</summary>
    void VerifyReplay(PvpReplayPayload payload);
}

/// <summary>Encodes a run screenshot for a Bundle; the host owns the image codec.</summary>
public interface IBundleScreenshotEncoder
{
    Task<BundleScreenshotBuildInputV6?> EncodeAsync(
        string absolutePath,
        long capturedAtMs,
        CancellationToken cancellationToken
    );
}

public sealed record RunPayloadComposition(
    RunPayloadV6 Payload,
    List<BundleBattleProjectionV6> Projections,
    IReadOnlyList<ReplayLoadFailure> ReplayFailures
);

// Unreadable: the file could not be read. Otherwise its bytes did not decode; Exception is null
// when they were rejected without one (for example, not gzip).
public sealed record ReplayLoadFailure(string BattleId, bool Unreadable, Exception? Exception);

public sealed class BundleCompositionException : Exception
{
    public BundleCompositionException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}
