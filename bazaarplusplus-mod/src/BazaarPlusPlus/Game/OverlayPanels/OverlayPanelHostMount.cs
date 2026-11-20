#nullable enable

using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Host;
using UnityEngine;

namespace BazaarPlusPlus.Game.OverlayPanels;

// Mount before the Main Overlay Panels, which resolve the host during their own setup.
internal sealed class OverlayPanelHostMount : IBppMountable
{
    public DesktopOverlayPanels? Host { get; private set; }

    public void Mount(GameObject host, IBppServices services)
    {
        Host = host.AddComponent<DesktopOverlayPanels>();
        if (services.Paths.DataRootDirectoryPath is { } dataRoot)
        {
            Host.Walkthrough = new WalkthroughControl(dataRoot);
            Host.ShotDirectory = Path.Combine(dataRoot, DesktopUiShot.DirectoryName);
        }
    }

    public void Unmount(GameObject host)
    {
        var component = host.GetComponent<DesktopOverlayPanels>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
        Host = null;
    }
}
