#nullable enable
namespace BazaarPlusPlus.Core.Events;

// No-payload signal published once per combat frame. A single shared instance is reused so the
// hot per-frame publish path allocates nothing.
internal sealed class CombatFrameAdvanced
{
    public static readonly CombatFrameAdvanced Instance = new();

    private CombatFrameAdvanced() { }
}
