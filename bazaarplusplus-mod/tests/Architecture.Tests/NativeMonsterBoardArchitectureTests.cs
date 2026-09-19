using Xunit;

namespace Architecture.Tests;

public sealed class NativeMonsterBoardArchitectureTests
{
    [Fact]
    public void Features_use_owned_boards_without_reaching_into_native_components()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus");
        foreach (var feature in new[] { "HistoryPanel", "LiveBuildPanel" })
        {
            var files = Directory.EnumerateFiles(
                Path.Combine(sourceRoot, "Game", feature),
                "*.cs",
                SearchOption.AllDirectories
            );
            var sources = files.Select(File.ReadAllText).ToArray();
            Assert.Contains(sources, source => source.Contains("OwnedMonsterBoardPreview"));
            Assert.Contains(sources, source => source.Contains("NativeMonsterBoardItemMapper.Map"));
            foreach (var source in sources)
            {
                Assert.DoesNotContain(".HandleBoardState(", source);
                Assert.DoesNotContain(".HandlePooling(", source);
                Assert.DoesNotContain("._activeCards", source);
                Assert.DoesNotContain("._sockets", source);
                Assert.DoesNotContain("new TCardInstanceItem", source);
            }
        }
        var liveRoot = Path.Combine(sourceRoot, "Game", "LiveBuildPanel");
        foreach (
            var file in Directory.EnumerateFiles(liveRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("UnityEngine.UIElements", source);
            Assert.DoesNotContain("BazaarPlusPlus.Game.HistoryPanel", source);
            Assert.DoesNotContain("ItemBoardPreviewSurface", source);
        }
        Assert.False(
            File.Exists(
                Path.Combine(
                    sourceRoot,
                    "GameInterop",
                    "ItemBoardPreview",
                    "ItemBoardPreviewSurface.cs"
                )
            )
        );
    }

    [Fact]
    public void Native_panels_share_supporter_attribution_through_the_supporters_module()
    {
        var gameRoot = Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "Game");
        foreach (var feature in new[] { "HistoryPanel", "LiveBuildPanel" })
        {
            var sources = Directory
                .EnumerateFiles(Path.Combine(gameRoot, feature, "Ui"), "*.cs")
                .Select(File.ReadAllText);
            Assert.Contains(
                sources,
                source => source.Contains("new BPPSupporterNativeAttributionRow(")
            );
            Assert.All(sources, source => Assert.DoesNotContain("SupporterTier4Text", source));
        }
        var shared = File.ReadAllText(
            Path.Combine(gameRoot, "Supporters", "Ui", "BPPSupporterNativeAttributionRow.cs")
        );
        Assert.DoesNotContain("BazaarPlusPlus.Game.HistoryPanel", shared);
        Assert.DoesNotContain("BazaarPlusPlus.Game.LiveBuildPanel", shared);
    }

    [Fact]
    public void Item_identity_and_geometry_come_from_loaded_native_cards()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "GameInterop",
                "MonsterBoardPreview",
                "OwnedMonsterBoardPreview.Items.cs"
            )
        );
        Assert.Contains("card._cardData.Id", source);
        Assert.Contains("card._currentFrame", source);
        Assert.Contains("GetWorldCorners", source);
        Assert.DoesNotContain("CalculateRelativeRectTransformBounds", source);
        Assert.DoesNotContain("Zip(", source);
    }

    private static string RepoRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
