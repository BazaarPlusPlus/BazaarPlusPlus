#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class PlaybackUiState
{
    internal static readonly HashSet<int> InitializedBoardUiControllers = new();
    internal static EncounterController? ActiveOpponentPortrait { get; set; }
}
