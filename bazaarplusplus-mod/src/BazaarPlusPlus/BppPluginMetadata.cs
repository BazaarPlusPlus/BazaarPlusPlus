#pragma warning disable CS0436
#nullable enable
namespace BazaarPlusPlus;

/// <summary>
/// Re-exports the BazaarPlusPlus BepInEx plugin GUID as a public compile-time constant, so a
/// caller can name the plugin without reaching into the per-assembly, internal, build-generated
/// <c>MyPluginInfo</c>.
/// </summary>
public static class BppPluginMetadata
{
    public const string Guid = MyPluginInfo.PLUGIN_GUID;
}
