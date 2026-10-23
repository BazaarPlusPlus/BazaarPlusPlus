using System.Reflection;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

// The embedded-seed gate: the builds.json seed the desktop host bundles must parse with the
// production corpus parser (`scripts/build.sh` runs this project with -p:BppSeedGateOnly=true).
// Parser, recall, scoring, and board projection are tested Core-only in
// LiveBuildRecommendationCore.Tests.

SeedGateTests.Run();

internal static class SeedGateTests
{
    public static void Run()
    {
        RegisterAssemblyResolution();

        TestEmbeddedSeedResourceIsBundledAndParses();
        TestBuildRatingTierStaysDeleted();

        Console.WriteLine("LiveBuild embedded seed checks passed.");
    }

    private static void TestEmbeddedSeedResourceIsBundledAndParses()
    {
        var source = new AssemblyResourceCatalogSource(
            typeof(TenWinBuildCatalogFactory).Assembly,
            TenWinBuildCatalogFactory.EmbeddedResourceName
        );
        var json = source.ReadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert(
            !string.IsNullOrWhiteSpace(json),
            "The bundled builds.json seed should be embedded in the assembly."
        );

        var corpus = TenWinBuildCorpus.Parse(json);
        Assert(corpus != null, "The bundled seed should parse with the production corpus parser.");
        Assert(
            corpus!.HeroCount == 8,
            "The bundled seed should contain all eight canonical heroes."
        );
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(
            corpus.GeneratedAtUtc == DateTimeOffset.Parse("2026-08-12T02:00:00Z"),
            "The bundled seed should carry its deterministic generated_at timestamp."
        );
        Assert(
            corpus.WindowEndUtc == DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
            "The bundled seed should carry its deterministic window.end timestamp."
        );
#endif
        Assert(corpus.BuildCount > 0, "The bundled seed should count at least one build.");
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(corpus.BuildCount == 1, "The deterministic fallback should contain one seed build.");
#endif
        Assert(
            corpus.HeroBuildCounts.All(row => row.Hero != "Hero8"),
            "Any The Dragons data present in the embedded seed must use the canonical summary identity."
        );
#if !REMOTE_EMBEDDED_DATA_PREPARED
        Assert(
            corpus.HeroBuildCounts.Any(row => row.Hero == "TheDragons" && row.BuildCount == 0),
            "The deterministic fallback should retain an empty canonical TheDragons hero."
        );
#else
        Assert(
            corpus.HeroBuildCounts.Any(row => row.Hero == "TheDragons"),
            "Prepared data should retain the canonical TheDragons hero."
        );
#endif
    }

    private static void TestBuildRatingTierStaysDeleted()
    {
        Assert(
            typeof(TenWinBuildCatalogFactory).Assembly.GetType(
                "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRatingTier"
            ) == null,
            "BuildRatingTier must be deleted from the desktop host assembly."
        );
    }

    private static void RegisterAssemblyResolution()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            if (
                !string.Equals(
                    assemblyName.Name,
                    "UnityEngine.CoreModule",
                    StringComparison.Ordinal
                )
            )
                return null;

            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "UnityEngine.CoreModule.dll");
            return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
