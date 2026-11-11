using Xunit;

namespace ScenarioRunner.Tests;

/// <summary>
/// A capsule slow enough to run beside the rest. xUnit runs test classes in parallel and the
/// tests of one class in series, so <see cref="ScenarioRunnerTests"/> runs every other capsule one
/// after another, and each subclass here runs its capsule at the same time as that sequence. A
/// subclass names a capsule that shares no file, port or database with any other capsule: each
/// writes under its own fresh temporary directory, and only <c>BundlePipeline.Tests</c> writes
/// <c>artifacts/bundle-pipeline/</c>.
/// </summary>
public abstract class ParallelScenarioRunner(string projectName)
{
    /// <summary>The capsules the subclasses run, which the sequential theory leaves out.</summary>
    internal static readonly IReadOnlySet<string> Projects = typeof(ParallelScenarioRunner)
        .Assembly.GetTypes()
        .Where(type => type.IsSubclassOf(typeof(ParallelScenarioRunner)) && !type.IsAbstract)
        .Select(type => ((ParallelScenarioRunner)Activator.CreateInstance(type)!).ProjectName)
        .ToHashSet(StringComparer.Ordinal);

    private string ProjectName { get; } = projectName;

    [Fact]
    [Trait("TestKind", "ScenarioRunner")]
    public Task Scenario_runner_passes()
    {
        // A name BppScenarioRunnerProjects no longer lists would run a stale build or none.
        Assert.Contains(ProjectName, ScenarioRunnerTests.RunnerProjects());
        return ScenarioRunnerTests.RunAsync(ProjectName);
    }
}

/// <summary>Bundle sealing and upload over real SQLite queues.</summary>
public sealed class BundlePipelineScenario() : ParallelScenarioRunner("BundlePipeline.Tests");

/// <summary>
/// Runs MSBuild and <c>scripts/build.sh fetch-data</c>, whose seed gates build and test in
/// Release; <c>just mod::test</c> builds and runs every capsule in Debug.
/// </summary>
public sealed class RemoteEmbeddedDataPipelineScenario()
    : ParallelScenarioRunner("RemoteEmbeddedDataPipeline.Tests");

/// <summary>Replay payload and video maintenance over SQLite.</summary>
public sealed class ReplayPayloadMaintenanceScenario()
    : ParallelScenarioRunner("ReplayPayloadMaintenance.Tests");
