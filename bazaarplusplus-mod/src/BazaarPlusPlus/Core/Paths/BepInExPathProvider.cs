#nullable enable
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BepInExPathProvider : IPathProvider
{
    public string? DataRootDirectoryPath { get; private set; }

    public string? PluginsDirectoryPath { get; private set; }

    public void Initialize()
    {
        DataRootDirectoryPath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            PathConstants.DataRootDirectoryName
        );
        PluginsDirectoryPath = BepInEx.Paths.PluginPath;
    }
}
