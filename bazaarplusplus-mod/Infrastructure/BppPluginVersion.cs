#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BazaarPlusPlus;

internal static class BppPluginVersion
{
    private static string _current = MyPluginInfo.PLUGIN_VERSION;
    private static int _initialized;

    public static string Current => _current;

    public static void Initialize(string pluginAssemblyPath)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        _current = Resolve(pluginAssemblyPath);
    }

    internal static string Resolve(string pluginAssemblyPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
                return MyPluginInfo.PLUGIN_VERSION;

            var versionPath = Path.ChangeExtension(pluginAssemblyPath, ".version");
            if (string.IsNullOrWhiteSpace(versionPath) || !File.Exists(versionPath))
                return MyPluginInfo.PLUGIN_VERSION;

            var version = File.ReadAllText(versionPath, Encoding.UTF8).Trim();
            return string.IsNullOrWhiteSpace(version) ? MyPluginInfo.PLUGIN_VERSION : version;
        }
        catch
        {
            return MyPluginInfo.PLUGIN_VERSION;
        }
    }
}
