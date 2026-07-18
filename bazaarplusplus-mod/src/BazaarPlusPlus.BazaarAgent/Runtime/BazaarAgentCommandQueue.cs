#nullable enable
using System.Collections.Concurrent;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentServerResponse
{
    public int HttpStatus { get; }
    public string JsonBody { get; }

    public BazaarAgentServerResponse(int status, string body)
    {
        HttpStatus = status;
        JsonBody = body;
    }
}

/// <summary>
/// One pending HTTP-thread command awaiting main-thread execution. Carries a
/// <see cref="TaskCompletionSource{T}"/> the HTTP handler awaits and a one-shot timeout timer.
/// Ownership of the response is decided exactly once: either the timeout claims it (503 to the
/// client, the command is skipped at dequeue) or the main-thread drain claims it via
/// <see cref="TryClaimForExecution"/> — which disarms the timer, so a command that has started
/// executing can no longer be answered 503 behind the executor's back.
/// </summary>
public sealed class BazaarAgentPendingCommand<TCommand>
{
    private const int ClaimNone = 0;
    private const int ClaimExecution = 1;
    private const int ClaimTimeout = 2;

    private readonly TaskCompletionSource<BazaarAgentServerResponse> _tcs = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _completed;
    private int _claimed;
    private Timer? _timer;

    public TCommand Command { get; }
    public string RequestId { get; }
    public Task<BazaarAgentServerResponse> ResponseTask => _tcs.Task;
    public bool IsDiscarded => Volatile.Read(ref _claimed) == ClaimTimeout;

    internal BazaarAgentPendingCommand(string requestId, TCommand command, int timeoutMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request id is required.", nameof(requestId));
        RequestId = requestId;
        Command = command;
        _timer = new Timer(TimeoutCallback, null, timeoutMilliseconds, Timeout.Infinite);
    }

    private void TimeoutCallback(object? _)
    {
        if (Interlocked.CompareExchange(ref _claimed, ClaimTimeout, ClaimNone) != ClaimNone)
            return; // already claimed for execution — the executor owns the response
        SetResponse(new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}"));
    }

    /// <summary>Claims the command for execution and disarms the timeout. Returns false when the
    /// timeout already claimed it (the caller must skip it — its 503 was already sent).</summary>
    internal bool TryClaimForExecution()
    {
        if (Interlocked.CompareExchange(ref _claimed, ClaimExecution, ClaimNone) != ClaimNone)
            return false;
        var timer = Interlocked.Exchange(ref _timer, null);
        try
        {
            timer?.Dispose();
        }
        catch
        {
            // Best-effort cleanup; the claim already excludes the timeout callback.
        }
        return true;
    }

    public void SetResponse(BazaarAgentServerResponse response)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;
        var timer = Interlocked.Exchange(ref _timer, null);
        try
        {
            timer?.Dispose();
        }
        catch
        {
            // Best-effort cleanup; completion is already published and the timer is collectible.
        }
        _tcs.TrySetResult(response);
    }
}

/// <summary>
/// HTTP-thread → main-thread command queue shared by the action and replay-control routes:
/// enqueue returns a task the HTTP handler awaits; the controller tick dequeues (claiming the
/// command, see <see cref="BazaarAgentPendingCommand{TCommand}.TryClaimForExecution"/>), executes
/// on the Unity main thread, and completes the response.
/// </summary>
public sealed class BazaarAgentCommandQueue<TCommand> : IDisposable
{
    private readonly ConcurrentQueue<BazaarAgentPendingCommand<TCommand>> _queue = new();
    private readonly int _timeoutMs;
    private int _disposed;

    public BazaarAgentCommandQueue(int timeoutMilliseconds) => _timeoutMs = timeoutMilliseconds;

    public Task<BazaarAgentServerResponse> EnqueueAndAwaitAsync(string requestId, TCommand command)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request id is required.", nameof(requestId));
        if (Volatile.Read(ref _disposed) != 0)
            return Task.FromResult(
                new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}")
            );
        var p = new BazaarAgentPendingCommand<TCommand>(requestId, command, _timeoutMs);
        _queue.Enqueue(p);
        return p.ResponseTask;
    }

    /// <summary>Dequeues the next command that has not timed out, claimed for execution: once
    /// returned, the timeout can no longer answer it and the caller must complete it.</summary>
    public BazaarAgentPendingCommand<TCommand>? TryDequeue()
    {
        while (_queue.TryDequeue(out var p))
        {
            if (p.TryClaimForExecution())
                return p;
        }
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        while (_queue.TryDequeue(out var p))
        {
            p.SetResponse(new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}"));
        }
    }
}
