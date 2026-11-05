using SteamKit2;
using SteamKit2.Authentication;

namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>One connection to Steam, and the login on it.</summary>
public sealed class SteamSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly CallbackManager callbacks;
    private readonly CancellationTokenSource stop = new();
    private readonly Thread pump;
    private readonly TaskCompletionSource connected = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource<SteamUser.LoggedOnCallback> loggedOn = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public SteamClient Client { get; } = new();
    public SteamApps Apps => Client.GetHandler<SteamApps>()!;
    public SteamContent Content => Client.GetHandler<SteamContent>()!;

    private SteamSession()
    {
        callbacks = new CallbackManager(Client);
        callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => connected.TrySetResult());
        callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            var lost = new IOException("The connection to Steam closed");
            connected.TrySetException(lost);
            loggedOn.TrySetException(lost);
        });
        callbacks.Subscribe<SteamUser.LoggedOnCallback>(result => loggedOn.TrySetResult(result));
        pump = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
                callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(200));
        })
        {
            IsBackground = true,
        };
        pump.Start();
    }

    private static async Task<SteamSession> Connect()
    {
        var session = new SteamSession();
        try
        {
            session.Client.Connect();
            await session.connected.Task.WaitAsync(Timeout);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Signs in with a saved login. A rejected login names the step that renews it.</summary>
    public static async Task<SteamSession> SignIn(SteamLogin login)
    {
        var session = await Connect();
        try
        {
            session
                .Client.GetHandler<SteamUser>()!
                .LogOn(
                    new SteamUser.LogOnDetails
                    {
                        Username = login.Account,
                        AccessToken = login.RefreshToken,
                        // Steam ends an older session with the same login id. Channels run at the
                        // same time on one account, beside its desktop client and iOS App.
                        LoginID = (uint)Random.Shared.Next(1, int.MaxValue),
                        ShouldRememberPassword = true,
                    }
                );
            var result = await session.loggedOn.Task.WaitAsync(Timeout);
            if (result.Result != EResult.OK)
                throw new InvalidOperationException(SteamLogin.DescribeFailure(result.Result));
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Signs in at the terminal, with Steam Guard, and returns the login to save.</summary>
    public static async Task<SteamLogin> CreateLogin(string account, string password)
    {
        using var session = await Connect();
        var auth = await session.Client.Authentication.BeginAuthSessionViaCredentialsAsync(
            new AuthSessionDetails
            {
                Username = account,
                Password = password,
                IsPersistentSession = true,
                Authenticator = new UserConsoleAuthenticator(),
            }
        );
        var result = await auth.PollingWaitForResultAsync();
        return new SteamLogin(result.AccountName, result.RefreshToken);
    }

    public void Dispose()
    {
        stop.Cancel();
        Client.Disconnect();
        pump.Join(TimeSpan.FromSeconds(5));
        stop.Dispose();
    }
}
