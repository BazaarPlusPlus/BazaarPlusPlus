#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Pure placement math for the hover comparison: can an item of a given width be placed so it
/// covers a target socket? Mirrors the game's contiguous-span rule (SocketedContainer
/// .CanPlaceOnSocket): an anchor is valid when every socket in [anchor, anchor + size) is
/// unblocked, and the item covers the target when the anchor lies within
/// [target - size + 1, target]. The caller decides what "blocked" means (occupied by another
/// item, or locked); the hovered item's own span should be reported unblocked so moving an
/// item within the board reads as placeable. This file stays free of Unity/game types so the
/// span rule is testable in a plain test project.
/// </summary>
internal static class MusicNoteBoardPlacement
{
    internal static bool CanCoverSocket(
        IReadOnlyList<bool> blockedSockets,
        int socketIndex,
        int itemSize
    )
    {
        if (blockedSockets == null)
            throw new ArgumentNullException(nameof(blockedSockets));
        if (itemSize <= 0)
            return false;
        var socketCount = blockedSockets.Count;
        if (socketIndex < 0 || socketIndex >= socketCount || itemSize > socketCount)
            return false;

        var firstAnchor = Math.Max(0, socketIndex - itemSize + 1);
        var lastAnchor = Math.Min(socketIndex, socketCount - itemSize);
        for (var anchor = firstAnchor; anchor <= lastAnchor; anchor++)
        {
            var fits = true;
            for (var offset = 0; offset < itemSize; offset++)
            {
                if (blockedSockets[anchor + offset])
                {
                    fits = false;
                    break;
                }
            }
            if (fits)
                return true;
        }
        return false;
    }
}
