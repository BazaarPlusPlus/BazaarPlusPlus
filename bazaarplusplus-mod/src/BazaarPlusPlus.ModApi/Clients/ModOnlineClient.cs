#nullable enable
using System;
using System.Net.Http;

namespace BazaarPlusPlus.ModApi.Clients;

public sealed class ModOnlineClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ModApiRoutes _routes;

    public ModOnlineClient(HttpClient httpClient, ModApiRoutes routes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public HttpClient HttpClient => _httpClient;

    public ModApiRoutes Routes => _routes;

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
