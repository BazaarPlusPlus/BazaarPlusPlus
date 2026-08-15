#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.GameInterop;

/// <summary>
/// The narrow, public game-interop surface that the separate-assembly BazaarAgent host
/// plugin consumes from BazaarPlusPlus. It exposes only encounter reads and hero-identity
/// translation; everything else in BazaarPlusPlus stays <c>internal</c>. BazaarPlusPlus does
/// not reference the agent module; it merely publishes this facade via
/// <see cref="BazaarAgentGameBridge"/>.
/// </summary>
public interface IBazaarAgentGameProbe
{
    /// <summary>Main thread only. Lightweight read of the current/choice-screen encounter ids.</summary>
    EncounterIdsSnapshot GetEncounterIds();

    /// <summary>Main thread only. Reads target-selection legality state.</summary>
    EncounterTargetingSnapshot GetTargetingState();

    /// <summary>Resolves the encounter type (merchant/trainer/event/...) for an encounter id,
    /// or <c>null</c> when it cannot be classified.</summary>
    string? ResolveEncounterType(string? encounterId);

    /// <summary>Resolves a client hero id through the current game's compatibility boundary.</summary>
    BazaarAgentHeroResolution ResolveHero(string? heroId);

    /// <summary>Returns the stable agent-facing id for a current runtime hero.</summary>
    string ToAgentHeroId(EHero hero);

    /// <summary>Returns the server-assigned id for the current run, when initialized.</summary>
    string? GetCurrentServerRunId();

    /// <summary>Resolves a runtime card's native localized title with stable fallbacks.</summary>
    string ResolveCardDisplayName(Card card);

    /// <summary>Resolves a runtime-valued card description using the native tooltip pipeline.</summary>
    string? ResolveCardDescription(Card card);
}

public interface IBazaarAgentTypedGameProbe
{
    BazaarAgentGameProbeOutcome<EncounterIdsSnapshot> GetEncounterIdsOutcome();

    BazaarAgentGameProbeOutcome<EncounterTargetingSnapshot> GetTargetingStateOutcome();
}

public readonly struct BazaarAgentGameProbeOutcome<TSnapshot>
{
    private BazaarAgentGameProbeOutcome(bool isSuccess, TSnapshot snapshot, Exception? exception)
    {
        IsSuccess = isSuccess;
        Snapshot = snapshot;
        Exception = exception;
    }

    public bool IsSuccess { get; }

    public TSnapshot Snapshot { get; }

    public Exception? Exception { get; }

    public static BazaarAgentGameProbeOutcome<TSnapshot> Success(TSnapshot snapshot) =>
        new(true, snapshot, null);

    public static BazaarAgentGameProbeOutcome<TSnapshot> Failure(
        TSnapshot fallback,
        Exception? exception
    ) => new(false, fallback, exception);
}
