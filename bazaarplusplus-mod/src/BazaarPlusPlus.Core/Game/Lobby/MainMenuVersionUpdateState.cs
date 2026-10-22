#nullable enable

namespace BazaarPlusPlus.Game.Lobby;

public readonly struct MainMenuVersionUpdateSnapshot
{
    public MainMenuVersionUpdateSnapshot(MainMenuUpdateStatus status, int revision)
    {
        Status = status;
        Revision = revision;
    }

    public MainMenuUpdateStatus Status { get; }
    public int Revision { get; }
}

public static class MainMenuVersionUpdateState
{
    private static readonly object SyncRoot = new();
    private static MainMenuUpdateStatus _status;
    private static int _revision;

    public static MainMenuVersionUpdateSnapshot Current
    {
        get
        {
            lock (SyncRoot)
                return new MainMenuVersionUpdateSnapshot(_status, _revision);
        }
    }

    public static void SetStatus(MainMenuUpdateStatus status)
    {
        lock (SyncRoot)
        {
            if (_status == status)
                return;

            _status = status;
            _revision++;
        }
    }
}
