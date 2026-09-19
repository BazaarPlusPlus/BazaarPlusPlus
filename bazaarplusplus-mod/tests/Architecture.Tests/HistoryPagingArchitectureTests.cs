#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class HistoryPagingArchitectureTests
{
    [Fact]
    public void Lists_do_not_read_snapshot_documents_or_payload_files()
    {
        var repository = Read("Game/HistoryPanel/Storage/HistoryPanelRepository.cs");
        var list = repository[
            ..repository.IndexOf(
                "public PvpBattleSnapshots? LoadSnapshots",
                StringComparison.Ordinal
            )
        ];
        Assert.DoesNotContain("battle_snapshots", list);
        Assert.DoesNotContain("json_valid", list);
        Assert.DoesNotContain("COUNT(", list);
        Assert.Contains("local_player_account_id = $account", list);
        Assert.Contains("string.IsNullOrWhiteSpace(accountId)", list);
        var pages = Read("Game/HistoryPanel/Storage/HistoryPanelRepository.Pages.cs");
        Assert.Contains("Math.Clamp(request.Limit, 1, 40)", pages);
        Assert.DoesNotContain("OFFSET", pages);
    }

    [Fact]
    public void Native_board_owns_rentals_and_borrows_only_tooltip_refresh()
    {
        var board = Read("GameInterop/MonsterBoardPreview/OwnedMonsterBoardPreview.cs");
        Assert.DoesNotContain("HandleBoardState", board);
        Assert.DoesNotContain("HandlePooling", board);
        Assert.Contains("NativeCardPrefabLoader.RentInactiveAsync", board);
        Assert.Contains("_rentals.Add(rental)", board);
        Assert.Contains("rental.Dispose()", board);
        var host = Read("GameInterop/CardPreview/NativeCardPreviewHost.Borrowed.cs");
        Assert.DoesNotContain("PoolObject", host);
        Assert.DoesNotContain("AcquireAsync", host);
        Assert.DoesNotContain("Object.Destroy", host);
        Assert.Contains("RefreshHoveredTooltip(", host);
        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(SourceRoot(), "Game", "HistoryPanel"),
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("CardPreviewBase", source);
            Assert.DoesNotContain(".PoolObject()", source);
        }
    }

    [Fact]
    public void Focus_suppression_preserves_native_context_and_navigation()
    {
        var lease = Read("GameInterop/Input/NativeTextInputLease.cs");
        Assert.DoesNotContain(".Push(", lease);
        Assert.DoesNotContain(".Pop(", lease);
        Assert.DoesNotContain("UINavigation.Disable", lease);
        Assert.Contains("Gameplay.Disable", lease);
        Assert.Contains("MenuShortcuts.Disable", lease);
        Assert.Contains("_context.Apply()", lease);
        var patch = Read("Patches/Input/NativeTextInputContextPatch.cs");
        Assert.Contains("HarmonyPostfix", patch);
        Assert.Contains("NativeTextInputLease.Reapply", patch);
        var actions = Read("Game/HistoryPanel/Ui/HistoryPanelView.Actions.cs");
        Assert.DoesNotContain("Rebuild(", actions);
        Assert.DoesNotContain("Object.Destroy", actions);
    }

    [Fact]
    public void History_rank_art_uses_native_mapping_without_live_rank_or_rating_substitution()
    {
        var ranks = Read("GameInterop/Ranks/NativeRankBadgeProvider.cs");
        Assert.Contains("source.GetRankBadge(rank)", ranks);
        Assert.Contains("NativeGlobalAssetLoader.LoadByReferenceAsync<Sprite>", ranks);
        Assert.DoesNotContain("ClientCache", ranks);
        var view = Read("Game/HistoryPanel/Ui/HistoryPanelView.Layout.cs");
        Assert.Contains("i => BattleBadge(m.VisibleBattles[i])", view);
        Assert.Contains("i => m.VisibleBattles[i].OpponentRank", view);
        Assert.Contains("i => m.Runs[i].PlayerRank", view);
        Assert.DoesNotContain("FormatBattleResult", view);
        Assert.DoesNotContain("RomanConverter", view);
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(SourceRoot(), path));

    private static string SourceRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string file = ""
    ) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../src/BazaarPlusPlus"));
}
