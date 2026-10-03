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
                    stopwatch.ElapsedMilliseconds,
                    "health_status_not_ok"
                );
            }

            if (!parsed.ServerTimeMs.HasValue)
                return ModApiHealthProbeResult.Failure(
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );
            // server_time_ms is part of the health contract: reject an out-of-range value.
            try
            {
                _ = DateTimeOffset.FromUnixTimeMilliseconds(parsed.ServerTimeMs.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ModApiHealthProbeResult.Failure(
                    stopwatch.ElapsedMilliseconds,
                    "server_time_invalid"
                );
            }

            return ModApiHealthProbeResult.Success(stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ModApiHealthProbeResult.FailureFrom(
                stopwatch.ElapsedMilliseconds,
                new ModApiFailure("transport_error", diagnosticException: ex)
            );
        }
    }
}

internal readonly struct ModApiHealthProbeResult
{
    private ModApiHealthProbeResult(
        bool succeeded,
        long roundTripMilliseconds,
        ModApiFailure? failure
    )
    {
        Succeeded = succeeded;
        RoundTripMilliseconds = roundTripMilliseconds;
        FailureInfo = failure;
    }

    public bool Succeeded { get; }
    public long RoundTripMilliseconds { get; }
    public ModApiFailure? FailureInfo { get; }
    public string? Error => FailureInfo?.UserCode;
    public Exception? DiagnosticException => FailureInfo?.DiagnosticException;

    public static ModApiHealthProbeResult Success(long roundTripMilliseconds) =>
        new(true, roundTripMilliseconds, null);

    public static ModApiHealthProbeResult Failure(long roundTripMilliseconds, string error) =>
        FailureFrom(roundTripMilliseconds, new ModApiFailure(error));

    internal static ModApiHealthProbeResult FailureFrom(
        long roundTripMilliseconds,
        ModApiFailure failure
    ) => new(false, roundTripMilliseconds, failure);
}
