#nullable enable
namespace BazaarPlusPlus.Core.GameState;

/// <summary>
/// What the host copies from a combat-simulation message on the game thread before the
/// simulation starts. It carries only the fields a Core consumer reads.
/// </summary>
public sealed class CombatSimSnapshot
{
    public CombatSimSnapshot(string messageId, int frameCount)
    {
        MessageId = messageId ?? string.Empty;
        FrameCount = frameCount;
    }

    /// <summary>The network message id; a re-sent message keeps its id.</summary>
    public string MessageId { get; }

    /// <summary>Simulation frames the client will play back.</summary>
    public int FrameCount { get; }
}
