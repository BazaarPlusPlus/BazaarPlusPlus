#nullable enable
using System.Collections.Concurrent;
using System.Reflection;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Models;

internal static class GhostDownloadSelectionTests
{
    public static void Run()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new UiContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            foreach (var finalState in new[] { "local_ready", "expired", "unavailable_payload" })
                Verify(finalState, context);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Verify(string finalState, UiContext context)
    {
        using var fixture = new GhostFixture(_ =>
            throw new InvalidOperationException("No HTTP expected after completion.")
        );
        var repository = fixture.Repository;
        repository.UpsertGhostBattles("account-local", [Import("A"), Import("B")]);
        var replays = Path.Combine(fixture.Root, "Replays");
        var state = new HistoryPanelState
        {
            CachedAccountId = "account-local",
            SectionMode = HistorySectionMode.Ghost,
            GhostPage = repository.ListGhostBattles(
                "account-local",
                GhostBattleFilter.All,
                false,
                new()
            ),
        };
        var a = state.GhostBattles.Single(row =>
            repository.TryGetGhostBundleReference(row.BattleId)!.RemoteBattleId == "A"
        );
        var b = state.GhostBattles.Single(row => row.BattleId != a.BattleId);
        using var coordinator = new HistoryPanelCoordinator(
            state,
            new(null!, new(repository, null, () => replays), null!, null, null, null),
            () => { },
            () => { },
            _ => { }
        );
        coordinator.SelectBattle(state.GhostPage.FindIndex(row => row.BattleId == a.BattleId));
        context.Until(() => !state.DetailLoading);
        state.ReplayActionInProgress = true;
        state.ReplayActionBattleId = a.BattleId;
        coordinator.SelectBattle(state.GhostPage.FindIndex(row => row.BattleId == b.BattleId));
        context.Until(() => !state.DetailLoading);

        // Apply the download's storage commit after the user has moved to another battle.
        if (finalState == "local_ready")
        {
            new GhostBattlePayloadStore(GhostBattlePayloadStore.ResolveDirectory(replays)).Save(
                new()
                {
                    BattleId = a.BattleId,
                    PerspectiveVersion = 1,
                    BattleManifest = new PvpBattleManifest
                    {
                        BattleId = a.BattleId,
                        Participants = new()
                        {
                            PlayerAccountId = "uploader",
                            OpponentAccountId = "account-local",
                        },
                    },
                    ReplayPayload = new PvpReplayPayload
                    {
                        BattleId = a.BattleId,
                        SpawnMessageBytes = [1],
                        CombatMessageBytes = [2],
                    },
                }
            );
            repository.MarkGhostReplayDownloaded(a.BattleId);
        }
        else
            repository.MarkGhostReplayUnavailable(a.BattleId, finalState, "test-failure");
        state.ReplayActionInProgress = false;
        // A successful download also takes this completion path when playback's selection guard rejects it.
        typeof(HistoryPanelCoordinator)
            .GetMethod("SetReplayFailure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(
                coordinator,
                [a, HistoryPanelText.ReplayFailed("History selection is no longer active.")]
            );
        context.Until(() => !state.PageLoading && !state.DetailLoading);
        Require(
            state.GetSelectedGhostBattle(state.GhostBattles)?.BattleId == b.BattleId,
            "Completion must preserve the user's new selection."
        );
        var currentA = state.GhostBattles.Single(row => row.BattleId == a.BattleId);
        Require(
            currentA.GhostReplayState == finalState,
            "Completion must refresh the changed row even while another battle is selected."
        );
        coordinator.SelectBattle(state.GhostPage.FindIndex(row => row.BattleId == a.BattleId));
        context.Until(() => !state.DetailLoading);
        var message = HistoryPanelDecisions.PreviewStatusOverride(state, currentA);
        if (finalState == "local_ready")
            Require(
                state.DetailSnapshots != null && message == null,
                "A downloaded replay must show its native board, not the stale canceled-start message."
            );
        else
            Require(
                message
                    == (
                        finalState == "expired"
                            ? HistoryPanelText.GhostReplayExpired()
                            : HistoryPanelText.GhostReplayPayloadUnavailable()
                    ),
                "A terminal download must expose its stored reason on reselection."
            );
    }

    private static GhostBattleImportRecord Import(string id) =>
        new()
        {
            BattleId = id,
            BundleId = "bundle",
            DownloadUrl = "https://r2.example/bundle",
            DownloadExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            RecordedAtUtc = DateTimeOffset.UtcNow,
            PlayerAccountId = "uploader",
            OpponentAccountId = "account-local",
            CombatKind = "PVPCombat",
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class UiContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue =
            new();

        public override void Post(SendOrPostCallback callback, object? state) =>
            _queue.Enqueue((callback, state));

        internal void Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("History read did not finish.");
                if (_queue.TryDequeue(out var work))
                    work.Callback(work.State);
                else
                    Thread.Sleep(1);
            }
        }
    }
}
