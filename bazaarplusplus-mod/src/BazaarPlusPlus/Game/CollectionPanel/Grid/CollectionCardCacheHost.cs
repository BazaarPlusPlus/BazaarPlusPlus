#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Static rendezvous point between the live panel instance (which owns the caches) and the
// Harmony patches (which are static and have no other handle to the panel). The panel
// installs its caches on Open() and uninstalls them when the runtime is disposed; the
// patches no-op when nothing is installed, which is the desired behaviour both before the
// panel exists and after it tears down.
internal static class CollectionCardCacheHost
{
    public static CollectionCardArtCache? ArtCache { get; private set; }
    public static CollectionCardMaterialCache? MaterialCache { get; private set; }

    public static void Install(CollectionCardArtCache art, CollectionCardMaterialCache material)
    {
        ArtCache = art;
        MaterialCache = material;
    }

    public static void Uninstall(CollectionCardArtCache? art, CollectionCardMaterialCache? material)
    {
        // Compare by reference: a previous Install may have been superseded by a newer panel
        // instance after a scene change. We only clear if the caller is still the active one.
        if (art != null && ReferenceEquals(ArtCache, art))
            ArtCache = null;
        if (material != null && ReferenceEquals(MaterialCache, material))
            MaterialCache = null;
    }
}
