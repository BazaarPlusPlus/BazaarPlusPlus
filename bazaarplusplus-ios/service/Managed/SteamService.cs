using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf.Meta;
using SteamKit2;
using SteamKit2.Authentication;

namespace BazaarPlusPlus.Ios.Service
{
    public static class SteamService
    {
        static readonly object gate = new object();
        static string state = "signedOut", message = "登录 Steam 以下载已拥有的游戏", root = "", assets = "";
        // savedAccount mirrors the Keychain entry so snapshots never touch the Keychain.
        static string savedAccount = "", errorCode = "", verification = "", qrUrl = "", deviceName = "iOS";
        static double progress;
        static long downloadedBytes, totalBytes, bytesPerSecond;
        static JObject profile = new JObject();
        static GameDefinition definition = null!;
        static bool launched;
        static Session? session;
        static bool busy;
        static long generation, busyOperation;
        static readonly AsyncLocal<long> operation = new AsyncLocal<long>();
        static readonly System.Collections.Generic.Dictionary<uint, CancellationTokenSource> pendingTickets = new System.Collections.Generic.Dictionary<uint, CancellationTokenSource>();
        // One configuration for every session, so the CM server list (and the servers it has
        // marked bad after a failed connection) carries over to the next attempt.
        static SteamConfiguration? configuration;
        public static void Initialize(string config)
        {
            RuntimeTypeModel.Default.AutoCompile = false;
            using (var compressed = new MemoryStream(Convert.FromBase64String("H4sIAAAAAAAC/ysuSU3M1S0qzSvJzE0FAAZ7NbcNAAAA")))
            using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
                if (reader.ReadToEnd() != "steam-runtime") throw new InvalidDataException("Runtime compression self-check failed");
            DebugLog.AddListener((category, text) => Console.WriteLine("STEAM_CONNECT " + category + ": " + text));
            var data = JObject.Parse(config);
            root = (string)data["root"]!; assets = (string)data["assets"]!;
            definition = new GameDefinition(assets, (string)data["platform"]!);
            profile = definition.Game;
            Directory.CreateDirectory(root);
            deviceName = (string?)data["deviceName"] ?? deviceName;
            savedAccount = SavedAccount();
            configuration = SteamConfiguration.Create(c => c.WithProtocolTypes(ProtocolTypes.WebSocket).WithMachineInfoProvider(new InstallationIdentity(root)));
            Set("signedOut", "登录 Steam 以下载已拥有的游戏");

        }
        internal static uint AppId => (uint)profile["steam"]!["appId"]!;
        internal static void Set(string next, string text, double fraction = 0, long done = 0, long total = 0, long speed = 0)
        {
            lock (gate)
            {
                if (operation.Value != 0 && operation.Value != generation) return;
                state = next; message = text; progress = fraction;
                downloadedBytes = done; totalBytes = total; bytesPerSecond = speed;
                if (next != "error") errorCode = "";
                if (next != "code") verification = "";
                if (next != "qr") qrUrl = "";
            }
            Console.WriteLine("STEAM_APP_STATE " + next);
        }
        public static string Snapshot()
        {
            lock (gate) return JsonConvert.SerializeObject(new {
                state, message, progress, downloadedBytes, totalBytes, bytesPerSecond, busy, launched, loggedIn = session?.LoggedIn ?? false,
                account = session?.LoggedIn == true ? session.Account : "", savedAccount, errorCode, verification, qrUrl,
                runtimeRoot = GameDefinition.RuntimeRoot(root, profile),
                canLaunch = definition.CanLaunch, platform = definition.Platform,
                game = new[] { profile }.Select(g => new {
                    id = (string)g["id"]!, title = (string)g["displayName"]!,
                    subtitle = (string?)g["library"]?["subtitle"] ?? "",
                    genre = (string?)g["library"]?["genre"] ?? "",
                    description = (string?)g["library"]?["description"] ?? "",
                    accent = (string?)g["library"]?["accent"] ?? "D99147",
                    monogram = (string?)g["library"]?["monogram"] ?? "G",
                    version = (string)g["testedGameVersion"]!,
                    compatibility = g["library"]?["platforms"]?[definition.Platform],
                    downloaded = GameDefinition.IsDownloaded(root, g)
                }).Single()
            });
        }
        public static void Command(string json)
        {
            var data = JObject.Parse(json);
            string action = (string)data["action"]!;
            if (action == "launch")
            {
                lock (gate)
                    if (!busy && state == "ready" && definition.CanLaunch && session?.LoggedIn == true) launched = true;
                return;
            }
            if (launched) return;
            if (action == "guard") { session?.SubmitCode((string)data["code"]!); return; }
            if (action == "cancel" || action == "logout")
            {
                Session? previous;
                lock (gate) { ++generation; previous = session; session = null; }
                previous?.Dispose();
                Native.bpp_steam_identity(AppId, 0);
                if (action == "logout") { Native.bpp_steam_save_session(""); lock (gate) savedAccount = ""; }
                Set("signedOut", "已退出登录"); return;
            }
            if (action != "login" && action != "restore" && action != "qr" && action != "download") return;
            long epoch;
            lock (gate) { if (busy) return; busy = true; epoch = ++generation; busyOperation = epoch; }
            _ = Task.Run(async () =>
            {
                operation.Value = epoch;
                Session? current = null;
                bool login = action == "login" || action == "restore" || action == "qr";
                try
                {
                    if (login)
                    {
                        session?.Dispose();
                        Native.bpp_steam_identity(AppId, 0);
                        string user = (string?)data["username"] ?? "", password = (string?)data["password"] ?? "", refresh = "";
                        data.Remove("password");
                        if (action == "restore")
                        {
                            string saved = Native.LoadSession();
                            if (saved.Length == 0) { Set("signedOut", "请登录 Steam"); return; }
                            var credentials = JObject.Parse(saved);
                            user = (string)credentials["username"]!; refresh = (string)credentials["refresh"]!;
                        }
                        var started = DateTime.UtcNow;
                        int connectAttempts = 0;
                        while (true)
                        {
                            current = new Session(user, password, refresh, action == "qr");
                            lock (gate) { if (epoch != generation) { current.Dispose(); return; } session = current; }
                            try { await current.Login(); break; }
                            // Steam drops unauthenticated connections after a minute or two, which is
                            // shorter than a QR scan can take; reconnect with a fresh code instead.
                            catch (Exception) when (current.ScanInterrupted && DateTime.UtcNow - started < QrWait)
                            {
                                Console.WriteLine("STEAM_QR_RECONNECT");
                                lock (gate) { if (epoch != generation) return; session = null; }
                                current.Dispose();
                            }
                            // A CM server that refuses the connection is marked bad in the shared
                            // list, so the next attempt picks a different server.
                            catch (IOException) when (current.NeverConnected && ++connectAttempts < 4)
                            {
                                Console.WriteLine("STEAM_CONNECT_RETRY " + connectAttempts);
                                lock (gate) { if (epoch != generation) return; session = null; }
                                current.Dispose();
                            }
                        }
                        lock (gate)
                        {
                            if (epoch != generation || session != current) return;
                            bool remembered = current.Persist();
                            if (remembered) savedAccount = current.Account;
                            Native.bpp_steam_identity(AppId, current.Client.SteamID!.ConvertToUInt64());
                            Set("loggedIn", "已登录，可以下载 " + (string)profile["displayName"]! + (remembered ? "" : "（未能记住登录，下次需重新验证）"));
                        }
                    }
                    else
                    {
                        current = session;
                        if (current == null || !current.LoggedIn) throw new InvalidOperationException("Not signed in");
                        await Downloader.Download(current.Client, profile, root, current.Cancellation);
                        current.Cancellation.ThrowIfCancellationRequested();
                        if (definition.CanLaunch)
                        {
                            Installer.Prepare(profile, root, assets, current.Cancellation);
                            Set("ready", "准备就绪", 1);
                        }
                        else Set("installed", (string?)profile["library"]?["platforms"]?[definition.Platform]?["reason"] ?? "已下载，设备适配中", 1);
                    }
                }
                catch (Exception error)
                {
                    lock (gate)
                    {
                        if (epoch != generation) return;
                        if (login && session == current) { session = null; current?.Dispose(); Native.bpp_steam_identity(AppId, 0); }
                        string safe = error is AuthenticationException auth ? auth.Result.ToString() : error.GetType().Name;
                        bool timeout = error is OperationCanceledException || error is TimeoutException;
                        bool qrExpired = action == "qr" && current != null && (current.ScanInterrupted || error is TimeoutException);
                        // A rejected refresh token will never succeed again; forget it.
                        if (action == "restore" && error is AuthenticationException)
                        { Native.bpp_steam_save_session(""); savedAccount = ""; }
                        if (qrExpired) safe = "QrExpired";
                        if (error is GameVersionUnsupportedException unsupported)
                        {
                            // Not an error: the download is kept, and the next App release adapts to it.
                            Set("outdated", "Steam 上的游戏已更新到 " + ShortVersion(unsupported.Found) + "，这个版本的 App 还在适配。适配完成后更新 App 即可开始游戏。", 1);
                            errorCode = "GameVersionUnsupported";
                            Console.WriteLine("STEAM_APP_GAME_OUTDATED " + unsupported.Found);
                            return;
                        }
                        Set("error", qrExpired ? "二维码已过期，请重新显示二维码"
                            : error is NoHttpsContentServerException ? "当前网络没有可用的安全下载节点，请切换网络后重试"
                            : timeout ? "Steam 请求超时或连接中断，请重试"
                            : error is AuthenticationException rejected ? Explain(rejected.Result, action == "restore")
                            : "操作失败（" + safe + "），请重试或重新登录");
                        errorCode = safe;
                        Console.WriteLine("STEAM_APP_ERROR " + safe + " " + error.StackTrace);
                    }
                }
                finally { lock (gate) { if (epoch == busyOperation) busy = false; } }
            });
        }
        static readonly TimeSpan QrWait = TimeSpan.FromSeconds(600);
        // "1.0.12764-prod-macos-arm64-013ff919" -> "1.0.12764"
        static string ShortVersion(string version) => version.Split('-')[0];
        static string Explain(EResult result, bool restoring)
        {
            switch (result)
            {
                case EResult.InvalidPassword: return restoring ? "保存的登录已失效，请重新登录" : "账号或密码不正确";
                case EResult.RateLimitExceeded: case EResult.AccountLoginDeniedThrottle: return "尝试次数过多，请稍后再试";
                case EResult.TwoFactorCodeMismatch: case EResult.InvalidLoginAuthCode: return "验证码不正确，请重新登录";
                case EResult.Expired: case EResult.FileNotFound: return restoring ? "保存的登录已失效，请重新登录" : "登录请求已过期，请重新登录";
                case EResult.AccessDenied: return restoring ? "保存的登录已失效，请重新登录" : "Steam 拒绝了这次登录，请确认是本人操作";
                default: return "Steam 登录失败（" + result + "），请重试";
            }
        }
        static string SavedAccount()
        {
            try
            {
                string saved = Native.LoadSession();
                return saved.Length == 0 ? "" : (string?)JObject.Parse(saved)["username"] ?? "";
            }
            catch (Newtonsoft.Json.JsonException) { return ""; }
        }
        public static void RequestTicket(string json)
        {
            var request = JObject.Parse(json); uint handle = (uint)request["handle"]!;
            Session? current;
            CancellationTokenSource cancellation;
            lock (gate)
            {
                current = session;
                if ((bool?)request["cancel"] == true)
                {
                    if (pendingTickets.TryGetValue(handle, out var pending)) pending.Cancel();
                    current?.Tickets.Cancel(handle);
                    return;
                }
                if (current == null || !current.LoggedIn)
                {
                    Native.bpp_steam_ticket(AppId, handle, Array.Empty<byte>(), 0, 2); return;
                }
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(current.Cancellation);
                pendingTickets.Add(handle, cancellation);
            }
            _ = Task.Run(async () =>
            {
                byte[] ticket = Array.Empty<byte>(); int result = 2;
                try
                {
                    ticket = await current.Tickets.Create(AppId, handle, (string?)request["identity"] ?? "", cancellation.Token);
                    result = 1;
                }
                catch (Exception error) { Console.WriteLine("STEAM_APP_TICKET_ERROR " + error.GetType().Name); }
                finally
                {
                    lock (gate)
                    {
                        if (session == current && !cancellation.IsCancellationRequested && current.LoggedIn)
                        {
                            Native.bpp_steam_ticket(AppId, handle, ticket, result == 1 ? ticket.Length : 0, result);
                            Console.WriteLine("STEAM_APP_WEB_TICKET " + (result == 1 ? "acknowledged" : "failed"));
                        }
                        else current.Tickets.Cancel(handle);
                        pendingTickets.Remove(handle);
                        cancellation.Dispose();
                    }
                    Array.Clear(ticket, 0, ticket.Length);
                }
            });
        }
        internal sealed class Session : IAuthenticator, IDisposable
        {
            public SteamClient Client { get; }
            public AuthTickets Tickets { get; } = new AuthTickets();
            readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            public CancellationToken Cancellation => cancellation.Token;
            public string Account => username;
            public bool ScanInterrupted { get; private set; }
            /// The transport failed before Steam answered; another CM server may still work.
            public bool NeverConnected => !connected.Task.IsCompleted || connected.Task.IsFaulted;
            volatile bool awaitingScan;
            public bool LoggedIn => loggedOn.Task.Status == TaskStatus.RanToCompletion && !Cancellation.IsCancellationRequested;
            readonly CallbackManager callbacks;
            readonly TaskCompletionSource<bool> connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> loggedOn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<string>? code;
            string username, password, refresh;
            readonly bool qr;
            CancellationToken authCancellation;
            volatile bool waitingForCode;
            IOException? disconnectError;
            int disposed;
            public Session(string user, string pass, string token, bool qr)
            {
                username = user; password = pass; refresh = token; this.qr = qr;
                Client = new SteamClient(configuration!);
                Client.DebugNetworkListener = new PacketTrace();
                Client.AddHandler(Tickets); callbacks = new CallbackManager(Client);
                callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => { Console.WriteLine("STEAM_CONNECTED_CALLBACK"); connected.TrySetResult(true); });
                callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
                {
                    if (awaitingScan) ScanInterrupted = true;
                    connected.TrySetException(new IOException("Steam disconnected"));
                    loggedOn.TrySetException(new IOException("Steam disconnected"));
                    if (!Cancellation.IsCancellationRequested)
                    {
                        disconnectError = new IOException("Steam disconnected"); cancellation.Cancel();
                        lock (gate) if (session == this) { Native.bpp_steam_identity(AppId, 0); state = "error"; message = "Steam 连接已断开，请重新登录"; }
                    }
                });
                callbacks.Subscribe<SteamUser.LoggedOnCallback>(r =>
                {
                    Console.WriteLine("STEAM_LOGON_RESULT " + r.Result);
                    if (r.Result == EResult.OK) loggedOn.TrySetResult(true);
                    else loggedOn.TrySetException(new AuthenticationException("Logon", r.Result));
                });
            }
            public async Task Login()
            {
                Set("connecting", "正在连接 Steam");
                DebugLog.Enabled = true;
                _ = Task.Run(() => {
                    try {
                        Console.WriteLine("STEAM_CALLBACK_PUMP started");
                        while (!Cancellation.IsCancellationRequested) callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
                    } catch (Exception error) {
                        Console.WriteLine("STEAM_CALLBACK_PUMP_ERROR " + error.GetType().Name + " " + error.StackTrace);
                        connected.TrySetException(error); loggedOn.TrySetException(error); cancellation.Cancel();
                    }
                });
                try { Client.Connect(); await Await(connected.Task, 40); }
                finally { DebugLog.Enabled = false; }
                Console.WriteLine("STEAM_AUTH_BEGIN");
                if (refresh.Length == 0 && qr)
                {
                    // Another device's Steam app scans the code; Steam rotates it while we wait.
                    var qrTask = Client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails {
                        DeviceFriendlyName = deviceName, IsPersistentSession = true });
                    await Await(qrTask, 40);
                    var auth = await qrTask;
                    void Show() { Set("qr", "用另一台设备上的 Steam 手机 App 扫描二维码"); lock (gate) qrUrl = auth.ChallengeURL; }
                    auth.ChallengeURLChanged = Show;
                    Show();
                    var poll = auth.PollingWaitForResultAsync(Cancellation);
                    awaitingScan = true;
                    try { await Await(poll, (int)QrWait.TotalSeconds); }
                    finally { awaitingScan = false; }
                    var result = await poll;
                    username = result.AccountName; refresh = result.RefreshToken;
                }
                else if (refresh.Length == 0)
                {
                    var details = new AuthSessionDetails { Username = username, Password = password,
                        DeviceFriendlyName = deviceName, IsPersistentSession = true, Authenticator = this };
                    var authTask = Client.Authentication.BeginAuthSessionViaCredentialsAsync(details);
                    await Await(authTask, 40);
                    var auth = await authTask;
                    Console.WriteLine("STEAM_AUTH_POLL");
                    password = ""; details.Password = null;
                    var poll = WaitForAuthentication(auth);
                    await Await(poll, 180);
                    var result = await poll;
                    username = result.AccountName; refresh = result.RefreshToken;
                }
                Set("connecting", "正在完成 Steam 登录");
                Client.GetHandler<SteamUser>()!.LogOn(new SteamUser.LogOnDetails { Username = username, AccessToken = refresh,
                    ShouldRememberPassword = true, LoginID = 19374212 });
                await Await(loggedOn.Task, 40);
                Cancellation.ThrowIfCancellationRequested();
            }
            public bool Persist()
            {
                Cancellation.ThrowIfCancellationRequested();
                bool saved = Native.bpp_steam_save_session(JsonConvert.SerializeObject(new { username, refresh })) == 0;
                if (saved) refresh = "";
                return saved;
            }
            async Task<AuthPollResult> WaitForAuthentication(CredentialsAuthSession auth)
            {
                // SteamKit's code flow waits for input without polling. The user can still
                // approve on their phone during that wait, so accept either confirmation.
                using (var phase = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
                {
                    authCancellation = phase.Token;
                    var interactive = auth.PollingWaitForResultAsync(phase.Token);
                    var mobile = PollWhileAwaitingCode(auth, phase.Token);
                    try { return await await Task.WhenAny(interactive, mobile); }
                    finally
                    {
                        phase.Cancel();
                        _ = interactive.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        _ = mobile.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
            }
            async Task<AuthPollResult> PollWhileAwaitingCode(CredentialsAuthSession auth, CancellationToken token)
            {
                var interval = auth.PollingInterval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : auth.PollingInterval;
                while (true)
                {
                    await Task.Delay(interval, token);
                    if (!waitingForCode) continue;
                    var result = await auth.PollAuthSessionStatusAsync();
                    if (result != null) return result;
                }
            }
            async Task Await(Task task, int seconds)
            {
                if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds), Cancellation)) != task)
                { if (disconnectError != null) throw disconnectError; Cancellation.ThrowIfCancellationRequested(); throw new TimeoutException(); }
                await task;
            }
            // Always take the code path: while it waits, PollWhileAwaitingCode still accepts
            // approval on the phone, so the player can do either without restarting the login.
            public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);
            public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => AskCode("app", previousCodeWasIncorrect
                ? "验证码不正确。请输入 Steam 令牌里新的验证码，或在手机上批准“" + deviceName + "”的登录请求"
                : "在 Steam 手机 App 中批准“" + deviceName + "”的登录请求，或输入 Steam 令牌里的 5 位验证码");
            public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => AskCode("email", previousCodeWasIncorrect
                ? "验证码不正确，请输入 Steam 发到 " + email + " 的最新验证码"
                : "请输入 Steam 发到 " + email + " 的验证码");
            async Task<string> AskCode(string kind, string prompt)
            {
                code = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = code;
                waitingForCode = true;
                using (authCancellation.Register(() => pending.TrySetCanceled()))
                {
                    Set("code", prompt);
                    verification = kind;
                    try { return await pending.Task; }
                    finally { waitingForCode = false; }
                }
            }
            public void SubmitCode(string value) { code?.TrySetResult(value.Trim()); }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                cancellation.Cancel(); code?.TrySetCanceled(); Client.Disconnect(); password = refresh = "";
            }
        }
        sealed class PacketTrace : IDebugNetworkListener
        {
            public void OnIncomingNetworkMessage(EMsg message, byte[] data) => Console.WriteLine("STEAM_RX " + message);
            public void OnOutgoingNetworkMessage(EMsg message, byte[] data) => Console.WriteLine("STEAM_TX " + message);
        }
    }
}
