#nullable enable
namespace BazaarPlusPlus.Core.GameState;

/// <summary>
/// The Dragons' hero ids across the game's enum-name transition, without the game's enum.
/// <c>TheDragonsHeroIdentity</c> in the desktop host adds the enum and display-name side;
/// delete both once every supported game build exposes the canonical enum name.
/// </summary>
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
