#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

// Switch the visible child without resizing the rail because the corpus card has a fixed height.
public enum LiveBuildCorpusState
{
    Pending,
    Failure,
    Empty,
    Summary,
}
