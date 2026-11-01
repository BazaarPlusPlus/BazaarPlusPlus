#nullable enable
using BazaarPlusPlus.Game.Lobby;

Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "3.3.0.preview")
        == " Version: 1.0.0 | BPP 3.3.0.preview ",
    "Label should preserve the existing text when there is nothing to update."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "3.2.9.prod", MainMenuUpdateStatus.Updating)
        == " Version: 1.0.0 | BPP 3.2.9.prod | updating ",
    "Label should say the Managed Payload is downloading."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "3.2.9.prod", MainMenuUpdateStatus.RestartToUpdate)
        == " Version: 1.0.0 | BPP 3.2.9.prod | restart to update ",
    "Label should say the next start swaps the staged version in."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "3.2.9.prod", MainMenuUpdateStatus.ManualUpdate)
        == " Version: 1.0.0 | BPP 3.2.9.prod | update via installer ",
    "The desktop manual update goes through the installer."
);
Assert(
    MainMenuVersionLabelFormatter.Build(
        "1.0.0",
        "3.2.9.prod",
        MainMenuUpdateStatus.ManualUpdate,
        MainMenuVersionLabelFormatter.AndroidManualUpdateText
    ) == " Version: 1.0.0 | BPP 3.2.9.prod | install the new BPP App ",
    "The Android manual update is a new Android App."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "3.2.9.prod", MainMenuUpdateStatus.RestartFromApp)
        == " Version: 1.0.0 | BPP 3.2.9.prod | restart from the BPP App ",
    "On Android the App stages a payload the copy can take; starting from it installs the payload."
);
Assert(
    MainMenuVersionLabelFormatter.Build(
        "1.0.0",
        "3.2.9.prod",
        MainMenuUpdateStatus.RestartFromApp,
        MainMenuVersionLabelFormatter.AndroidManualUpdateText,
        "请从 BPP App 重新启动"
    ) == " Version: 1.0.0 | BPP 3.2.9.prod | 请从 BPP App 重新启动 ",
    "The host localizes the restart-from-the-App text."
);
Assert(
    MainMenuVersionLabelFormatter.Build(
        "1.0.0",
        "6.1.0",
        MainMenuUpdateStatus.RestartToUpdate,
        modeText: MainMenuVersionLabelFormatter.CompatibilityModeText
    ) == " Version: 1.0.0 | BPP 6.1.0 | compatibility mode | restart to update ",
    "Compatibility Mode follows the BPP version, before the update status."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "6.1.0", modeText: "兼容模式")
        == " Version: 1.0.0 | BPP 6.1.0 | 兼容模式 ",
    "Compatibility Mode shows with nothing to update."
);
Assert(
    MainMenuVersionLabelFormatter.Build("1.0.0", "6.1.0", modeText: " ")
        == " Version: 1.0.0 | BPP 6.1.0 ",
    "A blank mode shows nothing."
);
Assert(
    MainMenuVersionLabelFormatter.Build(" ", "", MainMenuUpdateStatus.Updating)
        == " Version: unknown ",
    "Without a plugin version the label shows only the game version."
);

Console.WriteLine("Main menu version label tests passed.");

return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
