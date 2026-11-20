using BazaarPlusPlus.Game.OverlayPanels;

namespace BazaarPlusPlus.Mobile;

// Run each panel entry inside the host's main-thread scope.
internal sealed class MobilePanelHandle : IDisposable
{
    private const string DefaultSource = "request";

    private readonly Func<IDisposable> _enterMainThread;
    private IOverlayPanelHandle? _inner;
    private string _closeReason = DefaultSource;

    private MobilePanelHandle(Func<IDisposable> enterMainThread) =>
        _enterMainThread = enterMainThread;

    // Build the registration against its handle so callbacks can read open and close context.
    public static MobilePanelHandle Register(
        Func<MobilePanelHandle, OverlayPanelRegistration> build
    )
    {
        var platform = MobilePlatform.Port;
        return Register(build, platform.RegisterOverlayPanel, platform.EnterMainThread);
    }

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
