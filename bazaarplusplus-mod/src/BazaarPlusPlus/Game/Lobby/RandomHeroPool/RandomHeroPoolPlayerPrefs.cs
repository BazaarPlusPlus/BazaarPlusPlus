#nullable enable
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal static class RandomHeroPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomHeroPool.Selected";

    public static IReadOnlyCollection<string>? LoadSelectedHeroIds() =>
        RandomPoolPrefsHelpers.LoadIdCollection(BuildScopedPrefsKey(), RandomPoolKind.Hero);

    public static void SaveSelectedHeroIds(IEnumerable<string> heroIds) =>
        RandomPoolPrefsHelpers.SaveIdCollection(BuildScopedPrefsKey(), heroIds);

    public static bool TryResolveState(
        IEnumerable<string> unlockedHeroIds,
        out RandomHeroPoolState? state
    )
    {
        if (unlockedHeroIds is null)
            throw new ArgumentNullException(nameof(unlockedHeroIds));

        var normalizedUnlockedHeroIds = RandomPoolPrefsHelpers.NormalizeIds(unlockedHeroIds);
        if (normalizedUnlockedHeroIds.Length == 0)
        {
            state = null;
            return false;
        }

        state = RandomHeroPoolStateFactory.Create(normalizedUnlockedHeroIds, LoadSelectedHeroIds());
        return true;
    }

    public static IReadOnlyList<string> ResolveEffectivePool(IEnumerable<string> unlockedHeroIds)
    {
        if (!TryResolveState(unlockedHeroIds, out var state) || state == null)
        {
            return Array.Empty<string>();
        }

        var candidateHeroIds = state.SelectedHeroIds.ToArray();
        SaveSelectedHeroIds(candidateHeroIds);
        return candidateHeroIds;
    }

    private static string BuildScopedPrefsKey() =>
        $"{SelectedPoolPrefsKeyPrefix}.{RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(RandomPoolKind.Hero)}";
}
