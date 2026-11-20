#nullable enable
namespace BazaarPlusPlus.Core.Events;

// Reuse one instance to avoid allocating on the per-frame publish path.
public sealed class CombatFrameAdvanced
{
    public static readonly CombatFrameAdvanced Instance = new();

    private CombatFrameAdvanced() { }
}
