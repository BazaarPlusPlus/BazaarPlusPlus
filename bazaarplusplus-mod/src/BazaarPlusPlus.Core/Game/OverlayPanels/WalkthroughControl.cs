#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.OverlayPanels;

// The Walkthrough Control (GLOSSARY.md §Overlay panels): the device walkthrough
// (`.agents/skills/walkthrough/`) opens a Main Overlay Panel by writing `open <panel id>` to
// walkthrough-command.txt in the Data Root, and `shot <name>` asks the host for a capture of the
// game window (`just mod::ui-shots`; a host that cannot capture logs it as unsupported). Each
// host adapter asks it for a toggle every frame and feeds that to OverlayPanelHost as a hotkey
// toggle, so the lifecycle rules still apply: combat refuses the open and an open modal swallows
// it. The file is deleted before the command runs; a
// file the host cannot read or delete stops the control for the session, so no command repeats.
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

    // The panel to toggle this frame, or null. A request for the panel already open is a no-op,
    // since a toggle would close it.
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

    // The capture `shot <name>` asked for, once: the host writes it as <name>.png. The name is
    // lowercase letters, digits and dashes, so it cannot leave the capture folder.
    public string? TakeShot()
    {
        var shot = _shot;
        _shot = null;
        return shot;
    }

    public static bool IsShotName(string name) =>
        name.Length is > 0 and <= 64
        && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    // After the host's frame: one line with the request and the panel open now, since combat or a
    // modal can refuse the toggle.
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
