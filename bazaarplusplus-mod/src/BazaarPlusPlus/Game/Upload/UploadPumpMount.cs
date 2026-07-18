#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class UploadPumpMount : IBppMountable
{
    private readonly IPvpBattleCatalog _battleCatalog;
    private BackgroundUploadPump? _bazaarDbSnapshotPump;
    private BackgroundUploadPump? _runBundlePump;

    public UploadPumpMount(IPvpBattleCatalog battleCatalog)
    {
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
    }

    public void Mount(GameObject host, IBppServices services)
    {
        _bazaarDbSnapshotPump = host.AddComponent<BackgroundUploadPump>();
        _bazaarDbSnapshotPump.Initialize(services, new BazaarDbSnapshotUploadFeed());

        _runBundlePump = host.AddComponent<BackgroundUploadPump>();
        _runBundlePump.Initialize(services, new RunBundleUploadFeed(_battleCatalog));
    }

    public void Unmount(GameObject host)
    {
        if (_runBundlePump != null)
        {
            UnityEngine.Object.DestroyImmediate(_runBundlePump);
            _runBundlePump = null;
        }

        if (_bazaarDbSnapshotPump != null)
        {
            UnityEngine.Object.DestroyImmediate(_bazaarDbSnapshotPump);
            _bazaarDbSnapshotPump = null;
        }
    }
}
