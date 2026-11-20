#nullable enable
namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// Use the serialized health text because the native tooltip prefab regroups its chrome across builds.
internal static class NativeMonsterBoardHealthBar
{
    internal static T Root<T>(T healthText, Func<T, T?> parent, Func<T, bool> holdsBoardPart)
        where T : class
    {
        var root = healthText;
        while (parent(root) is { } next && !holdsBoardPart(next))
            root = next;
        return root;
    }
}
