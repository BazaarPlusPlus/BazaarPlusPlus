using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppInterop.Runtime;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidGameIds
{
    public static string? Hero(EHero hero)
    {
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

    // IL2CPP returns null or a boxed T for nullable getters. The generated Nullable proxy throws on
    // null and reads a boxed T with the wrong layout, so its HasValue and Value are unreliable.
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

    // Generated nullable setters unbox via Il2CppObjectBaseToPtrNotNull. A null proxy throws, so
    // represent absence with an empty box.
    public static Il2CppSystem.Nullable<T> Optional<T>(T? value)
        where T : struct =>
        value is { } present
            ? new Il2CppSystem.Nullable<T>(present)
            : new Il2CppSystem.Nullable<T>();

    // A bool from an IL2CPP tuple proxy can test true in managed code but act false when passed
    // back to the game.
    public static unsafe bool Canonical(bool value) => *(byte*)&value != 0;
}
