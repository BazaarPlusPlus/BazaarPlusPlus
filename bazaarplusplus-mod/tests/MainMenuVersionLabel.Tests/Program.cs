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
    MainMenuVersionLabelFormatter.Build(" ", "", MainMenuUpdateStatus.Updating)
        == " Version: unknown ",
    "Without a plugin version the label shows only the game version."
);

Assert(
    MainMenuVersionLabelFormatter.IosGameVersion("1.0.12651-prod-macos-arm64-d5cc730f")
        == "1.0.12651-prod-bppios-arm64-d5cc730f",
    "The iOS port names itself in place of the macOS platform token."
);
Assert(
    MainMenuVersionLabelFormatter.IosGameVersion("1.0.11358-ptr-MacOS-arm64-947c079a")
        == "1.0.11358-ptr-bppios-arm64-947c079a",
    "The platform token is matched regardless of case."
);
Assert(
    MainMenuVersionLabelFormatter.IosGameVersion("1.0.12651-prod-ios-arm64-d5cc730f")
        == "1.0.12651-prod-ios-arm64-d5cc730f",
    "A version without the macOS token is left as it is."
);
Assert(
    MainMenuVersionLabelFormatter.IosGameVersion("macosx-1.0.0") == "macosx-1.0.0",
    "Only a whole token is renamed, never part of one."
);
Assert(
    MainMenuVersionLabelFormatter.IosGameVersion("") == "",
    "An empty version stays empty so the label still shows unknown."
);
Assert(
    MainMenuVersionLabelFormatter.Build(
        MainMenuVersionLabelFormatter.IosGameVersion("1.0.12651-prod-macos-arm64-d5cc730f"),
        "6.2.0"
    ) == " Version: 1.0.12651-prod-bppios-arm64-d5cc730f | BPP 6.2.0 ",
    "The iOS label shows the renamed version before the BPP version."
);

Console.WriteLine("Main menu version label tests passed.");

return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
