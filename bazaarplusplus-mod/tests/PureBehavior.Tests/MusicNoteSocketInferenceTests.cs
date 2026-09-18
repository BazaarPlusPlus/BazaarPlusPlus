using BazaarPlusPlus.Game.MusicNotes;
using Xunit;

namespace PureBehavior.Tests;

public sealed class MusicNoteSocketInferenceTests
{
    [Fact]
    public void Anchor_in_middle_maps_every_slot_in_both_directions()
    {
        int?[] placed = [null, null, null, 0, null, null, null, null, null, null];
        Assert.Equal<int?>(
            [4, 5, 6, 0, 1, 2, 3, 4, 5, 6],
            MusicNoteSocketInference.Resolve(placed)
        );
    }

    [Fact]
    public void Existing_notes_are_preserved_while_other_slots_are_inferred()
    {
        int?[] placed = [2, null, null, null, null, null, null, 2, null, null];
        Assert.Equal<int?>(
            [2, 3, 4, 5, 6, 0, 1, 2, 3, 4],
            MusicNoteSocketInference.Resolve(placed)
        );
    }

    [Fact]
    public void No_anchor_has_no_unique_mapping()
    {
        Assert.All(MusicNoteSocketInference.Resolve(new int?[10]), value => Assert.Null(value));
    }
}
