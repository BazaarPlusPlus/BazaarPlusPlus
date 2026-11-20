#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Static Harmony patches use this to reach the active panel's caches.
internal static class CollectionCardCacheHost
{
    public static CollectionCardCacheSession? ActiveSession { get; private set; }
    public static CollectionCardArtCache? ArtCache => ActiveSession?.ArtCache;
    public static CollectionCardMaterialCache? MaterialCache => ActiveSession?.MaterialCache;

    public static CollectionCardCacheSession Install(
        CollectionCardArtCache art,
        CollectionCardMaterialCache material
    )
    {
        var session = new CollectionCardCacheSession(art, material);
        ActiveSession = session;
        return session;
    }

    public static void Uninstall(CollectionCardCacheSession? session)
    {
        if (session != null && ReferenceEquals(ActiveSession, session))
            ActiveSession = null;
    }
}

internal sealed class CollectionCardCacheSession
{
    public CollectionCardCacheSession(
        CollectionCardArtCache artCache,
        CollectionCardMaterialCache materialCache
    )
    {
        ArtCache = artCache;
        MaterialCache = materialCache;
    }

    public CollectionCardArtCache ArtCache { get; }
    public CollectionCardMaterialCache MaterialCache { get; }
}
