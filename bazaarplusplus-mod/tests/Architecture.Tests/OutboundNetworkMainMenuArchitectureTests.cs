#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

/// <summary>Outbound network ownership for the main-menu release check (ADR-0006).</summary>
public sealed class OutboundNetworkMainMenuArchitectureTests
{
    [Fact]
    public void Main_menu_controller_delegates_release_protocol_and_request_lifecycle()
    {
        var jsonConvert = CompiledArtifacts.Universe.RequireType("Newtonsoft.Json.JsonConvert");
        var getAsync = (typeof(HttpClient).FullName!, nameof(HttpClient.GetAsync));
        Holds(
            "The controller delegates HTTP and JSON to ReleaseManifestClient, which stays engine-free.",
            build =>
            {
                var controller = build.Type(
                    "BazaarPlusPlus.Game.Lobby.MainMenuVersionCheckController"
                );
                var engineFree = new[]
                {
                    build.Type(
                        "BazaarPlusPlus.Infrastructure.ReleaseManifest.ReleaseManifestClient"
                    ),
                    build.Type("BazaarPlusPlus.Game.Lobby.ReleaseManifestCheckLifecycle"),
                };
                var engine = FromAssembly(
                    RequireAssemblyPrefix(build, "UnityEngine"),
                    RequireAssemblyPrefix(build, "BepInEx")
                );
                return Accesses(controller, getAsync)
                    .Concat(References(controller, Is(jsonConvert)))
                    .Concat(References(engineFree, engine));
            }
        );
    }
}
