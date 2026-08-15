#nullable enable
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.GameInterop.Tooltips;

internal static class NativeCardTooltipContentRefresher
{
    internal static bool TryApply(CardTooltipController controller, CardTooltipData tooltipData)
    {
        if (controller == null || tooltipData == null || !controller.gameObject.activeInHierarchy)
            return false;

        var visibility = controller.PositioningCanvasGroup;
        var originalAlpha = visibility?.alpha ?? 0f;
        var originalInteractable = visibility?.interactable ?? false;
        var originalBlocksRaycasts = visibility?.blocksRaycasts ?? false;

        try
        {
            // ApplyPreviewTooltip rebuilds several ContentSizeFitter branches. Conceal the native
            // host for the synchronous rebuild so no intermediate geometry reaches the frame.
            if (visibility != null)
            {
                visibility.alpha = 0f;
                visibility.interactable = false;
                visibility.blocksRaycasts = false;
            }

            controller.ApplyPreviewTooltip(tooltipData);
            Canvas.ForceUpdateCanvases();
            var positioningRect = controller.PositioningRectTransform;
            if (positioningRect != null)
            {
                TempoUIUtility.ForceRebuildRecursive(positioningRect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(positioningRect);
            }
            controller.KeepTooltipWithinBounds();
            return ReferenceEquals(controller.CurrentTooltipData, tooltipData);
        }
        finally
        {
            if (visibility != null)
            {
                visibility.alpha = originalAlpha;
                visibility.interactable = originalInteractable;
                visibility.blocksRaycasts = originalBlocksRaycasts;
            }
        }
    }
}
