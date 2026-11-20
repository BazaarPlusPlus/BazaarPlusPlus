using BazaarPlusPlus.Android.Host;

namespace BazaarPlusPlus.Android.GameInterop;

// Direct awaits resume from an IL2CPP callback outside the pump, so later managed awaits can land
// on a pool thread. Poll the game task through the Unity-thread pump instead.
internal static class Il2CppTasks
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task AwaitAsync(
        Il2CppSystem.Threading.Tasks.Task? task,
        string operation,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (task == null)
            throw new InvalidOperationException($"{operation} returned no task.");
        if (!task.IsCompleted)
        {
            try
            {
                await AndroidMainThread.Host.WhenAsync(
                    () => task.IsCompleted,
                    timeout ?? DefaultTimeout,
                    cancellationToken
                );
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException($"{operation}: {exception.Message}", exception);
            }
        }
        ThrowIfUnsuccessful(task, operation);
    }

    public static async Task<T> AwaitAsync<T>(
        Il2CppSystem.Threading.Tasks.Task<T>? task,
        string operation,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (task == null)
            throw new InvalidOperationException($"{operation} returned no task.");
        await AwaitAsync(
            (Il2CppSystem.Threading.Tasks.Task)task,
            operation,
            timeout,
            cancellationToken
        );
        return task.Result;
    }

    private static void ThrowIfUnsuccessful(
        Il2CppSystem.Threading.Tasks.Task task,
        string operation
    )
    {
        if (task.IsFaulted)
        {
            var exception = task.Exception;
            var message =
                exception?.InnerException?.Message ?? exception?.Message ?? "unknown failure";
            throw new Il2CppTaskFaultedException(operation, message);
        }
        if (task.IsCanceled)
            throw new OperationCanceledException($"{operation} was canceled by the game.");
    }
}

internal sealed class Il2CppTaskFaultedException(string operation, string message)
    : Exception($"{operation} failed in the game: {message}")
{
    public string Operation { get; } = operation;
}
