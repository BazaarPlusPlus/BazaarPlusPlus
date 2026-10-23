using BazaarPlusPlus.Android.Host;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The one bridge from a game <c>Il2CppSystem.Threading.Tasks.Task</c> to a managed
/// <see cref="Task"/>. Awaiting the IL2CPP task directly compiles (its awaiter implements
/// <see cref="System.Runtime.CompilerServices.INotifyCompletion"/>) but resumes from an IL2CPP
/// callback outside the main-thread pump, and every later managed <c>await</c> then lands on a
/// pool thread. Here the task's state is polled once per frame on the main thread
/// (<see cref="AndroidMainThread.WhenAsync"/>), with no delegate marshaled into IL2CPP, and a
/// faulted task surfaces as <see cref="Il2CppTaskFaultedException"/> carrying the IL2CPP
/// exception's message. Every await of a game async method in the host goes through here.
/// </summary>
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
            await AndroidMainThread.Host.WhenAsync(
                () => task.IsCompleted,
                timeout ?? DefaultTimeout,
                cancellationToken
            );
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

/// <summary>A game task that faulted; the IL2CPP exception itself stays in the game.</summary>
internal sealed class Il2CppTaskFaultedException(string operation, string message)
    : Exception($"{operation} failed in the game: {message}")
{
    public string Operation { get; } = operation;
}
