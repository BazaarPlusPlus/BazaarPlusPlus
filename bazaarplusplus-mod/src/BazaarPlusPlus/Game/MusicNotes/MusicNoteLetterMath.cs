#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Pure inference of the note letter each board socket maps to. Mirrors the game's placement
/// rule (SocketedContainer.GatherCandidateSockets): once any music note is on the board, socket
/// index and note letter are congruent modulo the letter count, anchored at the first placed
/// note. With no anchor the mapping is undetermined and every socket resolves to null.
/// Letters are 0-based ordinals of EMusicNote (0 = A .. 6 = G); this file stays free of
/// Unity/game types so the letter rule is testable in a plain exe-runner project.
/// </summary>
internal static class MusicNoteLetterMath
{
    internal const int LetterCount = 7;

    /// <summary>
    /// <paramref name="placedLetters"/>[i] is the letter of the note occupying socket i, or null
    /// when socket i holds no music note. Returns one letter per socket index — the placed
    /// letter where present, the anchor-inferred letter elsewhere — or all nulls when the board
    /// has no anchor note.
    /// </summary>
    internal static int?[] InferLetters(IReadOnlyList<int?> placedLetters)
    {
        var result = new int?[placedLetters.Count];

        var anchorIndex = -1;
        var anchorLetter = 0;
        for (var i = 0; i < placedLetters.Count; i++)
        {
            if (placedLetters[i] is int letter)
            {
                anchorIndex = i;
                anchorLetter = letter;
                break;
            }
        }

        if (anchorIndex < 0)
            return result;

        for (var i = 0; i < result.Length; i++)
        {
            var inferred = (anchorLetter + (i - anchorIndex)) % LetterCount;
            if (inferred < 0)
                inferred += LetterCount;
            result[i] = placedLetters[i] ?? inferred;
        }

        return result;
    }
}
