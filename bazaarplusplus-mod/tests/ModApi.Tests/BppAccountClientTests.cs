#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

public sealed class BppAccountClientTests
{
    [Fact]
    public async Task BindRouteMatchesContract()
    {
        var requests = new List<(HttpMethod Method, string Path, string? Auth, string? Body)>();
        var responses = new Queue<HttpResponseMessage>();
        var handler = new StubHandler(
            async (req, _) =>
            {
                var body =
                    req.Content == null
                        ? null
                        : await req.Content.ReadAsStringAsync().ConfigureAwait(false);
                requests.Add(
                    (
                        req.Method,
                        req.RequestUri!.AbsolutePath,
                        req.Headers.Authorization?.ToString(),
                        body
                    )
                );
                return responses.Dequeue();
            }
        );

        using var session =
            ModApiSession.TryCreate(
                "https://mod-api-v6.bazaarplusplus.com",
                "6.0.0",
                "macos-arm64",
                "Test",
                TimeSpan.FromSeconds(15),
                handler
            ) ?? throw new InvalidOperationException("valid session");

        // 1. bind 200
        responses.Enqueue(
            Json(HttpStatusCode.OK, """{"user_name":"Alice","email_masked":"a***@example.com"}""")
        );
        var bind = await session.BindBppAccountAsync(
            "K7M2-Q9XR",
            "jwe.token.1",
            "acct-1",
            "Steam",
            "Paid",
            CancellationToken.None
        );
        Assert.True(bind.Succeeded);
        Assert.Equal("Alice", bind.UserName);
        Assert.Equal("a***@example.com", bind.EmailMasked);

        // 2. bind 400 invalid_bind_code
        responses.Enqueue(
            Json(
                HttpStatusCode.BadRequest,
                """{"error":{"code":"invalid_bind_code","message":"bad code","retryable":false,"request_id":"r1"}}"""
            )
        );
        var invalid = await session.BindBppAccountAsync(
            "WRONG777",
            "jwe.token.1",
            "acct-1",
            "Steam",
            "Paid",
            CancellationToken.None
        );
        Assert.False(invalid.Succeeded);
        Assert.Equal("invalid_bind_code", invalid.ErrorCode);

        // 3. bind 409 game_account_already_bound
        responses.Enqueue(
            Json(
                HttpStatusCode.Conflict,
                """{"error":{"code":"game_account_already_bound","message":"already bound","retryable":false,"request_id":"r2"}}"""
            )
        );
        var conflict = await session.BindBppAccountAsync(
            "K7M2-Q9XR",
            "jwe.token.1",
            "acct-1",
            "Steam",
            "Paid",
            CancellationToken.None
        );
        Assert.False(conflict.Succeeded);
        Assert.Equal("game_account_already_bound", conflict.ErrorCode);

        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal("/account/bind", requests[0].Path);
        using (var bindBody = JsonDocument.Parse(requests[0].Body!))
        {
            var root = bindBody.RootElement;
            Assert.Equal("K7M2-Q9XR", root.GetProperty("bind_code").GetString());
            Assert.Equal("jwe.token.1", root.GetProperty("game_token").GetString());
            Assert.Equal("acct-1", root.GetProperty("game_account_id").GetString());
            Assert.Equal("Steam", root.GetProperty("platform").GetString());
            Assert.Equal("Paid", root.GetProperty("flavor").GetString());
        }
        Assert.Null(requests[0].Auth);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }
}
