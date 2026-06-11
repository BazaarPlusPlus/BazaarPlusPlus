#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class GhostBattleClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public GhostBattleClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<GhostBattleQueryResult> QueryAgainstMeAsync(
        string playerAccountId,
        int limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(playerAccountId))
            {
                return GhostBattleQueryResult.Failure("player_account_id_required");
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
                return GhostBattleQueryResult.Failure(
                    ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody)
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

            return GhostBattleQueryResult.Success(importRecords);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleQueryResult.Failure(ModApiErrorFormatter.Truncate(ex.Message));
        }
    }

    public async Task<GhostBattleReplayLinkResult> RequestReplayDownloadLinkAsync(
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
                return GhostBattleReplayLinkResult.Failure(
                    ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody)
                );
            }

            var payload = JObject.Parse(responseBody);
            var downloadUrl = payload["download_url"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return GhostBattleReplayLinkResult.Failure("download_url_missing");
            }

            return GhostBattleReplayLinkResult.Success(downloadUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleReplayLinkResult.Failure(ModApiErrorFormatter.Truncate(ex.Message));
        }
    }

    public async Task<GhostBattleReplayBytesResult> DownloadReplayBytesAsync(
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
                return GhostBattleReplayBytesResult.Failure(
                    ModApiErrorFormatter.FormatHttpFailure(
                        statusCode,
                        Encoding.UTF8.GetString(responseBytes)
                    )
                );
            }

            return GhostBattleReplayBytesResult.Success(responseBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleReplayBytesResult.Failure(ModApiErrorFormatter.Truncate(ex.Message));
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        return new HttpRequestMessage(method, endpoint);
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
            PlayerPrestige = battle["player_prestige"]?.Value<int?>(),
            PlayerVictories = battle["player_victories"]?.Value<int?>(),
            OpponentName = battle["opponent_name"]?.Value<string>(),
            OpponentHero = battle["opponent_hero"]?.Value<string>(),
            OpponentRank = battle["opponent_rank"]?.Value<string>(),
            OpponentRating = battle["opponent_rating"]?.Value<int?>(),
            OpponentLevel = battle["opponent_level"]?.Value<int?>(),
            OpponentPrestige = battle["opponent_prestige"]?.Value<int?>(),
            OpponentVictories = battle["opponent_victories"]?.Value<int?>(),
            OpponentAccountId = battle["opponent_account_id"]?.Value<string>(),
            CombatKind = battle["combat_kind"]?.Value<string>()?.Trim() ?? "PVPCombat",
            Result = battle["result"]?.Value<string>()?.Trim(),
            WinnerCombatantId = battle["winner_combatant_id"]?.Value<string>()?.Trim(),
            LoserCombatantId = battle["loser_combatant_id"]?.Value<string>()?.Trim(),
            IsFinalBattle = battle["is_final_battle"]?.Value<bool?>() ?? false,
            ReplayAvailable = true,
            ReplayDownloaded = false,
            LastSyncedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}

public readonly struct GhostBattleQueryResult
{
    private GhostBattleQueryResult(
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

    public static GhostBattleQueryResult Success(IReadOnlyList<GhostBattleImportRecord> battles) =>
        new(true, battles, null);

    public static GhostBattleQueryResult Failure(string error) => new(false, null, error);
}

public readonly struct GhostBattleReplayLinkResult
{
    private GhostBattleReplayLinkResult(bool succeeded, string? downloadUrl, string? error)
    {
        Succeeded = succeeded;
        DownloadUrl = downloadUrl;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? DownloadUrl { get; }

    public string? Error { get; }

    public static GhostBattleReplayLinkResult Success(string downloadUrl) =>
        new(true, downloadUrl, null);

    public static GhostBattleReplayLinkResult Failure(string error) => new(false, null, error);
}

public readonly struct GhostBattleReplayBytesResult
{
    private GhostBattleReplayBytesResult(bool succeeded, byte[]? bytes, string? error)
    {
        Succeeded = succeeded;
        Bytes = bytes;
        Error = error;
    }

    public bool Succeeded { get; }

    public byte[]? Bytes { get; }

    public string? Error { get; }

    public static GhostBattleReplayBytesResult Success(byte[] bytes) => new(true, bytes, null);

    public static GhostBattleReplayBytesResult Failure(string error) => new(false, null, error);
}
