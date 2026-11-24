#nullable enable
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.HistoryPanel.BppAccount;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class BundleUploadDeviceTokenTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "bpp-upload-device-token-" + Guid.NewGuid().ToString("N")
    );

    public BundleUploadDeviceTokenTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task UploadAttachesDeviceTokenOnlyForMatchingActiveAccount()
    {
        var dbPath = Path.Combine(_root, "runs.sqlite3");
        var outboxRoot = Path.Combine(_root, "outbox");
        Directory.CreateDirectory(outboxRoot);
        var sessionStore = new BppAccountSessionStore(Path.Combine(_root, "bpp-account.json"));
        sessionStore.Save(
            new BppStoredAccountSession(
                "acct-a",
                "bppd_upload_secret_token",
                "PlayerA",
                "a***@qq.com"
            )
        );

        var queue = new BundleQueueStore(dbPath);
        EnqueueBundle(dbPath, outboxRoot, "run-1", "bundle-1");
        EnqueueBundle(dbPath, outboxRoot, "run-2", "bundle-2");

        var observedHeaders = new List<string?>();
        var handler = new CaptureAuthHandler(observedHeaders);
        using var modApi =
            ModApiSession.TryCreate(
                "https://example.test",
                "test",
                platformKey: null,
                "BundleUpload",
                TimeSpan.FromSeconds(30),
                handler
            ) ?? throw new InvalidOperationException("test Mod API session");

        using var session = new BundleUploadFeed.Session(
            new TestServices(new TestPaths(_root)),
            retryIntervalSeconds: 60,
            modApi,
            queue,
            new SystemBundleOutboxFiles(outboxRoot),
            utcNow: () => DateTimeOffset.UtcNow,
            jitterSample: () => 0.5d,
            deviceTokenForAccount: sessionStore.TryReadDeviceTokenForAccount
        );

        session.UpdateCurrentAccountId("acct-a");
        await session.RunAttemptAsync(CancellationToken.None);

        // Both bundles uploaded in the first batch under acct-a; now enqueue one more under acct-b.
        EnqueueBundle(dbPath, outboxRoot, "run-3", "bundle-3");
        session.UpdateCurrentAccountId("acct-b");
        await session.RunAttemptAsync(CancellationToken.None);

        Assert.Equal(3, observedHeaders.Count);
        Assert.Equal("Bearer bppd_upload_secret_token", observedHeaders[0]);
        Assert.Equal("Bearer bppd_upload_secret_token", observedHeaders[1]);
        Assert.Null(observedHeaders[2]);
    }

    private static void EnqueueBundle(
        string dbPath,
        string outboxRoot,
        string runId,
        string bundleId
    )
    {
        var bytes = Encoding.UTF8.GetBytes($"bundle-bytes-{bundleId}");
        var fileName = $"{bundleId}.bundle";
        File.WriteAllBytes(Path.Combine(outboxRoot, fileName), bytes);
        var sha256Hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var contentDigest = $"sha-256=:{Convert.ToBase64String(SHA256.HashData(bytes))}:";
        var nowMs = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_ms, status, next_attempt_at_ms) VALUES ($bundleId, $runId, $fileName, $sha, $digest, $length, 0, $nowMs, 'pending', $nowMs);";
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$sha", sha256Hex);
        command.Parameters.AddWithValue("$digest", contentDigest);
        command.Parameters.AddWithValue("$length", bytes.Length);
        command.Parameters.AddWithValue("$nowMs", nowMs);
        command.ExecuteNonQuery();
    }

    private sealed class CaptureAuthHandler(List<string?> headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            headers.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"status\":\"accepted\"}"),
                }
            );
        }
    }
}
