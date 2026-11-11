using BazaarPlusPlus.Game.Lobby;
using Xunit;

namespace MainMenuVersionLabel.Tests;

// Each test keeps the call shape of the capsule check it replaces, so the defaults of
// MainMenuVersionLabelFormatter.Build stay under test.
public sealed class MainMenuVersionLabelFormatterTests
{
    [Fact]
    public void Nothing_to_update_keeps_the_existing_text() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.3.0.preview ",
            MainMenuVersionLabelFormatter.Build("1.0.0", "3.3.0.preview")
        );

    [Fact]
    public void Updating_says_the_managed_payload_is_downloading() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | updating ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.Updating
            )
        );

    [Fact]
    public void Restart_to_update_says_the_next_start_swaps_the_staged_version_in() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | restart to update ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.RestartToUpdate
            )
        );

    [Fact]
    public void Desktop_manual_update_goes_through_the_installer() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | update via installer ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.ManualUpdate
            )
        );

    [Fact]
    public void Android_manual_update_is_a_new_android_app() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | install the new BPP App ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.ManualUpdate,
                MainMenuVersionLabelFormatter.AndroidManualUpdateText
            )
        );

    [Fact]
    public void Restart_from_app_says_starting_from_the_app_installs_the_payload() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | restart from the BPP App ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.RestartFromApp
            )
        );

    [Fact]
    public void The_host_localizes_the_restart_from_the_app_text() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | 请从 BPP App 重新启动 ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.RestartFromApp,
                MainMenuVersionLabelFormatter.AndroidManualUpdateText,
                "请从 BPP App 重新启动"
            )
        );

    [Fact]
    public void Nothing_to_update_says_the_release_does_not_adapt_the_game() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | not adapted to this game version ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.None,
                adaptationText: MainMenuVersionLabelFormatter.GameNotAdaptedText
            )
        );

    [Fact]
    public void The_self_update_status_comes_before_the_games_adaptation() =>
        Assert.Equal(
            " Version: 1.0.0 | BPP 3.2.9.prod | restart to update ",
            MainMenuVersionLabelFormatter.Build(
                "1.0.0",
                "3.2.9.prod",
                MainMenuUpdateStatus.RestartToUpdate,
                adaptationText: MainMenuVersionLabelFormatter.GameNotAdaptedText
            )
        );

    [Fact]
    public void Without_a_plugin_version_the_label_shows_only_the_game_version() =>
        Assert.Equal(
            " Version: unknown ",
            MainMenuVersionLabelFormatter.Build(" ", "", MainMenuUpdateStatus.Updating)
        );
}
