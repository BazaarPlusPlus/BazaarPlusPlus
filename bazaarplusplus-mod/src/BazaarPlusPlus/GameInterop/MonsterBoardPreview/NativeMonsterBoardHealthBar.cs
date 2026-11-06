#nullable enable
namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// The native monster board's health bar is the largest subtree that holds its health text
// and no board part. It is found from the serialized text, never by a path, because the
// tooltip prefab regroups its chrome between game builds.
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
