using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.LiveBuild;

/// <summary>
/// Ten-Win Builds as a Main Overlay Panel on a mobile platform: it registers the panel with the
/// host's Overlay Panel Host under <see cref="PanelId"/> through the Mobile Platform Port, which
/// ticks it every frame and closes it, and builds <see cref="TouchLiveBuildPanel"/> on the first
/// open, under the host's touch canvas and in the game's font. A build that throws is the
/// Overlay Panel Host's to log and roll back. The host opens it from its own entry (the touch icon
/// on Android, the dock button on iOS) with <see cref="RequestOpen"/>.
/// </summary>
internal sealed class TouchLiveBuildEntry : IDisposable
{
    /// <summary>The panel's id on both hosts.</summary>
    public const string PanelId = "live_build";

    private readonly TouchLiveBuildData _data;
    private readonly Func<Transform?> _parent;
    private readonly MobilePanelHandle _handle;

    /// <param name="data">The recommendations and supporters the panel reads.</param>
    /// <param name="parent">The host's touch canvas, or null before it exists.</param>
    public TouchLiveBuildEntry(TouchLiveBuildData data, Func<Transform?> parent)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _handle = MobilePanelHandle.Register(handle => new OverlayPanelRegistration(
            PanelId,
            onOpen: Open,
            onClose: reason => Panel?.Close(handle.Describe(reason)),
            tick: (_, _) => Panel?.Tick()
        )
        {
            IsModalInputActive = () => Panel?.IsInspecting == true,
            DismissModal = () => Panel?.CloseInspection(),
        });
    }

    /// <summary>The panel, once its first open built it.</summary>
    public TouchLiveBuildPanel? Panel { get; private set; }

    public OverlayRequestOutcome RequestOpen(string source) => _handle.RequestOpen(source);

    /// <summary>
    /// The host stops: the panel leaves the Overlay Panel Host, then gives its leases back and its
    /// view goes.
    /// </summary>
    public void Dispose()
    {
        _handle.Dispose();
        Panel?.Dispose();
        Panel = null;
    }

    // The host's open: the combat gate already passed.
    private bool Open()
    {
        if (Panel == null)
        {
            var parent = _parent();
            var font = MobilePlatform.Port.GameFont();
            if (parent == null || font == null)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.unavailable"),
                    ("canvas", parent != null),
                    ("font", font != null)
                );
                return false;
            }
            Panel = new TouchLiveBuildPanel(
                parent,
                font,
                _data,
                reason => _handle.RequestClose(reason)
            );
        }
        return Panel.Open(_handle.OpenSource);
    }
}
