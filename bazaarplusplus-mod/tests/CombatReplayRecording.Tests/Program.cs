#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;

ReplayPersistenceStateIsScopedPerRun();
ReplayPayloadStoreRoundTripsAndRejectsCorruption();
CapturedReplayRoutesSeparatePveFromPvpPersistence();
CurrentNativeReplayBecomesReadyWithoutPersistence();
OrdinaryManagedReplayKeepsRecordingButtonVisible();
RecordedManagedReplayTakesDisplayPriority();
RecordingRestartStatusIsLocalized();
StartFailureFeedbackSurvivesRefreshAndClearsAtItsBoundaries();
PvpBattleStoreRejectsPveManifests();

Console.WriteLine("Combat replay recording tests passed.");

static void ReplayPersistenceStateIsScopedPerRun()
{
    ReplayPersistenceStateTracker.Enqueued("run-a");
    ReplayPersistenceStateTracker.Enqueued("run-a");
    ReplayPersistenceStateTracker.Enqueued("run-b");
    Assert(ReplayPersistenceStateTracker.HasPending("run-a"), "run-a should be pending.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b should be pending.");

    ReplayPersistenceStateTracker.Completed("run-a");
    Assert(ReplayPersistenceStateTracker.HasPending("run-a"), "one run-a write remains.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b must be independent.");
    ReplayPersistenceStateTracker.Completed("run-a");
    Assert(!ReplayPersistenceStateTracker.HasPending("run-a"), "run-a should drain.");
    Assert(ReplayPersistenceStateTracker.HasPending("run-b"), "run-b must remain pending.");
    ReplayPersistenceStateTracker.Completed("run-b");
    Assert(!ReplayPersistenceStateTracker.HasPending("run-b"), "run-b should drain.");
}

static void ReplayPayloadStoreRoundTripsAndRejectsCorruption()
{
    var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-v5-{Guid.NewGuid():N}");
    try
    {
        var store = new CombatReplayPayloadStore(root);
        var payload = new PvpReplayPayload
        {
            BattleId = "battle-one",
            Version = 1,
            SpawnMessageBytes = [1, 2],
            CombatMessageBytes = [3, 4],
            DespawnMessageBytes = [5, 6],
        };
        store.Save(payload);
        var loaded = store.LoadDetailed(payload.BattleId);
        Assert(loaded.Status == FileBackedPayloadLoadStatus.Loaded, "Saved replay must load.");
        Assert(
            loaded.Payload?.CombatMessageBytes.SequenceEqual(payload.CombatMessageBytes) == true,
            "Replay bytes must round-trip exactly."
        );

        var path = Directory.EnumerateFiles(root).Single();
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        var corrupt = store.LoadDetailed(payload.BattleId);
        Assert(
            corrupt.Status == FileBackedPayloadLoadStatus.Invalid,
            "Corrupt replay files must be classified as invalid."
        );
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

static void CapturedReplayRoutesSeparatePveFromPvpPersistence()
{
    Assert(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" }
        ) == CapturedReplayRoute.CurrentNative,
        "PvE captures must stay on the in-memory current-native route."
    );
    Assert(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pvp", CombatKind = "PVPCombat" }
        ) == CapturedReplayRoute.PersistedPvp,
        "PvP captures must use the persisted catalog route."
    );
}

static void CurrentNativeReplayBecomesReadyWithoutPersistence()
{
    var state = new CurrentReplayRecordingState();
    state.LatchBattle("pve");
    state.EnterReplayState();
    state.SetAvailability(ready: true, reason: null);
    Assert(!state.Snapshot().CanStart, "An unrouted capture must not be recordable.");

    state.MarkCurrentNativeReady("pve");
    var snapshot = state.Snapshot();
    Assert(snapshot.CanStart, "A routed current-native capture should be recordable.");
    Assert(
        snapshot.Phase == CurrentReplayRecordingPhase.Ready,
        "The current-native route should transition directly to ready."
    );
}

static void OrdinaryManagedReplayKeepsRecordingButtonVisible()
{
    var managed = ReplayRecordingButtonSnapshotPolicy.OrdinaryManagedReplay(
        "ordinary-replay",
        recorderReady: true,
        replayReady: true,
        CurrentReplayRecordingStatusCode.None,
        unavailableReason: null
    );
    var currentNative = default(CurrentReplayRecordingSnapshot);

    var displayed = ReplayRecordingButtonSnapshotPolicy.Resolve(managed, currentNative);

    Assert(displayed.Visible, "An ordinary managed replay should keep the record button visible.");
    Assert(
        displayed.BattleId == "ordinary-replay",
        "The displayed snapshot should retain the active replay identity."
    );
    Assert(displayed.CanStart, "A completed ordinary replay should be recordable from the start.");
    Assert(
        displayed.Phase == CurrentReplayRecordingPhase.Ready,
        "A completed ordinary replay should expose the ready recording action."
    );

    var replaying = ReplayRecordingButtonSnapshotPolicy.OrdinaryManagedReplay(
        "ordinary-replay",
        recorderReady: true,
        replayReady: false,
        CurrentReplayRecordingStatusCode.ReplayInProgress,
        unavailableReason: null
    );
    Assert(replaying.Visible, "The record button should remain visible during playback.");
    Assert(!replaying.CanStart, "Recording must start from the beginning, not mid-replay.");
    Assert(
        replaying.Phase == CurrentReplayRecordingPhase.Preparing,
        "An in-progress replay should keep the action pending until it finishes."
    );
}

static void RecordedManagedReplayTakesDisplayPriority()
{
    var managed = new CurrentReplayRecordingSnapshot(
        CurrentReplayRecordingPhase.Recording,
        "managed-recording",
        "recording-id",
        null,
        null,
        Visible: true,
        CanStart: false,
        CanReveal: false
    );
    var currentNative = new CurrentReplayRecordingSnapshot(
        CurrentReplayRecordingPhase.Ready,
        "current-native",
        null,
        null,
        null,
        Visible: true,
        CanStart: true,
        CanReveal: false
    );

    var displayed = ReplayRecordingButtonSnapshotPolicy.Resolve(managed, currentNative);

    Assert(
        displayed.BattleId == "managed-recording",
        "The active managed replay should own the recording button state."
    );
    Assert(
        displayed.Phase == CurrentReplayRecordingPhase.Recording,
        "Recorded managed replay progress must not be hidden by current-native state."
    );
}

static void RecordingRestartStatusIsLocalized()
{
    var snapshot = new CurrentReplayRecordingSnapshot(
        CurrentReplayRecordingPhase.Preparing,
        "ordinary-replay",
        RecordingId: null,
        FinalFilePath: null,
        Reason: "raw diagnostic must not reach the tooltip",
        Visible: true,
        CanStart: false,
        CanReveal: false,
        StatusCode: CurrentReplayRecordingStatusCode.ReplayInProgress
    );

    var english = CurrentReplayRecordingText.Tooltip(
        snapshot,
        languageCode: "en",
        traditionalChinese: false
    );
    Assert(
        english.Contains("Finish the current replay", StringComparison.Ordinal),
        "English should resolve the typed replay blocker."
    );
    Assert(
        !english.Contains("raw diagnostic", StringComparison.Ordinal),
        "Raw diagnostic reasons must not be rendered."
    );

    var simplified = CurrentReplayRecordingText.Tooltip(
        snapshot,
        languageCode: "zh-CN",
        traditionalChinese: false
    );
    Assert(
        simplified.Contains("请先完成当前回放", StringComparison.Ordinal),
        "Simplified Chinese should resolve the typed replay blocker."
    );
    Assert(
        !simplified.Contains("Finish the current replay", StringComparison.Ordinal),
        "Simplified Chinese must not fall through to the English blocker."
    );

    var traditional = CurrentReplayRecordingText.Tooltip(
        snapshot,
        languageCode: "zh-CN",
        traditionalChinese: true
    );
    Assert(
        traditional.Contains("請先完成目前重播", StringComparison.Ordinal),
        "Traditional Chinese should resolve the typed replay blocker."
    );
}

static void StartFailureFeedbackSurvivesRefreshAndClearsAtItsBoundaries()
{
    var feedback = new CurrentReplayRecordingStartFailureFeedback();
    var ready = new CurrentReplayRecordingSnapshot(
        CurrentReplayRecordingPhase.Ready,
        "ordinary-replay",
        RecordingId: null,
        FinalFilePath: null,
        Reason: null,
        Visible: true,
        CanStart: true,
        CanReveal: false
    );

    feedback.ReportFailure(CurrentReplayRecordingStatusCode.NativeStartRejected, ready);
    Assert(
        feedback.Observe(ready) == CurrentReplayRecordingStatusCode.NativeStartRejected,
        "Immediate OnClicked-to-Refresh feedback must retain the typed start failure."
    );
    Assert(
        feedback.Observe(ready with { Reason = "new raw diagnostic" })
            == CurrentReplayRecordingStatusCode.NativeStartRejected,
        "A diagnostic-only change must not clear user-visible start feedback."
    );

    var preparing = ready with
    {
        Phase = CurrentReplayRecordingPhase.Preparing,
        CanStart = false,
        StatusCode = CurrentReplayRecordingStatusCode.StorageMoving,
    };
    Assert(
        feedback.Observe(preparing) == null,
        "The next meaningful recording state must clear stale start feedback."
    );

    feedback.ReportFailure(CurrentReplayRecordingStatusCode.NativeInvokeFailed, ready);
    feedback.Clear();
    Assert(
        feedback.Observe(ready) == null,
        "A successful retry must explicitly clear the prior start failure."
    );
}

static void PvpBattleStoreRejectsPveManifests()
{
    var path = Path.Combine(Path.GetTempPath(), $"bpp-pvp-route-{Guid.NewGuid():N}.db");
    var store = new PvpBattleSqliteStore(path);
    try
    {
        store.Save(new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" });
        throw new InvalidOperationException("The PvP store accepted a PvE manifest.");
    }
    catch (ArgumentException)
    {
        // Expected: routing regressions must fail instead of reporting false persistence success.
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
