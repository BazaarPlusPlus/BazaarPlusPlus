#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Touch;

/// <summary>
/// Borrowed native sprites; never clones native controls or their callbacks. The sprites are
/// game assets that outlive any one view, so they are kept process-wide: a construction scans
/// for a pair only when it is missing or Unity has collected it. Main thread only.
/// </summary>
internal sealed class TouchNativeArt
{
    // Button with Selected and Frame with TagFrame come from one native object each, so each
    // pair is replaced together. A pair rescans when its primary is missing or collected, or
    // when its captured secondary was collected; a secondary absent from the start (Selected
    // may legitimately be) never triggers a rescan.
    private static Sprite? _button;
    private static Sprite? _selected;
    private static Sprite? _frame;
    private static Sprite? _tagFrame;

    public Sprite? Button { get; }
    public Sprite? Selected { get; }
    public Sprite? Frame { get; }
    public Sprite? TagFrame { get; }

    public TouchNativeArt()
    {
        var scanned = false;
        try
        {
            if (!Alive(_button) || Lost(_selected))
            {
                scanned = true;
                ScanPopupButtons();
            }
            if (!Alive(_frame) || Lost(_tagFrame))
            {
                scanned = true;
                ScanTooltipFrames();
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.native_art.skin_failed"),
                exception
            );
        }
        Button = Live(_button);
        Selected = Live(_selected);
        Frame = Live(_frame);
        TagFrame = Live(_tagFrame);
        if (scanned)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.native_art.skin_loaded"),
                ("button", Button != null),
                ("frame", Frame != null),
                ("tag_frame", TagFrame != null)
            );
    }

    public bool NeedsRefresh => !Alive(Button) || !Alive(Frame) || !Alive(TagFrame);

    private static void ScanPopupButtons()
    {
        _button = null;
        _selected = null;
        foreach (var popup in Resources.FindObjectsOfTypeAll<TheBazaar.GenericPopup>())
        {
            if (popup._buttons == null)
                continue;
            foreach (var button in popup._buttons)
            {
                if (button == null || button.DefaultImage == null)
                    continue;
                _selected = button.ClickedImage;
                _button = button.DefaultImage;
                return;
            }
        }
    }

    private static void ScanTooltipFrames()
    {
        _frame = null;
        _tagFrame = null;
        foreach (
            var tooltip in Resources.FindObjectsOfTypeAll<TheBazaar.UI.Tooltips.BaseTooltipController>()
        )
        {
            var frame = tooltip.bronzeFrameSettings?.BackgroundFrame;
            if (frame == null)
                continue;
            _tagFrame = tooltip.bronzeFrameSettings?.TagFrame;
            _frame = frame;
            return;
        }
    }

    private static bool Alive(Sprite? sprite) => sprite != null;

    // Captured, then destroyed or collected; a reference check, so a never-found sprite is not lost.
    private static bool Lost(Sprite? sprite) => sprite is not null && !Alive(sprite);

    private static Sprite? Live(Sprite? sprite) => Alive(sprite) ? sprite : null;
}
