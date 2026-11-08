using System.Net.Http.Headers;
using System.Text.Json;

namespace BazaarPlusPlus.HistorySampler;

/// <summary>
/// The server's bundle_sync collection (<c>GET /bundles</c>, docs/api-reference.md in the server):
/// one fixed window of recent Bundles, each with a presigned download URL. The analyzer reads the
/// same feed (bazaarlens/bundle_source.py).
/// </summary>
internal sealed class BundleFeed : IDisposable
{
    // The server keeps Bundles for eight days and serves only windows that end a minute ago.
    public const int MaxLookbackHours = 8 * 24 - 1;
    private const long SettleLagMs = 120_000;
    private const int PageLimit = 200;
    private const long MaxBundleBytes = 64L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;

    public BundleFeed(string baseUrl, string token)
    {
        _baseUrl = new Uri(baseUrl.TrimEnd('/') + "/");
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BazaarPlusPlus-HistorySampler/1");
    }

    public sealed record Item(string BundleId, long AvailableAtMs, string DownloadUrl);

    /// <summary>Every Bundle that became available in the last <paramref name="hours"/>.</summary>
    public async Task<List<Item>> ListAsync(int hours, CancellationToken cancellation)
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - SettleLagMs;
        var from = before - Math.Clamp(hours, 1, MaxLookbackHours) * 3_600_000L;
        var items = new List<Item>();
        (long At, string Id)? cursor = null;
        while (true)
        {
            var query =
                $"bundles?available_from_ms={from}&available_before_ms={before}&limit={PageLimit}"
                + (
                    cursor is { } after
                        ? $"&after_available_at_ms={after.At}&after_bundle_id={Uri.EscapeDataString(after.Id)}"
                        : string.Empty
                );
            using var response = await _http.GetAsync(new Uri(_baseUrl, query), cancellation);
            var body = await response.Content.ReadAsStringAsync(cancellation);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"GET /bundles answered {(int)response.StatusCode}: {Trim(body)}"
                );
            using var page = JsonDocument.Parse(body);
            foreach (var item in page.RootElement.GetProperty("items").EnumerateArray())
                items.Add(
                    new Item(
                        item.GetProperty("bundle_id").GetString()!,
                        item.GetProperty("available_at_ms").GetInt64(),
                        item.GetProperty("download_url").GetString()!
                    )
                );
            var next = page.RootElement.GetProperty("next_after");
            if (next.ValueKind != JsonValueKind.Object)
                return items;
            cursor = (
                next.GetProperty("available_at_ms").GetInt64(),
                next.GetProperty("bundle_id").GetString()!
            );
        }
    }

    public async Task<byte[]> DownloadAsync(Item item, CancellationToken cancellation)
    {
        // The presigned URL carries its own authorization; the bearer token stays with the API.
        using var request = new HttpRequestMessage(HttpMethod.Get, item.DownloadUrl);
        request.Headers.Authorization = null;
        using var plain = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var response = await plain.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation
        );
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Bundle {item.BundleId} download answered {(int)response.StatusCode}."
            );
        if (response.Content.Headers.ContentLength > MaxBundleBytes)
            throw new InvalidOperationException($"Bundle {item.BundleId} is too large.");
        return await response.Content.ReadAsByteArrayAsync(cancellation);
    }

    private static string Trim(string text) => text.Length <= 300 ? text : text[..300];

    public void Dispose() => _http.Dispose();
}
