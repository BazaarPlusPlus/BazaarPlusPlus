#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using BazaarPlusPlus.Localization;

// History archive status is a table: observed facts in, list/board/page-label copy out. Every
// expectation is the HistoryPanelText entry itself, resolved under each installed language.
internal static class GhostMessageTests
{
    private const string FailureDetail = "network failure with retry guidance";

    private static readonly HistoryArchiveFacts Ghost = new(
        Section: HistorySectionMode.Ghost,
        AccountKnown: true,
        PageLoading: false,
        PageLoadFailed: false,
        GhostSync: GhostSyncPhase.Completed,
        FiltersActive: false,
        RowCount: 0,
        SelectedGhostReplay: null,
        Detail: HistoryDetailPhase.NotSelectedBattle,
        DetailFailed: false,
        PlayerBoard: default,
        OpponentBoard: default
    );

    private static readonly HistoryArchiveFacts GhostRow = Ghost with
    {
        RowCount = 1,
        Detail = HistoryDetailPhase.Loaded,
    };

    private static readonly HistoryArchiveFacts Runs = Ghost with
    {
        Section = HistorySectionMode.Runs,
        RowCount = 1,
    };

    private static HistorySelectedGhostReplay Replay(
        ReplayAvailability availability,
        bool inProgress = false,
        string? failure = null
    ) => new(availability, inProgress, failure);

    private static HistoryBoardFacts Board(NativeMonsterBoardStatus status, int partial = 1) =>
        new(status, partial);

    private sealed record Row(
        string Name,
        HistoryArchiveFacts Facts,
        Func<string> List,
        Func<string> Board,
        HistoryPageLabelMode Label = HistoryPageLabelMode.Totals,
        Func<string>? Opponent = null
    );

    private static string None() => string.Empty;

    private static readonly Row[] Table =
    [
        // List precedence: account → rows → read → sync running → filters → sync failed → empty.
        new(
            "Unknown account wins over a pending read and hides the page totals.",
            Ghost with
            {
                AccountKnown = false,
                PageLoading = true,
                GhostSync = GhostSyncPhase.Running,
            },
            HistoryPanelText.GhostAccountUnavailable,
            HistoryPanelText.GhostAccountUnavailable,
            HistoryPageLabelMode.Hidden
        ),
        new(
            "Unknown account with rows still explains account filtering.",
            GhostRow with
            {
                AccountKnown = false,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
            },
            HistoryPanelText.GhostAccountUnavailable,
            HistoryPanelText.GhostAccountUnavailable,
            HistoryPageLabelMode.Hidden
        ),
        new(
            "Rows hide the list message even while a sync fails.",
            GhostRow with
            {
                GhostSync = GhostSyncPhase.Failed,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
            },
            None,
            None
        ),
        new(
            "A pending read does not claim the archive is empty.",
            Ghost with
            {
                PageLoading = true,
                PageLoadFailed = true,
                GhostSync = GhostSyncPhase.Failed,
            },
            HistoryPanelText.LoadingPreview,
            HistoryPanelText.LoadingPreview,
            HistoryPageLabelMode.Loading
        ),
        new(
            "A failed read offers recovery.",
            Ghost with
            {
                PageLoadFailed = true,
                GhostSync = GhostSyncPhase.Running,
            },
            HistoryPanelText.GhostHistoryReadFailed,
            HistoryPanelText.GhostHistoryReadFailed
        ),
        new(
            "A running sync is distinct from an empty result.",
            Ghost with
            {
                GhostSync = GhostSyncPhase.Running,
                FiltersActive = true,
            },
            HistoryPanelText.SyncingGhostBattles,
            HistoryPanelText.SyncingGhostBattles
        ),
        new(
            "A filter-emptied page says filters even after a failed sync.",
            Ghost with
            {
                GhostSync = GhostSyncPhase.Failed,
                FiltersActive = true,
            },
            HistoryPanelText.NoGhostFilterMatches,
            HistoryPanelText.NoGhostFilterMatches
        ),
        new(
            "Zero rows after a failed sync must not claim nothing is saved.",
            Ghost with
            {
                GhostSync = GhostSyncPhase.Failed,
            },
            HistoryPanelText.GhostSyncIncomplete,
            HistoryPanelText.GhostSyncIncomplete
        ),
        new(
            "A completed sync with zero rows explains the discovery window.",
            Ghost,
            HistoryPanelText.NoSavedGhostBattles,
            HistoryPanelText.NoSavedGhostBattles
        ),
        new(
            "No sync attempt with zero rows is an empty archive.",
            Ghost with
            {
                GhostSync = GhostSyncPhase.NotStarted,
            },
            HistoryPanelText.NoSavedGhostBattles,
            HistoryPanelText.NoSavedGhostBattles
        ),
        // Selected Ghost battle preview.
        new(
            "A pending detail read shows loading before any replay state.",
            GhostRow with
            {
                Detail = HistoryDetailPhase.Loading,
                DetailFailed = true,
                SelectedGhostReplay = Replay(ReplayAvailability.Expired),
            },
            None,
            HistoryPanelText.LoadingPreview
        ),
        new(
            "This battle's download shows progress.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Remote, inProgress: true),
            },
            None,
            HistoryPanelText.DownloadingGhostReplay
        ),
        new(
            "This battle's saved replay start shows progress.",
            GhostRow with
            {
                DetailFailed = true,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved, inProgress: true),
            },
            None,
            HistoryPanelText.StartingReplay
        ),
        new(
            "Expiry supersedes a transient failure.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Expired, failure: FailureDetail),
            },
            None,
            HistoryPanelText.GhostReplayExpired
        ),
        new(
            "Unavailable payloads are specific.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(
                    ReplayAvailability.Unavailable,
                    failure: FailureDetail
                ),
            },
            None,
            HistoryPanelText.GhostReplayPayloadUnavailable
        ),
        new(
            "A remote replay keeps this battle's failure detail.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Remote, failure: FailureDetail),
            },
            None,
            () => FailureDetail
        ),
        new(
            "An undownloaded board invites download.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Remote),
            },
            None,
            HistoryPanelText.GhostReplayDownloadRequired
        ),
        new(
            "Re-fetching a missing saved replay exposes its download failure.",
            GhostRow with
            {
                Detail = HistoryDetailPhase.Missing,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved, failure: FailureDetail),
            },
            None,
            () => FailureDetail
        ),
        new(
            "A missing local payload offers a recovery action.",
            GhostRow with
            {
                Detail = HistoryDetailPhase.Missing,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
            },
            None,
            HistoryPanelText.GhostLocalReplayUnreadable
        ),
        new(
            "Unreadable local data does not blame the native renderer.",
            GhostRow with
            {
                DetailFailed = true,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
            },
            None,
            HistoryPanelText.GhostLocalReplayUnreadable
        ),
        new(
            "A detail read for another battle is not a missing payload.",
            GhostRow with
            {
                Detail = HistoryDetailPhase.NotSelectedBattle,
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
                PlayerBoard = Board(NativeMonsterBoardStatus.Empty),
            },
            None,
            HistoryPanelText.NoLocallyRenderableCards,
            Opponent: None
        ),
        new(
            "Saved boards, including genuine empty captures, keep native rendering results.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
                PlayerBoard = Board(NativeMonsterBoardStatus.Partial, 3),
            },
            None,
            () => HistoryPanelText.PartialPreview(3),
            Opponent: None
        ),
        new(
            "A board that has reported nothing yet shows no message.",
            GhostRow with
            {
                SelectedGhostReplay = Replay(ReplayAvailability.Saved),
            },
            None,
            None
        ),
        // Runs.
        new(
            "Run lists never receive Ghost guidance or hide totals without an account.",
            Runs with
            {
                AccountKnown = false,
                RowCount = 0,
                GhostSync = GhostSyncPhase.Failed,
            },
            None,
            None
        ),
        new(
            "A pending Runs page shows loading in its label.",
            Runs with
            {
                PageLoading = true,
            },
            None,
            None,
            HistoryPageLabelMode.Loading
        ),
        new(
            "A pending Runs detail read shows loading on both boards.",
            Runs with
            {
                Detail = HistoryDetailPhase.Loading,
                DetailFailed = true,
            },
            None,
            HistoryPanelText.LoadingPreview,
            Opponent: HistoryPanelText.LoadingPreview
        ),
        new(
            "Local-run detail failures keep their renderer message on both boards.",
            Runs with
            {
                DetailFailed = true,
            },
            None,
            HistoryPanelText.PreviewRendererInitFailed,
            Opponent: HistoryPanelText.PreviewRendererInitFailed
        ),
        new(
            "Each Runs board keeps its own native status.",
            Runs with
            {
                PlayerBoard = Board(NativeMonsterBoardStatus.Failed),
                OpponentBoard = Board(NativeMonsterBoardStatus.Loading),
            },
            None,
            HistoryPanelText.PreviewRendererInitFailed,
            Opponent: HistoryPanelText.LoadingPreview
        ),
        new(
            "Ready boards show no message.",
            Runs with
            {
                PlayerBoard = Board(NativeMonsterBoardStatus.Ready),
                OpponentBoard = Board(NativeMonsterBoardStatus.Ready),
            },
            None,
            None
        ),
    ];

    public static void Run()
    {
        try
        {
            foreach (
                var (language, mode) in new[]
                {
                    ("en", BppChineseLocaleMode.Mainland),
                    ("zh-Hans", BppChineseLocaleMode.Mainland),
                    ("zh-Hans", BppChineseLocaleMode.Taiwan),
                }
            )
            {
                L.Install(new Language(language), new LocaleMode(mode));
                foreach (var row in Table)
                {
                    var status = HistoryPanelDecisions.ArchiveStatus(row.Facts);
                    var where = $"[{language}/{mode}] {row.Name}";
                    Equal(status.ListMessage, row.List(), where + " (list)");
                    Equal(status.PlayerBoardMessage, row.Board(), where + " (player board)");
                    Equal(
                        status.OpponentBoardMessage,
                        (row.Opponent ?? row.Board)(),
                        where + " (opponent board)"
                    );
                    Equal(status.PageLabel, row.Label, where + " (label)");
                }
            }

            L.Install(new Language("zh-Hans"), new LocaleMode(BppChineseLocaleMode.Taiwan));
            Equal(
                HistoryPanelText.GhostSyncIncomplete(),
                "幽靈同步失敗，列表可能不完整。請重新打開歷史記錄後重試。",
                "Traditional Chinese derives the incomplete-sync copy from the Mainland text."
            );
            L.Install(new Language("en"), new LocaleMode(BppChineseLocaleMode.Mainland));
            ObserveFiltersToTheSelectedBattle();
            DownloadFailureMessages();
        }
        finally
        {
            L.Install(new Language("en"), new LocaleMode(BppChineseLocaleMode.Mainland));
        }
    }

    private static void ObserveFiltersToTheSelectedBattle()
    {
        var state = new HistoryPanelState
        {
            SectionMode = HistorySectionMode.Ghost,
            CachedAccountId = "local-account",
            GhostSync = GhostSyncPhase.Failed,
            GhostDayMin10 = true,
            ReplayActionInProgress = true,
            ReplayActionBattleId = "other-battle",
            ReplayFailureMessage = FailureDetail,
            DetailBattleId = "other-battle",
        };
        var battle = Battle(ReplayAvailability.Remote);
        var anchor = new HistoryCursor("2026-09-20T00:00:00Z", battle.BattleId);
        state.GhostPage = new HistoryCountedPage<HistoryBattleRecord>(
            [battle],
            anchor,
            anchor,
            1,
            1
        );
        var player = Board(NativeMonsterBoardStatus.Partial, 2);
        var facts = HistoryArchiveFacts.Observe(state, battle, player, default);
        Equal(
            facts,
            Ghost with
            {
                GhostSync = GhostSyncPhase.Failed,
                FiltersActive = true,
                RowCount = 1,
                SelectedGhostReplay = Replay(ReplayAvailability.Remote),
                PlayerBoard = player,
            },
            "Another battle's replay action and detail read must not reach this battle's facts."
        );

        state.ReplayActionBattleId = "battle";
        state.DetailBattleId = "battle";
        state.GhostSync = GhostSyncPhase.NotStarted;
        state.GhostDayMin10 = false;
        state.GhostBattleFilter = GhostBattleFilter.ILost;
        Equal(
            HistoryArchiveFacts.Observe(state, battle, default, default),
            Ghost with
            {
                GhostSync = GhostSyncPhase.NotStarted,
                FiltersActive = true,
                RowCount = 1,
                SelectedGhostReplay = Replay(
                    ReplayAvailability.Remote,
                    inProgress: true,
                    failure: FailureDetail
                ),
                Detail = HistoryDetailPhase.Missing,
            },
            "This battle's replay action, failure, and missing snapshots are its own facts."
        );

        state.DetailSnapshots = new PvpBattleSnapshots();
        state.DetailFailed = true;
        state.CachedAccountId = " ";
        Equal(
            HistoryArchiveFacts.Observe(state, battle, default, default).Detail,
            HistoryDetailPhase.Loaded,
            "Loaded snapshots for the selected battle are Loaded."
        );
        Equal(
            HistoryArchiveFacts.Observe(state, battle, default, default).AccountKnown,
            false,
            "A blank cached account is unknown."
        );
        state.DetailLoading = true;
        Equal(
            HistoryArchiveFacts.Observe(state, null, default, default),
            Ghost with
            {
                AccountKnown = false,
                GhostSync = GhostSyncPhase.NotStarted,
                FiltersActive = true,
                RowCount = 1,
                Detail = HistoryDetailPhase.Loading,
                DetailFailed = true,
            },
            "A pending detail read is global, as is a failed one."
        );

        state.DetailLoading = false;
        Equal(
            HistoryArchiveFacts
                .Observe(
                    state,
                    Battle(ReplayAvailability.Saved, HistoryBattleSource.Local),
                    default,
                    default
                )
                .SelectedGhostReplay,
            null,
            "A non-Ghost battle carries no Ghost replay facts."
        );
    }

    private static void DownloadFailureMessages()
    {
        Equal(
            HistoryPanelDecisions.GhostDownloadFailureMessage(
                ReplayAvailability.Expired,
                "ghost_replay_expired",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            ),
            HistoryPanelText.GhostReplayExpired(),
            "Expired downloads must not be generic errors."
        );
        foreach (
            var reason in new[]
            {
                HistoryPanelReplayReasonCode.GhostArtifactInvalid,
                HistoryPanelReplayReasonCode.GhostBattleMismatch,
            }
        )
            Equal(
                HistoryPanelDecisions.GhostDownloadFailureMessage(
                    null,
                    "ghost_bundle_invalid",
                    reason
                ),
                HistoryPanelText.GhostReplayPayloadUnavailable(),
                "Invalid downloaded data must explain unavailability."
            );
        Equal(
            HistoryPanelDecisions.GhostDownloadFailureMessage(
                ReplayAvailability.Unavailable,
                "ghost_replay_unavailable_payload",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            ),
            HistoryPanelText.GhostReplayPayloadUnavailable(),
            "Persisted unavailable state must remain specific."
        );
        Equal(
            HistoryPanelDecisions.GhostDownloadFailureMessage(
                null,
                "network_unavailable",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            ),
            HistoryPanelText.FailedToDownloadGhostReplay("network_unavailable"),
            "Transient failures must retain their diagnostic code and retry guidance."
        );
    }

    private static HistoryBattleRecord Battle(
        ReplayAvailability replay,
        HistoryBattleSource source = HistoryBattleSource.Ghost
    ) =>
        new(
            "battle",
            "run",
            DateTimeOffset.UtcNow.AddDays(-40),
            1,
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new HistoryBattleSnapshotCounts(),
            false,
            source,
            replay
        );

    private static void Equal<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException(
                $"{message} Expected '{expected}', got '{actual}'."
            );
    }

    private sealed class Language(string code) : ILanguageProvider
    {
        public string CurrentLanguageCode => code;
    }

    private sealed class LocaleMode(BppChineseLocaleMode mode) : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => mode;
    }
}
