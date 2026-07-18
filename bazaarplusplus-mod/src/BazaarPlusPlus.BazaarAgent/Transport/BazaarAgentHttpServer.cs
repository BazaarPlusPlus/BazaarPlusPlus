#nullable enable
using System.Globalization;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentHttpServer : IDisposable
{
    private const int MaxBodyBytes = 65536;
    private static long _fallbackRequestSequence;

    private static readonly JsonSerializerSettings _json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private readonly Func<BazaarAgentContextSnapshot?> _snapshotGetter;
    private readonly BazaarAgentCommandQueue<BazaarAgentAction> _queue;
    private readonly BazaarAgentCommandQueue<BazaarAgentReplayCommand> _replayQueue;
    private readonly IBazaarAgentLogger _logger;
    private readonly Func<string> _requestIdFactory;
    private readonly Func<
        HttpListenerContext,
        int,
        string,
        BazaarAgentHttpLogRoute,
        Task<byte[]?>
    >? _requestBodyReaderOverride;
    private readonly Func<
        HttpListenerContext,
        int,
        string,
        string?,
        Exception?
    > _errorEnvelopeWriter;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _cleanupStarted;
    private int _started;

    public int Port { get; }
    public bool IsRunning => Volatile.Read(ref _started) == 1;

    public BazaarAgentHttpServer(
        int port,
        Func<BazaarAgentContextSnapshot?> snapshotGetter,
        BazaarAgentCommandQueue<BazaarAgentAction> queue,
        BazaarAgentCommandQueue<BazaarAgentReplayCommand> replayQueue,
        IBazaarAgentLogger logger
    )
        : this(port, snapshotGetter, queue, replayQueue, logger, BazaarAgentUlid.New) { }

    internal BazaarAgentHttpServer(
        int port,
        Func<BazaarAgentContextSnapshot?> snapshotGetter,
        BazaarAgentCommandQueue<BazaarAgentAction> queue,
        BazaarAgentCommandQueue<BazaarAgentReplayCommand> replayQueue,
        IBazaarAgentLogger logger,
        Func<string> requestIdFactory,
        Func<
            HttpListenerContext,
            int,
            string,
            BazaarAgentHttpLogRoute,
            Task<byte[]?>
        >? requestBodyReaderOverride = null,
        Func<HttpListenerContext, int, string, string?, Exception?>? errorEnvelopeWriter = null
    )
    {
        Port = port;
        _snapshotGetter = snapshotGetter;
        _queue = queue;
        _replayQueue = replayQueue;
        _logger = logger;
        _requestIdFactory =
            requestIdFactory ?? throw new ArgumentNullException(nameof(requestIdFactory));
        _requestBodyReaderOverride = requestBodyReaderOverride;
        _errorEnvelopeWriter = errorEnvelopeWriter ?? TryWriteErrorEnvelopeCore;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;
        Volatile.Write(ref _cleanupStarted, 0);

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
        }
        catch
        {
            // A failed HttpListener.Start can leave a partially initialized listener behind.
            // It was never running, so cleanup must Close it without calling Stop: Mono's
            // HttpListener.Stop repeats the failed bind and throws the same SocketException,
            // which would turn one start-degradation episode into a second stop warning.
            Interlocked.Exchange(ref _started, 0);
            throw;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoop(token));
    }

    public BazaarAgentListenerStopReport Stop()
    {
        var report = new BazaarAgentListenerStopReport();
        var wasStarted = Interlocked.Exchange(ref _started, 0) == 1;
        if (Interlocked.Exchange(ref _cleanupStarted, 1) == 1)
            return report;
        report.Capture(BazaarAgentListenerStopPhase.Cancellation, () => _cts?.Cancel());
        if (wasStarted)
            report.Capture(BazaarAgentListenerStopPhase.ListenerStop, () => _listener?.Stop());
        report.Capture(BazaarAgentListenerStopPhase.ListenerClose, () => _listener?.Close());
        _cts = null;
        _listener = null;
        return report;
    }

    public void Dispose() => _ = Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null)
            return;
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _started, 0);
                    _logger.TryEmit(BazaarAgentLogEvents.ListenerFailed(Port, ex));
                }
                return;
            }
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        var requestId = NextFallbackRequestId();
        var route = BazaarAgentHttpLogRoute.Unknown;
        var logMethod = BazaarAgentHttpLogMethod.Other;
        try
        {
            var generatedRequestId = _requestIdFactory();
            if (string.IsNullOrWhiteSpace(generatedRequestId))
                throw new InvalidOperationException("The request id factory returned no value.");
            requestId = generatedRequestId;
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod;
            route = ResolveLogRoute(path);
            logMethod = ResolveLogMethod(method);
            if (
                string.Equals(path, "/v1/context", StringComparison.OrdinalIgnoreCase)
                && method == "GET"
            )
            {
                await HandleGetContext(ctx, requestId).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/actions", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostActions(ctx, requestId).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/replay/record", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostReplayRecord(ctx, requestId).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v1/replay/continue", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostReplayContinue(ctx, requestId).ConfigureAwait(false);
            }
            else
            {
                WriteErrorEnvelope(ctx, 404, "not-found", "unknown route");
            }
        }
        catch (BazaarAgentErrorResponseWriteException ex)
        {
            _logger.TryEmit(
                BazaarAgentLogEvents.HttpRequestFailed(
                    requestId,
                    route,
                    logMethod,
                    BazaarAgentLogReasonCode.HttpErrorResponseWriteException,
                    ex.WriteException
                )
            );
        }
        catch (BazaarAgentRequestBodyReadException ex)
        {
            var writeException = _errorEnvelopeWriter(ctx, 400, "invalid", "read failed");
            _logger.TryEmit(
                BazaarAgentLogEvents.HttpRequestFailed(
                    requestId,
                    route,
                    logMethod,
                    writeException == null
                        ? BazaarAgentLogReasonCode.HttpRequestBodyReadException
                        : BazaarAgentLogReasonCode.HttpErrorResponseWriteException,
                    writeException ?? ex.ReadException
                )
            );
        }
        catch (Exception ex)
        {
            var writeException = _errorEnvelopeWriter(ctx, 500, "internal", ex.GetType().Name);
            _logger.TryEmit(
                BazaarAgentLogEvents.HttpRequestFailed(
                    requestId,
                    route,
                    logMethod,
                    writeException == null
                        ? BazaarAgentLogReasonCode.HttpHandlerException
                        : BazaarAgentLogReasonCode.HttpErrorResponseWriteException,
                    writeException ?? ex
                )
            );
        }
        finally
        {
            try
            {
                ctx.Response.Close();
            }
            catch (Exception closeEx)
            {
                _logger.TryEmitDebug(() =>
                    BazaarAgentLogEvents.HttpResponseCloseFailed(requestId, route, closeEx)
                );
            }
        }
    }

    private static BazaarAgentHttpLogRoute ResolveLogRoute(string path)
    {
        if (string.Equals(path, "/v1/context", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.Context;
        if (string.Equals(path, "/v1/actions", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.Actions;
        if (string.Equals(path, "/v1/replay/record", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.ReplayRecord;
        if (string.Equals(path, "/v1/replay/continue", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.ReplayContinue;
        return BazaarAgentHttpLogRoute.Unknown;
    }

    private static string NextFallbackRequestId() =>
        "f"
        + Interlocked
            .Increment(ref _fallbackRequestSequence)
            .ToString("x7", CultureInfo.InvariantCulture);

    private static BazaarAgentHttpLogMethod ResolveLogMethod(string method)
    {
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogMethod.Get;
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogMethod.Post;
        return BazaarAgentHttpLogMethod.Other;
    }

    private async Task HandleGetContext(HttpListenerContext ctx, string requestId)
    {
        var snap = _snapshotGetter();
        if (snap is null)
        {
            WriteErrorEnvelope(ctx, 503, "unavailable", null);
            return;
        }

        var inm = ctx.Request.Headers["If-None-Match"];
        if (inm == snap.ETag)
        {
            ctx.Response.StatusCode = 304;
            return;
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["ETag"] = snap.ETag;
        var body = JsonConvert.SerializeObject(snap.Context, _json);
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private async Task HandlePostActions(HttpListenerContext ctx, string requestId)
    {
        var body = await ReadBodyWithCap(
                ctx,
                MaxBodyBytes,
                requestId,
                BazaarAgentHttpLogRoute.Actions
            )
            .ConfigureAwait(false);
        if (body is null)
            return;

        BazaarAgentAction? action;
        try
        {
            var json = Encoding.UTF8.GetString(body);
            action = JsonConvert.DeserializeObject<BazaarAgentAction>(json, _json);
        }
        catch (JsonException)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "malformed json");
            return;
        }

        if (action is null)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "empty body");
            return;
        }

        var res = await _queue.EnqueueAndAwaitAsync(requestId, action).ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    private async Task HandlePostReplayRecord(HttpListenerContext ctx, string requestId)
    {
        // Raw binary route: the body is a GhostBattlePayload msgpack+gzip blob, never JSON.
        // It gets its own (much larger) cap and bypasses the action parser entirely.
        var body = await ReadBodyWithCap(
                ctx,
                BazaarAgentRuntimeDefaults.MaxRecordBodyBytes,
                requestId,
                BazaarAgentHttpLogRoute.ReplayRecord
            )
            .ConfigureAwait(false);
        if (body is null)
            return;

        if (body.Length == 0)
        {
            WriteErrorEnvelope(ctx, 400, "invalid", "empty body");
            return;
        }

        var battleId = ctx.Request.Headers["X-Bpp-Battle-Id"];
        if (string.IsNullOrWhiteSpace(battleId))
            battleId = ctx.Request.QueryString["battleId"];

        var res = await _replayQueue
            .EnqueueAndAwaitAsync(
                requestId,
                new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Start, body, battleId)
            )
            .ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    private async Task HandlePostReplayContinue(HttpListenerContext ctx, string requestId)
    {
        var res = await _replayQueue
            .EnqueueAndAwaitAsync(
                requestId,
                new BazaarAgentReplayCommand(BazaarAgentReplayControlKind.Continue, null, null)
            )
            .ConfigureAwait(false);
        await WriteQueueResponse(ctx, res).ConfigureAwait(false);
    }

    // How much of an over-cap request body gets drained after a 413 so the client can read the
    // response instead of hitting a TCP reset, and for how long. Beyond either bound, closing
    // with unread data (and the reset that follows) is the lesser evil — the time bound keeps a
    // stalled client (declared length, never sends) from parking the handler task forever.
    private const long MaxRejectedBodyDrainBytes =
        2L * BazaarAgentRuntimeDefaults.MaxRecordBodyBytes;
    private const int MaxRejectedBodyDrainMilliseconds = 5000;

    /// <summary>Reads the request body up to <paramref name="maxBytes"/>. Returns <c>null</c>
    /// after writing a 413/400 error envelope when the cap is exceeded or the read fails.</summary>
    private async Task<byte[]?> ReadBodyWithCap(
        HttpListenerContext ctx,
        int maxBytes,
        string requestId,
        BazaarAgentHttpLogRoute route
    )
    {
        if (_requestBodyReaderOverride != null)
        {
            try
            {
                return await _requestBodyReaderOverride(ctx, maxBytes, requestId, route)
                    .ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new BazaarAgentRequestBodyReadException(ex);
            }
        }

        // Pre-check Content-Length header
        var declaredHeader = ctx.Request.Headers["Content-Length"];
        if (long.TryParse(declaredHeader, out var declared) && declared > maxBytes)
        {
            await RejectTooLarge(ctx, requestId, route).ConfigureAwait(false);
            return null;
        }

        // Stream-read with size cap
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await ctx
                    .Request.InputStream.ReadAsync(buf, 0, buf.Length)
                    .ConfigureAwait(false);
                if (read <= 0)
                    break;
                total += read;
                if (total > maxBytes)
                {
                    await RejectTooLarge(ctx, requestId, route).ConfigureAwait(false);
                    return null;
                }
                ms.Write(buf, 0, read);
            }
            return ms.ToArray();
        }
        catch (IOException ex)
        {
            throw new BazaarAgentRequestBodyReadException(ex);
        }
    }

    /// <summary>Writes the 413 envelope, then drains the unread request body (bounded in bytes
    /// AND time). Closing with unread data makes HttpListener reset the connection, so a client
    /// still streaming the body would see a broken pipe instead of the 413. Responding first
    /// keeps clients that never send a body (header-only probes) from waiting on the drain.</summary>
    private async Task RejectTooLarge(
        HttpListenerContext ctx,
        string requestId,
        BazaarAgentHttpLogRoute route
    )
    {
        WriteErrorEnvelope(ctx, 413, "invalid", "body too large");
        try
        {
            var buf = new byte[8192];
            long drained = 0;
            var drainClock = System.Diagnostics.Stopwatch.StartNew();
            while (drained < MaxRejectedBodyDrainBytes)
            {
                var remainingMs = MaxRejectedBodyDrainMilliseconds - drainClock.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    return;

                // HttpListener request streams do not reliably honor cancellation tokens, so
                // race the read against a delay; a stalled client loses the race and gets the
                // connection reset from the caller's Close() instead of holding this task.
                var readTask = ctx.Request.InputStream.ReadAsync(buf, 0, buf.Length);
                var completed = await Task.WhenAny(readTask, Task.Delay((int)remainingMs))
                    .ConfigureAwait(false);
                if (completed != readTask)
                {
                    // Observe the orphaned read's eventual fault (triggered by the close) so it
                    // doesn't surface as an unobserved task exception.
                    _ = readTask.ContinueWith(
                        t => _ = t.Exception,
                        TaskContinuationOptions.OnlyOnFaulted
                            | TaskContinuationOptions.ExecuteSynchronously
                    );
                    return;
                }

                var read = await readTask.ConfigureAwait(false);
                if (read <= 0)
                    return;
                drained += read;
            }
        }
        catch (Exception ex)
        {
            // The client may abort once it sees the 413 — nothing left to salvage.
            _logger.TryEmitDebug(() =>
                BazaarAgentLogEvents.RejectedBodyDrainStopped(requestId, route, ex)
            );
        }
    }

    private static async Task WriteQueueResponse(
        HttpListenerContext ctx,
        BazaarAgentServerResponse res
    )
    {
        ctx.Response.StatusCode = res.HttpStatus;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(res.JsonBody);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private void WriteErrorEnvelope(
        HttpListenerContext ctx,
        int status,
        string code,
        string? details
    )
    {
        var exception = _errorEnvelopeWriter(ctx, status, code, details);
        if (exception != null)
            throw new BazaarAgentErrorResponseWriteException(exception);
    }

    private static Exception? TryWriteErrorEnvelopeCore(
        HttpListenerContext ctx,
        int status,
        string code,
        string? details
    )
    {
        try
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var envelope = new Dictionary<string, object?> { ["error"] = code };
            if (details is not null)
                envelope["details"] = details;
            var json = JsonConvert.SerializeObject(envelope, _json);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class BazaarAgentErrorResponseWriteException : Exception
    {
        internal BazaarAgentErrorResponseWriteException(Exception writeException)
            : base("The HTTP error response could not be written.", writeException) =>
            WriteException = writeException;

        internal Exception WriteException { get; }
    }

    private sealed class BazaarAgentRequestBodyReadException : Exception
    {
        internal BazaarAgentRequestBodyReadException(Exception readException)
            : base("The HTTP request body could not be read.", readException) =>
            ReadException = readException;

        internal Exception ReadException { get; }
    }
}
