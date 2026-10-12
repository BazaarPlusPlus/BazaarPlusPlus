#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

// CombatReplayRuntime.TryLoadSavedBattle's result. Busy means the payload gate is held right now;
// Missing means the saved battle or its payload is gone.
internal enum SavedBattleLoadOutcome
{
    Loaded,
    Missing,
    Busy,
    Unavailable,
}
