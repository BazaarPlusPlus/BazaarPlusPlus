#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal sealed class LiveBuildPanelMount : IBppMountable
{
    public void Mount(GameObject host, IBppServices services)
    {
        host.AddComponent<LiveBuildPanel>();
        BppLog.Info("LiveBuildPanelMount", "LiveBuildPanel mounted.");
    }

    public void Unmount(GameObject host)
    {
        var panel = host.GetComponent<LiveBuildPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
