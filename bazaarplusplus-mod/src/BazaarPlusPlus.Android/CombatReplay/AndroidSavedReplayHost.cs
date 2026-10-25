using BazaarPlusPlus.Android.CombatReplay.Bootstrap;
using BazaarPlusPlus.Android.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;

namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// The Android host's side of Saved Replay Playback (ADR-0020): the main-thread pump every entry
/// point runs in, the Il2Cpp loader, bootstrap and opponent, the cleanup each exit path runs (no
/// opening-state restore on this host), the History row probe's short cache, and the
/// <c>android.replay.*</c> events and counters fed from the module's notices. Records no video.
/// </summary>
internal sealed class AndroidSavedReplayHost : ISavedReplayHost
{
    private static readonly TimeSpan SavedReplayCacheLifetime = TimeSpan.FromSeconds(5);
    private readonly SavedReplayCatalog _saved;
    private readonly AndroidReplayOpponent _opponent = new();
    private readonly Dictionary<string, (bool Saved, DateTime At)> _savedReplayCache = new(
        StringComparer.Ordinal
    );
    private long _starts;
    private long _startFailures;
    private long _exitsHandled;
    private long _menuReturns;

    public AndroidSavedReplayHost(SavedReplayCatalog saved)
    {
        _saved = saved ?? throw new ArgumentNullException(nameof(saved));
    }

    public long Starts => Interlocked.Read(ref _starts);

    public long StartFailures => Interlocked.Read(ref _startFailures);

    public long ExitsHandled => Interlocked.Read(ref _exitsHandled);

    public long MenuReturns => Interlocked.Read(ref _menuReturns);

    public IDisposable EnterHostContext() => AndroidMainThread.Host.Enter();

    public float Now => UnityEngine.Time.realtimeSinceStartup;

    public bool InReplayState => IsInReplayState();

    // The lobby the menu return waits for is the one History's reopen waits for.
    public bool LobbyLoaded => AndroidLobbyScene.IsLoaded();

    public bool RecordsVideo => false;

    // History asks on every refresh; the answer is cached briefly so a refresh does not open the
    // DELETE-mode database on the main thread each time (ReplaySaved rechecks the catalog).
    public bool CanOfferSavedReplay(string battleId)
    {
        var now = DateTime.UtcNow;
        if (
            _savedReplayCache.TryGetValue(battleId, out var cached)
            && now - cached.At < SavedReplayCacheLifetime
        )
            return cached.Saved;
        bool saved;
        try
        {
            saved = _saved.HasSavedReplay(battleId);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.catalog_read_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
            return false;
        }
        if (_savedReplayCache.Count > 256)
            _savedReplayCache.Clear();
        _savedReplayCache[battleId] = (saved, now);
        return saved;
    }

    public void ForgetOffer(string battleId) => _savedReplayCache.Remove(battleId);

    public ISavedReplayStart BeginStart(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var sequence = AndroidCombatReplayLoader.Load(payload);
        AndroidReplayPlaybackUi.ClearBoardUi();
        return new AndroidSavedReplayStart(manifest, sequence, outcome, _opponent);
    }

    public ReplayPlaybackCleanupStep[] CleanupSteps(
        SavedReplayCleanupPath path,
        string? battleId
    ) =>
        path == SavedReplayCleanupPath.StartFailed
            ?
            [
                new("opponent_portrait", () => _opponent.Cleanup(battleId)),
                new("hero_restore", _opponent.RestoreSelectedHeroOverride),
            ]
            :
            [
                new("hero_restore", _opponent.RestoreSelectedHeroOverride),
                new("opponent_portrait", () => _opponent.Cleanup(battleId)),
                new("playback_ui", AndroidReplayPlaybackUi.ClearBoardUi),
            ];

    public void ReturnToMainMenu()
    {
        var runManager =
            Services.Get<Il2Cpp.RunManager>()
            ?? throw new InvalidOperationException("RunManager is unavailable.");
        runManager.ReturnToMainMenu();
    }

    public void Notify(in SavedReplayNotice notice)
    {
        switch (notice.Kind)
        {
            case SavedReplayNoticeKind.StartBegun:
                Interlocked.Increment(ref _starts);
                // Read before the start bootstraps: StartBegun precedes the start's first await.
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.replay.start_requested"),
                    ("battle_id", notice.BattleId, BppLogCorrelationPolicy.Short),
                    ("source", notice.Source),
                    ("from_lobby", !AndroidReplayBootstrap.IsBootstrapReady())
                );
                break;
            case SavedReplayNoticeKind.StartFailed:
                Interlocked.Increment(ref _startFailures);
                break;
            case SavedReplayNoticeKind.ExitRequested:
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.replay.exit_requested"),
                    ("decision", notice.ExitOutcome)
                );
                if (notice.ExitOutcome == SavedReplayExitOutcome.Proceed)
                    Interlocked.Increment(ref _exitsHandled);
                break;
            case SavedReplayNoticeKind.MenuReturnStarted:
                Interlocked.Increment(ref _menuReturns);
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.replay.menu_return_started"
                    ),
                    ("battle_id", notice.BattleId, BppLogCorrelationPolicy.Short),
                    ("end_reason_code", notice.EndReason)
                );
                break;
            case SavedReplayNoticeKind.MenuReturnSettled:
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.replay.menu_return_observed"
                    ),
                    ("battle_id", notice.BattleId, BppLogCorrelationPolicy.Short),
                    ("decision", notice.TimedOut ? "complete_timeout" : "complete_confirmed"),
                    ("scene", AndroidLobbyScene.Describe())
                );
                break;
        }
    }

    internal static bool IsInReplayState()
    {
        try
        {
            return AppState.CurrentState?.TryCast<ReplayState>() != null;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class AndroidSavedReplayStart(
    PvpBattleManifest manifest,
    CombatSequenceMessages sequence,
    IReplayPlaybackOutcomeSink outcome,
    AndroidReplayOpponent opponent
) : ISavedReplayStart
{
    private AndroidReplayBootstrapContext? _context;

    public void Prepare()
    {
        opponent.Cleanup(outcome.BattleId);
        opponent.ApplySelectedHeroOverride(manifest);
        Data.ResetRunData();
    }

    public bool NeedsBootstrap => !AndroidReplayBootstrap.IsBootstrapReady();

    public async Task<bool> BootstrapAsync()
    {
        var bootstrappedFromLobby = await AndroidReplayBootstrap.EnsureBootstrapReadyAsync(
            outcome.BattleId
        );
        _context = AndroidReplayBootstrap.ResolveDependencies();
        return bootstrappedFromLobby;
    }

    public async Task PrepareOpponentAsync()
    {
        AndroidReplayOpponent.EnsureOpponentIdentity(manifest, sequence.SpawnMessage, outcome);
        await opponent.EnsureTemporaryPortraitAsync(manifest, outcome);
    }

    public Task InjectAsync(Func<ReplayPlaybackPublishOutcome> publishStarting) =>
        AndroidReplayBootstrap.InjectSavedReplayAsync(
            _context ?? throw new InvalidOperationException("Replay bootstrap did not resolve."),
            manifest,
            sequence,
            outcome,
            publishStarting
        );

    public Task<ReplayBootstrapRollback> RollbackBootstrapAsync() =>
        AndroidReplayBootstrap.RollbackBootstrapAsync(outcome);
}
