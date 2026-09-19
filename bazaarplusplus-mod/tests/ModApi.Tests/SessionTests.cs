#nullable enable
using System.Net;
using BazaarPlusPlus.ModApi.Clients;

internal static class SessionTests
{
    public static async Task RunAsync()
    {
        var handler = new TrackingHandler();
        using (
            var session =
                ModApiSession.TryCreate(
                    "https://api.example/path?ignored=1",
                    "5.1 beta",
                    "Online Client",
                    TimeSpan.FromSeconds(37),
                    handler
                ) ?? throw new InvalidOperationException("valid session")
        )
        {
            var health = await session.ProbeHealthAsync(CancellationToken.None);
            Assert(health.Succeeded, "typed health operation");
            Assert(
                handler.RequestUri?.ToString() == "https://api.example/health",
                "normalized route"
            );
            Assert(
                handler.UserAgent == "BazaarPlusPlus/5.1-beta Online-Client/5.1-beta",
                "standard user agent"
            );
        }
        Assert(handler.Disposed, "session owns handler transport");

        var unusedHandler = new TrackingHandler();
        var invalid = ModApiSession.TryCreate(
            "not-a-url",
            "1.0.0",
            "Test",
            TimeSpan.FromSeconds(1),
            unusedHandler
        );
        Assert(invalid == null, "invalid base URL has no session");
        Assert(!unusedHandler.Disposed, "invalid session does not take handler ownership");
        unusedHandler.Dispose();

        var uploadHandler = new TrackingHandler();
        var historyHandler = new TrackingHandler();
        var uploadSession = Create(uploadHandler, "BundleUpload");
        using var historySession = Create(historyHandler, "OnlineClient");
        uploadSession.Dispose();
        var independentHealth = await historySession.ProbeHealthAsync(CancellationToken.None);
        Assert(uploadHandler.Disposed, "upload session transport disposed");
        Assert(!historyHandler.Disposed, "history session transport remains live");
        Assert(independentHealth.Succeeded, "one session disposal cannot close another session");

        Console.WriteLine("SessionTests passed.");
    }

    private static ModApiSession Create(HttpMessageHandler handler, string suffix) =>
        ModApiSession.TryCreate(
            "https://api.example",
            "1.0.0",
            suffix,
            TimeSpan.FromSeconds(30),
            handler
        ) ?? throw new InvalidOperationException("valid session");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? UserAgent { get; private set; }
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"ok\",\"server_time_ms\":1780444800000}"
                    ),
                }
            );
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
