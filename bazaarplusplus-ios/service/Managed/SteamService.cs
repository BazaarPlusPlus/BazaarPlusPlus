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
    internal sealed class QrExpiredException : Exception { }
    /// Which failures an automatic retry can outlast.
    public static class Failures
    {
        public static bool IsDiskFull(Exception error) =>
            error is IOException && (error.HResult == unchecked((int)0x80070070) || error.HResult == 28 || error.HResult == unchecked((int)0x80070027));
        /// Steam has refused this login for good; it will never work again.
        public static bool IsRejectedLogin(EResult result) =>
            result == EResult.InvalidPassword || result == EResult.AccessDenied || result == EResult.Expired ||
            result == EResult.Revoked || result == EResult.FileNotFound;
        public static bool IsTransient(Exception error)
        {
            switch (error)
            {
                case AggregateException many: return many.InnerExceptions.All(IsTransient);
                case AuthenticationException auth:
                    return auth.Result == EResult.TryAnotherCM || auth.Result == EResult.ServiceUnavailable || auth.Result == EResult.Busy ||
                        auth.Result == EResult.Timeout || auth.Result == EResult.NoConnection || auth.Result == EResult.Fail;
                // A content server that answers with a client error will answer the same way next time.
                case SteamKitWebRequestException web:
                    return (int)web.StatusCode >= 500 || web.StatusCode == System.Net.HttpStatusCode.RequestTimeout || (int)web.StatusCode == 429;
                case IOException io: return !IsDiskFull(io);
                case OperationCanceledException _:
                case TimeoutException _:
                case System.Net.Http.HttpRequestException _:
                case System.Net.WebException _:
                case System.Net.Sockets.SocketException _:
                case AsyncJobFailedException _:
                    return true;
                default: return false;
            }
        }
    }
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
        // A dropped Steam connection is being brought back from the saved login. The player stays
        // signed in meanwhile; snapshots report it so the launcher never asks to sign in again.
        static bool reconnecting;
        // Cleared when the player cancels a sign-in or the saved login fails for good, until they sign in again.
        static bool autoSignIn = true;
        // The running download's own cancellation: stopping it keeps the session, and a dropped
        // Steam connection leaves it running, because content servers need no Steam connection.
        static CancellationTokenSource? downloadRun;
        static TaskCompletionSource<bool> retryNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        static string DownloadWantedPath => Path.Combine(root, "download-wanted");
        static readonly AsyncLocal<long> operation = new AsyncLocal<long>();
        static readonly System.Collections.Generic.Dictionary<uint, CancellationTokenSource> pendingTickets = new System.Collections.Generic.Dictionary<uint, CancellationTokenSource>();
        // One configuration for every session, so the CM server list (and the servers it has
        // marked bad after a failed connection) carries over to the next attempt.
        static SteamConfiguration? configuration;
        static InstallationIdentity identity = null!;
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
            identity = new InstallationIdentity(root);
            configuration = SteamConfiguration.Create(c => c.WithProtocolTypes(ProtocolTypes.WebSocket).WithMachineInfoProvider(identity));
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
                state, message, progress, downloadedBytes, totalBytes, bytesPerSecond, busy, launched, reconnecting,
                loggedIn = session?.LoggedIn == true || reconnecting,
                account = session?.LoggedIn == true ? session.Account : reconnecting ? savedAccount : "", savedAccount, errorCode, verification, qrUrl,
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
            if (action == "foreground") { Recover(); return; }
            if (action == "cancel" || action == "logout")
            {
                Session? previous = null;
                bool downloadStopped;
                lock (gate)
                {
                    downloadStopped = action == "cancel" && downloadRun != null;
                    downloadRun?.Cancel();
                    WantDownload(false);
                    ++generation;
                    WakeRetry();
                    if (!downloadStopped)
                    {
                        previous = session; session = null; reconnecting = false;
                        autoSignIn = false;
                    }
                }
                if (downloadStopped) { Set("loggedIn", "已取消下载"); return; }
                previous?.Dispose();
                Native.bpp_steam_identity(AppId, 0);
                if (action == "logout") { Native.bpp_steam_save_session(""); lock (gate) savedAccount = ""; }
                Set("signedOut", action == "logout" ? "已退出登录" : "已取消登录"); return;
            }
            if (action != "login" && action != "restore" && action != "qr" && action != "download") return;
            long epoch;
            lock (gate) { if (busy) return; busy = true; epoch = ++generation; busyOperation = epoch; }
            _ = Task.Run(async () =>
            {
                operation.Value = epoch;
                try
                {
                    if (action == "download")
                    {
                        await RunDownload(epoch);
                    }
                    else
                    {
                        lock (gate) autoSignIn = true;
                        bool quiet;
                        lock (gate) quiet = reconnecting;
                        var signedIn = await SignIn(action, data, epoch, quiet);
                        // A download the player started picks up again as soon as they are signed in.
                        if (signedIn != null && File.Exists(DownloadWantedPath)) await RunDownload(epoch);
                    }
                }
                catch (Exception error)
                {
                    lock (gate)
                    {
                        if (epoch != generation) return;
                        if (session != null && !session.LoggedIn)
                        {
                            var dead = session; session = null; dead.Dispose();
                            Native.bpp_steam_identity(AppId, 0);
                        }
                        // Without a session, automatic sign-in stops here: the next attempt is the player's.
                        if (session?.LoggedIn != true) autoSignIn = false;
                        reconnecting = false;
                        string safe = error is AuthenticationException auth ? auth.Result.ToString() : error.GetType().Name;
                        bool timeout = error is OperationCanceledException || error is TimeoutException;
                        bool qrExpired = error is QrExpiredException;
                        bool restoring = action == "restore" || action == "download";
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
                            : Failures.IsDiskFull(error) ? "设备存储空间不足，请清理后重试"
                            : timeout ? "Steam 请求超时或连接中断，请重试"
                            : error is AuthenticationException rejected ? Explain(rejected.Result, restoring)
                            : "操作失败（" + safe + "），请重试或重新登录");
                        errorCode = safe;
                        Console.WriteLine("STEAM_APP_ERROR " + safe + " " + error.StackTrace);
                    }
                }
                finally { lock (gate) { if (epoch == busyOperation) busy = false; } }
                Recover();
            });
        }
        /// Sign in again from the saved login whenever nothing else is running and no live session exists:
        /// at launch, on returning to the foreground, after a dropped connection, and after a download ends.
        static void Recover()
        {
            lock (gate)
            {
                WakeRetry();
                if (launched || busy || !autoSignIn || savedAccount.Length == 0 || session?.LoggedIn == true) return;
            }
            Command("{\"action\":\"restore\"}");
        }
        static void WakeRetry()
        {
            lock (gate)
            {
                retryNow.TrySetResult(true);
                retryNow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        /// The backoff between automatic attempts; returning to the foreground ends it early.
        static async Task WaitBeforeRetry(int failures, CancellationToken cancellation)
        {
            Task woken;
            lock (gate) woken = retryNow.Task;
            int seconds = Math.Min(30, 1 << Math.Min(failures, 5));
            await Task.WhenAny(woken, Task.Delay(TimeSpan.FromSeconds(seconds), cancellation));
            cancellation.ThrowIfCancellationRequested();
        }
        // Survives the App being killed in the background, so the next launch carries on without a tap.
        static void WantDownload(bool wanted)
        {
            if (wanted) File.WriteAllText(DownloadWantedPath, "");
            else File.Delete(DownloadWantedPath);
        }
        /// Null when a cancel overtook the sign-in, or no saved login exists to restore.
        static async Task<Session?> SignIn(string action, JObject data, long epoch, bool quiet)
        {
            session?.Dispose();
            Native.bpp_steam_identity(AppId, 0);
            string user = (string?)data["username"] ?? "", password = (string?)data["password"] ?? "", refresh = "";
            data.Remove("password");
            var started = DateTime.UtcNow;
            int connectAttempts = 0, failures = 0;
            while (true)
            {
                Session? current = null;
                try
                {
                    if (action == "restore")
                    {
                        string saved = Native.LoadSession();
                        if (saved.Length == 0)
                        {
                            // The Keychain has nothing to give while the device is locked; only a saved account means that.
                            lock (gate) if (savedAccount.Length != 0) throw new IOException("Keychain locked");
                            Set("signedOut", "请登录 Steam");
                            return null;
                        }
                        var credentials = JObject.Parse(saved);
                        user = (string)credentials["username"]!; refresh = (string)credentials["refresh"]!;
                    }
                    current = new Session(user, password, refresh, action == "qr", quiet);
                    lock (gate) { if (epoch != generation) { current.Dispose(); return null; } session = current; }
                    try { await current.Login(); }
                    // Steam drops unauthenticated connections after a minute or two, which is
                    // shorter than a QR scan can take; reconnect with a fresh code instead.
                    catch (Exception) when (current.ScanInterrupted && DateTime.UtcNow - started < QrWait)
                    {
                        Console.WriteLine("STEAM_QR_RECONNECT");
                        lock (gate) { if (epoch != generation) return null; session = null; }
                        current.Dispose();
                        continue;
                    }
                    // A CM server that refuses the connection is marked bad in the shared
                    // list, so the next attempt picks a different server.
                    catch (IOException) when (current.NeverConnected && ++connectAttempts < 4)
                    {
                        Console.WriteLine("STEAM_CONNECT_RETRY " + connectAttempts);
                        lock (gate) { if (epoch != generation) return null; session = null; }
                        current.Dispose();
                        continue;
                    }
                    catch (Exception error) when (action == "qr" && (current.ScanInterrupted || error is TimeoutException))
                    { throw new QrExpiredException(); }
                    lock (gate)
                    {
                        if (epoch != generation || session != current) return null;
                        bool remembered = current.Persist();
                        if (remembered) savedAccount = current.Account;
                        Native.bpp_steam_identity(AppId, current.Client.SteamID!.ConvertToUInt64());
                        reconnecting = false;
                        if (!quiet) Set("loggedIn", "已登录，可以下载 " + (string)profile["displayName"]! + (remembered ? "" : "（未能记住登录，下次需重新验证）"));
                    }
                    return current;
                }
                // A saved login survives anything but Steam rejecting it, so a bad connection never asks for the password.
                catch (Exception error) when (action == "restore" && Failures.IsTransient(error))
                {
                    Console.WriteLine("STEAM_RESTORE_RETRY " + error.GetType().Name);
                    lock (gate) { if (epoch != generation) return null; session = null; }
                    current?.Dispose();
                    if (!quiet) Set("connecting", "暂时无法连接 Steam，正在重试");
                    await WaitBeforeRetry(++failures, CancellationToken.None);
                    lock (gate) if (epoch != generation) return null;
                }
                catch (AuthenticationException rejected) when (action == "restore" && Failures.IsRejectedLogin(rejected.Result))
                {
                    Native.bpp_steam_save_session("");
                    lock (gate) savedAccount = "";
                    throw;
                }
            }
        }
        /// Download and prepare the game, for as long as it takes: a lost network, a dropped Steam
        /// connection or a suspended App only delay it, and it keeps its place through each of them.
        static async Task RunDownload(long epoch)
        {
            using (var run = new CancellationTokenSource())
            {
                lock (gate)
                {
                    if (epoch != generation) return;
                    downloadRun = run;
                    WantDownload(true);
                }
                try
                {
                    var progress = new DownloadProgress((next, text, fraction, done, total, speed) => Set(next, text, fraction, done, total, speed));
                    int failures = 0; long received = 0; bool fetched = false;
                    while (true)
                    {
                        try
                        {
                            var current = await Connected(epoch, progress);
                            run.Token.ThrowIfCancellationRequested();
                            if (current == null) throw new InvalidOperationException("Not signed in");
                            if (!fetched)
                            {
                                await Downloader.Download(current.Client, profile, root, progress, run.Token);
                                fetched = true;
                                lock (gate) if (epoch == generation) WantDownload(false);
                            }
                            run.Token.ThrowIfCancellationRequested();
                            // The connection may have dropped during the transfer, and the game needs one to start.
                            lock (gate) if (session?.LoggedIn == true) break;
                        }
                        catch (Exception error) when (!run.IsCancellationRequested && Failures.IsTransient(error))
                        {
                            Console.WriteLine("STEAM_DOWNLOAD_RETRY " + error.GetType().Name);
                            if (progress.Received != received) { received = progress.Received; failures = 0; }
                            progress.Stage("网络中断，正在重试");
                            await WaitBeforeRetry(++failures, run.Token);
                        }
                        catch (Exception error) when (!run.IsCancellationRequested && !(error is AuthenticationException))
                        {
                            // Retrying cannot help; the player decides. A rejected login stays wanted: signing in again carries on.
                            lock (gate) if (epoch == generation) WantDownload(false);
                            throw;
                        }
                    }
                    if (definition.CanLaunch)
                    {
                        Installer.Prepare(profile, root, assets, run.Token);
                        Set("ready", "准备就绪", 1);
                    }
                    else Set("installed", (string?)profile["library"]?["platforms"]?[definition.Platform]?["reason"] ?? "已下载，设备适配中", 1);
                }
                finally { lock (gate) if (downloadRun == run) downloadRun = null; }
            }
        }
        static async Task<Session?> Connected(long epoch, DownloadProgress progress)
        {
            lock (gate)
            {
                if (session?.LoggedIn == true) return session;
                reconnecting = true;
            }
            progress.Stage("正在重新连接 Steam");
            return await SignIn("restore", new JObject(), epoch, true);
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
            readonly bool qr, quiet;
            CancellationToken authCancellation;
            volatile bool waitingForCode;
            volatile bool loggedInElsewhere;
            IOException? disconnectError;
            int disposed;
            public Session(string user, string pass, string token, bool qr, bool quiet)
            {
                username = user; password = pass; refresh = token; this.qr = qr; this.quiet = quiet;
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
                        bool signedIn = loggedOn.Task.Status == TaskStatus.RanToCompletion;
                        disconnectError = new IOException("Steam disconnected"); cancellation.Cancel();
                        lock (gate)
                        {
                            if (session != this) return;
                            Native.bpp_steam_identity(AppId, 0);
                            if (launched) { state = "error"; message = "Steam 连接已断开，请重新登录"; return; }
                            if (signedIn && savedAccount.Length != 0 && (!loggedInElsewhere || busy)) reconnecting = true;
                            // Signing straight back in would log out the other session, which could do the same.
                            // Returning to this App, or a tap, is what takes the account back.
                            if (signedIn && loggedInElsewhere && !busy)
                            {
                                state = "error"; message = "账号已在别处登录，点“连接 Steam”继续"; errorCode = "LoggedInElsewhere";
                            }
                        }
                        if (signedIn && !loggedInElsewhere) Recover();
                    }
                });
                callbacks.Subscribe<SteamUser.LoggedOffCallback>(r =>
                {
                    Console.WriteLine("STEAM_LOGGED_OFF " + r.Result);
                    loggedInElsewhere = r.Result == EResult.LoggedInElsewhere;
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
                if (!quiet) Set("connecting", "正在连接 Steam");
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
                if (!quiet) Set("connecting", "正在完成 Steam 登录");
                Client.GetHandler<SteamUser>()!.LogOn(new SteamUser.LogOnDetails { Username = username, AccessToken = refresh,
                    ShouldRememberPassword = true, LoginID = identity.LoginId });
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
