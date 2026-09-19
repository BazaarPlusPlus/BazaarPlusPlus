using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class OutboundNetworkMainMenuArchitectureTests
{
    [Fact]
    public void Main_menu_controller_delegates_release_protocol_and_request_lifecycle()
    {
        var root = TestInputs.RepoRoot;
        var controller = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "Lobby",
                "MainMenuVersionCheckController.cs"
            )
        );
        var adapter = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Infrastructure",
                "ReleaseManifest",
                "ReleaseManifestClient.cs"
            )
        );
        var lifecycle = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "Lobby",
                "ReleaseManifestCheckLifecycle.cs"
            )
        );

        Assert.Contains("new ReleaseManifestClient", controller);
        Assert.Contains("_lifecycle.Begin(httpClient)", controller);
        Assert.Contains("_lifecycle.RunAsync(", controller);
        Assert.DoesNotContain("private HttpClient", controller, StringComparison.Ordinal);
        Assert.DoesNotContain(".GetAsync(", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonConvert", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("_generation", controller, StringComparison.Ordinal);
        Assert.Contains(".GetAsync(", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("UnityEngine", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("BepInEx", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("UnityEngine", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("BepInEx", lifecycle, StringComparison.Ordinal);
    }
}
