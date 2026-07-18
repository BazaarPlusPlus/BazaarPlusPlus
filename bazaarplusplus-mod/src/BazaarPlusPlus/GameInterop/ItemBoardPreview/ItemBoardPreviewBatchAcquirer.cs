#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardPreviewBatchAcquirer
{
    internal static Task<ItemBoardPreviewAcquireResult[]> AcquireAsync(
        INativeCardPreviewScope scope,
        IReadOnlyList<NativeCardPreviewSubject> subjects,
        CancellationToken cancellationToken = default
    )
    {
        if (scope == null)
            throw new ArgumentNullException(nameof(scope));
        if (subjects == null)
            throw new ArgumentNullException(nameof(subjects));

        return Task.WhenAll(
            subjects.Select(subject => AcquireOneAsync(scope, subject, cancellationToken))
        );
    }

    private static async Task<ItemBoardPreviewAcquireResult> AcquireOneAsync(
        INativeCardPreviewScope scope,
        NativeCardPreviewSubject subject,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var outcome = await scope.AcquireAsync(subject, cancellationToken);
            return outcome.Session != null
                ? ItemBoardPreviewAcquireResult.Success(outcome.Session, subject)
                : ItemBoardPreviewAcquireResult.Failed(subject, null, outcome.Failure);
        }
        catch (OperationCanceledException)
        {
            return ItemBoardPreviewAcquireResult.FromCanceled(subject);
        }
        catch (Exception ex)
        {
            return ItemBoardPreviewAcquireResult.Failed(subject, ex, null);
        }
    }
}

internal readonly record struct ItemBoardPreviewAcquireResult(
    INativeCardPreviewSession? Session,
    NativeCardPreviewSubject Subject,
    Exception? Exception,
    NativeCardPreviewFailure? NativeFailure,
    bool Canceled
)
{
    internal Guid TemplateId => Subject.TemplateId;

    internal static ItemBoardPreviewAcquireResult Success(
        INativeCardPreviewSession session,
        NativeCardPreviewSubject subject
    ) => new(session, subject, null, null, Canceled: false);

    internal static ItemBoardPreviewAcquireResult Failed(
        NativeCardPreviewSubject subject,
        Exception? exception,
        NativeCardPreviewFailure? nativeFailure
    ) => new(null, subject, exception, nativeFailure, Canceled: false);

    internal static ItemBoardPreviewAcquireResult FromCanceled(NativeCardPreviewSubject subject) =>
        new(null, subject, null, null, Canceled: true);
}
