#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
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
            return GhostBattleSyncResult.Failure(
                "player_account_id_unavailable",
                HistoryPanelGhostSyncReasonCode.IdentityUnavailable
            );

        var apiClient = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var syncStartedAtUtc = DateTimeOffset.UtcNow;
        var queryResult = await apiClient.QueryAgainstMeAsync(
            playerAccountId!,
            MaxSyncBattleLimit,
            cancellationToken
        );
        if (!queryResult.Succeeded)
        {
            return GhostBattleSyncResult.Failure(
                queryResult.Error ?? "ghost_sync_failed",
                HistoryPanelGhostSyncReasonCode.QueryFailed
            );
        }

        try
        {
            _repository.UpsertGhostBattles(playerAccountId!, queryResult.Battles);
            _repository.MarkOldUndownloadedGhostBattlesDeleted(syncStartedAtUtc);
            if (ShouldAdvanceCheckpoint(queryResult.Battles.Count, MaxSyncBattleLimit))
                _repository.SaveGhostSyncCheckpointUtc(playerAccountId!, syncStartedAtUtc);
        }
        catch (Exception ex)
        {
            return GhostBattleSyncResult.Failure(
                "ghost_sync_repository_failed",
                HistoryPanelGhostSyncReasonCode.RepositoryFailed,
                ex
            );
        }
        return GhostBattleSyncResult.Success(queryResult.Battles.Count);
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return GhostBattleReplayDownloadResult.Failure(
                "battle_id_required",
                HistoryPanelReplayReasonCode.ReplayUnavailable
            );
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return GhostBattleReplayDownloadResult.Failure(
                "replay_directory_required",
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        var apiClient = new GhostBattleClient(_onlineClient.HttpClient, _onlineClient.Routes);
        var linkResult = await apiClient.RequestReplayDownloadLinkAsync(
            battleId,
            cancellationToken
        );
        if (!linkResult.Succeeded)
        {
            return GhostBattleReplayDownloadResult.Failure(
                linkResult.Error ?? "ghost_replay_link_failed",
                HistoryPanelReplayReasonCode.GhostDownloadLinkFailed
            );
        }

        var bytesResult = await apiClient.DownloadReplayBytesAsync(
            linkResult.DownloadUrl!,
            cancellationToken
        );
        if (!bytesResult.Succeeded || bytesResult.Bytes == null)
        {
            return GhostBattleReplayDownloadResult.Failure(
                bytesResult.Error ?? "ghost_replay_payload_failed",
                HistoryPanelReplayReasonCode.GhostDownloadFailed
            );
        }

        var extraction = ExtractPayloadFromArtifact(battleId, bytesResult.Bytes);
        var payload = extraction.Payload;
        if (!extraction.Succeeded || !IsValidGhostBattlePayload(payload))
        {
            return GhostBattleReplayDownloadResult.Failure(
                "replay_payload_missing",
                extraction.ReasonCode,
                extraction.Exception
            );
        }

        if (!string.Equals(payload!.ReplayPayload!.BattleId, battleId, StringComparison.Ordinal))
        {
            return GhostBattleReplayDownloadResult.Failure(
                "ghost_replay_battle_id_mismatch",
                HistoryPanelReplayReasonCode.GhostBattleMismatch
            );
        }

        var payloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        payloadStore.Save(payload);
        _repository.MarkGhostReplayDownloaded(
            battleId,
            HistoryBattlePreviewProjection.CountSnapshots(
                payload.BattleManifest!.Snapshots.PlayerHand,
                payload.BattleManifest.Snapshots.PlayerSkills,
                payload.BattleManifest.Snapshots.OpponentHand,
                payload.BattleManifest.Snapshots.OpponentSkills
            )
        );
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
            BppLog.DebugEvent(
                HistoryPanelLogEvents.GhostIdentityReadFailed,
                ex,
                () =>
                    [
                        HistoryPanelLogEvents.GhostIdentityReasonCode.Bind(
                            HistoryPanelGhostIdentityReasonCode.ClientCacheReadFailed
                        ),
                    ]
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

    // Compatibility seam retained for the existing artifact fidelity tests. The replay workflow
    // consumes the typed result below so a malformed artifact reaches its single request owner.
    private static GhostBattlePayload? TryExtractPayloadFromArtifact(
        string battleId,
        byte[] responseBytes
    ) => ExtractPayloadFromArtifact(battleId, responseBytes).Payload;

    private static GhostBattleArtifactExtractionResult ExtractPayloadFromArtifact(
        string battleId,
        byte[] responseBytes
    )
    {
        if (
            string.IsNullOrWhiteSpace(battleId)
            || responseBytes == null
            || responseBytes.Length == 0
        )
            return GhostBattleArtifactExtractionResult.Failure(
                HistoryPanelReplayReasonCode.GhostArtifactInvalid
            );

        try
        {
            if (
                !RunBundleArtifactCodec.TryDeserialize(responseBytes, out var artifact, out _)
                || artifact == null
            )
            {
                return GhostBattleArtifactExtractionResult.Failure(
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );
            }

            var battle = artifact.Battles?.FirstOrDefault(candidate =>
                string.Equals(candidate.BattleId, battleId, StringComparison.Ordinal)
            );
            if (battle == null)
                return GhostBattleArtifactExtractionResult.Failure(
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

            if (battle.ReplayPayload == null)
                return GhostBattleArtifactExtractionResult.Failure(
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

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
                return GhostBattleArtifactExtractionResult.Failure(
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

            var battleManifest = BuildBattleManifest(artifact, battleId, battle);
            if (battleManifest == null)
                return GhostBattleArtifactExtractionResult.Failure(
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

            return GhostBattleArtifactExtractionResult.Success(
                new GhostBattlePayload
                {
                    BattleId = battleId,
                    BattleManifest = battleManifest,
                    ReplayPayload = replayPayload,
                }
            );
        }
        catch (Exception)
        {
            // Artifact content is untrusted; parser exception prose may echo private payload data.
            return GhostBattleArtifactExtractionResult.Failure(
                HistoryPanelReplayReasonCode.GhostArtifactInvalid
            );
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
                PlayerIncome = battle.Participants.PlayerIncome,
                PlayerGold = battle.Participants.PlayerGold,
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
    private GhostBattleSyncResult(
        bool succeeded,
        int importedCount,
        string? error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public int ImportedCount { get; }

    public string? Error { get; }
    public HistoryPanelGhostSyncReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static GhostBattleSyncResult Success(int importedCount) =>
        new(true, importedCount, null, HistoryPanelGhostSyncReasonCode.Completed, null);

    public static GhostBattleSyncResult Failure(
        string error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, 0, error, reasonCode, exception);
}

internal readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(
        bool succeeded,
        string? error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Succeeded = succeeded;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public string? Error { get; }
    public HistoryPanelReplayReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static GhostBattleReplayDownloadResult Success() =>
        new(true, null, HistoryPanelReplayReasonCode.Completed, null);

    public static GhostBattleReplayDownloadResult Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, error, reasonCode, exception);
}

internal readonly struct GhostBattleArtifactExtractionResult
{
    private GhostBattleArtifactExtractionResult(
        GhostBattlePayload? payload,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Payload = payload;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    internal bool Succeeded => Payload != null;
    internal GhostBattlePayload? Payload { get; }
    internal HistoryPanelReplayReasonCode ReasonCode { get; }
    internal Exception? Exception { get; }

    internal static GhostBattleArtifactExtractionResult Success(GhostBattlePayload payload) =>
        new(payload, HistoryPanelReplayReasonCode.Completed, null);

    internal static GhostBattleArtifactExtractionResult Failure(
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(null, reasonCode, exception);
}
