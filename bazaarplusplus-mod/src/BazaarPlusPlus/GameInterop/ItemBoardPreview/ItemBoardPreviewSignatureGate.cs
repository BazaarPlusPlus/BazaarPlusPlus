#nullable enable
using System.Threading.Tasks;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardPreviewSignatureGate
{
    public static bool ShouldCache(Task? aggregate)
    {
        if (aggregate == null)
            return false;
        if (!aggregate.IsCompleted)
            return false;
        return !aggregate.IsFaulted && !aggregate.IsCanceled;
    }
}
