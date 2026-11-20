#nullable enable
namespace BazaarPlusPlus.Core.GameState;

// Take the snapshot on the game thread before the simulation starts.
public sealed class CombatSimSnapshot
{
    public CombatSimSnapshot(string messageId, int frameCount)
    {
        MessageId = messageId ?? string.Empty;
        FrameCount = frameCount;
    }

    // A re-sent network message keeps its original ID.
    public string MessageId { get; }

    public int FrameCount { get; }
}
