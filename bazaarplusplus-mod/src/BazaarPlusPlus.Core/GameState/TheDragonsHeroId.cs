#nullable enable
namespace BazaarPlusPlus.Core.GameState;

// Keep the legacy alias while any supported game build still exposes it.
public static class TheDragonsHeroId
{
    public const string CanonicalId = "TheDragons";
    public const string LegacyId = "Hero8";

    public static bool IsAlias(string? heroId) =>
        !string.IsNullOrWhiteSpace(heroId)
        && (
            string.Equals(heroId!.Trim(), LegacyId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(heroId.Trim(), CanonicalId, StringComparison.OrdinalIgnoreCase)
        );
}
