#nullable enable
using System.Globalization;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.ModApi.Clients;

internal sealed class GhostBattleClient
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
        if (string.IsNullOrWhiteSpace(playerAccountId))
            return GhostBattleQueryResult.Failure("player_account_id_required");

        try
        {
            var endpoint = new UriBuilder(_routes.QueryGhostBattles)
            {
                Query =
                    $"player_account_id={Uri.EscapeDataString(playerAccountId.Trim())}&limit={Math.Clamp(limit, 1, 200)}",
            }.Uri.ToString();
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var parsed = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, cancellationToken)
                .ConfigureAwait(false);
            if (!parsed.IsSuccess)
            {
                return GhostBattleQueryResult.Failure(
                    new ModApiFailure(parsed.UserCode, response: parsed),
                    parsed.StatusCode,
                    parsed.RetryAfterSeconds
                );
            }

            var battlesToken = JObject.Parse(parsed.Body)["battles"] as JArray;
            var records = new List<GhostBattleImportRecord>();
            if (battlesToken != null)
            {
                foreach (var child in battlesToken)
                {
                    if (child is JObject battle && TryParseBattle(battle, out var record))
                        records.Add(record);
                }
            }

            return GhostBattleQueryResult.Success(records);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBattleQueryResult.Failure(
                new ModApiFailure("transport_error", diagnosticException: ex)
            );
        }
    }

    public async Task<GhostBundleDownloadResult> DownloadBundleAsync(
        string downloadUrl,
        CancellationToken cancellationToken
    )
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
            return GhostBundleDownloadResult.Failure("download_url_invalid");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var parsed = await ModApiResponse
                    .ReadAsync(
                        response,
                        new ModApiBodyReadPolicy(16 * 1024, "bundle_too_large"),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return GhostBundleDownloadResult.Failure(
                    new ModApiFailure(parsed.UserCode, response: parsed),
                    parsed.StatusCode
                );
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > BundleLimitsV5.MaxBundleBytes)
                return GhostBundleDownloadResult.Failure("bundle_too_large");

            var bytes = await ReadBoundedAsync(
                    response.Content,
                    BundleLimitsV5.MaxBundleBytes,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return GhostBundleDownloadResult.Success(bytes);
        }
        catch (GhostDownloadLimitException)
        {
            return GhostBundleDownloadResult.Failure("bundle_too_large");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GhostBundleDownloadResult.Failure(
                new ModApiFailure("transport_error", diagnosticException: ex)
            );
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream
                .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return output.ToArray();
            if (output.Length + read > maxBytes)
                throw new GhostDownloadLimitException();
            output.Write(buffer, 0, read);
        }
    }

    private static bool TryParseBattle(JObject battle, out GhostBattleImportRecord record)
    {
        record = null!;
        var battleId = ReadString(battle, "battle_id");
        var bundleId = ReadString(battle, "bundle_id");
        var downloadUrl = ReadString(battle, "download_url");
        if (
            string.IsNullOrEmpty(battleId)
            || string.IsNullOrEmpty(bundleId)
            || string.IsNullOrEmpty(downloadUrl)
            || !TryReadUnixMilliseconds(battle["recorded_at_ms"], out var recordedAt)
            || !TryReadUnixMilliseconds(battle["download_expires_at_ms"], out var expiresAt)
            || battle["player"] is not JObject player
            || battle["opponent"] is not JObject opponent
        )
            return false;

        var uploaderAccountId = ReadString(player, "account_id");
        var opponentAccountId = ReadString(opponent, "account_id");
        if (string.IsNullOrEmpty(uploaderAccountId) || string.IsNullOrEmpty(opponentAccountId))
            return false;

        record = new GhostBattleImportRecord
        {
            BattleId = battleId,
            BundleId = bundleId,
            DownloadUrl = downloadUrl,
            DownloadExpiresAtUtc = expiresAt,
            RecordedAtUtc = recordedAt,
            Day = ReadNonNegativeInt(battle, "day"),
            Hour = ReadNonNegativeInt(battle, "hour"),
            EncounterId = ReadString(battle, "encounter_id"),
            PlayerName = ReadString(player, "display_name") ?? string.Empty,
            PlayerAccountId = uploaderAccountId,
            PlayerHero = ReadString(player, "hero_name"),
            PlayerRank = ReadString(player, "rank"),
            PlayerRating = ReadNonNegativeInt(player, "rating"),
            PlayerLevel = ReadNonNegativeInt(player, "level"),
            PlayerPrestige = ReadNonNegativeInt(player, "prestige"),
            PlayerVictories = ReadNonNegativeInt(player, "victories"),
            OpponentName = ReadString(opponent, "display_name") ?? string.Empty,
            OpponentAccountId = opponentAccountId,
            OpponentHero = ReadString(opponent, "hero_name"),
            OpponentRank = ReadString(opponent, "rank"),
            OpponentRating = ReadNonNegativeInt(opponent, "rating"),
            OpponentLevel = ReadNonNegativeInt(opponent, "level"),
            OpponentPrestige = ReadNonNegativeInt(opponent, "prestige"),
            OpponentVictories = ReadNonNegativeInt(opponent, "victories"),
            CombatKind = ReadString(battle, "combat_kind") ?? "pvp",
            Result = NormalizeResult(ReadString(battle, "result")),
            WinnerCombatantId = ReadString(battle, "winner_combatant_id"),
            LoserCombatantId = ReadString(battle, "loser_combatant_id"),
            IsFinalBattle = battle["is_final_battle"]?.Value<bool?>() ?? false,
            ReplayAvailable = Uri.TryCreate(downloadUrl, UriKind.Absolute, out _),
            ReplayState = "remote_available",
            LastSyncedAtUtc = DateTimeOffset.UtcNow,
        };
        return record.Day.HasValue && record.Hour.HasValue;
    }

    private static string NormalizeResult(string? value) =>
        value != null
        && (
            string.Equals(value, "win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
        )
            ? value.ToLowerInvariant()
            : "unknown";

    private static string? ReadString(JObject source, string name)
    {
        var value =
            source[name]?.Type == JTokenType.String ? source[name]?.Value<string>()?.Trim() : null;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? ReadNonNegativeInt(JObject source, string name)
    {
        var token = source[name];
        if (token == null || token.Type == JTokenType.Null)
            return null;
        if (
            !long.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
            return null;
        return value is >= 0 and <= int.MaxValue ? (int)value : null;
    }

    private static bool TryReadUnixMilliseconds(JToken? token, out DateTimeOffset value)
    {
        value = default;
        if (
            token == null
            || !long.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var milliseconds
            )
            || milliseconds < 0
        )
            return false;
        try
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed class GhostDownloadLimitException : Exception { }
}

public readonly struct GhostBattleQueryResult
{
    private GhostBattleQueryResult(
        bool succeeded,
        IReadOnlyList<GhostBattleImportRecord>? battles,
        ModApiFailure? failure,
        int? statusCode,
        int? retryAfterSeconds
    )
    {
        Succeeded = succeeded;
        Battles = battles ?? Array.Empty<GhostBattleImportRecord>();
        FailureInfo = failure;
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public bool Succeeded { get; }
    public IReadOnlyList<GhostBattleImportRecord> Battles { get; }
    public ModApiFailure? FailureInfo { get; }
    public string? Error => FailureInfo?.UserCode;
    public Exception? DiagnosticException => FailureInfo?.DiagnosticException;
    public int? StatusCode { get; }
    public int? RetryAfterSeconds { get; }

    public static GhostBattleQueryResult Success(IReadOnlyList<GhostBattleImportRecord> battles) =>
        new(true, battles, null, null, null);

    public static GhostBattleQueryResult Failure(
        ModApiFailure failure,
        int? statusCode = null,
        int? retryAfterSeconds = null
    ) => new(false, null, failure, statusCode, retryAfterSeconds);

    public static GhostBattleQueryResult Failure(
        string error,
        int? statusCode = null,
        int? retryAfterSeconds = null
    ) => Failure(new ModApiFailure(error), statusCode, retryAfterSeconds);
}

public readonly struct GhostBundleDownloadResult
{
    private GhostBundleDownloadResult(
        bool succeeded,
        byte[]? bytes,
        ModApiFailure? failure,
        int? statusCode
    )
    {
        Succeeded = succeeded;
        Bytes = bytes;
        FailureInfo = failure;
        StatusCode = statusCode;
    }

    public bool Succeeded { get; }
    public byte[]? Bytes { get; }
    public ModApiFailure? FailureInfo { get; }
    public string? Error => FailureInfo?.UserCode;
    public Exception? DiagnosticException => FailureInfo?.DiagnosticException;
    public int? StatusCode { get; }

    public static GhostBundleDownloadResult Success(byte[] bytes) => new(true, bytes, null, null);

    public static GhostBundleDownloadResult Failure(
        ModApiFailure failure,
        int? statusCode = null
    ) => new(false, null, failure, statusCode);

    public static GhostBundleDownloadResult Failure(string error, int? statusCode = null) =>
        Failure(new ModApiFailure(error), statusCode);
}
