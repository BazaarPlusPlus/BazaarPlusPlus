#nullable enable
using BazaarPlusPlus.TestSupport;
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class BundleSealConvergenceArchitectureTests
{
    [Fact]
    public void Convergence_core_is_dependency_free_and_time_is_relative()
    {
        Holds(
            "BundleSealConvergence decides from relative seconds; clocks, tasks, and storage stay outside.",
            build =>
            {
                _ = build.TypesIn("BazaarPlusPlus.Storage");
                return References(
                    build.Type("BazaarPlusPlus.Game.BundlePipeline.BundleSealConvergence"),
                    type =>
                        Is(typeof(DateTime).FullName!, typeof(DateTimeOffset).FullName!)(type)
                        || InNamespace(typeof(Task).Namespace!, "BazaarPlusPlus.Storage")(type)
                );
            }
        );

        var project = TestInputs.MsBuild(
            "tests/BundleSealConvergence.Tests/BundleSealConvergence.Tests.csproj"
        );
        Assert.DoesNotContain(
            project.Descendants(),
            element =>
                element.Name.LocalName == "ManagedPath"
                || (
                    !element.HasElements
                    && element.Value.Contains("$(ManagedPath)", StringComparison.Ordinal)
                )
                || element
                    .Attributes()
                    .Any(attribute =>
                        attribute.Value.Contains("$(ManagedPath)", StringComparison.Ordinal)
                    )
        );
    }
}
