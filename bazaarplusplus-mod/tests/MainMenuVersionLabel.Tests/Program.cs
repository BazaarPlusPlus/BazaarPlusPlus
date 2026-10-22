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
    ) == " Version: 1.0.0 | BPP 3.2.9.prod | download the new APK ",
    "The Android manual update is a new APK."
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
