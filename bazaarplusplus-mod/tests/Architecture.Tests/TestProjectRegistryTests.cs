#nullable enable
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

// A test project runs only when a registry names it: one of the two solutions, or
// BppScenarioRunnerProjects (Directory.Build.props), which ScenarioRunner.Tests runs. The
// Managed-free lane, `just mod::test-core`, runs BazaarPlusPlus.CoreTests.slnx and the capsules
// BppTestCoreProjects names. A project outside them builds nowhere and runs nowhere, silently.
public sealed class TestProjectRegistryTests
{
    private const string DesktopSolution = "tests/BazaarPlusPlus.Tests.slnx";
    private const string CoreSolution = "tests/BazaarPlusPlus.CoreTests.slnx";

    // Projects that no registry runs, each with its reason. Keep this list short.
    private static readonly Dictionary<string, string> Unregistered = new(StringComparer.Ordinal)
    {
        ["CombatImpact.CorpusExport"] =
            "A command-line exporter, not a test: `just mod::test-corpus` runs it with arguments, "
            + "and ScenarioRunner.Tests builds it for its corpus export test.",
    };

    // Core-solution projects that `just mod::test` does not run, each with its reason.
    private static readonly Dictionary<string, string> CoreLaneOnly = new(StringComparer.Ordinal)
    {
        ["AndroidHost.Tests"] =
            "It builds against the Interop Snapshot (`just mod::fetch android publisher`), "
            + "not a Managed snapshot, so only the Core lane runs it.",
    };

    [Fact]
    public void Every_test_project_is_in_a_solution_or_the_scenario_runner()
    {
        var registered = DesktopProjects()
            .Concat(CoreProjects())
            .Concat(ScenarioRunnerProjects())
            .ToHashSet(StringComparer.Ordinal);

        var missing = TestProjects()
            .Select(project => project.Name)
            .Where(name => !registered.Contains(name) && !Unregistered.ContainsKey(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These test projects run nowhere. Add each to "
                + DesktopSolution
                + ", "
                + CoreSolution
                + " or BppScenarioRunnerProjects:\n  "
                + string.Join("\n  ", missing)
        );
    }

    [Fact]
    public void Exceptions_name_existing_unregistered_projects()
    {
        var registered = DesktopProjects()
            .Concat(CoreProjects())
            .Concat(ScenarioRunnerProjects())
            .ToHashSet(StringComparer.Ordinal);
        var existing = TestProjects().Select(project => project.Name).ToHashSet();

        var stale = Unregistered
            .Keys.Where(name => !existing.Contains(name) || registered.Contains(name))
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "Remove these from TestProjectRegistryTests.Unregistered; they are gone or registered:\n  "
                + string.Join("\n  ", stale)
        );
    }

    [Fact]
    public void Managed_free_test_projects_run_in_the_core_lane()
    {
        var coreLane = CoreProjects().Concat(TestCoreProjects()).ToHashSet(StringComparer.Ordinal);

        var missing = TestProjects()
            .Where(project => project.ManagedFree && !coreLane.Contains(project.Name))
            .Select(project => project.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These test projects set BppRequiresManagedPath=false but `just mod::test-core` "
                + "skips them. Add each to "
                + CoreSolution
                + " or BppTestCoreProjects:\n  "
                + string.Join("\n  ", missing)
        );
    }

    // Without this, a project in both solutions can leave the desktop one and still satisfy every
    // other rule here, while `just mod::test` stops running it.
    [Fact]
    public void Core_solution_projects_also_run_in_the_desktop_lane()
    {
        var desktop = DesktopProjects().ToHashSet(StringComparer.Ordinal);
        var core = CoreProjects().ToHashSet(StringComparer.Ordinal);

        var missing = core.Where(name => !desktop.Contains(name) && !CoreLaneOnly.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var stale = CoreLaneOnly
            .Keys.Where(name => !core.Contains(name) || desktop.Contains(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These projects are in "
                + CoreSolution
                + " only, so `just mod::test` skips them. Add each to "
                + DesktopSolution
                + ":\n  "
                + string.Join("\n  ", missing)
        );
        Assert.True(
            stale.Length == 0,
            "Remove these from TestProjectRegistryTests.CoreLaneOnly; they are gone or in "
                + DesktopSolution
                + ":\n  "
                + string.Join("\n  ", stale)
        );
    }

    [Fact]
    public void Core_lane_capsules_are_scenario_runner_capsules()
    {
        var scenarioRunner = ScenarioRunnerProjects().ToHashSet(StringComparer.Ordinal);

        var missing = TestCoreProjects().Where(name => !scenarioRunner.Contains(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            "These BppTestCoreProjects capsules are missing from BppScenarioRunnerProjects, "
                + "so `just mod::test` skips them:\n  "
                + string.Join("\n  ", missing)
        );
    }

    [Fact]
    public void Every_registered_name_has_a_test_project()
    {
        var missing = new List<string>();
        foreach (var (registry, names) in Registries())
        {
            foreach (var name in names)
            {
                var project = Path.Combine(TestInputs.RepoRoot, "tests", name, name + ".csproj");
                if (!File.Exists(project))
                    missing.Add($"{registry}: {name} (no tests/{name}/{name}.csproj)");
            }
        }

        Assert.True(
            missing.Count == 0,
            "These registry entries name no test project:\n  " + string.Join("\n  ", missing)
        );
    }

    private static IEnumerable<(string Registry, IReadOnlyList<string> Names)> Registries()
    {
        yield return (DesktopSolution, DesktopProjects());
        yield return (CoreSolution, CoreProjects());
        yield return ("BppScenarioRunnerProjects", ScenarioRunnerProjects());
        yield return ("BppTestCoreProjects", TestCoreProjects());
        yield return ("TestProjectRegistryTests.Unregistered", Unregistered.Keys.ToArray());
    }

    private static IReadOnlyList<string> DesktopProjects() => SolutionProjects(DesktopSolution);

    private static IReadOnlyList<string> CoreProjects() => SolutionProjects(CoreSolution);

    private static IReadOnlyList<string> ScenarioRunnerProjects() =>
        PropertyList("BppScenarioRunnerProjects");

    private static IReadOnlyList<string> TestCoreProjects() => PropertyList("BppTestCoreProjects");

    // A solution entry is tests/<Name>/<Name>.csproj; the project name is the file name, so an
    // entry whose directory and file disagree surfaces as a missing project.
    private static IReadOnlyList<string> SolutionProjects(string solution) =>
        TestInputs
            .MsBuild(solution)
            .Descendants("Project")
            .Select(project => (string?)project.Attribute("Path"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileNameWithoutExtension(path!.Replace('\\', '/')))
            .ToArray();

    // One unconditional definition, the way ScenarioRunner.Tests and scripts/test.sh read it.
    private static IReadOnlyList<string> PropertyList(string property) =>
        TestInputs
            .MsBuild("Directory.Build.props")
            .Descendants(property)
            .Single()
            .Value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

    private static IReadOnlyList<TestProject> TestProjects() =>
        Directory
            .EnumerateDirectories(Path.Combine(TestInputs.RepoRoot, "tests"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
            .Select(path =>
            {
                var relative = TestInputs.RepoRelative(path);
                var managedFree = TestInputs
                    .MsBuild(relative)
                    .Descendants("BppRequiresManagedPath")
                    .Any(element =>
                        string.Equals(
                            element.Value.Trim(),
                            "false",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                return new TestProject(Path.GetFileNameWithoutExtension(path), managedFree);
            })
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();

    private sealed record TestProject(string Name, bool ManagedFree);
}
