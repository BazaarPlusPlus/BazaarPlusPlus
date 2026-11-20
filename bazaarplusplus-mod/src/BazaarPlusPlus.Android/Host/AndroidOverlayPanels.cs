using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Host;

// The game's escape hook and the touch UI can see the same press in one frame, so a consumed press
// must be deduplicated.
internal sealed class AndroidOverlayPanels
{
    private const long LoggedPasses = 5;

    private readonly OverlayPanelHost _host;
    private readonly Func<int> _frameCount;
    private readonly WalkthroughControl _walkthrough;
    private Func<string, bool>? _closeTooltipPreview;
    private long _sceneGeneration;
    private string _sceneToken = "0";
    private int _consumedFrame = -1;
    private int _seenFrame = -1;
    private long _presses;
    private long _consumed;
    private long _passed;

    public AndroidOverlayPanels(
        Func<bool> isInCombat,
        Action<string> releaseRentedCards,
        Func<int> frameCount,
        WalkthroughControl walkthrough
    )
    {
        ArgumentNullException.ThrowIfNull(releaseRentedCards);
        _frameCount = frameCount ?? throw new ArgumentNullException(nameof(frameCount));
        _walkthrough = walkthrough ?? throw new ArgumentNullException(nameof(walkthrough));
        _host = new OverlayPanelHost(
            isInCombat,
            reason => releaseRentedCards(ReleaseReason(reason))
        );
        _host.ObserveScene(_sceneToken);
    }

    public string? OpenPanelId => _host.OpenPanelId;

    public IOverlayPanelHandle Register(OverlayPanelRegistration registration)
    {
        using var scope = AndroidMainThread.Host.Enter();
        return _host.Register(registration);
    }

    public void SetTooltipPreview(Func<string, bool>? close) => _closeTooltipPreview = close;

    public void Frame(float deltaTime)
    {
        using var scope = AndroidMainThread.Host.Enter();
        var toggle = _walkthrough.NextToggle(deltaTime, _host.OpenPanelId);
        _host.Frame(new OverlayPanelFrame(_sceneToken, deltaTime, false, toggle));
        _walkthrough.AfterFrame(_host.OpenPanelId);
        if (_walkthrough.TakeShot() is { } shot)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.walkthrough.shot_unsupported"
                ),
                ("name", shot)
            );
    }

    // Panels must give up their leases before the scene change destroys the rented cards.
    public void SceneLoaded()
    {
        using var scope = AndroidMainThread.Host.Enter();
        _sceneGeneration++;
        _sceneToken = _sceneGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _host.ObserveScene(_sceneToken);
    }

    public bool TryConsumeBack(string source)
    {
        using var scope = AndroidMainThread.Host.Enter();
        var frame = _frameCount();
        if (frame == _consumedFrame)
            return true;
        var firstSighting = frame != _seenFrame;
        _seenFrame = frame;
        if (firstSighting)
            _presses++;

        if (_host.OpenPanelId == null && _closeTooltipPreview?.Invoke("back_key") == true)
        {
            _consumedFrame = frame;
            _consumed++;
            return true;
        }
        var outcome = _host.Escape();
        var target = outcome.Kind switch
        {
            OverlayEscapeKind.DismissedModal => outcome.PanelId + ".modal",
            OverlayEscapeKind.Closed => outcome.PanelId,
            _ => null,
        };
        if (target != null)
        {
            _consumedFrame = frame;
            _consumed++;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.back_key.consumed"),
                ("source", source),
                ("target", target)
            );
            return true;
        }
        if (firstSighting && ++_passed <= LoggedPasses)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.back_key.passed"),
                ("source", source)
            );
        return false;
    }

    public BppLogField[] BackSummary() =>
        [
            ("back_key_presses", _presses),
            ("back_key_consumed", _consumed),
            ("back_key_passed", _passed),
        ];

    private static string ReleaseReason(OverlayReleaseReason reason) =>
        reason == OverlayReleaseReason.SceneChange ? "scene_changed" : "switched_off";
}
