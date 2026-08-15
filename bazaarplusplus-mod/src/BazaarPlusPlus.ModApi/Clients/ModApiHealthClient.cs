#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.ModApi.Http;
using BazaarPlusPlus.ModApi.Models;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Clients;

internal sealed class ModApiHealthClient
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
            var parsedResponse = await ModApiResponse
                .ReadAsync(response, ModApiBodyReadPolicy.Json, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            if (!parsedResponse.IsSuccess)
                return ModApiHealthProbeResult.FailureFrom(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    new ModApiFailure(parsedResponse.UserCode, response: parsedResponse)
                );

            ModApiHealthResponse? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<ModApiHealthResponse>(parsedResponse.Body);
            }
            catch (JsonException)
            {
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );
            }
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

            DateTime serverTimeUtc;
            if (!parsed.ServerTimeMs.HasValue)
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );
            try
            {
                serverTimeUtc = DateTimeOffset
                    .FromUnixTimeMilliseconds(parsed.ServerTimeMs.Value)
                    .UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return ModApiHealthProbeResult.Failure(
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );
            }

            return ModApiHealthProbeResult.Success(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                parsed.Status,
                serverTimeUtc
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ModApiHealthProbeResult.FailureFrom(
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                new ModApiFailure("transport_error", diagnosticException: ex)
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
        ModApiFailure? failure
    )
    {
        Succeeded = succeeded;
        ProbedAtUtc = probedAtUtc;
        RoundTripMilliseconds = roundTripMilliseconds;
        Status = status;
        ServerTimeUtc = serverTimeUtc;
        FailureInfo = failure;
    }

    public bool Succeeded { get; }
    public DateTime ProbedAtUtc { get; }
    public long RoundTripMilliseconds { get; }
    public string? Status { get; }
    public DateTime? ServerTimeUtc { get; }
    public ModApiFailure? FailureInfo { get; }
    public string? Error => FailureInfo?.UserCode;
    public Exception? DiagnosticException => FailureInfo?.DiagnosticException;

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
    ) => FailureFrom(probedAtUtc, roundTripMilliseconds, new ModApiFailure(error));

    internal static ModApiHealthProbeResult FailureFrom(
        DateTime probedAtUtc,
        long roundTripMilliseconds,
        ModApiFailure failure
    ) => new(false, probedAtUtc, roundTripMilliseconds, null, null, failure);
}
