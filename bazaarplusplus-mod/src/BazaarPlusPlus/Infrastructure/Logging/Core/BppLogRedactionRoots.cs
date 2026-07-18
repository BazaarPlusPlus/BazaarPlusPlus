#nullable enable
namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogRedactionRoots
{
    internal static BppLogRedactionRoots Empty { get; } = new(null, null, null, null);

    internal BppLogRedactionRoots(
        string? gameRoot,
        string? dataRoot,
        string? pluginRoot,
        string? homeRoot
    )
    {
        GameRoot = gameRoot;
        DataRoot = dataRoot;
        PluginRoot = pluginRoot;
        HomeRoot = homeRoot;
    }

    internal string? GameRoot { get; }

    internal string? DataRoot { get; }

    internal string? PluginRoot { get; }

    internal string? HomeRoot { get; }
}
