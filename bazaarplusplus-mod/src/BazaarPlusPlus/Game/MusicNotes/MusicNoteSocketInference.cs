#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

internal static class MusicNoteSocketInference
{
    // Same first-anchor/modulo-seven rule as SocketedContainer.GatherCandidateSockets,
    // without that method's availability filtering: a slot's letter survives occupancy.
    internal static int?[] Resolve(IReadOnlyList<int?> placed)
    {
        var result = new int?[placed.Count];
        var anchor = -1;
        for (var i = 0; i < placed.Count; i++)
        {
            if (placed[i].HasValue)
            {
                anchor = i;
                break;
            }
        }
        if (anchor < 0)
            return result;
        for (var i = 0; i < result.Length; i++)
            result[i] = placed[i] ?? (((placed[anchor]!.Value + i - anchor) % 7 + 7) % 7);
        return result;
    }
}
