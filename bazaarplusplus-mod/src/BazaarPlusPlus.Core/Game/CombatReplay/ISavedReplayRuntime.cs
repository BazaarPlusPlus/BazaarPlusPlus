#nullable enable
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// The saved-replay playback a host's combat replay runtime offers History: whether a saved
/// battle can replay, the replay of a saved or an already loaded battle, and the read of a saved
/// battle under the playback payload lease. Each host implements it over its own runtime; a
/// replay leaves only through the exits ADR-0002 names.
/// </summary>
public interface ISavedReplayRuntime
{
    // A replay is starting, playing, or returning to the menu.
    bool HasSavedReplaySession { get; }

    // Worker-only: waits for the shared payload gate without touching host/game objects.
    // Null means the runtime is unavailable; the caller must not delete rows or payloads.
    IDisposable? AcquirePayloadMaintenance();

    bool CanReplaySavedCombats(out string reason);

    bool CanReplaySavedBattle(string battleId, out string reason);

    bool ReplaySaved(string battleId, bool recordVideo);

    // Plays a manifest and payload the caller already holds; source names where they came from.
    // Player is the recorder in both a local manifest and a Ghost payload's manifest.
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

/// <summary>
/// Whether the host can record a replay's video. <see cref="Supported"/> says whether the host
/// records video at all (a host that ships no encoder hides the record action);
/// <see cref="CanRecord"/> says whether it can record now. <see cref="Prewarm"/> runs on the UI
/// thread, where a host can read its video settings and load its encoder.
/// </summary>
public interface IReplayRecordingAvailability
{
    bool Supported { get; }

    void Prewarm();

    bool CanRecord();
}
