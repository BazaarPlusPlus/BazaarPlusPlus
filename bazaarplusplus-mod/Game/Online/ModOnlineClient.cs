#nullable enable
using System;
using System.Net.Http;

namespace BazaarPlusPlus.Game.Online;

internal sealed class ModOnlineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly V3Routes _routes;

    public ModOnlineClient(HttpClient httpClient, V3Routes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public HttpClient HttpClient => _httpClient;

    public V3Routes Routes => _routes;

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
