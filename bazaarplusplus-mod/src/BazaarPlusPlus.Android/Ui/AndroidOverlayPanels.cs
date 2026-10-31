using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android adapter of the Overlay Panel Host (GLOSSARY.md §Overlay panels): History, Collection
/// and Ten-Win Builds register with Core's <see cref="OverlayPanelHost"/> through it, which keeps
/// one of them open at a time, closes them for combat, a scene change and Panel Switch-Off, and
/// releases the rented cards once every panel let go of its own (on a scene change or a
/// switch-off, never on a plain close). The scene is a generation counter that
/// <see cref="SceneLoaded"/> advances. The back key (Escape under Unity) is answered at once,
/// inside the game's escape hooks (<see cref="BackKeyPatches"/>): the open panel's modal first,
/// then the panel. One press can reach BPP
/// twice in a frame, through the game's escape hook and through the touch root's own keyboard
/// read, so a consumed press is stamped with its frame and the second sighting is consumed
/// without closing a second panel. Every entry runs inside the main-thread pump's scope; nothing
/// here touches the IL2CPP runtime, which the host injects as delegates (ADR-0016 §3).
/// </summary>
internal sealed class AndroidOverlayPanels
{
    private const long LoggedPasses = 5;

    private readonly OverlayPanelHost _host;
    private readonly Func<int> _frameCount;
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
    public MobilePanelHandle Register(Func<MobilePanelHandle, OverlayPanelRegistration> build) =>
        MobilePanelHandle.Register(build, Register, () => AndroidMainThread.Host.Enter());

    /// <summary>
    /// Registers a Main Overlay Panel with Core's host: the Mobile Platform Port's overlay
    /// registration (<see cref="AndroidMobilePlatform.RegisterOverlayPanel"/>).
    /// </summary>
    public IOverlayPanelHandle Register(OverlayPanelRegistration registration)
    {
        using var scope = AndroidMainThread.Host.Enter();
        return _host.Register(registration);
    }

    /// <summary>The native tooltip's auxiliary drawer, dismissed before native Back handling.</summary>
    public void SetTooltipPreview(Func<string, bool>? close) => _closeTooltipPreview = close;

    /// <summary>Main thread, every frame: the lifecycle rules, then every panel's tick.</summary>
    public void Frame(float deltaTime)
    {
        using var scope = AndroidMainThread.Host.Enter();
        _host.Frame(new OverlayPanelFrame(_sceneToken, deltaTime, false, null));
    }

    /// <summary>
    /// A scene loaded: the host closes what the scene policies close,
    /// tells every panel, and releases the rented cards. The rental destroys every card right
    /// after this, so no lease may outlive it.
    /// </summary>
    public void SceneLoaded()
    {
        using var scope = AndroidMainThread.Host.Enter();
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
