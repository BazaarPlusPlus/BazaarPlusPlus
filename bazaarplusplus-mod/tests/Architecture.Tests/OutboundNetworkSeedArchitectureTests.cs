using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class OutboundNetworkSeedArchitectureTests
{
    [Fact]
    public void Build_seed_target_delegates_transport_and_contains_no_inline_csharp()
    {
        var root = TestInputs.RepoRoot;
        var targets = File.ReadAllText(
            Path.Combine(root, "src", "BazaarPlusPlus", "RemoteEmbeddedData.targets")
        );
        var fetcher = File.ReadAllText(
            Path.Combine(root, "build", "RemoteEmbeddedDataFetcher", "RemoteEmbeddedDataFetch.cs")
        );
        var script = File.ReadAllText(Path.Combine(root, "run.sh"));

        Assert.Contains("RemoteEmbeddedDataFetcherProject", targets);
        Assert.Contains("<Exec", targets);
        Assert.DoesNotContain("RoslynCodeTaskFactory", targets);
        Assert.DoesNotContain("<![CDATA[", targets);
        Assert.Contains("HttpClient", fetcher);
        Assert.Contains("PromoteSeedSet", fetcher);
        Assert.Contains("run_seed_gates", script);
        Assert.Contains("TestKind=EmbeddedSeed", script);
        Assert.Contains("promote \"$staging_directory\"", script);
        Assert.Contains("for arg in \"$@\"", script);
        Assert.DoesNotContain("local args=(\"$@\")", script);
    }
}
