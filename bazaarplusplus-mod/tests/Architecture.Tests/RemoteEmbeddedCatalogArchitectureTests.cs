#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class RemoteEmbeddedCatalogArchitectureTests
{
    [Fact]
    public void Supporters_are_a_third_shared_catalog_consumer_owned_by_composition()
    {
        var getTempPath = (typeof(Path).FullName!, nameof(Path.GetTempPath));
        Holds(
            "The supporter facade projects the shared catalog; transport and cache paths live elsewhere.",
            build =>
            {
                var facade = build.Type("BazaarPlusPlus.Game.Supporters.BPPSupporterCatalog");
                return References(facade, Is(typeof(HttpClient).FullName!))
                    .Concat(Accesses(facade, getTempPath));
            }
        );
    }

    [Fact]
    public void Catalog_lifetimes_are_owned_by_composition_and_voice_module()
    {
        const string recommendations = "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations";
        var universe = CompiledArtifacts.Universe;
        var createCatalog = universe.RequireMember(
            recommendations + ".TenWinBuildCatalogFactory",
            "Create"
        );
        var newRepository = universe.RequireMember(
            recommendations + ".BuildRecommendationRepository",
            ".ctor"
        );
        Holds(
            "Composition creates and disposes the build catalog; the panel only consumes it.",
            build =>
                Accesses(
                    build.Type("BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel"),
                    createCatalog,
                    newRepository
                )
        );
    }

    [Fact]
    public void Catalog_cache_paths_are_anchored_to_game_root()
    {
        var dataPath = CompiledArtifacts.Universe.RequireMember(
            "UnityEngine.Application",
            "get_dataPath"
        );
        Holds(
            "Catalog caches are written under the game root, never Application.dataPath.",
            build =>
                Accesses(
                    new[]
                    {
                        build.Type(
                            "BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.TenWinBuildCatalogFactory"
                        ),
                        build.Type("BazaarPlusPlus.Game.VoiceSubtitles.VoiceLinesCatalogFactory"),
                    },
                    dataPath
                )
        );
    }
}
