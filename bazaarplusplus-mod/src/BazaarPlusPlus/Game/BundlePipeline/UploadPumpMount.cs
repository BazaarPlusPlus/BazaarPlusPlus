#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.Host;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class UploadPumpMount : IBppMountable
{
    private BackgroundUploadPump? _pump;

    public void Mount(UnityEngine.GameObject host, IBppServices services)
    {
        _pump = host.AddComponent<BackgroundUploadPump>();
        _pump.Initialize(services);
    }

    public void Unmount(UnityEngine.GameObject host)
    {
        if (_pump == null)
            return;
        UnityEngine.Object.DestroyImmediate(_pump);
        _pump = null;
    }
}
