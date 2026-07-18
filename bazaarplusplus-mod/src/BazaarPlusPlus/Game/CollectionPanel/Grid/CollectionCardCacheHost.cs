#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Static rendezvous point between the live panel instance (which owns the caches) and the
// Harmony patches (which are static and have no other handle to the panel). The panel
// installs its caches on Open() and uninstalls them when the runtime is disposed; the
// patches no-op when nothing is installed, which is the desired behaviour both before the
// panel exists and after it tears down.
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
