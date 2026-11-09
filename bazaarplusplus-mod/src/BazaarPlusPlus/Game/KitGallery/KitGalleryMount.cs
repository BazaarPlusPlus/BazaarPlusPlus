#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Host;
using UnityEngine;

namespace BazaarPlusPlus.Game.KitGallery;

// The Kit Gallery (ADR-0027): a Debug-build Main Overlay Panel that shows every Desktop UI kit
// control in every state beside the game's art. It has no hotkey; the Walkthrough Control opens
// it (`open kit-gallery`), and `just mod::ui-shots` captures it.
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
