#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Host;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

/// <summary>The desktop's Unity driver of Core's <see cref="UploadPump"/>.</summary>
internal sealed class BackgroundUploadPump : MonoBehaviour
{
    private UploadPump? _pump;

    public void Initialize(IBppServices services)
    {
        _pump = UploadPump.Start(services, DesktopPlatformKey.Current, Time.unscaledTime);
    }

    private void Update() => _pump?.Tick(Time.unscaledTime);

    private void OnDestroy()
    {
        _pump?.Dispose();
        _pump = null;
    }
}
