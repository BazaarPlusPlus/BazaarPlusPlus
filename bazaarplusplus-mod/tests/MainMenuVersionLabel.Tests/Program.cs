#nullable enable
using BazaarPlusPlus.Game.Lobby;

Assert(
    MainMenuVersionComparer.IsUpdateAvailable("3.2.9.preview", "3.3.0"),
    "Older preview builds should report update available."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("3.3.0.preview", "3.3.0"),
    "Matching preview builds should not report update available."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("3.3.0.prod", "3.3.0"),
    "Matching production builds should not report update available."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("3.3.0.preveiw", "3.3.0"),
    "Misspelled preview suffix should still be ignored by first-three-number parsing."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("3.3.0.t20260604.120000.dev", "3.3.0"),
    "Local timestamped debug builds should compare by the first three version numbers."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("4.0.0.preview", "3.3.0"),
    "Newer local builds should not report update available."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("not-a-version", "3.3.0"),
    "Invalid current versions should fail closed."
);
Assert(
    !MainMenuVersionComparer.IsUpdateAvailable("3.2.9.preview", "not-a-version"),
    "Invalid remote versions should fail closed."
);

var normalLabel = MainMenuVersionLabelFormatter.Build("1.0.0", "3.3.0.preview");
Assert(
    normalLabel == " Version: 1.0.0 | BPP 3.3.0.preview ",
    "Label should preserve the existing text when no update is available."
);

var updateLabel = MainMenuVersionLabelFormatter.Build("1.0.0", "3.2.9.preview", true);
Assert(
    updateLabel == " Version: 1.0.0 | BPP 3.2.9.preview | update available ",
    "Label should append the update notice when requested."
);

Console.WriteLine("Main menu version label tests passed.");

return;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
