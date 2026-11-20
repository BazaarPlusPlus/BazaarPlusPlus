#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

/// <summary>The game version also keys translations bundled in the game itself, which have no downloaded TranslationEtag.</summary>
public sealed class CollectionCatalogCacheIdentity : IEquatable<CollectionCatalogCacheIdentity>
{
    public CollectionCatalogCacheIdentity(
        string gameDataEtag,
        string translationEtag,
        string language,
        string gameVersion,
        string modBuild
    )
    {
        GameDataEtag = gameDataEtag ?? string.Empty;
        TranslationEtag = translationEtag ?? string.Empty;
        Language = language ?? string.Empty;
        GameVersion = gameVersion ?? string.Empty;
        ModBuild = modBuild ?? string.Empty;
    }

    public string GameDataEtag { get; }

    public string TranslationEtag { get; }

    public string Language { get; }

    public string GameVersion { get; }

    public string ModBuild { get; }

    public static CollectionCatalogCacheIdentity? Resolve(
        string gameDataManifestPath,
        string translationsManifestPath,
        string language,
        string gameVersion,
        string modBuild
    )
    {
        if (!DataManifestEtag.TryRead(gameDataManifestPath, "GameData", out var gameData))
            return null;
        DataManifestEtag.TryRead(translationsManifestPath, language, out var translation);
        return new CollectionCatalogCacheIdentity(
            gameData,
            translation,
            language,
            gameVersion,
            modBuild
        );
    }

    public bool Equals(CollectionCatalogCacheIdentity? other) =>
        other != null
        && string.Equals(GameDataEtag, other.GameDataEtag, StringComparison.Ordinal)
        && string.Equals(TranslationEtag, other.TranslationEtag, StringComparison.Ordinal)
        && string.Equals(Language, other.Language, StringComparison.Ordinal)
        && string.Equals(GameVersion, other.GameVersion, StringComparison.Ordinal)
        && string.Equals(ModBuild, other.ModBuild, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CollectionCatalogCacheIdentity);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(GameDataEtag);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TranslationEtag);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Language);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(GameVersion);
            return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ModBuild);
        }
    }
}
