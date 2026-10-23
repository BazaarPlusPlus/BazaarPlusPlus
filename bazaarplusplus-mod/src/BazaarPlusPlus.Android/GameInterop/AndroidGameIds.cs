using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppInterop.Runtime;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>The BPP forms of IL2CPP game values, as the desktop host writes them.</summary>
internal static class AndroidGameIds
{
    /// <summary>The canonical hero id that leaves the process (SQLite rows, uploads).</summary>
    public static string? Hero(EHero hero)
    {
        // A hero facet's name is its canonical id; a hero BPP has no facet for keeps its name.
        if (AndroidCardFacets.Hero.TryToFacet(hero, out var facet))
            return facet.ToString();
        var name = hero.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static int? Attribute(
        Il2CppBazaarGameClient.Domain.Models.Player player,
        EPlayerAttributeType attribute
    )
    {
        // The dictionary GetAttributeValue reads, without its int? return (see Unboxed).
        var attributes = player.Attributes;
        return attributes != null && attributes.TryGetValue(attribute, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// The value of a <c>T?</c> an IL2CPP getter returns. The runtime returns null or a boxed
    /// <c>T</c>; the generated getter wraps it in an <c>Il2CppSystem.Nullable</c> proxy, which
    /// throws on null and otherwise reads the box with Nullable's layout (hasValue first), so its
    /// HasValue and Value are wrong. Never read that proxy's HasValue, Value, or ToString.
    /// </summary>
    public static unsafe T? Unboxed<T>(Func<Il2CppSystem.Nullable<T>> read)
        where T : unmanaged
    {
        Il2CppSystem.Nullable<T> boxed;
        try
        {
            boxed = read();
        }
        catch (NullReferenceException)
        {
            return null;
        }
        return boxed == null ? null : *(T*)IL2CPP.il2cpp_object_unbox(boxed.Pointer);
    }
}
