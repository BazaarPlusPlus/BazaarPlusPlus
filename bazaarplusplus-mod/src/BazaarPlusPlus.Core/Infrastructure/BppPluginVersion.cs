#nullable enable
using System.Text;

namespace BazaarPlusPlus.Infrastructure;

public static class BppPluginVersion
{
    // Every BPP assembly carries the product VERSION, so Core's own version is the build's.
    private static readonly string BuildVersion =
        typeof(BppPluginVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static string _current = BuildVersion;
    private static int _initialized;

    public static string Current => _current;

    public static void Initialize(string pluginAssemblyPath)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        _current = Resolve(pluginAssemblyPath);
    }

    public static string Resolve(string pluginAssemblyPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
                return BuildVersion;

            var versionPath = Path.ChangeExtension(pluginAssemblyPath, ".version");
            if (string.IsNullOrWhiteSpace(versionPath) || !File.Exists(versionPath))
                return BuildVersion;

            var version = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
            return string.IsNullOrWhiteSpace(version) ? BuildVersion : version;
        }
        catch
        {
            return BuildVersion;
        }
    }
}
