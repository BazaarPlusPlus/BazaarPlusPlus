#nullable enable

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>Pure contextual visibility policy for the Shift-activated socket overlay.</summary>
internal static class MusicNoteOverlayVisibilityPolicy
{
    internal static bool ShouldShow(
        bool activationActive,
        bool hasPlayer,
        bool isInCombat,
        bool isReplay,
        bool isRecapOpen,
        bool isNewDayTransitionActive
    ) =>
        activationActive
        && hasPlayer
        && !isInCombat
        && !isReplay
        && !isRecapOpen
        && !isNewDayTransitionActive;
}
