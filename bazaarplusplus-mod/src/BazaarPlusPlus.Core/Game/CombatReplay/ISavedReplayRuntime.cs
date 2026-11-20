#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

public interface ISavedReplayRuntime
{
    bool HasSavedReplaySession { get; }

    // Call on a worker because this can block on the payload gate. A null lease forbids deletion.
    IDisposable? AcquirePayloadMaintenance();

    bool CanReplaySavedCombats(out string reason);

    bool CanReplaySavedBattle(string battleId, out string reason);

    // Call on the Host thread. The worker read must recheck the selection on that thread before starting playback.
    Task<bool> ReplaySavedAsync(
        string battleId,
        bool recordVideo,
        CancellationToken cancellationToken = default,
        Func<bool>? canCommit = null
    );

    // Player identifies the recorder in both local and Ghost Battle manifests.
    bool ReplayLoadedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo,
        CombatReplayPlaybackSource source
    );

    SavedBattleLoadOutcome TryLoadSavedBattle(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    );
}

public interface IReplayRecordingAvailability
{
    bool Supported { get; }

    void Prewarm();

    bool CanRecord();
}
