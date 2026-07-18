#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativePreviewPresentationTransaction
{
    internal static NativePreviewActionResult Apply(
        bool show,
        Func<NativePreviewActionResult> applyNative,
        Action revealSupplementalVisuals,
        Action concealSupplementalVisuals
    )
    {
        if (applyNative == null)
            throw new ArgumentNullException(nameof(applyNative));
        if (revealSupplementalVisuals == null)
            throw new ArgumentNullException(nameof(revealSupplementalVisuals));
        if (concealSupplementalVisuals == null)
            throw new ArgumentNullException(nameof(concealSupplementalVisuals));

        if (!show)
        {
            try
            {
                return applyNative();
            }
            finally
            {
                concealSupplementalVisuals();
            }
        }

        // CardPreviewBase.Show controls only the art and frame. Keep native supplemental
        // visuals concealed until that native operation has succeeded so setup/cancellation
        // cannot expose a half-created preview.
        concealSupplementalVisuals();
        var result = applyNative();
        if (result.Status == NativePreviewActionStatus.Applied)
            revealSupplementalVisuals();
        return result;
    }
}
