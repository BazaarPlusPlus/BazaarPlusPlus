#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.OverlayPanels;

// The walkthrough waits for walkthrough-command.txt to disappear before reading the outcome.
// Delete the file before running a command to prevent repeating it on later frames.
public sealed class WalkthroughControl
{
    public const string CommandFileName = "walkthrough-command.txt";

    // The walkthrough waits for the file to vanish; half a second keeps that wait short for one
    // file check per poll.
    private const float PollIntervalSeconds = 0.5f;
    private const string OpenVerb = "open ";
    private const string ShotVerb = "shot ";

    private readonly string _path;
    private float _sincePoll;
    private bool _stopped;
    private string? _requested;
    private string? _shot;

    public WalkthroughControl(string dataRoot) => _path = Path.Combine(dataRoot, CommandFileName);

    // Ignore an open request for the current panel because the host would toggle it closed.
    public string? NextToggle(float deltaTime, string? openPanelId)
    {
        if (_stopped)
            return null;
        _sincePoll += deltaTime;
        if (_sincePoll < PollIntervalSeconds)
            return null;
        _sincePoll = 0f;
        if (!File.Exists(_path))
            return null;

        string command;
        try
        {
            command = File.ReadAllText(_path).Trim();
            File.Delete(_path);
        }
        catch (Exception ex)
        {
            _stopped = true;
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.walkthrough.stopped"
                ),
                ("error", ex.GetType().Name)
            );
            return null;
        }

        if (command.StartsWith(ShotVerb, StringComparison.Ordinal))
        {
            var name = command.Substring(ShotVerb.Length).Trim();
            if (IsShotName(name))
                _shot = name;
            else
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.OverlayPanels,
                        "overlay_panels.walkthrough.rejected"
                    ),
                    ("command", command)
                );
            return null;
        }

        var panelId = command.StartsWith(OpenVerb, StringComparison.Ordinal)
            ? command.Substring(OpenVerb.Length).Trim()
            : "";
        // The HUD registers with the host but never opens (GLOSSARY.md §Main Overlay Panel).
        if (panelId.Length == 0 || panelId == "hud")
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.walkthrough.rejected"
                ),
                ("command", command)
            );
            return null;
        }
        _requested = panelId;
        return panelId == openPanelId ? null : panelId;
    }

    // The name is restricted to lowercase letters, digits and dashes so captures cannot escape the capture folder.
    public string? TakeShot()
    {
        var shot = _shot;
        _shot = null;
        return shot;
    }

    public static bool IsShotName(string name) =>
        name.Length is > 0 and <= 64
        && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    // Report after the host's frame because combat or a modal can refuse the toggle.
    public void AfterFrame(string? openPanelId)
    {
        if (_requested == null)
            return;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.OverlayPanels, "overlay_panels.walkthrough.command"),
            ("panel_id", _requested),
            ("open_panel_id", openPanelId ?? "none")
        );
        _requested = null;
    }
}
