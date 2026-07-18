#nullable enable
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public static class RandomHeroPoolPreferences
{
    public static IReadOnlyCollection<string> Sanitize(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        return RandomHeroPoolStateFactory
            .Create(unlockedHeroIds, savedPoolHeroIds)
            .SelectedHeroIds.ToArray();
    }
}
