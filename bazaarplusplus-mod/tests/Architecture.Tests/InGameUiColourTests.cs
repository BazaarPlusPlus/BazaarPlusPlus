using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class InGameUiColourTests
{
    // The Desktop UI the in-game kit covers so far (ADR-0026). The Collection panel and the old
    // UiTokens palette join when they move to the tokens.
    private static readonly string[] Scope =
    {
        "BazaarPlusPlus.Game.HistoryPanel",
        "BazaarPlusPlus.Game.LiveBuildPanel",
        "BazaarPlusPlus.Game.CombatStatusBar",
        "BazaarPlusPlus.Game.Supporters",
        "BazaarPlusPlus.GameInterop.Heroes",
    };

    // A token holds channels in 0..1; this desktop hover lifts a native sprite above them.
    private const string OverBrightHover =
        "BazaarPlusPlus.Game.HistoryPanel.Ui.HistoryPanelView::get_OverBrightHover";

    [Fact]
    public void Desktop_panels_draw_only_in_game_tokens()
    {
        Holds(
            "Desktop panels take colours from InGameTheme (InGameTheme.WithAlpha for a fraction), "
                + "never a Color literal or a parsed colour string.",
            build =>
            {
                InGameColourRule.RequireThemeConstructsColours(
                    build.Type("BazaarPlusPlus.Infrastructure.UiTokens.InGameTheme")
                );
                var exempt = build
                    .Type("BazaarPlusPlus.Game.HistoryPanel.Ui.HistoryPanelView")
                    .Methods.Single(method => method.ToString() == OverBrightHover)
                    .ToString();
                return InGameColourRule.Violations(Scope.SelectMany(build.TypesIn), exempt);
            }
        );
    }
}
