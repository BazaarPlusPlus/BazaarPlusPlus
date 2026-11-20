#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

public interface ISettingsDockEntry
{
    int Order { get; }

    BppSettingsDockDefinition Build(BppSettings config);
}
