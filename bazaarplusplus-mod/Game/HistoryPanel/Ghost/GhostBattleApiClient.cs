#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.ModApi;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal sealed class GhostBattleApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRequestSigner _requestSigner;
    private readonly ModApiRoutes _routes;

    public GhostBattleApiClient(
        HttpClient httpClient,
        ModApiRequestSigner requestSigner,
        ModApiRoutes routes
    )
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<GhostBattleApiResult> QueryAgainstMeAsync(
        string clientId,
        string installId,
        int lookbackDays,
        int limit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var endpoint = new UriBuilder(_routes.QueryGhostBattles)
            {
                Query = $"days={Math.Clamp(lookbackDays, 1, 14)}&limit={Math.Clamp(limit, 1, 200)}",
            }.Uri.ToString();
            using var request = _requestSigner.CreateSignedRequest(
                HttpMethod.Get,
                endpoint,
                null,
                clientId,
                installId
            );
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
                    ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody),
                    shouldFallback: statusCode >= 500 || statusCode == 429,
                    shouldReRegister: statusCode == 401
                        || statusCode == 403
                        || (
                            statusCode == 404
                            && ModApiErrorFormatter.IndicatesMissingClient(responseBody)
                        )
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
            return GhostBattleApiResult.Failure(
                ModApiErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }

    public async Task<GhostBattleReplayDownloadLinkResult> RequestReplayDownloadLinkAsync(
        string battleId,
        string clientId,
        string installId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var endpoint = _routes.CreateReplayLink(battleId);
            using var request = _requestSigner.CreateSignedRequest(
                HttpMethod.Post,
                endpoint,
                null,
                clientId,
                installId
            );
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
                    ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody),
                    shouldFallback: statusCode >= 500 || statusCode == 429,
                    shouldReRegister: statusCode == 401
                        || statusCode == 403
                        || (
                            statusCode == 404
                            && ModApiErrorFormatter.IndicatesMissingClient(responseBody)
                        )
                );
            }

            var payload = JObject.Parse(responseBody);
            var downloadUrl = payload["download_url"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return GhostBattleReplayDownloadLinkResult.Failure(
                    "download_url_missing",
                    shouldFallback: false,
                    shouldReRegister: false
                );
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
                ModApiErrorFormatter.Truncate(ex.Message),
                shouldFallback: true,
                shouldReRegister: false
            );
        }
    }

    public async Task<GhostBattleReplayPayloadResult> DownloadReplayPayloadAsync(
        string downloadUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, downloadUrl),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                return GhostBattleReplayPayloadResult.Failure(
                    ModApiErrorFormatter.FormatHttpFailure(statusCode, responseBody)
                );
            }

            var payload = Newtonsoft.Json.JsonConvert.DeserializeObject<GhostBattlePayload>(
                responseBody,
                ModApiSerialization.SerializerSettings
            );
            if (
                payload?.ReplayPayload == null
                || payload.BattleManifest == null
                || string.IsNullOrWhiteSpace(payload.ReplayPayload.BattleId)
            )
            {
                return GhostBattleReplayPayloadResult.Failure("replay_payload_missing");
            }

            return GhostBattleReplayPayloadResult.Success(payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleReplayPayloadResult.Failure(
                ModApiErrorFormatter.Truncate(ex.Message)
            );
        }
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

        // The against-me endpoint returns the uploaded battle from the remote player's
        // perspective, where our local player is the "opponent". Flip participant and
        // outcome fields into the local HistoryPanel perspective before persisting.
        return new GhostBattleImportRecord
        {
            BattleId = battleId,
            RecordedAtUtc = parsedRecordedAtUtc,
            Day = battle["day"]?.Value<int?>(),
            Hour = battle["hour"]?.Value<int?>(),
            EncounterId = battle["encounter_id"]?.Value<string>(),
            PlayerHero = battle["opponent_hero"]?.Value<string>(),
            PlayerRank = battle["opponent_rank"]?.Value<string>(),
            PlayerRating = battle["opponent_rating"]?.Value<int?>(),
            PlayerLevel = battle["opponent_level"]?.Value<int?>(),
            OpponentName = battle["player_name"]?.Value<string>(),
            OpponentHero = battle["player_hero"]?.Value<string>(),
            OpponentRank = battle["player_rank"]?.Value<string>(),
            OpponentRating = battle["player_rating"]?.Value<int?>(),
            OpponentLevel = battle["player_level"]?.Value<int?>(),
            OpponentAccountId = battle["player_account_id"]?.Value<string>(),
            CombatKind = battle["combat_kind"]?.Value<string>()?.Trim() ?? "PVPCombat",
            Result = FlipBattleResult(battle["result"]?.Value<string>()),
            WinnerCombatantId = FlipCombatantId(battle["winner_combatant_id"]?.Value<string>()),
            LoserCombatantId = FlipCombatantId(battle["loser_combatant_id"]?.Value<string>()),
            ReplayAvailable = battle["replay"]?["available"]?.Value<bool>() == true,
            ReplayDownloaded = false,
            LastSyncedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string? FlipBattleResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return result;

        var trimmed = result.Trim();
        if (
            string.Equals(trimmed, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Won", StringComparison.OrdinalIgnoreCase)
        )
            return "Lost";

        if (
            string.Equals(trimmed, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Lost", StringComparison.OrdinalIgnoreCase)
        )
            return "Won";

        return trimmed;
    }

    private static string? FlipCombatantId(string? combatantId)
    {
        if (string.IsNullOrWhiteSpace(combatantId))
            return combatantId;

        return combatantId.Trim() switch
        {
            "Player" => "Opponent",
            "Opponent" => "Player",
            _ => combatantId,
        };
    }
}

internal readonly struct GhostBattleApiResult : IModApiAuthenticatedResult
{
    private GhostBattleApiResult(
        bool succeeded,
        IReadOnlyList<GhostBattleImportRecord>? battles,
        string? error,
        bool shouldFallback,
        bool shouldReRegister
    )
    {
        Succeeded = succeeded;
        Battles = battles ?? Array.Empty<GhostBattleImportRecord>();
        Error = error;
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<GhostBattleImportRecord> Battles { get; }

    public string? Error { get; }

    public bool ShouldFallback { get; }

    public bool ShouldReRegister { get; }

    public static GhostBattleApiResult Success(IReadOnlyList<GhostBattleImportRecord> battles) =>
        new(true, battles, null, false, false);

    public static GhostBattleApiResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, null, error, shouldFallback, shouldReRegister);
}

internal readonly struct GhostBattleReplayDownloadLinkResult : IModApiAuthenticatedResult
{
    private GhostBattleReplayDownloadLinkResult(
        bool succeeded,
        string? downloadUrl,
        string? error,
        bool shouldFallback,
        bool shouldReRegister
    )
    {
        Succeeded = succeeded;
        DownloadUrl = downloadUrl;
        Error = error;
        ShouldFallback = shouldFallback;
        ShouldReRegister = shouldReRegister;
    }

    public bool Succeeded { get; }

    public string? DownloadUrl { get; }

    public string? Error { get; }

    public bool ShouldFallback { get; }

    public bool ShouldReRegister { get; }

    public static GhostBattleReplayDownloadLinkResult Success(string downloadUrl) =>
        new(true, downloadUrl, null, false, false);

    public static GhostBattleReplayDownloadLinkResult Failure(
        string error,
        bool shouldFallback,
        bool shouldReRegister
    ) => new(false, null, error, shouldFallback, shouldReRegister);
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
