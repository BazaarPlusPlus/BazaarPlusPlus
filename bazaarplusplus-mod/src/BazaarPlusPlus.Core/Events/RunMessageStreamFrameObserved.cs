#nullable enable
namespace BazaarPlusPlus.Core.Events;

// A frame carries exactly one of a game message, a sent command, or a lightweight PvE combat summary.
public sealed class RunMessageStreamFrameObserved
{
    // Keep the game's original MessagePack bytes for the Run Message Stream.
    public byte[]? Message { get; set; }

    // A response message links to the originating CommandNumber.
    public int? RespondsToCommand { get; set; }

    // Command numbers are unique for the host process lifetime.
    public int? CommandNumber { get; set; }

    public string? Command { get; set; }

    // The stream contract keys arguments by the game's parameter names.
    public string? CommandArgsJson { get; set; }

    // Present when a PvE NetMessageCombatSim is summarized instead of stored in full (ADR-0028).
    public PveCombatSummary? PveCombat { get; set; }

    // Frames held before this command belong to no run.
    public bool StartsRun { get; set; }
}

public sealed class PveCombatSummary
{
    public string? EncounterId { get; set; }

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? Winner { get; set; }

    public string? Loser { get; set; }

    public int FrameCount { get; set; }

    public List<float>? OpponentHealthThresholdsForGold { get; set; }

    public List<float>? OpponentHealthThresholdsForXp { get; set; }

    public int GoldThresholdsHit { get; set; }

    public int XpThresholdsHit { get; set; }

    public Dictionary<string, Dictionary<string, int>>? CardStats { get; set; }
}
