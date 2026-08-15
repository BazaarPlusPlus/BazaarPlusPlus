#nullable enable
namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPreferenceMigration
{
    internal static IReadOnlyCollection<string>? LoadCanonicalFirst(
        IReadOnlyList<string> readKeys,
        Func<string, bool> hasKey,
        Func<string, IReadOnlyCollection<string>?> load,
        Action<string, IReadOnlyCollection<string>> save,
        Action<string> delete
    )
    {
        if (readKeys == null || readKeys.Count == 0)
            throw new ArgumentException(
                "At least one preference key is required.",
                nameof(readKeys)
            );
        if (hasKey == null)
            throw new ArgumentNullException(nameof(hasKey));
        if (load == null)
            throw new ArgumentNullException(nameof(load));
        if (save == null)
            throw new ArgumentNullException(nameof(save));
        if (delete == null)
            throw new ArgumentNullException(nameof(delete));

        var canonicalKey = readKeys[0];
        if (hasKey(canonicalKey))
            return load(canonicalKey);

        for (var index = 1; index < readKeys.Count; index++)
        {
            var legacyKey = readKeys[index];
            if (!hasKey(legacyKey))
                continue;

            var selectedIds = load(legacyKey);
            if (selectedIds == null)
                return null;

            // Keep the legacy key as the recovery copy until the canonical write succeeds.
            save(canonicalKey, selectedIds);
            delete(legacyKey);
            return selectedIds;
        }

        return null;
    }
}
