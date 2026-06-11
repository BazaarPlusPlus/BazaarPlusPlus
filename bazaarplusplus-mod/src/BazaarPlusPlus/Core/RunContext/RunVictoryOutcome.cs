#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

// Mod-owned mirror of the game's EVictoryCondition. Keeping a Core-owned enum means IRunContext
// (re-exported to every feature via IBppServices) carries no game-DLL type.
// Game/CombatStatusBar/CombatStatusBarModule derives this from the combat-sim winner at the boundary; mirrors the existing RunExitKind precedent.
internal enum RunVictoryOutcome
{
    Win,
    Lose,
    Draw,
}
