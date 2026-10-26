#nullable enable
using System.Net;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Bundle;

internal static class GhostBundleImportTests
{
    internal static async Task RunAsync()
    {
        await ImportAsync(malformedReplay: false);
        await ImportAsync(malformedReplay: true);
        await ImportAsync(malformedReplay: true, rejectedReplay: true);
    }

    // malformedReplay fails projecting the battle; rejectedReplay makes the replay's messages fail
    // the host's verification instead. Either is an import failure.
    private static async Task ImportAsync(bool malformedReplay, bool rejectedReplay = false)
    {
        const string battleId = "valid-battle";
        const string bundleId = "01J00000000000000000000903";
        var replay = new
        {
            Version = 1,
            SpawnMessageBytes = rejectedReplay
                ? new byte[] { FakePvpBattleEvidence.RejectedReplayMarker, 1 }
                : new byte[] { 1, 2, 3 },
            CombatMessageBytes = new byte[] { 4, 5, 6 },
            DespawnMessageBytes = new byte[] { 7, 8, 9 },
        };
        var payload = new RunPayloadV6
        {
            RunId = "valid-run",
            PlayerAccountId = "account-uploader",
            ReplayableBattleIds = [battleId],
            Battles =
            [
                new RunBattleV6
                {
                    BattleId = battleId,
                    Facts = new BattleFactsV6
                    {
                        // Beyond DateTimeOffset's range, so projecting the replay fails.
                        RecordedAtMs =
                            malformedReplay && !rejectedReplay ? long.MaxValue : 1_790_380_800_000,
                        CombatKind = "PVPCombat",
                    },
                    Participants = new BattleParticipantsV6
                    {
                        Player = new BattleParticipantV6 { AccountId = "account-uploader" },
                        Opponent = new BattleParticipantV6 { AccountId = "account-local" },
                    },
                    Snapshots = new BattleCardSnapshotsV6
                    {
                        CardSets =
                        [
                            .. new[]
                            {
                                "player_hand",
                                "player_skills",
                                "opponent_hand",
                                "opponent_skills",
                            }.Select(label => new BattleCardSetV6
                            {
                                Label = label,
                                Status = "Captured",
                            }),
                        ],
                    },
                    Replay = new BattleReplayV6
                    {
                        Version = replay.Version,
                        SpawnMessageBytes = replay.SpawnMessageBytes,
                        CombatMessageBytes = replay.CombatMessageBytes,
                        DespawnMessageBytes = replay.DespawnMessageBytes,
                    },
                },
            ],
        };
        var bundle = BundleV6Codec.Build(
            new BundleBuildInputV6
            {
                BundleId = bundleId,
                RunId = payload.RunId,
                PlayerAccountId = payload.PlayerAccountId,
                CreatedAtMs = 1_790_380_800_000,
                RunPayload = RunPayloadV6Codec.Encode(payload),
                Battles =
                [
                    new BundleBattleProjectionV6
                    {
                        BattleId = battleId,
                        RecordedAtMs = 1_790_380_800_000,
                        CombatKind = "pvp",
                        Result = "unknown",
                        WinnerCombatantId = "Player",
                        LoserCombatantId = "Opponent",
                        Player = new BundleCombatantProjectionV6
                        {
                            AccountId = "account-uploader",
                            DisplayName = "Uploader",
                        },
                        Opponent = new BundleCombatantProjectionV6
                        {
                            AccountId = "account-local",
                            DisplayName = "Local",
                        },
                    },
                ],
            }
        );
        using var fixture = new GhostFixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bundle.Bytes),
        });
        var record = new GhostBattleImportRow
        {
            BattleId = battleId,
            BundleId = bundleId,
            DownloadUrl = "https://r2.example/valid-bundle",
            DownloadExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            RecordedAtUtc = DateTimeOffset.UtcNow,
            PlayerAccountId = "account-uploader",
            OpponentAccountId = "account-local",
            CombatKind = "PVPCombat",
        };
        fixture.Repository.UpsertGhostBattles("account-local", [record]);
        var localId = fixture
            .Repository.ListGhostBattles(
                "account-local",
                HistoryPanelDataService.RecorderOutcome(GhostBattleFilter.All),
                false,
                new()
            )
            .Rows.Single()
            .BattleId;
        var result = await fixture.Service.DownloadReplayAsync(
            localId,
            Path.Combine(fixture.Root, "Replays"),
            CancellationToken.None
        );
        if (malformedReplay)
        {
            if (result.Succeeded || result.Error != "ghost_bundle_import_failed")
                throw new InvalidOperationException(
                    "An unexpected import failure must persist ghost_bundle_import_failed."
                );
            fixture.Repository.UpsertGhostBattles("account-local", [record]);
            var again = await fixture.Service.DownloadReplayAsync(
                localId,
                Path.Combine(fixture.Root, "Replays"),
                CancellationToken.None
            );
            if (
                again.Succeeded
                || fixture.Handler.Count != 1
                || fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState
                    != ReplayAvailability.Unavailable
                || fixture
                    .Repository.ListGhostBattles(
                        "account-local",
                        HistoryPanelDataService.RecorderOutcome(GhostBattleFilter.All),
                        false,
                        new()
                    )
                    .Rows.Single()
                    .Replay != ReplayAvailability.Unavailable
            )
                throw new InvalidOperationException(
                    "A newly failed payload must not be resurrected by repeated discovery."
                );
            return;
        }
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Valid Ghost Bundle import failed: {result.Error}",
                result.Exception
            );
        if (
            fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState
            != ReplayAvailability.Saved
        )
            throw new InvalidOperationException("Valid Ghost Bundle must become local_ready.");
        var cached = await fixture.Service.DownloadReplayAsync(
            localId,
            Path.Combine(fixture.Root, "Replays"),
            CancellationToken.None
        );
        if (!cached.Succeeded || fixture.Handler.Count != 1)
            throw new InvalidOperationException(
                "A saved Ghost replay must load from cache without another download."
            );
        // Playback takes the payload the download confirmed instead of reading the file again.
        if (
            result.Payload?.BattleId != localId
            || cached.Payload?.BattleId != localId
            || cached.Payload.ReplayPayload.CombatMessageBytes.Length == 0
            || ReferenceEquals(result.Payload, cached.Payload)
        )
            throw new InvalidOperationException(
                "A download and a cached load must each hand back the identity-checked payload."
            );
    }
}
