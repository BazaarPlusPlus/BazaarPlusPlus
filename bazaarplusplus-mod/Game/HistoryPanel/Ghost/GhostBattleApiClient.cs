#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.Online;
using BazaarPlusPlus.Game.Online.Models;
using BazaarPlusPlus.Game.PvpBattles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleApiClient
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public GhostBattleApiClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<GhostBattleApiResult> QueryAgainstMeAsync(
        string playerAccountId,
        int limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(playerAccountId))
            {
                return GhostBattleApiResult.Failure("player_account_id_required");
            }

            var endpoint = new UriBuilder(_routes.QueryGhostBattles)
            {
                Query =
                    $"player_account_id={Uri.EscapeDataString(playerAccountId.Trim())}&limit={Math.Clamp(limit, 1, 200)}",
            }.Uri.ToString();
            using var request = CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                return GhostBattleApiResult.Failure(
                    V3ErrorFormatter.FormatHttpFailure(statusCode, responseBody)
                );
            }

            var payload = JObject.Parse(responseBody);
            var battlesToken = payload["battles"] as JArray;
            var importRecords = new List<GhostBattleImportRecord>();
            if (battlesToken != null)
            {
                foreach (var battleChild in battlesToken)
                {
                    if (battleChild is not JObject battleToken)
                        continue;

                    var importRecord = TryParseBattle(battleToken);
                    if (importRecord != null)
                        importRecords.Add(importRecord);
                }
            }

            return GhostBattleApiResult.Success(importRecords);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleApiResult.Failure(V3ErrorFormatter.Truncate(ex.Message));
        }
    }

    public async Task<GhostBattleReplayDownloadLinkResult> RequestReplayDownloadLinkAsync(
        string battleId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var endpoint = _routes.CreateReplayLink(battleId);
            using var request = CreateRequest(HttpMethod.Post, endpoint);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                return GhostBattleReplayDownloadLinkResult.Failure(
                    V3ErrorFormatter.FormatHttpFailure(statusCode, responseBody)
                );
            }

            var payload = JObject.Parse(responseBody);
            var downloadUrl = payload["download_url"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return GhostBattleReplayDownloadLinkResult.Failure("download_url_missing");
            }

            return GhostBattleReplayDownloadLinkResult.Success(downloadUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleReplayDownloadLinkResult.Failure(
                V3ErrorFormatter.Truncate(ex.Message)
            );
        }
    }

    public async Task<GhostBattleReplayPayloadResult> DownloadReplayPayloadAsync(
        string battleId,
        string downloadUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, downloadUrl);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                return GhostBattleReplayPayloadResult.Failure(
                    V3ErrorFormatter.FormatHttpFailure(
                        statusCode,
                        Encoding.UTF8.GetString(responseBytes)
                    )
                );
            }

            var payload = TryExtractPayloadFromArtifact(battleId, responseBytes);
            if (!IsValidGhostBattlePayload(payload))
            {
                return GhostBattleReplayPayloadResult.Failure("replay_payload_missing");
            }

            return GhostBattleReplayPayloadResult.Success(payload!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleReplayPayloadResult.Failure(V3ErrorFormatter.Truncate(ex.Message));
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        return new HttpRequestMessage(method, endpoint);
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
            var artifact = V3RunBundleArtifactCodec.Deserialize(responseBytes);
            if (artifact == null)
                return null;

            var battle = artifact?.Battles?.FirstOrDefault(candidate =>
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
        catch
        {
            return null;
        }
    }

    private static PvpBattleManifest? BuildBattleManifest(
        RunArtifactV3? artifact,
        string battleId,
        RunArtifactBattleV3 battle
    )
    {
        if (battle.Manifest == null || battle.Participants == null || battle.Snapshots == null)
            return null;

        return new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = artifact?.RunId,
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
                OpponentName = battle.Participants.OpponentName,
                OpponentAccountId = battle.Participants.OpponentAccountId,
                OpponentHero = battle.Participants.OpponentHero,
                OpponentRank = battle.Participants.OpponentRank,
                OpponentRating = battle.Participants.OpponentRating,
                OpponentLevel = battle.Participants.OpponentLevel,
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
        BattleSnapshotsArtifactV3 snapshots,
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
                capture?.Items?.Select(item => item.Clone()).ToList()
                ?? new List<CombatReplay.CombatReplayCardSnapshot>(),
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

    private static GhostBattleImportRecord? TryParseBattle(JObject battle)
    {
        var battleId = battle["battle_id"]?.Value<string>()?.Trim();
        var recordedAtUtc = battle["recorded_at_utc"]?.Value<string>()?.Trim();
        if (
            string.IsNullOrWhiteSpace(battleId)
            || string.IsNullOrWhiteSpace(recordedAtUtc)
            || !DateTimeOffset.TryParse(recordedAtUtc, out var parsedRecordedAtUtc)
        )
        {
            return null;
        }

        return new GhostBattleImportRecord
        {
            BattleId = battleId,
            RecordedAtUtc = parsedRecordedAtUtc,
            Day = battle["day"]?.Value<int?>(),
            Hour = battle["hour"]?.Value<int?>(),
            EncounterId = battle["encounter_id"]?.Value<string>(),
            PlayerName = battle["player_name"]?.Value<string>(),
            PlayerAccountId = battle["player_account_id"]?.Value<string>(),
            PlayerHero = battle["player_hero"]?.Value<string>(),
            PlayerRank = battle["player_rank"]?.Value<string>(),
            PlayerRating = battle["player_rating"]?.Value<int?>(),
            PlayerLevel = battle["player_level"]?.Value<int?>(),
            OpponentName = battle["opponent_name"]?.Value<string>(),
            OpponentHero = battle["opponent_hero"]?.Value<string>(),
            OpponentRank = battle["opponent_rank"]?.Value<string>(),
            OpponentRating = battle["opponent_rating"]?.Value<int?>(),
            OpponentLevel = battle["opponent_level"]?.Value<int?>(),
            OpponentAccountId = battle["opponent_account_id"]?.Value<string>(),
            CombatKind = battle["combat_kind"]?.Value<string>()?.Trim() ?? "PVPCombat",
            Result = battle["result"]?.Value<string>()?.Trim(),
            WinnerCombatantId = battle["winner_combatant_id"]?.Value<string>()?.Trim(),
            LoserCombatantId = battle["loser_combatant_id"]?.Value<string>()?.Trim(),
            IsBundleFinalBattle = battle["is_bundle_final_battle"]?.Value<bool>() == true,
            ReplayAvailable = battle["replay"]?["available"]?.Value<bool>() == true,
            ReplayDownloaded = false,
            LastSyncedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}

internal readonly struct GhostBattleApiResult
{
    private GhostBattleApiResult(
        bool succeeded,
        IReadOnlyList<GhostBattleImportRecord>? battles,
        string? error
    )
    {
        Succeeded = succeeded;
        Battles = battles ?? Array.Empty<GhostBattleImportRecord>();
        Error = error;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<GhostBattleImportRecord> Battles { get; }

    public string? Error { get; }

    public static GhostBattleApiResult Success(IReadOnlyList<GhostBattleImportRecord> battles) =>
        new(true, battles, null);

    public static GhostBattleApiResult Failure(string error) => new(false, null, error);
}

internal readonly struct GhostBattleReplayDownloadLinkResult
{
    private GhostBattleReplayDownloadLinkResult(
        bool succeeded,
        string? downloadUrl,
        string? error
    )
    {
        Succeeded = succeeded;
        DownloadUrl = downloadUrl;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? DownloadUrl { get; }

    public string? Error { get; }

    public static GhostBattleReplayDownloadLinkResult Success(string downloadUrl) =>
        new(true, downloadUrl, null);

    public static GhostBattleReplayDownloadLinkResult Failure(string error) =>
        new(false, null, error);
}

internal readonly struct GhostBattleReplayPayloadResult
{
    private GhostBattleReplayPayloadResult(
        bool succeeded,
        GhostBattlePayload? payload,
        string? error
    )
    {
        Succeeded = succeeded;
        Payload = payload;
        Error = error;
    }

    public bool Succeeded { get; }

    public GhostBattlePayload? Payload { get; }

    public string? Error { get; }

    public static GhostBattleReplayPayloadResult Success(GhostBattlePayload payload) =>
        new(true, payload, null);

    public static GhostBattleReplayPayloadResult Failure(string error) => new(false, null, error);
}
