using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

public sealed class V5DataPipelineArchitectureTests
{
    [Fact]
    public void ModApi_has_only_v5_bundle_routes_and_no_multipart_wire()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.ModApi");
        var source = ReadSources(root);
        Assert.Contains("/bundles", source);
        Assert.Contains("/ghost-battles", source);
        Assert.DoesNotContain("mod-api-v4", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/run-bundles", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("multipart", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bazaardb/snapshots", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_has_one_bundle_feed_and_v5_data_root()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var main = ReadSources(Path.Combine(sourceRoot, "BazaarPlusPlus"));
        var storage = ReadSources(Path.Combine(sourceRoot, "BazaarPlusPlus.Storage"));
        Assert.Contains("BazaarPlusPlusV5", storage);
        Assert.Contains("enum UploadFeedKind\n{\n    Bundle,", main);
        Assert.DoesNotContain("RunBundleUploadFeed", main);
        Assert.DoesNotContain("BazaarDbSnapshotUploadFeed", main);
        Assert.DoesNotContain("RunSyncStateStore", main);
        Assert.DoesNotContain("BattleReplaySyncStateStore", main);
        Assert.DoesNotContain("ReplicatedRunLogStore", main);
    }

    [Fact]
    public void Supporter_cache_is_rooted_in_the_shared_v5_data_directory()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var legacyDirectoryName = "BazaarPlusPlus" + "V4";
        Assert.DoesNotContain(
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains(legacyDirectoryName, StringComparison.Ordinal)
        );

        var facade = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "Supporters",
                "BPPSupporterCatalog.cs"
            )
        );
        var factory = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "Supporters",
                "SupporterCatalogFactory.cs"
            )
        );
        Assert.DoesNotContain("Path.GetTempPath", facade, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(dataRootPath, CacheFileName)", factory);
    }

    [Fact]
    public void Run_bundle_writer_and_ghost_reader_share_one_replayability_contract()
    {
        var mainRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        var composer = File.ReadAllText(
            Path.Combine(mainRoot, "Game", "BundlePipeline", "RunPayloadComposer.cs")
        );
        var ghostReader = File.ReadAllText(
            Path.Combine(mainRoot, "Game", "HistoryPanel", "Ghost", "GhostBattleSyncService.cs")
        );

        Assert.Contains("RunBundleV5Contract.IsReplayable", composer, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static bool IsReplayable",
            composer,
            StringComparison.Ordinal
        );
        Assert.Contains("RunBundleV5Contract.Open", ghostReader, StringComparison.Ordinal);
        Assert.DoesNotContain("RunPayloadV5Codec", ghostReader, StringComparison.Ordinal);
        Assert.DoesNotContain("HasCompleteSnapshots", ghostReader, StringComparison.Ordinal);
    }

    [Fact]
    public void ModApi_has_one_owner_for_messagepack_gzip_framing()
    {
        var root = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.ModApi");
        var owners = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("GZipStream", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(new[] { "MessagePackGzipFraming.cs" }, owners);
    }

    [Fact]
    public void ModApi_clients_delegate_shared_response_metadata_and_error_envelopes()
    {
        var modApiRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus.ModApi");
        var clients = ReadSources(Path.Combine(modApiRoot, "Clients"));

        Assert.DoesNotContain("Headers.RetryAfter", clients, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Request-Id", clients, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[\"error\"]", clients, StringComparison.Ordinal);
        var response = File.ReadAllText(Path.Combine(modApiRoot, "Http", "ModApiResponse.cs"));
        Assert.Contains("$\"http_{statusCode}\"", response, StringComparison.Ordinal);
        Assert.DoesNotContain("Normalize(error.Code)", response, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(modApiRoot, "ModApiErrorFormatter.cs")));
        Assert.False(File.Exists(Path.Combine(modApiRoot, "Http", "ModApiJsonPost.cs")));
    }

    [Fact]
    public void ModApi_session_is_the_only_public_mod_backend_client_chain()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var modApiRoot = Path.Combine(sourceRoot, "BazaarPlusPlus.ModApi");
        var clientsRoot = Path.Combine(modApiRoot, "Clients");
        var session = File.ReadAllText(Path.Combine(clientsRoot, "ModApiSession.cs"));
        var game = ReadSources(Path.Combine(sourceRoot, "BazaarPlusPlus"));

        Assert.Contains("new BundleUploadClient", session, StringComparison.Ordinal);
        Assert.Contains("new GhostBattleClient", session, StringComparison.Ordinal);
        Assert.Contains("new ModApiHealthClient", session, StringComparison.Ordinal);
        Assert.DoesNotContain("public HttpClient", session, StringComparison.Ordinal);
        Assert.DoesNotContain("public ModApiRoutes", session, StringComparison.Ordinal);
        Assert.DoesNotContain("new BundleUploadClient", game, StringComparison.Ordinal);
        Assert.DoesNotContain("new GhostBattleClient", game, StringComparison.Ordinal);
        Assert.DoesNotContain("new ModApiHealthClient", game, StringComparison.Ordinal);
        Assert.DoesNotContain("ModOnlineClient", ReadSources(sourceRoot), StringComparison.Ordinal);
        Assert.Contains(
            "internal sealed class ModApiRoutes",
            File.ReadAllText(Path.Combine(modApiRoot, "ModApiRoutes.cs")),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Bundle_queue_store_is_the_only_runtime_owner_of_queue_sql()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var storageRoot = Path.Combine(sourceRoot, "BazaarPlusPlus.Storage");
        var gameRoot = Path.Combine(sourceRoot, "BazaarPlusPlus", "Game", "BundlePipeline");
        var queueStore = File.ReadAllText(
            Path.Combine(storageRoot, "BundleQueue", "BundleQueueStore.cs")
        );
        var schema = File.ReadAllText(Path.Combine(storageRoot, "RunLog", "RunLogSchema.cs"));
        var coordinator = File.ReadAllText(Path.Combine(gameRoot, "BundleSealCoordinator.cs"));
        var feed = File.ReadAllText(Path.Combine(gameRoot, "BundleUploadFeed.cs"));
        var files = File.ReadAllText(Path.Combine(gameRoot, "BundleOutboxFiles.cs"));

        Assert.Contains("BundleSealJobsTableName", queueStore);
        Assert.Contains("BundleOutboxTableName", queueStore);
        Assert.Contains("BundleSealJobsTableName", schema);
        Assert.Contains("BundleOutboxTableName", schema);
        Assert.DoesNotContain("BundleSealJobsTableName", coordinator);
        Assert.DoesNotContain("BundleOutboxTableName", coordinator);
        Assert.DoesNotContain("SqliteConnection", coordinator);
        Assert.DoesNotContain("CommandText", coordinator);
        Assert.DoesNotContain("BundleSealJobsTableName", feed);
        Assert.DoesNotContain("BundleOutboxTableName", feed);
        Assert.DoesNotContain("SqliteConnection", feed);
        Assert.DoesNotContain("CommandText", feed);
        Assert.Contains("IBundleOutboxFiles", files);
        Assert.Contains("_queueStore.RecordOutcome", feed);
        Assert.DoesNotContain("ScreenshotState == \"", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UpdateScreenshotState(runId, \"",
            coordinator,
            StringComparison.Ordinal
        );
        var attempt = feed[
            ..feed.IndexOf("public IDisposable? SubscribeArmSignals", StringComparison.Ordinal)
        ];
        Assert.True(
            attempt.IndexOf("ApplyResponse(row, response)", StringComparison.Ordinal)
                < attempt.IndexOf("_files.Delete(row.FileName)", StringComparison.Ordinal),
            "Upload state must commit before the sidecar is deleted."
        );
    }

    [Fact]
    public void History_panel_mount_keeps_local_mode_when_mod_api_is_unavailable()
    {
        var historyRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game", "HistoryPanel");
        var plan = File.ReadAllText(Path.Combine(historyRoot, "HistoryPanelMountPlan.cs"));
        var mount = File.ReadAllText(Path.Combine(historyRoot, "HistoryPanelMount.cs"));

        Assert.Contains("MountLocalOnly", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("OnlineClient", mount, StringComparison.Ordinal);
        Assert.Contains("host.AddComponent<HistoryPanel>()", mount, StringComparison.Ordinal);
    }

    private static string ReadSources(string root) =>
        string.Join(
            "\n",
            Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
        );

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
