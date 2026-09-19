using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

public sealed class BundleSealConvergenceArchitectureTests
{
    [Fact]
    public void Convergence_core_is_dependency_free_and_time_is_relative()
    {
        var root = RepoRoot();
        var core = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "BundlePipeline",
                "BundleSealConvergence.cs"
            )
        );
        var coordinator = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus",
                "Game",
                "BundlePipeline",
                "BundleSealCoordinator.cs"
            )
        );
        var queueStore = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BazaarPlusPlus.Storage",
                "BundleQueue",
                "BundleQueueStore.cs"
            )
        );
        var testProject = File.ReadAllText(
            Path.Combine(
                root,
                "tests",
                "BundleSealConvergence.Tests",
                "BundleSealConvergence.Tests.csproj"
            )
        );

        Assert.DoesNotContain("using ", core, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime", core, StringComparison.Ordinal);
        Assert.DoesNotContain("Task", core, StringComparison.Ordinal);
        Assert.DoesNotContain("Storage", core, StringComparison.Ordinal);
        Assert.Contains("float secondsUntilInputDeadline", core, StringComparison.Ordinal);
        Assert.Contains("SqliteUtcInstant.Parse", queueStore, StringComparison.Ordinal);
        Assert.Contains("job.InputDeadlineAtUtc - now", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("<ManagedPath>", testProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$(ManagedPath)", testProject, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
