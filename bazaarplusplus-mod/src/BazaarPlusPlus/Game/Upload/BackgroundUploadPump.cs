#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Storage.Sqlite;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class BackgroundUploadPump : MonoBehaviour
{
    private UploadPump? _pump;

    public void Initialize(IBppServices services)
    {
        _pump = UploadPump.Start(
            services,
            HostPlatformKey.Current,
            Time.unscaledTime,
            SqliteOpenOptions.Default,
            BppClientCacheBridge.TryGetProfileAccountId
        );
    }

    private void Update() => _pump?.Tick(Time.unscaledTime);

    private void OnDestroy()
    {
        _pump?.Dispose();
        _pump = null;
    }
}
