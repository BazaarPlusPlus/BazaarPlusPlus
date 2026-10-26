#nullable enable
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

/// <summary>
/// Discovers the Ghost battles recorded against the signed-in account and downloads their
/// replays. The host supplies what only it can answer: the profile account, which each call reads
/// once on the caller's thread before its first await; the game-typed reads of PvP evidence; and
/// whether the game's static data knows a card template, for the row's board counts, which runs
/// on a worker thread after the download.
/// </summary>
public sealed class GhostBattleSyncService
{
    private const int MaxSyncBattleLimit = 200;
    private readonly HistoryPanelRepository _repository;
    private readonly ModApiSession _modApiSession;
    private readonly Func<string?> _profileAccountId;
    private readonly IPvpBattleEvidenceBinding _evidence;
    private readonly Func<Guid, bool> _hasCardTemplate;

    // A discovery that succeeded for an account answers the next ones within this window as up to
    // date without a request, so moving in and out of the Ghost section does not query each time.
    private static readonly TimeSpan SyncTtl = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    // Last successful discovery per account, read and written only under _syncGate.
    private readonly Dictionary<string, long> _lastSyncedUtcTicks = new(StringComparer.Ordinal);
    private long _cooldownUntilUtcTicks;

    public GhostBattleSyncService(
        HistoryPanelRepository repository,
        ModApiSession modApiSession,
        Func<string?> profileAccountId,
        IPvpBattleEvidenceBinding evidence,
        Func<Guid, bool> hasCardTemplate
    )
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _modApiSession = modApiSession ?? throw new ArgumentNullException(nameof(modApiSession));
        _profileAccountId =
            profileAccountId ?? throw new ArgumentNullException(nameof(profileAccountId));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _hasCardTemplate =
            hasCardTemplate ?? throw new ArgumentNullException(nameof(hasCardTemplate));
    }

    public async Task<GhostBattleSyncResult> SyncRecentBattlesAsync(
        CancellationToken cancellationToken
    )
    {
        // A session boundary cancels a sync that unwinds only later, so the next session's sync
        // waits for it rather than failing. The wait resumes on the caller's context because the
        // account resolver reads the game profile.
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now.UtcTicks < Interlocked.Read(ref _cooldownUntilUtcTicks))
                return GhostBattleSyncResult.Failure(
                    "ghost_sync_rate_limited",
                    HistoryPanelGhostSyncReasonCode.QueryFailed
                );

            var playerAccountId = ResolvePlayerAccountId();
            if (string.IsNullOrWhiteSpace(playerAccountId))
                return GhostBattleSyncResult.Failure(
                    "player_account_id_unavailable",
                    HistoryPanelGhostSyncReasonCode.IdentityUnavailable
                );

            if (
                _lastSyncedUtcTicks.TryGetValue(playerAccountId!, out var syncedAt)
                && now.UtcTicks - syncedAt < SyncTtl.Ticks
            )
                return GhostBattleSyncResult.UpToDate();

            var result = await QueryAndPersistAsync(playerAccountId!, cancellationToken)
                .ConfigureAwait(false);
            if (result.Succeeded)
                _lastSyncedUtcTicks[playerAccountId!] = DateTimeOffset.UtcNow.UtcTicks;
            return result;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task<GhostBattleReplayDownloadResult> DownloadReplayAsync(
        string battleId,
        string replayDirectoryPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return Failure("battle_id_required", HistoryPanelReplayReasonCode.ReplayUnavailable);
        if (string.IsNullOrWhiteSpace(replayDirectoryPath))
            return Failure(
                "replay_directory_required",
                HistoryPanelReplayReasonCode.ReplayDirectoryUnavailable
            );

        cancellationToken.ThrowIfCancellationRequested();
        var account = ResolvePlayerAccountId();
        var reference = _repository.TryGetGhostBundleReference(battleId);
        if (reference == null)
            return Failure("ghost_battle_missing", HistoryPanelReplayReasonCode.ReplayUnavailable);

        if (string.IsNullOrWhiteSpace(account) || reference.LocalPlayerAccountId != account)
            return Failure(
                "ghost_account_mismatch",
                HistoryPanelReplayReasonCode.ReplayUnavailable
            );
        var payloadStore = new GhostBattlePayloadStore(
            GhostBattlePayloadStore.ResolveDirectory(replayDirectoryPath)
        );
        if (reference.ReplayState == ReplayAvailability.Saved)
        {
            var cached = await Task.Run(
                    () => payloadStore.LoadDetailed(reference.LocalBattleId),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (
                cached.Status == FileBackedPayloadLoadStatus.Loaded
                && GhostBattlePayloadReader.MatchesIdentity(
                    cached.Payload,
                    reference.LocalBattleId,
                    account!,
                    reference.UploaderAccountId
                )
            )
                return GhostBattleReplayDownloadResult.Success(cached.Payload!);
        }
        if (reference.ReplayState is ReplayAvailability.Expired or ReplayAvailability.Unavailable)
            return AvailabilityFailure(reference.ReplayState);

        var refreshed = false;
        if (reference.DownloadExpiresAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            reference = await RefreshReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            refreshed = true;
            if (reference == null)
            {
                _repository.MarkGhostReplayUnavailable(
                    battleId,
                    ReplayAvailability.Expired,
                    "url_expired"
                );
                return AvailabilityFailure(ReplayAvailability.Expired);
            }
        }

        var download = await _modApiSession
            .DownloadGhostBundleAsync(reference.DownloadUrl, cancellationToken)
            .ConfigureAwait(false);
        if (!download.Succeeded && download.StatusCode is 403 or 404 && !refreshed)
        {
            reference = await RefreshReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            refreshed = true;
            if (reference != null)
                download = await _modApiSession
                    .DownloadGhostBundleAsync(reference.DownloadUrl, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (!download.Succeeded || download.Bytes == null)
        {
            if (download.StatusCode is not (403 or 404))
                return Failure(
                    download.Error ?? "ghost_bundle_download_failed",
                    HistoryPanelReplayReasonCode.GhostDownloadFailed,
                    download.DiagnosticException
                );
            _repository.MarkGhostReplayUnavailable(
                battleId,
                ReplayAvailability.Expired,
                "object_unavailable"
            );
            return AvailabilityFailure(ReplayAvailability.Expired, download.DiagnosticException);
        }

        var extraction = ExtractPayload(reference!, download.Bytes, _evidence);
        if (!extraction.Succeeded || extraction.Payload == null)
        {
            _repository.MarkGhostReplayUnavailable(
                battleId,
                ReplayAvailability.Unavailable,
                extraction.Error ?? "payload_invalid"
            );
            return Failure(
                extraction.Error ?? "ghost_bundle_import_failed",
                extraction.ReasonCode,
                extraction.Exception
            );
        }

        payloadStore.Save(extraction.Payload);
        // Ghost table player_*/opponent_* columns retain the recorder perspective.
        _repository.MarkGhostReplayDownloaded(
            battleId,
            HistoryBattleSnapshotCounter.Count(
                extraction.Payload.BattleManifest.Snapshots,
                _hasCardTemplate
            )
        );
        return GhostBattleReplayDownloadResult.Success(extraction.Payload);
    }

    private async Task<GhostBattleSyncResult> QueryAndPersistAsync(
        string playerAccountId,
        CancellationToken cancellationToken
    )
    {
        var result = await _modApiSession
            .QueryGhostBattlesAgainstMeAsync(playerAccountId, MaxSyncBattleLimit, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.StatusCode == 429)
            {
                var cooldown = TimeSpan.FromSeconds(Math.Max(1, result.RetryAfterSeconds ?? 60));
                Interlocked.Exchange(
                    ref _cooldownUntilUtcTicks,
                    DateTimeOffset.UtcNow.Add(cooldown).UtcTicks
                );
            }
            return GhostBattleSyncResult.Failure(
                result.Error ?? "ghost_sync_failed",
                HistoryPanelGhostSyncReasonCode.QueryFailed,
                result.DiagnosticException
            );
        }

        try
        {
            _repository.UpsertGhostBattles(
                playerAccountId,
                result.Battles.Select(ToImportRow).ToList()
            );
            _repository.MarkOldUndownloadedGhostBattlesDeleted(DateTimeOffset.UtcNow);
            return GhostBattleSyncResult.Success(
                result.Battles.Count,
                result.Battles.Count >= MaxSyncBattleLimit
            );
        }
        catch (Exception ex)
        {
            return GhostBattleSyncResult.Failure(
                "ghost_sync_repository_failed",
                HistoryPanelGhostSyncReasonCode.RepositoryFailed,
                ex
            );
        }
    }

    private async Task<GhostBundleReference?> RefreshReferenceAsync(
        GhostBundleReference reference,
        CancellationToken cancellationToken
    )
    {
        var result = await QueryAndPersistAsync(reference.LocalPlayerAccountId, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? _repository.TryGetGhostBundleReference(reference.LocalBattleId)
            : null;
    }

    private static GhostPayloadExtraction ExtractPayload(
        GhostBundleReference reference,
        byte[] bundleBytes,
        IPvpBattleEvidenceBinding evidence
    )
    {
        try
        {
            var openResult = RunBundleV6Contract.Open(bundleBytes);
            if (!openResult.Succeeded)
            {
                var runIdentityMismatch =
                    openResult.FailureKind == RunBundleOpenFailureKind.RunIdentityMismatch;
                return GhostPayloadExtraction.Failure(
                    runIdentityMismatch
                        ? "ghost_run_identity_mismatch"
                        : openResult.Reason ?? "ghost_bundle_import_failed",
                    runIdentityMismatch
                        ? HistoryPanelReplayReasonCode.GhostBattleMismatch
                        : HistoryPanelReplayReasonCode.GhostArtifactInvalid,
                    openResult.Exception
                );
            }

            var opened = openResult.Value!;
            if (
                !string.Equals(
                    opened.Bundle.Manifest.BundleId,
                    reference.BundleId,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    opened.Bundle.Manifest.Run.PlayerAccountId,
                    reference.UploaderAccountId,
                    StringComparison.Ordinal
                )
            )
                return GhostPayloadExtraction.Failure(
                    "ghost_bundle_identity_mismatch",
                    HistoryPanelReplayReasonCode.GhostBattleMismatch
                );

            if (!opened.TryGetReplayableBattle(reference.RemoteBattleId, out var battle))
                return GhostPayloadExtraction.Failure(
                    "ghost_battle_replay_incomplete",
                    HistoryPanelReplayReasonCode.GhostArtifactInvalid
                );

            var replay = new PvpReplayPayload
            {
                BattleId = reference.LocalBattleId,
                Version = battle!.Replay!.Version,
                SpawnMessageBytes = battle.Replay.SpawnMessageBytes.ToArray(),
                CombatMessageBytes = battle.Replay.CombatMessageBytes.ToArray(),
                DespawnMessageBytes = battle.Replay.DespawnMessageBytes.ToArray(),
            };
            evidence.VerifyReplay(replay);
            var manifest = GhostManifestProjection.BuildRecorderPerspectiveManifest(
                reference,
                opened.Payload.RunId,
                battle,
                evidence
            );
            return GhostPayloadExtraction.Success(
                new GhostBattlePayload
                {
                    BattleId = reference.LocalBattleId,
                    PerspectiveVersion = 1,
                    BattleManifest = manifest,
                    ReplayPayload = replay,
                }
            );
        }
        catch (Exception ex)
        {
            return GhostPayloadExtraction.Failure(
                // Terminal: discovery keeps the payload unavailable, so a truly invalid
                // replay is not retried on every sync.
                "ghost_bundle_import_failed",
                HistoryPanelReplayReasonCode.GhostArtifactInvalid,
                ex
            );
        }
    }

    private static GhostBattleImportRow ToImportRow(GhostBattleImportRecord battle) =>
        new()
        {
            BattleId = battle.BattleId,
            BundleId = battle.BundleId,
            DownloadUrl = battle.DownloadUrl,
            DownloadExpiresAtUtc = battle.DownloadExpiresAtUtc,
            RecordedAtUtc = battle.RecordedAtUtc,
            Day = battle.Day,
            Hour = battle.Hour,
            EncounterId = battle.EncounterId,
            PlayerName = battle.PlayerName,
            PlayerAccountId = battle.PlayerAccountId,
            PlayerHero = battle.PlayerHero,
            PlayerRank = battle.PlayerRank,
            PlayerRating = battle.PlayerRating,
            PlayerLevel = battle.PlayerLevel,
            PlayerPrestige = battle.PlayerPrestige,
            PlayerVictories = battle.PlayerVictories,
            OpponentName = battle.OpponentName,
            OpponentHero = battle.OpponentHero,
            OpponentRank = battle.OpponentRank,
            OpponentRating = battle.OpponentRating,
            OpponentLevel = battle.OpponentLevel,
            OpponentPrestige = battle.OpponentPrestige,
            OpponentVictories = battle.OpponentVictories,
            OpponentAccountId = battle.OpponentAccountId,
            CombatKind = battle.CombatKind,
            Result = battle.Result,
            WinnerCombatantId = battle.WinnerCombatantId,
            LoserCombatantId = battle.LoserCombatantId,
            IsFinalBattle = battle.IsFinalBattle,
        };

    // The host's profile read may throw (the game's client cache is not ready); that reads as no
    // account, as a missing profile does.
    private string? ResolvePlayerAccountId()
    {
        try
        {
            return _profileAccountId()?.Trim();
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.HistoryPanel,
                    "history_panel.ghost_identity.read_failed"
                ),
                ex,
                () => [("reason_code", HistoryPanelGhostIdentityReasonCode.ClientCacheReadFailed)]
            );
            return null;
        }
    }

    private static GhostBattleReplayDownloadResult Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => GhostBattleReplayDownloadResult.Failure(error, reasonCode, exception);

    // The error code stays a diagnostic; the panel reads Availability.
    private static GhostBattleReplayDownloadResult AvailabilityFailure(
        ReplayAvailability availability,
        Exception? exception = null
    ) =>
        GhostBattleReplayDownloadResult.Failure(
            "ghost_replay_" + ReplayAvailabilityCodec.ToStoredState(availability),
            HistoryPanelReplayReasonCode.GhostDownloadFailed,
            exception,
            availability
        );
}

public readonly struct GhostBattleSyncResult
{
    private GhostBattleSyncResult(
        bool succeeded,
        int importedCount,
        string? error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception,
        bool discoveryLimitReached = false
    )
    {
        Succeeded = succeeded;
        ImportedCount = importedCount;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
        DiscoveryLimitReached = discoveryLimitReached;
    }

    public bool Succeeded { get; }
    public int ImportedCount { get; }
    public bool DiscoveryLimitReached { get; }
    public string? Error { get; }
    public HistoryPanelGhostSyncReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    public static GhostBattleSyncResult Success(int importedCount, bool discoveryLimitReached) =>
        new(
            true,
            importedCount,
            null,
            HistoryPanelGhostSyncReasonCode.Completed,
            null,
            discoveryLimitReached
        );

    // A successful discovery for the same account is recent enough: nothing was requested.
    public static GhostBattleSyncResult UpToDate() =>
        new(true, 0, null, HistoryPanelGhostSyncReasonCode.UpToDate, null);

    public static GhostBattleSyncResult Failure(
        string error,
        HistoryPanelGhostSyncReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, 0, error, reasonCode, exception);
}

public readonly struct GhostBattleReplayDownloadResult
{
    private GhostBattleReplayDownloadResult(
        GhostBattlePayload? payload,
        string? error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception,
        ReplayAvailability? availability
    )
    {
        Payload = payload;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
        Availability = availability;
    }

    public bool Succeeded => Payload != null;

    // The payload the download confirmed: the identity-checked cached file, or the one it just
    // extracted and saved. Playback uses it rather than reading the file again.
    public GhostBattlePayload? Payload { get; }
    public string? Error { get; }
    public HistoryPanelReplayReasonCode ReasonCode { get; }
    public Exception? Exception { get; }

    // Set when the failure is the battle's persisted Expired or Unavailable state.
    public ReplayAvailability? Availability { get; }

    public static GhostBattleReplayDownloadResult Success(GhostBattlePayload payload) =>
        new(
            payload ?? throw new ArgumentNullException(nameof(payload)),
            null,
            HistoryPanelReplayReasonCode.Completed,
            null,
            null
        );

    public static GhostBattleReplayDownloadResult Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null,
        ReplayAvailability? availability = null
    ) => new(null, error, reasonCode, exception, availability);
}

internal readonly struct GhostPayloadExtraction
{
    private GhostPayloadExtraction(
        GhostBattlePayload? payload,
        string? error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception
    )
    {
        Payload = payload;
        Error = error;
        ReasonCode = reasonCode;
        Exception = exception;
    }

    internal bool Succeeded => Payload != null;
    internal GhostBattlePayload? Payload { get; }
    internal string? Error { get; }
    internal HistoryPanelReplayReasonCode ReasonCode { get; }
    internal Exception? Exception { get; }

    internal static GhostPayloadExtraction Success(GhostBattlePayload payload) =>
        new(payload, null, HistoryPanelReplayReasonCode.Completed, null);

    internal static GhostPayloadExtraction Failure(
        string error,
        HistoryPanelReplayReasonCode reasonCode,
        Exception? exception = null
    ) => new(null, error, reasonCode, exception);
}
