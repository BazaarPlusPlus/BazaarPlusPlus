#nullable enable
namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewScopeDisposal
{
    private readonly object _gate = new();
    private readonly Func<Task> _dispose;
    private Task? _completion;

    internal NativeCardPreviewScopeDisposal(Func<Task> dispose) =>
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));

    internal ValueTask DisposeAsync()
    {
        TaskCompletionSource<object?>? starter = null;
        Task completion;
        lock (_gate)
        {
            if (_completion == null)
            {
                starter = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _completion = starter.Task;
            }
            completion = _completion;
        }

        if (starter != null)
            _ = CompleteAsync(starter);
        return new ValueTask(completion);
    }

    private async Task CompleteAsync(TaskCompletionSource<object?> completion)
    {
        try
        {
            await _dispose();
            completion.TrySetResult(null);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
