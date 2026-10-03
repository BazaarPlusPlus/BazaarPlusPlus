#nullable enable
using BazaarPlusPlus.TestSupport;
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

/// <summary>
/// Guards dependency directions that the single-assembly main project cannot express to the
/// compiler. Behavior, UI geometry, call order, and exact implementation text belong in feature
/// tests; they are deliberately not pinned here.
/// </summary>
public sealed class CoreLayeringTests
{
    private const string Core = "BazaarPlusPlus.Core";
    private const string Game = "BazaarPlusPlus.Game";
    private const string GameInterop = "BazaarPlusPlus.GameInterop";
    private const string HistoryUi = "BazaarPlusPlus.Game.HistoryPanel.Ui";

    [Fact]
    public void History_view_cannot_own_native_history_workflows_or_leak_into_services()
    {
        var universe = CompiledArtifacts.Universe;
        var nativeHistory = new[]
        {
            universe.RequireType("TheBazaar.UI.MatchHistoryScreenController"),
            universe.RequireType("TheBazaar.UI.MatchHistoryRunDetailsController"),
            universe.RequireType("TheBazaar.RunHistoryListEntry"),
        };
        Holds(
            "The history view must not drive the native match-history screens.",
            build => References(build.TypesIn(HistoryUi), Is(nativeHistory))
        );
        Holds(
            "HistoryPanelView is reachable only from its own namespace and the HistoryPanel bridge.",
            build =>
            {
                var view = build.Type(HistoryUi + ".HistoryPanelView").FullName;
                var bridge = build.Type("BazaarPlusPlus.Game.HistoryPanel.HistoryPanel").FullName;
                return References(
                    build.Types.Where(type =>
                        !CompiledBuild.IsInNamespace(type.Namespace, HistoryUi)
                        && type.FullName != bridge
                    ),
                    Is(view)
                );
            }
        );
    }

    [Fact]
    public void Core_is_the_pure_dependency_floor()
    {
        Holds(
            "Core must remain below Game, GameInterop, and game assemblies.",
            build =>
            {
                _ = build.TypesIn(Game);
                _ = build.TypesIn(GameInterop);
                var gameAssemblies = new[]
                {
                    RequireAssemblyPrefix(build, "BazaarGame"),
                    RequireAssemblyPrefix(build, "TheBazaar"),
                };
                var interopOwners = new[]
                {
                    build.Type(Core + ".Runtime.IBppServices").FullName,
                    build.Type(Core + ".Runtime.BppRuntimeServices").FullName,
                };
                var core = build.TypesIn(Core);
                return References(core, FromAssembly(gameAssemblies))
                    .Concat(References(core, InNamespace(Game)))
                    .Concat(
                        References(
                            core.Where(type => !interopOwners.Contains(type.FullName)),
                            InNamespace(GameInterop)
                        )
                    );
            }
        );
    }

    [Fact]
    public void GameInterop_is_reusable_across_features()
    {
        Holds(
            "GameInterop must not depend on feature namespaces.",
            build =>
            {
                _ = build.TypesIn(Game);
                return References(build.TypesIn(GameInterop), InNamespace(Game));
            }
        );
    }

    [Fact]
    public void Feature_ownership_does_not_cross_between_collection_history_live_build_and_replay()
    {
        var rules = new[]
        {
            (Scope: Game + ".CollectionPanel", Forbidden: Game + ".HistoryPanel"),
            (Scope: Game + ".CombatReplay", Forbidden: Game + ".HistoryPanel.Ghost"),
            (Scope: Game + ".HistoryPanel", Forbidden: Game + ".LiveBuildPanel"),
            (Scope: Game + ".LiveBuildPanel", Forbidden: Game + ".HistoryPanel"),
        };
        Holds(
            "Features must collaborate through shared GameInterop seams, not each other's internals.",
            build =>
                rules.SelectMany(rule =>
                {
                    _ = build.TypesIn(rule.Forbidden);
                    return References(build.TypesIn(rule.Scope), InNamespace(rule.Forbidden));
                })
        );
    }

    [Fact]
    public void Voice_subtitle_policy_does_not_depend_on_native_audio_types()
    {
        var universe = CompiledArtifacts.Universe;
        var audioNamespaces = new[]
        {
            universe.RequireNamespace("FMOD"),
            universe.RequireNamespace("FMODUnity"),
        };
        var voPlayer = universe.RequireType("VOPlayer");
        Holds(
            "Native voice playback belongs in GameInterop or patches, not feature policy.",
            build =>
            {
                var policy = build.TypesIn(Game + ".VoiceSubtitles");
                return References(policy, InNamespace(audioNamespaces))
                    .Concat(References(policy, Is(voPlayer)));
            }
        );
    }

    [Fact]
    public void Production_projects_share_ManagedPath_discovery()
    {
        var imports = TestInputs
            .MsBuild("Directory.Build.props")
            .Descendants()
            .Where(element => element.Name.LocalName == "Import")
            .Select(element => (string?)element.Attribute("Project") ?? "");
        Assert.Contains(
            imports,
            project => project.EndsWith("build/ManagedPath.props", StringComparison.Ordinal)
        );

        // Enumerate from the project roots, never the repo root: an embedded worktree under
        // .claude/ holds a stale checkout whose csproj still declares <ManagedPath>.
        var overrides = new[] { "src", "tests", "build" }
            .SelectMany(root =>
                Directory.EnumerateFiles(
                    Path.Combine(TestInputs.RepoRoot, root),
                    "*.csproj",
                    SearchOption.AllDirectories
                )
            )
            .Select(path => TestInputs.RepoRelative(path))
            .Where(relative =>
                TestInputs
                    .MsBuild(relative)
                    .Descendants()
                    .Any(element => element.Name.LocalName == "ManagedPath")
            )
            .ToArray();
        Assert.True(
            overrides.Length == 0,
            "ManagedPath discovery belongs only in build/ManagedPath.props:\n  "
                + string.Join("\n  ", overrides)
        );
    }
}
