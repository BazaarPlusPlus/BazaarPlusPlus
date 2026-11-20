using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.Collection;

internal sealed class TouchCollectionEntry : IDisposable
{
    public const string PanelId = "collection";

    private readonly TouchCollectionData _data;
    private readonly Func<Transform?> _parent;
    private readonly MobilePanelHandle _handle;

    public TouchCollectionEntry(TouchCollectionData data, Func<Transform?> parent)
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
            IsModalInputActive = () => Panel?.HasModalBackStep == true,
            DismissModal = () => Panel?.DismissModalBackStep(),
        });
    }

    public TouchCollectionPanel? Panel { get; private set; }

    public bool IsVisible => _handle.IsVisible;

    public OverlayRequestOutcome RequestOpen(string source) => _handle.RequestOpen(source);

    public void FlushHeroPreference() => _data.HeroPreference.Flush();

    public void Dispose()
    {
        _handle.Dispose();
        Panel?.Close("stopped");
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
                    new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.unavailable"),
                    ("canvas", parent != null),
                    ("font", font != null)
                );
                return false;
            }
            Panel = new TouchCollectionPanel(
                parent,
                font,
                _data,
                reason => _handle.RequestClose(reason)
            );
        }
        return Panel.Open(_handle.OpenSource);
    }
}
