#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleSyncService
{
    private const int MaxSyncBattleLimit = 200;

    private readonly HistoryPanelRepository _repository;
    private readonly ModOnlineClient _onlineClient;

    public GhostBattleSyncService(HistoryPanelRepository repository, ModOnlineClient onlineClient)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        var playerAccountId = ResolvePlayerAccountId();
        if (string.IsNullOrWhiteSpace(playerAccountId))
            return GhostBattleSyncResult.Failure("player_account_id_unavailable");

        var apiClient = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var queryResult = await apiClient.QueryAgainstMeAsync(
            playerAccountId!,
            MaxSyncBattleLimit,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            return GhostBattleSyncResult.Failure(queryResult.Error ?? "ghost_sync_failed");
        }

        _repository.UpsertGhostBattles(playerAccountId!, queryResult.Battles);
        _repository.MarkOldUndownloadedGhostBattlesDeleted(syncStartedAtUtc);
        if (ShouldAdvanceCheckpoint(queryResult.Battles.Count, MaxSyncBattleLimit))
            _repository.SaveGhostSyncCheckpointUtc(playerAccountId!, syncStartedAtUtc);
        return GhostBattleSyncResult.Success(queryResult.Battles.Count);
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return GhostBattleReplayDownloadResult.Failure("battle_id_required");
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return GhostBattleReplayDownloadResult.Failure("replay_directory_required");

        var apiClient = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed"
            );
        }

        var bytesResult = await apiClient.DownloadReplayBytesAsync(
            linkResult.DownloadUrl!,
            cancellationToken
        );
        if (!bytesResult.Succeeded || bytesResult.Bytes == null)
        {
            return GhostBattleReplayDownloadResult.Failure(
                bytesResult.Error ?? "ghost_replay_payload_failed"
            );
        }

        var payload = TryExtractPayloadFromArtifact(battleId, bytesResult.Bytes);
        if (!IsValidGhostBattlePayload(payload))
        {
            return GhostBattleReplayDownloadResult.Failure("replay_payload_missing");
        }

        if (!string.Equals(payload!.ReplayPayload!.BattleId, battleId, StringComparison.Ordinal))
        {
            return GhostBattleReplayDownloadResult.Failure("ghost_replay_battle_id_mismatch");
        }

        var payloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        payloadStore.Save(payload);
        _repository.MarkGhostReplayDownloaded(battleId);
        return GhostBattleReplayDownloadResult.Success();
    }

    private static string? ResolvePlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "GhostBattleSync",
                $"ResolvePlayerAccountId failed: {ex.GetType().Name}: {ex.Message}"
            );
            return null;
        }
    }

    private static bool ShouldAdvanceCheckpoint(int importedCount, int limit)
    {
        return importedCount < limit;
    }

    private static bool IsValidGhostBattlePayload(GhostBattlePayload? payload)
    {
        return payload?.ReplayPayload != null
            && payload.BattleManifest != null
            && !string.IsNullOrWhiteSpace(payload.ReplayPayload.BattleId);
    }

    private static GhostBattlePayload? TryExtractPayloadFromArtifact(
        string battleId,
        byte[] responseBytes
    )
    {
        if (
            string.IsNullOrWhiteSpace(battleId)
            || responseBytes == null
            || responseBytes.Length == 0
        )
            return null;

        try
        {
            if (
                !RunBundleArtifactCodec.TryDeserialize(
                    responseBytes,
                    out var artifact,
                    out var artifactError
                )
                || artifact == null
            )
            {
                BppLog.Warn(
                    "GhostBattleSync",
                    $"Failed to deserialize replay artifact for battle '{battleId}': {artifactError ?? "unknown_error"}"
                );
                return null;
            }

            var battle = artifact.Battles?.FirstOrDefault(candidate =>
                string.Equals(candidate.BattleId, battleId, StringComparison.Ordinal)
            );
            if (battle == null)
                return null;

            if (battle.ReplayPayload == null)
                return null;

            var replayPayload = new PvpReplayPayload
            {
                BattleId = battle.ReplayPayload.BattleId,
                Version = battle.ReplayPayload.Version,
                SpawnMessageBytes = battle.ReplayPayload.SpawnMessageBytes?.ToArray() ?? [],
                CombatMessageBytes = battle.ReplayPayload.CombatMessageBytes?.ToArray() ?? [],
                DespawnMessageBytes = battle.ReplayPayload.DespawnMessageBytes?.ToArray() ?? [],
            };
            if (
                string.IsNullOrWhiteSpace(replayPayload.BattleId)
                || replayPayload.SpawnMessageBytes.Length == 0
                || replayPayload.CombatMessageBytes.Length == 0
                || replayPayload.DespawnMessageBytes.Length == 0
            )
                return null;

            var battleManifest = BuildBattleManifest(artifact, battleId, battle);
            if (battleManifest == null)
                return null;

            return new GhostBattlePayload
            {
                BattleId = battleId,
                BattleManifest = battleManifest,
                ReplayPayload = replayPayload,
            };
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "GhostBattleSync",
                $"Failed to extract replay artifact for battle '{battleId}': {ex.Message}"
            );
            return null;
        }
    }

    private static PvpBattleManifest? BuildBattleManifest(
        RunArtifact artifact,
        string battleId,
        RunArtifactBattle battle
    )
    {
        if (battle.Manifest == null || battle.Participants == null || battle.Snapshots == null)
            return null;

        return new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = artifact.RunId,
            RecordedAtUtc = DateTimeOffset.Parse(battle.Manifest.RecordedAtUtc),
            CombatKind = battle.Manifest.CombatKind,
            Day = battle.Manifest.Day,
            Hour = battle.Manifest.Hour,
            EncounterId = battle.Manifest.EncounterId,
            Participants = new PvpBattleParticipants
            {
                PlayerName = battle.Participants.PlayerName,
                PlayerAccountId = battle.Participants.PlayerAccountId,
                PlayerHero = battle.Participants.PlayerHero,
                PlayerRank = battle.Participants.PlayerRank,
                PlayerRating = battle.Participants.PlayerRating,
                PlayerLevel = battle.Participants.PlayerLevel,
                PlayerPrestige = battle.Participants.PlayerPrestige,
                PlayerVictories = battle.Participants.PlayerVictories,
                OpponentName = battle.Participants.OpponentName,
                OpponentAccountId = battle.Participants.OpponentAccountId,
                OpponentHero = battle.Participants.OpponentHero,
                OpponentRank = battle.Participants.OpponentRank,
                OpponentRating = battle.Participants.OpponentRating,
                OpponentLevel = battle.Participants.OpponentLevel,
                OpponentPrestige = battle.Participants.OpponentPrestige,
                OpponentVictories = battle.Participants.OpponentVictories,
            },
            Outcome = new PvpBattleOutcome
            {
                Result = battle.Manifest.Result,
                WinnerCombatantId = battle.Manifest.WinnerCombatantId,
                LoserCombatantId = battle.Manifest.LoserCombatantId,
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = BuildCapture(battle.Snapshots, "player_hand"),
                PlayerSkills = BuildCapture(battle.Snapshots, "player_skills"),
                OpponentHand = BuildCapture(battle.Snapshots, "opponent_hand"),
                OpponentSkills = BuildCapture(battle.Snapshots, "opponent_skills"),
            },
        };
    }

    private static PvpBattleCardSetCapture BuildCapture(
        BattleSnapshotsArtifact snapshots,
        string label
    )
    {
        var capture = snapshots.CardSets?.FirstOrDefault(cardSet =>
            string.Equals(cardSet.Label, label, StringComparison.Ordinal)
        );

        return new PvpBattleCardSetCapture
        {
            Status = ParseEnum(capture?.Status, PvpBattleCaptureStatus.Missing),
            Source = ParseEnum(capture?.Source, PvpBattleCaptureSource.Unknown),
            Items =
                capture?.Items?.Select(MapToCardSnapshot).ToList()
                ?? new List<PvpBattleCardSnapshot>(),
        };
    }

    private static PvpBattleCardSnapshot MapToCardSnapshot(CardSetItemArtifact item)
    {
        return new PvpBattleCardSnapshot
        {
            InstanceId = item.InstanceId,
            TemplateId = item.TemplateId,
            Type = (BazaarGameShared.Domain.Core.Types.ECardType)item.Type,
            Size = (BazaarGameShared.Domain.Core.Types.ECardSize)item.Size,
            Section = item.Section.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EInventorySection?)item.Section.Value
                : null,
            Socket = item.Socket.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EContainerSocketId?)item.Socket.Value
                : null,
            Name = item.Name,
            Tier = item.Tier,
            Enchant = item.Enchant,
            Tags = new List<string>(item.Tags ?? new List<string>()),
            Attributes = new Dictionary<string, int>(
                item.Attributes ?? new Dictionary<string, int>()
            ),
        };
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return
            !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            ? parsed
            : fallback;
    }
}

internal readonly struct GhostBattleSyncResult
{
    private GhostBattleSyncResult(bool succeeded, int importedCount, string? error)
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
    }

    public bool Succeeded { get; }

    public int ImportedCount { get; }

    public string? Error { get; }

    public static GhostBattleSyncResult Success(int importedCount) =>
        new(true, importedCount, null);

    public static GhostBattleSyncResult Failure(string error) => new(false, 0, error);
}

internal readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static GhostBattleReplayDownloadResult Success() => new(true, null);

    public static GhostBattleReplayDownloadResult Failure(string error) => new(false, error);
}
