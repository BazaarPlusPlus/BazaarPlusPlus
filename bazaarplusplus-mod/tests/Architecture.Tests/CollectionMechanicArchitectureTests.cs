using System.Runtime.CompilerServices;
using Xunit;

namespace Architecture.Tests;

public sealed class CollectionMechanicArchitectureTests
{
    [Fact]
    public void Collection_mechanic_projection_stays_at_the_catalog_VM_boundary()
    {
        var collectionRoot = CollectionRoot();
        var callers = Directory
            .GetFiles(collectionRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                File.ReadAllText(path)
                    .Contains("CollectionMechanicFacts.Project(", StringComparison.Ordinal)
            )
            .Select(path => Path.GetRelativePath(collectionRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.Equal(new[] { "Data/CollectionCardVm.From.cs" }, callers);
    }

    [Fact]
    public void Collection_filter_and_refresh_paths_do_not_walk_game_effect_graphs()
    {
        var collectionRoot = CollectionRoot();
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(collectionRoot, "Data", "CollectionFilterEngine.cs")),
            File.ReadAllText(Path.Combine(collectionRoot, "CollectionPanel.cs")),
        };
        var forbiddenEffectGraphTerms = new[]
        {
            "AbilityIds",
            "AuraIds",
            "TCardAbility",
            "TCardAura",
            "TAction",
        };

        foreach (var source in sources)
        foreach (var forbidden in forbiddenEffectGraphTerms)
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    private static string CollectionRoot() =>
        Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game", "CollectionPanel");

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
