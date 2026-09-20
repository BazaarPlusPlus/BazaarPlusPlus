#nullable enable
using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

public sealed class RemoteEmbeddedCatalogArchitectureTests
{
    [Fact]
    public void Supporters_are_a_third_shared_catalog_consumer_owned_by_composition()
    {
        var root = RepoRoot();
        var supporterRoot = Path.Combine(root, "src", "BazaarPlusPlus", "Game", "Supporters");
        var facade = File.ReadAllText(Path.Combine(supporterRoot, "BPPSupporterCatalog.cs"));
        var module = File.ReadAllText(Path.Combine(supporterRoot, "SupporterCatalogModule.cs"));
        var factory = File.ReadAllText(Path.Combine(supporterRoot, "SupporterCatalogFactory.cs"));
        var composition = File.ReadAllText(
            Path.Combine(root, "src", "BazaarPlusPlus", "BppComposition.cs")
        );

        Assert.Contains("IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>", module);
        Assert.Contains("RemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>", factory);
        Assert.Contains("new SupporterCatalogModule", composition);
        Assert.Contains("_featureRegistry.Register(_supporterCatalogModule)", composition);
        Assert.DoesNotContain("HttpClient", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.GetTempPath", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("Task", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("IDisposable", facade, StringComparison.Ordinal);
        Assert.Contains("SupporterLogEvents.CatalogDegraded", module);
        Assert.Contains("SupporterLogEvents.CatalogRecovered", module);
        Assert.Contains("SupporterLogEvents.CacheWriteDegraded", module);
        Assert.Contains("SupporterLogEvents.CacheWriteRecovered", module);
        Assert.True(
            module.IndexOf("catalog?.Dispose()", StringComparison.Ordinal)
                < module.IndexOf(
                    "BPPSupporterCatalog.DetachAndResetProjection()",
                    StringComparison.Ordinal
                ),
            "The catalog must be disposed before its synchronous projection is reset."
        );
    }

    [Fact]
    public void Shared_catalog_contract_and_state_machine_live_in_Infrastructure()
    {
        var sourceRoot = MainSourceRoot();
        var contracts = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Infrastructure",
                "RemoteEmbeddedCatalog",
                "RemoteEmbeddedCatalogContracts.cs"
            )
        );
        var implementation = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Infrastructure",
                "RemoteEmbeddedCatalog",
                "RemoteEmbeddedCatalog.cs"
            )
        );

        Assert.Contains("interface IRemoteEmbeddedCatalog<TSnapshot> : IDisposable", contracts);
        Assert.Contains("bool TryGet(out CatalogSnapshot<TSnapshot> snapshot);", contracts);
        Assert.Contains(
            "ValueTask WarmAsync(CancellationToken cancellationToken = default);",
            contracts
        );
        Assert.Contains("ValueTask<CatalogRefreshResult<TSnapshot>> RefreshAsync(", contracts);
        Assert.Contains("WarmFlight? _warmFlight", implementation);
        Assert.Contains("RefreshFlight? _refreshFlight", implementation);
        Assert.Contains("abstract class CatalogFlight", implementation);
        Assert.Contains("bool Abandoned", implementation);
        Assert.Contains("AbandonFlight(warmFlight, disposedByOwner: true)", implementation);
        Assert.Contains("AbandonFlight(refreshFlight, disposedByOwner: true)", implementation);
        Assert.Contains("SemaphoreSlim _remotePublishGate", implementation);
        Assert.Contains("object _observerSync", implementation);

        foreach (
            var featureFile in FeatureSources(
                Path.Combine("Game", "LiveBuildPanel"),
                Path.Combine("Game", "VoiceSubtitles")
            )
        )
        {
            var source = File.ReadAllText(featureFile);
            Assert.DoesNotContain("_warmUpTask", source);
            Assert.DoesNotContain("_backgroundRefreshInProgress", source);
            Assert.DoesNotContain("_attemptedLoad", source);
            Assert.DoesNotContain("TaskCompletionSource", source);
            Assert.DoesNotContain("ICatalogRefreshScheduler", source);
            Assert.DoesNotContain("ICatalogClock", source);
        }
    }

    [Fact]
    public void Legacy_feature_catalog_loaders_and_test_hooks_are_removed()
    {
        var sourceRoot = MainSourceRoot();
        Assert.False(
            File.Exists(
                Path.Combine(
                    sourceRoot,
                    "Game",
                    "LiveBuildPanel",
                    "Recommendations",
                    "BuildRecommendationCorpusLoadSelector.cs"
                )
            )
        );
        Assert.False(
            File.Exists(
                Path.Combine(sourceRoot, "Game", "VoiceSubtitles", "VoiceLinesRepository.cs")
            )
        );

        var allFeatureSource = string.Join(
            "\n",
            FeatureSources(
                    Path.Combine("Game", "LiveBuildPanel"),
                    Path.Combine("Game", "VoiceSubtitles")
                )
                .Select(File.ReadAllText)
        );
        Assert.DoesNotContain("ConfigureTenWinRemoteForTests", allFeatureSource);
        Assert.DoesNotContain("ConfigureVoiceLines", allFeatureSource);
        Assert.DoesNotContain("BuildRecommendationCorpusLoadSelector", allFeatureSource);
        Assert.DoesNotContain("VoiceLinesRepository", allFeatureSource);
    }

    [Fact]
    public void Catalog_lifetimes_are_owned_by_composition_and_voice_module()
    {
        var sourceRoot = MainSourceRoot();
        var composition = File.ReadAllText(Path.Combine(sourceRoot, "BppComposition.cs"));
        var panel = File.ReadAllText(
            Path.Combine(sourceRoot, "Game", "LiveBuildPanel", "LiveBuildPanel.cs")
        );
        var voiceModule = File.ReadAllText(
            Path.Combine(sourceRoot, "Game", "VoiceSubtitles", "VoiceSubtitlesModule.cs")
        );

        Assert.Contains("IRemoteEmbeddedCatalog<TenWinBuildCorpus>", composition);
        Assert.Contains("TenWinBuildCatalogFactory.Create(", composition);
        Assert.Contains("_buildRecommendationCatalog.Dispose();", composition);
        Assert.DoesNotContain("new BuildRecommendationRepository", panel);
        Assert.DoesNotContain("TenWinBuildCatalogFactory.Create", panel);
        Assert.DoesNotContain("_buildRecommendationCatalog.Dispose", panel);

        Assert.Contains("IRemoteEmbeddedCatalog<VoiceLine[]>", voiceModule);
        var dispose = voiceModule.IndexOf("_catalog.Dispose();", StringComparison.Ordinal);
        var reset = voiceModule.IndexOf(
            "VoiceLineCatalog.Reset();",
            dispose,
            StringComparison.Ordinal
        );
        Assert.True(dispose >= 0, "VoiceSubtitlesModule.Stop must dispose its catalog.");
        Assert.True(
            reset > dispose,
            "Voice catalog reset must follow disposal to reject late publishes."
        );
    }

    [Fact]
    public void Catalog_cache_paths_are_anchored_to_game_root()
    {
        var sourceRoot = MainSourceRoot();
        var factories = new[]
        {
            Path.Combine(
                sourceRoot,
                "Game",
                "LiveBuildPanel",
                "Recommendations",
                "TenWinBuildCatalogFactory.cs"
            ),
            Path.Combine(sourceRoot, "Game", "VoiceSubtitles", "VoiceLinesCatalogFactory.cs"),
        };

        foreach (var factory in factories)
        {
            var source = File.ReadAllText(factory);
            Assert.Contains("dataRootPath", source);
            Assert.DoesNotContain("Application.dataPath", source);
        }
    }

    private static IEnumerable<string> FeatureSources(params string[] relativeDirectories)
    {
        var sourceRoot = MainSourceRoot();
        return relativeDirectories.SelectMany(relative =>
            Directory.EnumerateFiles(
                Path.Combine(sourceRoot, relative),
                "*.cs",
                SearchOption.AllDirectories
            )
        );
    }

    private static string MainSourceRoot() => Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", ".."));
    }
}
