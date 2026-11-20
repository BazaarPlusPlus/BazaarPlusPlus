using System.Globalization;
using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace LiveBuildPanel.Tests;

public sealed class TenWinBuildMarkdownTests
{
    private const string NumberFont =
        "<font=\"BazaarNumbers_Regular_SDF\" material=\"BazaarNumbers_Regular_SDF_OutlineTooltip_M\">";

    private static readonly Guid Sanhua = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Jellyfish = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cannon = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("zh-CN", "tests/LiveBuildPanel.Tests/fixtures/ten-win-build.zh.md")]
    [InlineData("en", "tests/LiveBuildPanel.Tests/fixtures/ten-win-build.en.md")]
    public void CopiedBuildMatchesGolden(string language, string golden)
    {
        L.Install(() => language, () => BppChineseLocaleMode.Mainland);
        // Run counts group digits by the current culture; the goldens use the invariant one.
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string? actual;
        try
        {
            actual = LiveBuildPanelText.BuildMarkdown(
                Snapshot(),
                () => "Dragons",
                Describe,
                IconLabel
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.NotNull(actual);
        Compare(golden, actual!);
    }

    [Fact]
    public void NothingIsCopiedWithoutARecommendation()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

        var snapshot = new LiveBuildPanelSnapshot
        {
            Hero = CardHero.Vanessa,
            MatchesState = LiveBuildMatchesState.NoCandidates,
        };

        Assert.Null(
            LiveBuildPanelText.BuildMarkdown(snapshot, () => "Dragons", Describe, IconLabel)
        );
    }

    private static LiveBuildPanelSnapshot Snapshot() =>
        new()
        {
            Hero = CardHero.Vanessa,
            MatchesState = LiveBuildMatchesState.HasRecommendation,
            Recommendation = new BuildRecommendation
            {
                MatchedCardCount = 1,
                TenWinRunCount = 1234,
                CompletedRunCount = 4627,
                TenWinRateBps = 2667,
                P75TenWinFinalDay = 13,
                Board = new BppItemBoard(
                    BppItemBoardId.FinalBuild,
                    BppItemBoardType.Reference,
                    [
                        new BppItemBoardCard
                        {
                            TemplateId = Cannon,
                            Order = 0,
                            Tier = CardTier.Diamond,
                            Size = CardSize.Medium,
                            SourceSocketId = 4,
                        },
                        new BppItemBoardCard
                        {
                            TemplateId = Sanhua,
                            Order = 1,
                            Tier = CardTier.Silver,
                            Size = CardSize.Small,
                            EnchantmentType = CardEnchantment.Deadly,
                            SourceSocketId = 0,
                        },
                        new BppItemBoardCard
                        {
                            TemplateId = Jellyfish,
                            Order = 2,
                            Tier = CardTier.Gold,
                            Size = CardSize.Small,
                            SourceSocketId = 2,
                        },
                    ]
                ),
            },
        };

    private static TenWinBuildItemText Describe(BppItemBoardCard card)
    {
        var chinese = LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode);
        if (card.TemplateId == Sanhua)
            return new TenWinBuildItemText(
                chinese ? "三花" : "Sanhua",
                chinese ? "致命" : "Deadly",
                chinese ? ["伙伴", "武器"] : ["Friend", "Weapon"],
                6f,
                null,
                chinese
                    ? $"<line-height=1.6em>造成 <size=125%><voffset=-4><sprite name=Damage>{NumberFont}<color=#F24B3FFF>20</color></font></size></voffset> 伤害</line-height>\n"
                        + $"暴击率: <sprite name=CritChance>{NumberFont}<color=#F2C94CFF>4%</color></font>\n"
                        + "当你使用另一件<color=#5BA3F2FF>武器</color>时，此物品获得 <sprite name=CritChance><color=#F2C94CFF>5%</color>暴击率\n"
                        + $"触发 <size=125%><voffset=-4><sprite name=Poison><color=#0EBE4FFF>剧毒</color></size></voffset> 时，此物品获得 <size=125%><voffset=-4><sprite name=Regen>{NumberFont}<color=#8EEA31FF>3</color></font></size></voffset>\n"
                        + "当其<sprite name=CritChance><color=#F2C94CFF>暴击</color>时，充能一件物品 1 秒"
                    : $"<line-height=1.6em>Deal <size=125%><voffset=-4><sprite name=Damage>{NumberFont}<color=#F24B3FFF>20</color></font></size></voffset> Damage</line-height>\n"
                        + $"Crit Chance: <sprite name=CritChance>{NumberFont}<color=#F2C94CFF>4%</color></font>\n"
                        + "When you use another <color=#5BA3F2FF>Weapon</color>, this gains <sprite name=CritChance><color=#F2C94CFF>5%</color> Crit Chance\n"
                        + $"When you <size=125%><voffset=-4><sprite name=Poison><color=#0EBE4FFF>Poison</color></size></voffset>, this gains <size=125%><voffset=-4><sprite name=Regen>{NumberFont}<color=#8EEA31FF>3</color></font></size></voffset>\n"
                        + "When it <sprite name=CritChance><color=#F2C94CFF>Crits</color>, Charge an item 1 second(s)"
            );
        if (card.TemplateId == Jellyfish)
            return new TenWinBuildItemText(
                chinese ? "水母" : "Jellyfish",
                null,
                chinese ? ["水生"] : ["Aquatic"],
                null,
                null,
                chinese
                    ? "<style=Caution>你的水生物品冷却时间缩短 10%</style>\n\n<style=Caution>你的水生物品冷却时间缩短 10%</style>"
                    : "<style=Caution>Your Aquatic items have 10% reduced cooldown</style>\n\n<style=Caution>Your Aquatic items have 10% reduced cooldown</style>"
            );
        return new TenWinBuildItemText(
            chinese ? "加农炮" : "Cannon",
            null,
            [],
            4.5f,
            2f,
            string.Empty
        );
    }

    private static string? IconLabel(string icon)
    {
        var chinese = LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode);
        return icon switch
        {
            "Damage" => chinese ? "伤害" : "Damage",
            "CritChance" => chinese ? "暴击率" : "Crit Chance",
            "Poison" => chinese ? "剧毒" : "Poison",
            "Regen" => chinese ? "生命再生" : "Regen",
            _ => null,
        };
    }

    private static void Compare(string golden, string actual)
    {
        var path = Path.Combine(TestInputs.RepoRoot, golden);
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }
        Assert.True(
            File.Exists(path),
            $"{golden} is missing; create it with BPP_UPDATE_GOLDENS=1."
        );
        var expected = TestInputs.Markdown(golden).Replace("\r\n", "\n");
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"The copied build differs from {golden}.\n--- expected\n{expected}\n--- actual\n{actual}"
        );
    }
}
