#nullable enable
using BazaarPlusPlus.ModApi.Bundle;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// Builds one run's Bundle V6 inputs from the local history database and stored Replay
/// Payloads. The host implements it, because composing a run reads game-typed PvP evidence.
/// </summary>
public interface IRunPayloadComposer
{
    string ResolvePlayerAccountId(string runId, string? frozenAccountId);

    RunPayloadComposition Compose(string runId, string playerAccountId);

    void FitToBudget(RunPayloadComposition composition);
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
