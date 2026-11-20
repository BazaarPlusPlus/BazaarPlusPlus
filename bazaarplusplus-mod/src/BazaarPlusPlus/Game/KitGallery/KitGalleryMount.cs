#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Host;
using UnityEngine;

namespace BazaarPlusPlus.Game.KitGallery;

// The debug gallery has no hotkey. Open it with Walkthrough Control's open kit-gallery command.
internal sealed class KitGalleryMount : IBppMountable
{
    internal const string PanelId = "kit-gallery";

    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private KitGalleryView? _view;
    private IOverlayPanelHandle? _handle;

    public KitGalleryMount(Func<DesktopOverlayPanels?> overlayHost) => _overlayHost = overlayHost;

    public void Mount(GameObject host, IBppServices services)
    {
        if (_overlayHost() is not { } overlay)
            return;
        _view = new KitGalleryView(host.transform, () => _handle?.RequestClose());
        _handle = overlay.Register(
            new OverlayPanelRegistration(
                PanelId,
                onOpen: () =>
                {
                    _view.SetVisible(true);
                    return true;
                },
                onClose: _ => _view.SetVisible(false),
                tick: (_, _) => _view.Tick()
            )
        );
    }

    public void Unmount(GameObject host)
    {
        _handle?.Dispose();
        _handle = null;
        _view?.Dispose();
        _view = null;
    }
}
