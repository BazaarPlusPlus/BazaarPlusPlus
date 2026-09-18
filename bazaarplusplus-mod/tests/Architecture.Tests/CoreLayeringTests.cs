#nullable enable
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Guards dependency directions that the single-assembly main project cannot express to the
/// compiler. Behavior, UI geometry, call order, and exact implementation text belong in feature
/// tests; they are deliberately not pinned here.
/// </summary>
public sealed class CoreLayeringTests
{
    [Fact]
    public void History_view_cannot_own_native_history_workflows_or_leak_into_services()
    {
        var sourceRoot = MainSourceRoot();
        var viewRoot = Path.Combine(sourceRoot, "Game", "HistoryPanel", "Ui");
        foreach (var file in SourceFiles(viewRoot))
        {
            var source = File.ReadAllText(file);
            foreach (
                var controller in new[]
                {
                    "MatchHistoryScreenController",
                    "MatchHistoryRunDetailsController",
                    "RunHistoryListEntry",
                }
            )
                Assert.DoesNotContain(controller, source);
        }
        var allowedBridge = Path.Combine(sourceRoot, "Game", "HistoryPanel", "HistoryPanel.Ui.cs");
        foreach (
            var file in SourceFiles(sourceRoot)
                .Where(file =>
                    !file.StartsWith(
                        viewRoot + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal
                    )
                    && file != allowedBridge
                )
        )
            Assert.DoesNotContain("HistoryPanelView", File.ReadAllText(file));
    }

    private static readonly HashSet<string> AllowedCoreGameInteropImports = new(
        StringComparer.Ordinal
    )
    {
        "Core/Runtime/IBppServices.cs",
        "Core/Runtime/BppRuntimeServices.cs",
    };

    [Fact]
    public void Core_is_the_pure_dependency_floor()
    {
        var sourceRoot = MainSourceRoot();
        var violations = new List<string>();

        foreach (var file in SourceFiles(Path.Combine(sourceRoot, "Core")))
        {
            var relative = Relative(sourceRoot, file);
            foreach (var import in NamespaceImports(file))
            {
                if (
                    import.StartsWith("BazaarGame", StringComparison.Ordinal)
                    || import.StartsWith("TheBazaar", StringComparison.Ordinal)
                    || import.StartsWith("BazaarPlusPlus.Game.", StringComparison.Ordinal)
                    || (
                        import.StartsWith("BazaarPlusPlus.GameInterop", StringComparison.Ordinal)
                        && !AllowedCoreGameInteropImports.Contains(relative)
                    )
                )
                {
                    violations.Add($"{relative}: using {import}");
                }
            }
        }

        AssertNoViolations(
            "Core must remain below Game, GameInterop, and game assemblies.",
            violations
        );
    }

    [Fact]
    public void GameInterop_is_reusable_across_features()
    {
        AssertNoImports(
            Path.Combine(MainSourceRoot(), "GameInterop"),
            "GameInterop must not depend on feature namespaces.",
            "BazaarPlusPlus.Game."
        );
    }

    [Fact]
    public void Feature_ownership_does_not_cross_between_collection_history_live_build_and_replay()
    {
        var gameRoot = Path.Combine(MainSourceRoot(), "Game");
        var rules = new[]
        {
            new BoundaryRule(
                Path.Combine(gameRoot, "CollectionPanel"),
                "BazaarPlusPlus.Game.HistoryPanel",
                "CollectionPanel -> HistoryPanel"
            ),
            new BoundaryRule(
                Path.Combine(gameRoot, "CombatReplay"),
                "BazaarPlusPlus.Game.HistoryPanel.Ghost",
                "CombatReplay -> HistoryPanel.Ghost"
            ),
            new BoundaryRule(
                Path.Combine(gameRoot, "HistoryPanel"),
                "BazaarPlusPlus.Game.LiveBuildPanel",
                "HistoryPanel -> LiveBuildPanel"
            ),
            new BoundaryRule(
                Path.Combine(gameRoot, "LiveBuildPanel"),
                "BazaarPlusPlus.Game.HistoryPanel",
                "LiveBuildPanel -> HistoryPanel"
            ),
        };

        var violations = rules
            .SelectMany(rule =>
                SourceFiles(rule.Root)
                    .Where(file =>
                        File.ReadAllText(file)
                            .Contains(rule.ForbiddenNamespace, StringComparison.Ordinal)
                    )
                    .Select(file => $"{rule.Label}: {Relative(MainSourceRoot(), file)}")
            )
            .ToArray();

        AssertNoViolations(
            "Features must collaborate through shared GameInterop seams, not each other's internals.",
            violations
        );
    }

    [Fact]
    public void Shared_runtime_concepts_have_one_GameInterop_owner()
    {
        var sourceRoot = MainSourceRoot();
        Assert.True(
            File.Exists(
                Path.Combine(sourceRoot, "GameInterop", "DayTiers", "GameDataDayTierResolver.cs")
            )
        );
        Assert.False(
            File.Exists(Path.Combine(sourceRoot, "Game", "Encounters", "DayTierSchedule.cs"))
        );
        Assert.True(
            File.Exists(
                Path.Combine(sourceRoot, "GameInterop", "CardPreview", "NativeCardPreviewHost.cs")
            )
        );

        var forbiddenLegacyOwners = new[]
        {
            "DayTierSchedule",
            "NativeCardPreviewHandle",
            "NativeCardPreviewLease",
        };
        var violations = SourceFiles(sourceRoot)
            .Where(file =>
                forbiddenLegacyOwners.Any(token =>
                    File.ReadAllText(file).Contains(token, StringComparison.Ordinal)
                )
            )
            .Select(file => Relative(sourceRoot, file))
            .ToArray();

        AssertNoViolations("Removed duplicate runtime owners must stay removed.", violations);
    }

    [Fact]
    public void Voice_subtitle_policy_does_not_depend_on_native_audio_types()
    {
        var root = Path.Combine(MainSourceRoot(), "Game", "VoiceSubtitles");
        var forbidden = new[]
        {
            "using FMOD",
            "using FMODUnity",
            "VOPlayer",
            "EventInstance",
            "EVENT_CALLBACK",
            "PLAYBACK_STATE",
        };
        var violations = SourceFiles(root)
            .SelectMany(file =>
                forbidden
                    .Where(token =>
                        File.ReadAllText(file).Contains(token, StringComparison.Ordinal)
                    )
                    .Select(token => $"{Relative(MainSourceRoot(), file)}: {token}")
            )
            .ToArray();

        AssertNoViolations(
            "Native voice playback belongs in GameInterop or patches, not feature policy.",
            violations
        );
    }

    [Fact]
    public void Replay_state_exit_stays_owned_by_the_combat_replay_runtime()
    {
        var repoRoot = RepoRoot();
        const string owner = "src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs";
        var illegalReplayExits = SourceFiles(MainSourceRoot())
            .Where(file =>
                File.ReadAllText(file).Contains("ExitRecapReplayState(", StringComparison.Ordinal)
            )
            .Select(file => Relative(repoRoot, file))
            .Where(path => !string.Equals(path, owner, StringComparison.Ordinal))
            .ToArray();
        AssertNoViolations(
            "CombatReplayRuntime.TryContinueReplay is the only programmatic ReplayState exit; "
                + "video finalization depends on it.",
            illegalReplayExits
        );
    }

    [Fact]
    public void Production_projects_live_under_src_and_share_ManagedPath_discovery()
    {
        var repoRoot = RepoRoot();
        Assert.Empty(
            Directory.EnumerateFiles(
                repoRoot,
                "BazaarPlusPlus*.csproj",
                SearchOption.TopDirectoryOnly
            )
        );

        var expectedProjects = new[]
        {
            "BazaarPlusPlus",
            "BazaarPlusPlus.ModApi",
            "BazaarPlusPlus.Storage",
            "BazaarPlusPlus.Localization",
        };
        foreach (var project in expectedProjects)
        {
            Assert.True(
                File.Exists(Path.Combine(repoRoot, "src", project, $"{project}.csproj")),
                $"Missing production project src/{project}/{project}.csproj."
            );
        }

        var directoryProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        Assert.Contains("build/ManagedPath.props", directoryProps, StringComparison.Ordinal);
        // Enumerate from the project roots, never the repo root: an embedded worktree under
        // .claude/ holds a stale checkout whose csproj still declares <ManagedPath>.
        var projectRoots = new[]
        {
            Path.Combine(repoRoot, "src"),
            Path.Combine(repoRoot, "tests"),
            Path.Combine(repoRoot, "build"),
        };
        var projectOverrides = projectRoots
            .SelectMany(root =>
                Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            )
            .Where(path =>
                File.ReadAllText(path).Contains("<ManagedPath>", StringComparison.Ordinal)
            )
            .Select(path => Relative(repoRoot, path))
            .ToArray();
        AssertNoViolations(
            "ManagedPath discovery belongs only in build/ManagedPath.props.",
            projectOverrides
        );
    }

    private static void AssertNoImports(string root, string message, params string[] prefixes)
    {
        var violations = SourceFiles(root)
            .SelectMany(file =>
                NamespaceImports(file)
                    .Where(import =>
                        prefixes.Any(prefix => import.StartsWith(prefix, StringComparison.Ordinal))
                    )
                    .Select(import => $"{Relative(RepoRoot(), file)}: using {import}")
            )
            .ToArray();
        AssertNoViolations(message, violations);
    }

    private static IEnumerable<string> NamespaceImports(string file) =>
        File.ReadLines(file)
            .Select(line => line.Trim())
            .Where(line =>
                line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';')
            )
            .Select(line => line[6..^1].Trim());

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            : throw new DirectoryNotFoundException(root);

    private static void AssertNoViolations(string message, IEnumerable<string> violations)
    {
        var materialized = violations.Distinct(StringComparer.Ordinal).Order().ToArray();
        Assert.True(materialized.Length == 0, message + "\n" + string.Join("\n", materialized));
    }

    private static string MainSourceRoot() => Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record BoundaryRule(string Root, string ForbiddenNamespace, string Label);
}
