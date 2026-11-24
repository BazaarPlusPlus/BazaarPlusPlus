#nullable enable
using System.Runtime.InteropServices;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Host;

internal sealed class BepInExPathProvider : IPathProvider
{
    public string? DataRootDirectoryPath { get; private set; }

    public void Initialize()
    {
        DataRootDirectoryPath = DesktopDataRoot.Resolve(
            BepInEx.Paths.GameRootPath,
            PathConstants.DataRootDirectoryName,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        );
    }
}
