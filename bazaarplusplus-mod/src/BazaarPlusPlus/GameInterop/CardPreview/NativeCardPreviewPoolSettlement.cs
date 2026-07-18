#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewPoolSettlement
{
    internal static T Prepare<T>(
        T resource,
        Action<T> prepare,
        Action<T> returnOnCancellation,
        Action<T> destroy,
        CancellationToken token
    )
        where T : class
    {
        if (resource == null)
            throw new ArgumentNullException(nameof(resource));

        var owned = true;
        try
        {
            prepare(resource);
            if (token.IsCancellationRequested)
            {
                returnOnCancellation(resource);
                owned = false;
                token.ThrowIfCancellationRequested();
            }

            owned = false;
            return resource;
        }
        finally
        {
            if (owned)
                destroy(resource);
        }
    }
}
