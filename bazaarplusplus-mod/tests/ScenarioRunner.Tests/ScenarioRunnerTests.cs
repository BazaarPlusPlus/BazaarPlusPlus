using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace ScenarioRunner.Tests;

/// <summary>
/// Executes source-shadow scenario capsules out of process. The process boundary is part of the
/// contract: runners may install AssemblyResolve handlers, define incompatible runtime shims, or
/// mutate other process-global state.
/// </summary>
public sealed class ScenarioRunnerTests
{
    private const string SeedRunner = "LiveBuildRecommendations.Tests";

    public static IEnumerable<object[]> DefaultRunners() =>
        RunnerProjects().Where(name => name != SeedRunner).Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(DefaultRunners))]
    [Trait("TestKind", "ScenarioRunner")]
    public Task Scenario_runner_passes(string projectName) => RunAsync(projectName);

    [Fact]
    [Trait("TestKind", "ScenarioRunner")]
    [Trait("TestKind", "EmbeddedSeed")]
    public Task Live_build_embedded_seed_passes() => RunAsync(SeedRunner);

    private static async Task RunAsync(string projectName)
    {
        var root = RepoRoot();
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var assembly = Path.Combine(
            root,
            "tests",
            projectName,
            "bin",
            configuration,
            "net10.0",
            projectName + ".dll"
        );
        Assert.True(File.Exists(assembly), $"Scenario assembly was not built: {assembly}");

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);

        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException(
                $"Could not start scenario runner {projectName}."
            );
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.True(
            process.ExitCode == 0,
            $"{projectName} exited with {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}"
        );
    }

    private static IReadOnlyList<string> RunnerProjects()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var raw = props.Descendants("BppScenarioRunnerProjects").Single().Value;
        return raw.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
