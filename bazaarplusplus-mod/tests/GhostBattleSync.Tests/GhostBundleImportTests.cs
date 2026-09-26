#nullable enable
using System.Net;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Models;

internal static class GhostBundleImportTests
{
    internal static async Task RunAsync()
    {
        await ImportAsync(legacyFailure: false, malformedReplay: false);
        await ImportAsync(legacyFailure: true, malformedReplay: false);
        await ImportAsync(legacyFailure: true, malformedReplay: true);
    }

    private static async Task ImportAsync(bool legacyFailure, bool malformedReplay)
    {
        const string battleId = "valid-battle";
        const string bundleId = "01J00000000000000000000903";
        var replay = new PvpReplayPayloadFactory().Create(
            battleId,
            new PvpBattleSequenceWindow
            {
                SpawnMessage = new NetMessageGameSim(new GameSim()) { MessageId = "spawn" },
                CombatMessage = new NetMessageCombatSim(new CombatSim()) { MessageId = "combat" },
                DespawnMessage = new NetMessageGameSim(new GameSim()) { MessageId = "despawn" },
            }
        );
        var payload = new RunPayloadV5
        {
            RunId = "valid-run",
            PlayerAccountId = "account-uploader",
            ReplayableBattleIds = [battleId],
            Battles =
            [
                new RunBattleV5
                {
                    BattleId = battleId,
                    Facts = new BattleFactsV5
                    {
                        RecordedAtUtc = malformedReplay
                            ? "invalid-timestamp"
                            : "2026-09-26T00:00:00Z",
                        CombatKind = "PVPCombat",
                    },
                    Participants = new BattleParticipantsV5
                    {
                        Player = new BattleParticipantV5 { AccountId = "account-uploader" },
                        Opponent = new BattleParticipantV5 { AccountId = "account-local" },
                    },
                    Snapshots = new BattleCardSnapshotsV5
                    {
                        CardSets =
                        [
                            .. new[]
                            {
                                "player_hand",
                                "player_skills",
                                "opponent_hand",
                                "opponent_skills",
                            }.Select(label => new BattleCardSetV5
                            {
                                Label = label,
                                Status = "Captured",
                            }),
                        ],
                    },
                    Replay = new BattleReplayV5
                    {
                        Version = replay.Version,
                        SpawnMessageBytes = replay.SpawnMessageBytes,
                        CombatMessageBytes = replay.CombatMessageBytes,
                        DespawnMessageBytes = replay.DespawnMessageBytes,
                    },
                },
            ],
        };
        var bundle = BundleV5Codec.Build(
            new BundleBuildInputV5
            {
                BundleId = bundleId,
                RunId = payload.RunId,
                PlayerAccountId = payload.PlayerAccountId,
                CreatedAtMs = 1_790_380_800_000,
                RunPayload = RunPayloadV5Codec.Encode(payload),
                Battles =
                [
                    new BundleBattleProjectionV5
                    {
                        BattleId = battleId,
                        RecordedAtMs = 1_790_380_800_000,
                        CombatKind = "pvp",
                        Result = "unknown",
                        Player = new BundleCombatantProjectionV5
                        {
                            AccountId = "account-uploader",
                            DisplayName = "Uploader",
                        },
                        Opponent = new BundleCombatantProjectionV5
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
        var record = new GhostBattleImportRecord
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
            .Repository.ListGhostBattles("account-local", GhostBattleFilter.All, false, new())
            .Rows.Single()
            .BattleId;
        if (legacyFailure)
        {
            fixture.Repository.MarkGhostReplayUnavailable(
                localId,
                "unavailable_payload",
                "ghost_bundle_invalid"
            );
            fixture.Repository.UpsertGhostBattles("account-local", [record]);
            if (
                !fixture
                    .Repository.ListGhostBattles(
                        "account-local",
                        GhostBattleFilter.All,
                        false,
                        new()
                    )
                    .Rows.Single()
                    .ReplayAvailable
            )
                throw new InvalidOperationException(
                    "Discovery must restore the replay action for legacy compatibility failures."
                );
        }
        var result = await fixture.Service.DownloadReplayAsync(
            localId,
            Path.Combine(fixture.Root, "Replays"),
            CancellationToken.None
        );
        if (malformedReplay)
        {
            if (result.Succeeded || result.Error != "ghost_bundle_import_failed")
                throw new InvalidOperationException(
                    "A fresh unexpected import failure must be distinct from the legacy compatibility failure."
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
                    != "unavailable_payload"
                || fixture
                    .Repository.ListGhostBattles(
                        "account-local",
                        GhostBattleFilter.All,
                        false,
                        new()
                    )
                    .Rows.Single()
                    .ReplayAvailable
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
        if (fixture.Repository.TryGetGhostBundleReference(localId)?.ReplayState != "local_ready")
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
    }
}
