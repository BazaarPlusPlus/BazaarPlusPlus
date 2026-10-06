using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace ScenarioRunner.Tests;

/// <summary>
/// Executes source-shadow scenario capsules out of process. The process boundary is part of the
/// contract: runners may install AssemblyResolve handlers, define incompatible runtime shims, or
/// mutate other process-global state.
/// </summary>
public sealed partial class ScenarioRunnerTests
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

    private static Task RunAsync(string projectName) =>
        RunAsync(projectName, [], new Dictionary<string, string>());

    private static async Task RunAsync(
        string projectName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var root = RepoRoot();
        // Capsules share the host's output layout, including the platform segment
        // supplied by release preparation. A fixed bin/<configuration> path can
        // accidentally execute a stale capsule or fail on a clean runner.
        var outputDirectory = Path.GetRelativePath(
            Path.Combine(root, "tests", "ScenarioRunner.Tests"),
            AppContext.BaseDirectory
        );
        var assembly = Path.Combine(
            root,
            "tests",
            projectName,
            outputDirectory,
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
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException(
                $"Could not start scenario runner {projectName}."
            );
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        // A stuck capsule must report its name instead of consuming the entire CI job timeout.
        // Include redirected output: an inherited pipe can stay open after the process exits.
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var completion = Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);
        var timedOut = false;
        try
        {
            await completion.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            timedOut = true;
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        var stdout = stdoutTask.IsCompletedSuccessfully
            ? stdoutTask.Result
            : "<stdout did not close>";
        var stderr = stderrTask.IsCompletedSuccessfully
            ? stderrTask.Result
            : "<stderr did not close>";

        Assert.False(
            timedOut,
            $"{projectName} exceeded two minutes.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}"
        );
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
