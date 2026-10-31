using BazaarPlusPlus.Game.OverlayPanels;

namespace BazaarPlusPlus.Mobile;

/// <summary>
/// A Main Overlay Panel's handle on a mobile platform: Core's handle, plus the source of the last
/// open request and the string reason of the last close request, which the panel's log lines
/// carry. Each entry runs inside the host's main-thread scope
/// (<see cref="IMobilePlatformPort.EnterMainThread"/>).
/// </summary>
internal sealed class MobilePanelHandle : IDisposable
{
    private const string DefaultSource = "request";

    private readonly Func<IDisposable> _enterMainThread;
    private IOverlayPanelHandle? _inner;
    private string _closeReason = DefaultSource;

    private MobilePanelHandle(Func<IDisposable> enterMainThread) =>
        _enterMainThread = enterMainThread;

    /// <summary>
    /// Registers a panel through the host's Overlay Panel Host. The registration is built against
    /// its handle, so its callbacks can read the source of an open and the reason of a close.
    /// </summary>
    public static MobilePanelHandle Register(
        Func<MobilePanelHandle, OverlayPanelRegistration> build
    )
    {
        var platform = MobilePlatform.Port;
        return Register(build, platform.RegisterOverlayPanel, platform.EnterMainThread);
    }

    /// <summary>
    /// As <see cref="Register(Func{MobilePanelHandle, OverlayPanelRegistration})"/>, through the
    /// host's own <paramref name="register"/> and main-thread scope.
    /// </summary>
    public static MobilePanelHandle Register(
        Func<MobilePanelHandle, OverlayPanelRegistration> build,
        Func<OverlayPanelRegistration, IOverlayPanelHandle> register,
        Func<IDisposable> enterMainThread
    )
    {
        if (build == null)
            throw new ArgumentNullException(nameof(build));
        if (register == null)
            throw new ArgumentNullException(nameof(register));
        if (enterMainThread == null)
            throw new ArgumentNullException(nameof(enterMainThread));
        using var scope = enterMainThread();
        var handle = new MobilePanelHandle(enterMainThread);
        handle._inner = register(build(handle));
        return handle;
    }

    public bool IsVisible => _inner?.IsVisible == true;

    /// <summary>The source of the open being executed: what the last RequestOpen named.</summary>
    public string OpenSource { get; private set; } = DefaultSource;

    public OverlayRequestOutcome RequestOpen(string source)
    {
        using var scope = _enterMainThread();
        if (_inner == null)
            return OverlayRequestOutcome.UnknownPanel;
        OpenSource = source;
        return _inner.RequestOpen();
    }

    public OverlayRequestOutcome RequestClose(string reason)
    {
        using var scope = _enterMainThread();
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
        using var scope = _enterMainThread();
        _inner?.Dispose();
        _inner = null;
    }
}
