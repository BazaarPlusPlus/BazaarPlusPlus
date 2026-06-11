#nullable enable

namespace BazaarPlusPlus.Game.Lobby;

internal readonly struct MainMenuVersionUpdateSnapshot
{
    public MainMenuVersionUpdateSnapshot(bool updateAvailable, int revision)
    {
        UpdateAvailable = updateAvailable;
        Revision = revision;
    }

    public bool UpdateAvailable { get; }
    public int Revision { get; }
}

internal static class MainMenuVersionUpdateState
{
    private static readonly object SyncRoot = new();
    private static bool _updateAvailable;
    private static int _revision;

    public static MainMenuVersionUpdateSnapshot Current
    {
        get
        {
            lock (SyncRoot)
                return new MainMenuVersionUpdateSnapshot(_updateAvailable, _revision);
        }
    }

    public static void Reset() => SetUpdateAvailable(false);

    public static void SetUpdateAvailable(bool updateAvailable)
    {
        lock (SyncRoot)
        {
            if (_updateAvailable == updateAvailable)
                return;

            _updateAvailable = updateAvailable;
            _revision++;
        }
    }
}
