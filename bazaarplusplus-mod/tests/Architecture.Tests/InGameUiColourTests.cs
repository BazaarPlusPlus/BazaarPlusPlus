using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class InGameUiColourTests
{
    // The Desktop UI the in-game kit covers (ADR-0026, ADR-0027).
    private static readonly string[] Scope =
    {
        "BazaarPlusPlus.Game.CollectionPanel",
        "BazaarPlusPlus.Game.HistoryPanel",
        "BazaarPlusPlus.Game.LiveBuildPanel",
        "BazaarPlusPlus.Game.CombatStatusBar",
        "BazaarPlusPlus.Game.Supporters",
        "BazaarPlusPlus.GameInterop.Heroes",
        "BazaarPlusPlus.GameInterop.NativeSkin",
        "BazaarPlusPlus.Infrastructure.UiTokens",
        "BazaarPlusPlus.Infrastructure.DesktopKit",
    };

    // The Kit Gallery is compiled only into Debug builds (ADR-0027 §5).
    private const string KitGallery = "BazaarPlusPlus.Game.KitGallery";

    private static IEnumerable<string> ScopeOf(CompiledBuild build) =>
        build.Configuration == "Debug" ? Scope.Append(KitGallery) : Scope;

    private const string NativeSkin = "BazaarPlusPlus.GameInterop.NativeSkin.DesktopNativeSkin";

    // A token holds channels in 0..1; this desktop hover lifts a native sprite above them.
    private const string OverBrightHover = NativeSkin + "::get_OverBrightHover";

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
                    .Type(NativeSkin)
                    .Methods.Single(method => method.ToString() == OverBrightHover)
                    .ToString();
                return InGameColourRule.Violations(
                    ScopeOf(build).SelectMany(build.TypesIn),
                    exempt
                );
            }
        );
    }

    [Fact]
    public void Only_the_desktop_native_skin_names_the_native_button_sprites()
    {
        Holds(
            "The game's Btn_Rectangle sprites are loaded and addressed only by DesktopNativeSkin.",
            build =>
            {
                var kit = build.Type(NativeSkin);
                if (!Literals(new[] { kit }, StringComparison.Ordinal, "Btn_Rectangle").Any())
                    throw new InvalidOperationException(
                        $"{build.Configuration}: {NativeSkin} no longer names Btn_Rectangle."
                    );
                return Literals(
                    build.Types.Where(type => type.FullName != NativeSkin),
                    StringComparison.Ordinal,
                    "Btn_Rectangle"
                );
            }
        );
    }
}
