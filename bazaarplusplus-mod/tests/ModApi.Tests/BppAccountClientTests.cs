#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

public sealed class BppAccountClientTests
{
    [Fact]
    public async Task AccountRoutesAndDeviceTokenHeadersMatchContract()
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
            Json(
                HttpStatusCode.OK,
                """{"device_token":"bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","user_name":"Alice","email_masked":"a***@example.com"}"""
            )
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
        Assert.Equal("bppd_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", bind.DeviceToken);
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

        // 4. account/devices 200
        responses.Enqueue(
            Json(
                HttpStatusCode.OK,
                """{"device_token":"bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","user_name":"Alice","email_masked":"a***@example.com"}"""
            )
        );
        var device = await session.IssueBppAccountDeviceTokenAsync(
            "jwe.token.2",
            "acct-1",
            "Steam",
            "Paid",
            CancellationToken.None
        );
        Assert.True(device.Succeeded);
        Assert.Equal("bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", device.DeviceToken);

        // 5. account/me 200 and 401
        responses.Enqueue(
            Json(HttpStatusCode.OK, """{"user_name":"Alice","email_masked":"a***@example.com"}""")
        );
        var me = await session.GetBppAccountMeAsync(
            "bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            CancellationToken.None
        );
        Assert.True(me.Succeeded);
        Assert.Equal("Alice", me.UserName);
        Assert.Equal("a***@example.com", me.EmailMasked);

        responses.Enqueue(
            Json(
                HttpStatusCode.Unauthorized,
                """{"error":{"code":"unauthorized","message":"bad token","retryable":false,"request_id":"r2"}}"""
            )
        );
        var unauth = await session.GetBppAccountMeAsync(
            "bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            CancellationToken.None
        );
        Assert.False(unauth.Succeeded);
        Assert.True(unauth.IsUnauthorized);

        // 6. UploadBundleAsync & QueryGhostBattlesAgainstMeAsync attach Bearer only when token is given
        responses.Enqueue(
            Json(
                HttpStatusCode.Created,
                """{"bundle_id":"01J00000000000000000000001","run_id":"run-1","outcome":"stored"}"""
            )
        );
        using (var payload = new MemoryStream(Encoding.UTF8.GetBytes("bundle")))
        {
            var uploaded = await session.UploadBundleAsync(
                payload,
                payload.Length,
                "sha-256=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=:",
                "01J00000000000000000000001",
                "run-1",
                CancellationToken.None,
                "bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            );
            Assert.Equal(BundleUploadDisposition.Uploaded, uploaded.Disposition);
        }

        responses.Enqueue(Json(HttpStatusCode.OK, """{"battles":[]}"""));
        var ghostsWithToken = await session.QueryGhostBattlesAgainstMeAsync(
            "acct-1",
            20,
            CancellationToken.None,
            "bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
        );
        Assert.True(ghostsWithToken.Succeeded);

        responses.Enqueue(Json(HttpStatusCode.OK, """{"battles":[]}"""));
        var ghostsWithoutToken = await session.QueryGhostBattlesAgainstMeAsync(
            "acct-2",
            20,
            CancellationToken.None,
            deviceToken: null
        );
        Assert.True(ghostsWithoutToken.Succeeded);

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
        Assert.Equal("/account/devices", requests[3].Path);
        Assert.Equal("/account/me", requests[4].Path);
        Assert.Equal("Bearer bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", requests[4].Auth);
        Assert.Equal("Bearer bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", requests[6].Auth);
        Assert.Equal("Bearer bppd_BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", requests[7].Auth);
        Assert.Null(requests[8].Auth);
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
