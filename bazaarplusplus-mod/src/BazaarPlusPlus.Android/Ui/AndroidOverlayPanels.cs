using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android adapter of the Overlay Panel Host (CONTEXT.md §Overlay panels): History, Collection
/// and Ten-Win Builds register with Core's <see cref="OverlayPanelHost"/> through it, which keeps
/// one of them open at a time, closes them for combat, a scene change and Panel Switch-Off, and
/// releases the rented cards once every panel let go of its own (on a scene change or a
/// switch-off, never on a plain close). The scene is a generation counter that
/// <see cref="SceneLoaded"/> advances. The back key (Escape under Unity) is answered at once,
/// inside the game's escape hooks (<see cref="BackKeyPatches"/>): the open panel's modal first,
/// then the panel, then the touch menu, which is not a Main Overlay Panel. One press can reach BPP
/// twice in a frame, through the game's escape hook and through the touch root's own keyboard
/// read, so a consumed press is stamped with its frame and the second sighting is consumed
/// without closing a second panel. Every entry runs inside the main-thread pump's scope; nothing
/// here touches the IL2CPP runtime, which the host injects as delegates (ADR-0016 §3).
/// </summary>
internal sealed class AndroidOverlayPanels
{
    private const long LoggedPasses = 5;
    private const string TouchMenuTarget = "touch_menu";

    private readonly OverlayPanelHost _host;
    private readonly Func<int> _frameCount;
    private Func<string, bool>? _closeTouchMenu;
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
        Func<int> frameCount
    )
    {
        ArgumentNullException.ThrowIfNull(releaseRentedCards);
        _frameCount = frameCount ?? throw new ArgumentNullException(nameof(frameCount));
        _host = new OverlayPanelHost(
            isInCombat,
            reason => releaseRentedCards(ReleaseReason(reason))
        );
        _host.ObserveScene(_sceneToken);
    }

    /// <summary>The open Main Overlay Panel's id, or null.</summary>
    public string? OpenPanelId => _host.OpenPanelId;

    /// <summary>
    /// Registers a Main Overlay Panel. The registration is built against its handle, so its
    /// callbacks can read the source of an open and the string reason of a close.
    /// </summary>
    public AndroidPanelHandle Register(Func<AndroidPanelHandle, OverlayPanelRegistration> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        using var scope = AndroidMainThread.Host.Enter();
        var handle = new AndroidPanelHandle();
        handle.Attach(_host.Register(build(handle)));
        return handle;
    }

    /// <summary>The touch menu's close, asked after the panels on a back press and on a scene load.</summary>
    public void SetTouchMenu(Func<string, bool>? close) => _closeTouchMenu = close;

    /// <summary>Main thread, every frame: the lifecycle rules, then every panel's tick.</summary>
    public void Frame(float deltaTime)
    {
        using var scope = AndroidMainThread.Host.Enter();
        _host.Frame(new OverlayPanelFrame(_sceneToken, deltaTime, false, null));
    }

    /// <summary>
    /// A scene loaded: the touch menu closes, then the host closes what the scene policies close,
    /// tells every panel, and releases the rented cards. The rental destroys every card right
    /// after this, so no lease may outlive it.
    /// </summary>
    public void SceneLoaded()
    {
        using var scope = AndroidMainThread.Host.Enter();
        CloseTouchMenu("scene_changed");
        _sceneGeneration++;
        _sceneToken = _sceneGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _host.ObserveScene(_sceneToken);
    }

    /// <summary>
    /// A back press reached BPP through <paramref name="source"/>; true when BPP closed something
    /// for it, so the game must not handle it.
    /// </summary>
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

        var outcome = _host.Escape();
        var target = outcome.Kind switch
        {
            OverlayEscapeKind.DismissedModal => outcome.PanelId + ".modal",
            OverlayEscapeKind.Closed => outcome.PanelId,
            _ => CloseTouchMenu("back_key") ? TouchMenuTarget : null,
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

    private bool CloseTouchMenu(string reason)
    {
        if (_closeTouchMenu is not { } close)
            return false;
        try
        {
            return close(reason);
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError($"{TouchMenuTarget}:{reason}", exception);
            return false;
        }
    }

    private static string ReleaseReason(OverlayReleaseReason reason) =>
        reason == OverlayReleaseReason.SceneChange ? "scene_changed" : "switched_off";
}

/// <summary>
/// A Main Overlay Panel's handle on Android: the Core handle, plus the source of the last open
/// request and the string reason of the last close request, which the panel's log lines carry.
/// </summary>
internal sealed class AndroidPanelHandle : IDisposable
{
    private const string DefaultSource = "request";

    private IOverlayPanelHandle? _inner;
    private string _closeReason = DefaultSource;

    public bool IsVisible => _inner?.IsVisible == true;

    /// <summary>The source of the open being executed: what the last RequestOpen named.</summary>
    public string OpenSource { get; private set; } = DefaultSource;

    public OverlayRequestOutcome RequestOpen(string source)
    {
        using var scope = AndroidMainThread.Host.Enter();
        if (_inner == null)
            return OverlayRequestOutcome.UnknownPanel;
        OpenSource = source;
        return _inner.RequestOpen();
    }

    public OverlayRequestOutcome RequestClose(string reason)
    {
        using var scope = AndroidMainThread.Host.Enter();
        if (_inner == null)
            return OverlayRequestOutcome.UnknownPanel;
        _closeReason = reason;
        return _inner.RequestClose();
    }

    /// <summary>The close reason as the panels' log lines name it.</summary>
    public string Describe(OverlayCloseReason reason) =>
        reason switch
        {
            OverlayCloseReason.Escape => "back_key",
            OverlayCloseReason.SceneChange => "scene_changed",
            OverlayCloseReason.Combat => "combat",
            OverlayCloseReason.Superseded => "superseded",
            OverlayCloseReason.SwitchedOff => "switched_off",
            OverlayCloseReason.OpenFailed => "open_failed",
            OverlayCloseReason.HotkeyToggle => "hotkey",
            _ => _closeReason,
        };

    /// <summary>Unregisters the panel without a close: the panel's own Dispose returns its leases.</summary>
    public void Dispose()
    {
        using var scope = AndroidMainThread.Host.Enter();
        _inner?.Dispose();
        _inner = null;
    }

    internal void Attach(IOverlayPanelHandle inner) => _inner = inner;
}
