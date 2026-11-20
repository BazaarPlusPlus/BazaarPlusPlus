using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.LiveBuild;

internal sealed class TouchLiveBuildEntry : IDisposable
{
    public const string PanelId = "live_build";

    private readonly TouchLiveBuildData _data;
    private readonly Func<Transform?> _parent;
    private readonly MobilePanelHandle _handle;

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

    public TouchLiveBuildPanel? Panel { get; private set; }

    public OverlayRequestOutcome RequestOpen(string source) => _handle.RequestOpen(source);

    public void Dispose()
    {
        _handle.Dispose();
        Panel?.Dispose();
        Panel = null;
    }

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
