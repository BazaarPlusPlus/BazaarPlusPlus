#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Bundle;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>The card enums a board capture stores by the game's member name.</summary>
public enum PvpCardEnum
{
    CardType,
    CardSize,
    InventorySection,
    ContainerSocket,
}

/// <summary>Only the Host binds captured member names and Replay Payload messages to the game's types.</summary>
public interface IPvpBattleEvidenceBinding
{
    /// <summary>The game's value of <paramref name="name"/>; throws for a name the game lacks.</summary>
    int CardEnumValue(PvpCardEnum kind, string name);

    bool TryCardEnumName(PvpCardEnum kind, int value, out string name);

    /// <summary>Throws when a message does not decode with the game's serializer.</summary>
    void VerifyReplay(PvpReplayPayload payload);
}

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

// An existing file rejected by the decoder can have a null Exception, for example when it is not gzip.
public sealed record ReplayLoadFailure(string BattleId, bool Unreadable, Exception? Exception);

public sealed class BundleCompositionException : Exception
{
    public BundleCompositionException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}
