#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class ModApiHealthClient
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public ModApiHealthClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public async Task<ModApiHealthProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient
                .GetAsync(_routes.Health, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    $"http_{(int)response.StatusCode}"
                );

            var parsed = JsonConvert.DeserializeObject<ModApiHealthResponse>(body);
            if (
                parsed == null
                || !string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase)
            )
            {
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "health_status_not_ok"
                );
            }

            if (!DateTime.TryParse(parsed.ServerTimeUtc, out var serverTimeUtc))
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );

            return ModApiHealthProbeResult.Success(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                parsed.Status,
                serverTimeUtc.ToUniversalTime()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ModApiHealthProbeResult.Failure(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                ModApiErrorFormatter.Truncate(ex.Message)
            );
        }
    }
}

public readonly struct ModApiHealthProbeResult
{
    private ModApiHealthProbeResult(
        bool succeeded,
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string? status,
        DateTime? serverTimeUtc,
        string? error
    )
    {
        Succeeded = succeeded;
        ProbedAtUtc = probedAtUtc;
        RoundTripMilliseconds = roundTripMilliseconds;
        Status = status;
        ServerTimeUtc = serverTimeUtc;
        Error = error;
    }

    public bool Succeeded { get; }
    public DateTime ProbedAtUtc { get; }
    public long RoundTripMilliseconds { get; }
    public string? Status { get; }
    public DateTime? ServerTimeUtc { get; }
    public string? Error { get; }

    public static ModApiHealthProbeResult Success(
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string status,
        DateTime serverTimeUtc
    ) => new(true, probedAtUtc, roundTripMilliseconds, status, serverTimeUtc, null);

    public static ModApiHealthProbeResult Failure(
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        string error
    ) => new(false, probedAtUtc, roundTripMilliseconds, null, null, error);
}
