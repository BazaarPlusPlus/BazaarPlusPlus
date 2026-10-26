#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// A feature module's contribution to the BPP settings dock. Each feature owns one
/// implementation; the registry collects them so BppSettingsDockCatalog does not
/// need to import individual feature namespaces.
/// </summary>
public interface ISettingsDockEntry
{
    /// <summary>Lower value = earlier in the settings dock.</summary>
    int Order { get; }

    BppSettingsDockDefinition Build(BppSettings config);
}
