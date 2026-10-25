using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>Borrowed native sprites; never clones native controls or their callbacks.</summary>
internal sealed class AndroidNativeArt
{
    public Sprite? Button { get; private set; }
    public Sprite? Selected { get; private set; }
    public Sprite? Frame { get; private set; }
    public Sprite? TagFrame { get; private set; }

    public AndroidNativeArt()
    {
        try
        {
            foreach (var popup in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.GenericPopup>())
            {
                if (popup._buttons == null)
                    continue;
                foreach (var button in popup._buttons)
                {
                    if (button == null || button.DefaultImage == null)
                        continue;
                    Button = button.DefaultImage;
                    Selected = button.ClickedImage;
                    break;
                }
                if (Button != null)
                    break;
            }
            foreach (
                var tooltip in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.UI.Tooltips.BaseTooltipController>()
            )
            {
                var frame = tooltip.bronzeFrameSettings?.BackgroundFrame;
                if (frame == null)
                    continue;
                Frame = frame;
                TagFrame = tooltip.bronzeFrameSettings?.TagFrame;
                break;
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_failed"),
                exception
            );
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_loaded"),
            ("button", Button != null),
            ("frame", Frame != null),
            ("tag_frame", TagFrame != null)
        );
    }

    public bool NeedsRefresh => Button == null || Frame == null || TagFrame == null;
}
