#nullable enable
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class OutboundNetworkSeedArchitectureTests
{
    // The fetcher and build.sh seed gates are verified by running `just mod::fetch-data`.
    [Fact]
    public void Build_seed_target_delegates_transport_to_the_fetcher()
    {
        var targets = TestInputs.MsBuild("src/BazaarPlusPlus/RemoteEmbeddedData.targets");
        var elements = targets.Descendants().ToArray();

        Assert.Contains(
            elements,
            element =>
                element.Name.LocalName == "Exec"
                && ((string?)element.Attribute("Command") ?? "").Contains(
                    "$(RemoteEmbeddedDataFetcherProject)",
                    StringComparison.Ordinal
                )
        );
    }
}
