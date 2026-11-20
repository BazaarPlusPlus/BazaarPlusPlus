#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

public static class ReplaySavedStateRules
{
    public static uint ResolvePositiveUInt(uint rawValue, int? manifestValue)
    {
        if (rawValue > 0)
            return rawValue;

        return manifestValue is > 0 ? unchecked((uint)manifestValue.Value) : rawValue;
    }

    public static int ResolveLevel(int? rawValue, int? manifestValue)
    {
        if (rawValue is > 0)
            return rawValue.Value;
        if (manifestValue is > 0)
            return manifestValue.Value;

        return 1;
    }

    public static int ResolveOpeningLevel(int? openingLevel, int currentLevel)
    {
        return openingLevel is > 0 ? openingLevel.Value : currentLevel;
    }
}
