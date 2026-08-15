#nullable enable
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
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
    private readonly Func<ulong, BazaarAgentContextSnapshot?> _nextSnapshotGetter;
    private readonly BazaarAgentCommandQueue<BazaarAgentAction> _queue;
    private readonly IBazaarAgentLogger _logger;
    private readonly BazaarAgentActivityFeed _activityFeed;
    private readonly BazaarAgentProtocolV3Projector _protocolV3 = new();
    private readonly SemaphoreSlim _actionExecutionGate = new(1, 1);
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
        IBazaarAgentLogger logger,
        BazaarAgentActivityFeed? activityFeed = null
    )
        : this(
            port,
            snapshotGetter,
            _ => snapshotGetter(),
            queue,
            logger,
            requestIdFactory: BazaarAgentUlid.New,
            activityFeed: activityFeed
        ) { }

    public BazaarAgentHttpServer(
        int port,
        Func<BazaarAgentContextSnapshot?> snapshotGetter,
        Func<ulong, BazaarAgentContextSnapshot?> nextSnapshotGetter,
        BazaarAgentCommandQueue<BazaarAgentAction> queue,
        IBazaarAgentLogger logger,
        BazaarAgentActivityFeed? activityFeed = null
    )
        : this(
            port,
            snapshotGetter,
            nextSnapshotGetter,
            queue,
            logger,
            requestIdFactory: BazaarAgentUlid.New,
            activityFeed: activityFeed
        ) { }

    internal BazaarAgentHttpServer(
        int port,
        Func<BazaarAgentContextSnapshot?> snapshotGetter,
        Func<ulong, BazaarAgentContextSnapshot?>? nextSnapshotGetter,
        BazaarAgentCommandQueue<BazaarAgentAction> queue,
        IBazaarAgentLogger logger,
        Func<string> requestIdFactory,
        Func<
            HttpListenerContext,
            int,
            string,
            BazaarAgentHttpLogRoute,
            Task<byte[]?>
        >? requestBodyReaderOverride = null,
        Func<HttpListenerContext, int, string, string?, Exception?>? errorEnvelopeWriter = null,
        BazaarAgentActivityFeed? activityFeed = null
    )
    {
        Port = port;
        _snapshotGetter = snapshotGetter;
        _nextSnapshotGetter = nextSnapshotGetter ?? (_ => snapshotGetter());
        _queue = queue;
        _logger = logger;
        _activityFeed = activityFeed ?? new BazaarAgentActivityFeed();
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
            if (method == "GET" && IsDashboardPath(path))
            {
                await HandleDashboardGet(ctx, path).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v3/context", StringComparison.OrdinalIgnoreCase)
                && method == "GET"
            )
            {
                await HandleGetV3Context(ctx, requestId).ConfigureAwait(false);
            }
            else if (
                string.Equals(path, "/v3/actions", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                await HandlePostV3Action(ctx, requestId).ConfigureAwait(false);
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
        if (string.Equals(path, "/v3/context", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.Context;
        if (string.Equals(path, "/v3/actions", StringComparison.OrdinalIgnoreCase))
            return BazaarAgentHttpLogRoute.Actions;
        return BazaarAgentHttpLogRoute.Unknown;
    }

    private static bool IsDashboardPath(string path) =>
        string.Equals(path, "/", StringComparison.Ordinal)
        || string.Equals(path, "/dashboard", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase);

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

    private async Task HandleDashboardGet(HttpListenerContext ctx, string path)
    {
        if (string.Equals(path, "/dashboard/api/bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = _activityFeed.GetSince(
                0,
                maximumEvents: 512,
                include: IsDashboardProtocolActivity
            );
            await WriteDashboardJson(
                    ctx,
                    new
                    {
                        snapshot.LatestSequence,
                        snapshot.EarliestSequence,
                        snapshot.Events,
                    }
                )
                .ConfigureAwait(false);
            return;
        }

        if (string.Equals(path, "/dashboard/api/events", StringComparison.OrdinalIgnoreCase))
        {
            var after = ParseNonNegativeLong(ctx.Request.QueryString["after"]);
            var waitMilliseconds = ClampInt(ctx.Request.QueryString["waitMs"], 25000, 0, 25000);
            var snapshot = await _activityFeed
                .WaitForEventsAsync(
                    after,
                    maximumEvents: 128,
                    waitMilliseconds: waitMilliseconds,
                    include: IsDashboardProtocolActivity
                )
                .ConfigureAwait(false);
            await WriteDashboardJson(
                    ctx,
                    new
                    {
                        snapshot.LatestSequence,
                        snapshot.EarliestSequence,
                        snapshot.Events,
                    }
                )
                .ConfigureAwait(false);
            return;
        }

        var relativePath =
            string.Equals(path, "/", StringComparison.Ordinal)
            || string.Equals(path, "/dashboard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/dashboard/", StringComparison.OrdinalIgnoreCase)
                ? "index.html"
                : path["/dashboard/".Length..];
        await WriteDashboardAsset(ctx, relativePath).ConfigureAwait(false);
    }

    private static bool IsDashboardProtocolActivity(BazaarAgentActivityEvent activity) =>
        activity.Kind.StartsWith("agent.", StringComparison.Ordinal);

    private static long ParseNonNegativeLong(string? text)
    {
        return long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? Math.Max(0, value)
            : 0;
    }

    private static int ClampInt(string? text, int defaultValue, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return defaultValue;
        return Math.Clamp(parsed, minimum, maximum);
    }

    private static async Task WriteDashboardJson(HttpListenerContext ctx, object payload)
    {
        var body = JsonConvert.SerializeObject(payload, _json);
        var bytes = Encoding.UTF8.GetBytes(body);
        ConfigureDashboardHeaders(ctx.Response, "application/json; charset=utf-8", "no-store");
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private static async Task WriteDashboardAsset(HttpListenerContext ctx, string relativePath)
    {
        if (
            string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains('\\')
        )
        {
            WriteErrorEnvelopeStatic(ctx, 404, "not-found", "unknown dashboard asset");
            return;
        }

        var resourceName = "BazaarPlusPlus.BazaarAgent.Dashboard." + relativePath.TrimStart('/');
        var assembly = typeof(BazaarAgentHttpServer).GetTypeInfo().Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            WriteErrorEnvelopeStatic(ctx, 503, "unavailable", "dashboard assets were not embedded");
            return;
        }

        ConfigureDashboardHeaders(
            ctx.Response,
            DashboardContentType(relativePath),
            string.Equals(relativePath, "index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-store"
                : "public, max-age=31536000, immutable"
        );
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(ctx.Response.OutputStream).ConfigureAwait(false);
    }

    private static void ConfigureDashboardHeaders(
        HttpListenerResponse response,
        string contentType,
        string cacheControl
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = cacheControl;
        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; "
            + "script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static string DashboardContentType(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }

    private static void WriteErrorEnvelopeStatic(
        HttpListenerContext ctx,
        int status,
        string code,
        string? details
    )
    {
        _ = TryWriteErrorEnvelopeCore(ctx, status, code, details);
    }

    private async Task HandleGetV3Context(HttpListenerContext ctx, string requestId)
    {
        var sessionId = ctx.Request.Headers[BazaarAgentProtocolV3.SessionHeader];
        var hasSession = !string.IsNullOrWhiteSpace(sessionId);
        var revisionText = ctx.Request.Headers[BazaarAgentProtocolV3.RevisionHeader];
        var hasRevision = !string.IsNullOrWhiteSpace(revisionText);
        if (hasSession != hasRevision)
        {
            WriteErrorEnvelope(
                ctx,
                409,
                "resync-required",
                "session and revision must be supplied together"
            );
            return;
        }
        ulong revision = 0;
        if (hasRevision && !TryParseRevision(revisionText, out revision))
        {
            WriteErrorEnvelope(
                ctx,
                400,
                "invalid",
                "X-Bazaar-Agent-Revision must be an unsigned integer"
            );
            return;
        }
        var snapshot = hasRevision ? _nextSnapshotGetter(revision) : _snapshotGetter();
        if (snapshot is null)
        {
            WriteErrorEnvelope(ctx, 503, "unavailable", null);
            return;
        }
        BazaarAgentV3Projection projection;
        if (!hasSession)
            projection = _protocolV3.Bootstrap(snapshot);
        else if (!_protocolV3.TryProjectDelta(snapshot, sessionId, revision, out projection))
        {
            WriteErrorEnvelope(ctx, 409, "resync-required", "session or revision is stale");
            return;
        }
        var json = JsonConvert.SerializeObject(projection.View, _json);
        await WriteV3Response(ctx, 200, snapshot, projection.SessionId, json).ConfigureAwait(false);
        _activityFeed.Publish(
            "agent.view.sent",
            requestId,
            "/v3/context",
            "Agent v3 context at revision "
                + snapshot.TickId.ToString(CultureInfo.InvariantCulture),
            statusCode: 200,
            tickId: snapshot.TickId,
            responseJson: json
        );
    }

    private async Task HandlePostV3Action(HttpListenerContext ctx, string requestId)
    {
        var request = await ReadJsonBody<BazaarAgentV3ActionRequest>(
                ctx,
                requestId,
                BazaarAgentHttpLogRoute.Actions
            )
            .ConfigureAwait(false);
        if (request is null)
            return;
        var snapshot = _snapshotGetter();
        if (snapshot is null)
        {
            WriteErrorEnvelope(ctx, 503, "unavailable", null);
            return;
        }
        var sessionId = ctx.Request.Headers[BazaarAgentProtocolV3.SessionHeader];
        if (!_protocolV3.IsCurrentSession(sessionId, request.Revision, snapshot.TickId))
        {
            WriteErrorEnvelope(ctx, 409, "resync-required", "session or revision is stale");
            return;
        }
        if (!TryTranslateV3Action(snapshot, sessionId, request, out var action, out var error))
        {
            WriteErrorEnvelope(ctx, 409, "stale-or-unavailable", error);
            return;
        }

        var requestJson = JsonConvert.SerializeObject(request, _json);
        _activityFeed.Publish(
            "agent.action.received",
            requestId,
            "/v3/actions",
            "Agent v3 action " + request.Operation,
            requestJson: requestJson
        );
        await _actionExecutionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var response = await _queue
                .EnqueueAndAwaitAsync(requestId, action!)
                .ConfigureAwait(false);
            var responseJson = await WriteV3ActionResponse(ctx, response, request.Revision)
                .ConfigureAwait(false);
            _activityFeed.Publish(
                response.HttpStatus >= 400 ? "agent.action.rejected" : "agent.action.completed",
                requestId,
                "/v3/actions",
                "Agent v3 action "
                    + request.Operation
                    + " completed with HTTP "
                    + response.HttpStatus,
                statusCode: response.HttpStatus,
                requestJson: requestJson,
                responseJson: responseJson
            );
        }
        finally
        {
            _actionExecutionGate.Release();
        }
    }

    private static bool TryParseRevision(string? value, out ulong revision) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision);

    private async Task<string> WriteV3ActionResponse(
        HttpListenerContext ctx,
        BazaarAgentServerResponse response,
        ulong appliedRevision
    )
    {
        var snapshot = _snapshotGetter();
        if (snapshot is null)
        {
            await WriteQueueResponse(ctx, response).ConfigureAwait(false);
            return response.JsonBody;
        }
        if (response.HttpStatus >= 400)
        {
            await WriteV3Response(
                    ctx,
                    response.HttpStatus,
                    snapshot,
                    ctx.Request.Headers[BazaarAgentProtocolV3.SessionHeader] ?? "",
                    response.JsonBody
                )
                .ConfigureAwait(false);
            return response.JsonBody;
        }
        if (
            !_protocolV3.TryProjectDelta(
                snapshot,
                ctx.Request.Headers[BazaarAgentProtocolV3.SessionHeader],
                appliedRevision,
                out var projection
            )
        )
        {
            WriteErrorEnvelope(
                ctx,
                409,
                "resync-required",
                "context changed while applying action"
            );
            return "{\"error\":\"resync-required\"}";
        }
        var result = JToken.Parse(response.JsonBody);
        var status = result.Value<string>("status") ?? "accepted";
        var body = JsonConvert.SerializeObject(new { status, next = projection.View }, _json);
        await WriteV3Response(ctx, response.HttpStatus, snapshot, projection.SessionId, body)
            .ConfigureAwait(false);
        return body;
    }

    private static async Task WriteV3Response(
        HttpListenerContext ctx,
        int status,
        BazaarAgentContextSnapshot snapshot,
        string sessionId,
        string body
    )
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["ETag"] = snapshot.ETag;
        if (!string.IsNullOrWhiteSpace(sessionId))
            ctx.Response.Headers[BazaarAgentProtocolV3.SessionHeader] = sessionId;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private bool TryTranslateV3Action(
        BazaarAgentContextSnapshot snapshot,
        string? sessionId,
        BazaarAgentV3ActionRequest request,
        out BazaarAgentAction? action,
        out string? error
    )
    {
        action = null;
        error = null;
        var operation = (request.Operation ?? "").Trim().ToLowerInvariant();
        var context = snapshot.Context;
        BazaarAgentActionKind kind;
        switch (operation)
        {
            case "start":
                kind = BazaarAgentActionKind.StartOrContinueRun;
                break;
            case "reroll":
                kind = BazaarAgentActionKind.Reroll;
                break;
            case "exit":
                kind = BazaarAgentActionKind.ExitState;
                break;
            case "menu":
                kind = BazaarAgentActionKind.ReturnToMenu;
                break;
            case "continue":
                kind = BazaarAgentActionKind.Continue;
                break;
            case "sell":
                kind = BazaarAgentActionKind.SellItem;
                break;
            case "move":
                kind = BazaarAgentActionKind.MoveItem;
                break;
            case "select":
                return TryTranslateSelect(snapshot, sessionId, request, out action, out error);
            default:
                error = "unknown op";
                return false;
        }

        string? instanceId = null;
        BazaarAgentTargetSection? target = null;
        IReadOnlyList<string>? sockets = null;
        if (kind is BazaarAgentActionKind.SellItem or BazaarAgentActionKind.MoveItem)
        {
            if (!TryResolveOwnedCard(context, sessionId, request.Id, out var card, out instanceId))
            {
                error = "unknown or stale card id";
                return false;
            }
            if (
                kind == BazaarAgentActionKind.MoveItem
                && !TryBuildPlacement(
                    context,
                    card!,
                    request.Target,
                    request.StartSlot,
                    out target,
                    out sockets,
                    out error
                )
            )
                return false;
        }
        if (!context.AvailableActions.Any(option => option.ActionKind == kind))
        {
            error = "op is not available";
            return false;
        }
        action = new BazaarAgentAction
        {
            ActionKind = kind,
            CardInstanceId = instanceId,
            TargetSection = target,
            TargetSockets = sockets,
            Hero = request.Hero,
            PlayMode = request.PlayMode,
            ForTickId = request.Revision,
        };
        return true;
    }

    private bool TryTranslateSelect(
        BazaarAgentContextSnapshot snapshot,
        string? sessionId,
        BazaarAgentV3ActionRequest request,
        out BazaarAgentAction? action,
        out string? error
    )
    {
        action = null;
        error = null;
        if (!_protocolV3.TryResolveInstanceId(sessionId, request.Id ?? "", out var instanceId))
        {
            error = "unknown or stale card id";
            return false;
        }
        var context = snapshot.Context;
        var card = context
            .SelectionOptions.Concat(context.BoardItems)
            .Concat(context.ChestItems)
            .Concat(context.PlayerSkills)
            .FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null)
        {
            error = "card is no longer present";
            return false;
        }

        BazaarAgentTargetSection? target = null;
        IReadOnlyList<string>? sockets = null;
        BazaarAgentActionKind[] kinds;
        if (
            card.Kind == BazaarAgentCardKind.Item
            && card.Location == BazaarAgentCardLocation.Selection
        )
        {
            if (
                !TryBuildPlacement(
                    context,
                    card,
                    request.Target,
                    request.StartSlot,
                    out target,
                    out sockets,
                    out error
                )
            )
                return false;
            kinds = new[] { BazaarAgentActionKind.SelectItem };
        }
        else if (card.Kind == BazaarAgentCardKind.Skill)
        {
            kinds = new[] { BazaarAgentActionKind.SelectSkill };
        }
        else if (card.Kind == BazaarAgentCardKind.Encounter)
        {
            kinds = new[] { BazaarAgentActionKind.SelectEncounter };
        }
        else
        {
            kinds = new[]
            {
                BazaarAgentActionKind.SelectItem,
                BazaarAgentActionKind.CommitToPedestal,
            };
        }

        var option = context.AvailableActions.FirstOrDefault(option =>
            kinds.Contains(option.ActionKind)
            && option.CardInstanceId == instanceId
            && (target is null || option.TargetSection == target)
            && (sockets is null || SocketsEqual(option.TargetSockets, sockets))
        );
        if (option is null)
        {
            error = "select target is not available";
            return false;
        }
        action = new BazaarAgentAction
        {
            ActionKind = option.ActionKind,
            CardInstanceId = instanceId,
            TargetSection = option.TargetSection,
            TargetSockets = option.TargetSockets,
            ForTickId = request.Revision,
        };
        return true;
    }

    private bool TryResolveOwnedCard(
        BazaarAgentContext context,
        string? sessionId,
        string? token,
        out BazaarAgentCardSnapshot? card,
        out string? instanceId
    )
    {
        card = null;
        instanceId = null;
        if (!_protocolV3.TryResolveInstanceId(sessionId, token ?? "", out var resolved))
            return false;
        card = context
            .BoardItems.Concat(context.ChestItems)
            .FirstOrDefault(candidate => candidate.InstanceId == resolved);
        if (card is null)
            return false;
        instanceId = resolved;
        return true;
    }

    private static bool TryBuildPlacement(
        BazaarAgentContext context,
        BazaarAgentCardSnapshot card,
        string? targetText,
        int? startSlot,
        out BazaarAgentTargetSection? target,
        out IReadOnlyList<string>? sockets,
        out string? error
    )
    {
        target = null;
        sockets = null;
        error = null;
        if (string.Equals(targetText, "board", StringComparison.OrdinalIgnoreCase))
        {
            if (startSlot is null)
            {
                error = "board requires at";
                return false;
            }
            target = BazaarAgentTargetSection.Hand;
            sockets = BuildSockets(card.Size, startSlot.Value);
            if (sockets is null)
            {
                error = "board at is outside the board";
                return false;
            }
            return true;
        }
        if (string.Equals(targetText, "chest", StringComparison.OrdinalIgnoreCase))
        {
            if (startSlot is not null)
            {
                error = "chest does not accept at";
                return false;
            }
            target = BazaarAgentTargetSection.Stash;
            sockets = FindAutomaticChestSockets(context.ChestItems, card.Size);
            if (sockets is null)
            {
                error = "chest has no automatic placement";
                return false;
            }
            return true;
        }
        error = "to must be board or chest";
        return false;
    }

    private static IReadOnlyList<string>? BuildSockets(string? sizeText, int start)
    {
        var size = BazaarAgentCardSize.Parse(sizeText, fallback: 0);
        if (size is < 1 or > 3 || start < 0 || start + size > 10)
            return null;
        return Enumerable
            .Range(start, size)
            .Select(slot => "Socket_" + slot.ToString(CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static IReadOnlyList<string>? FindAutomaticChestSockets(
        IReadOnlyList<BazaarAgentCardSnapshot> chest,
        string? sizeText
    )
    {
        var size = BazaarAgentCardSize.Parse(sizeText, fallback: 0);
        if (size is < 1 or > 3)
            return null;
        var occupied = new bool[10];
        foreach (var item in chest)
        {
            if (!TryParseSocket(item.SocketId, out var start))
                continue;
            var itemSize = BazaarAgentCardSize.Parse(item.Size, fallback: 0);
            for (var slot = start; slot < start + itemSize && slot < occupied.Length; slot++)
                occupied[slot] = true;
        }
        for (var start = 0; start <= occupied.Length - size; start++)
            if (Enumerable.Range(start, size).All(slot => !occupied[slot]))
                return BuildSockets(sizeText, start);
        return null;
    }

    private static bool TryParseSocket(string? socket, out int slot)
    {
        slot = -1;
        const string prefix = "Socket_";
        return socket is not null
            && socket.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                socket.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out slot
            )
            && slot is >= 0 and < 10;
    }

    private static bool SocketsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        return true;
    }

    private async Task<T?> ReadJsonBody<T>(
        HttpListenerContext ctx,
        string requestId,
        BazaarAgentHttpLogRoute route
    )
        where T : class
    {
        var bytes = await ReadBodyWithCap(ctx, MaxBodyBytes, requestId, route)
            .ConfigureAwait(false);
        if (bytes is null)
            return null;
        try
        {
            var result = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), _json);
            if (result is not null)
                return result;
        }
        catch (JsonException) { }
        WriteErrorEnvelope(ctx, 400, "invalid", "malformed or empty json");
        return null;
    }

    private static string BuildActionSummary(BazaarAgentAction action)
    {
        var target = string.IsNullOrWhiteSpace(action.CardInstanceId)
            ? ""
            : " for " + action.CardInstanceId;
        return "Action " + action.ActionKind + target;
    }

    private static string BuildActionResultSummary(BazaarAgentAction action, int statusCode) =>
        "Action "
        + action.ActionKind
        + " completed with HTTP "
        + statusCode.ToString(CultureInfo.InvariantCulture);

    // How much of an over-cap request body gets drained after a 413 so the client can read the
    // response instead of hitting a TCP reset, and for how long. Beyond either bound, closing
    // with unread data (and the reset that follows) is the lesser evil — the time bound keeps a
    // stalled client (declared length, never sends) from parking the handler task forever.
    private const long MaxRejectedBodyDrainBytes = 2L * MaxBodyBytes;
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
