#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class BundleSealConvergenceArchitectureTests
{
    [Fact]
    public void Convergence_core_is_dependency_free_and_time_is_relative()
    {
        foreach (
            var core in new[]
            {
                "BundleSealConvergence",
                "BundleSealFailurePolicy",
                "BundleSealWakeSchedule",
            }
        )
            Holds(
                $"{core} decides from relative seconds; clocks, tasks, and storage stay outside.",
                build =>
                {
                    _ = build.TypesIn("BazaarPlusPlus.Storage");
                    return References(
                        build.Type("BazaarPlusPlus.Game.BundlePipeline." + core),
                        type =>
                            Is(typeof(DateTime).FullName!, typeof(DateTimeOffset).FullName!)(type)
                            || InNamespace(typeof(Task).Namespace!, "BazaarPlusPlus.Storage")(type)
                    );
                }
            );
    }
}
