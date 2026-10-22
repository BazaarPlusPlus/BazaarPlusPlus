#nullable enable
namespace BazaarPlusPlus.Core.GameState;

/// <summary>
/// What a host's native monster-board preview reports to the panel that owns it.
/// </summary>
public enum NativeMonsterBoardStatus
{
    Loading,
    Ready,
    Partial,
    Empty,
    Failed,
}
