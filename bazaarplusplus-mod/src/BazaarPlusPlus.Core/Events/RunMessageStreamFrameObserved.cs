#nullable enable
namespace BazaarPlusPlus.Core.Events;

// A frame carries exactly one of a game message or a sent command.
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

    // Frames held before this command belong to no run.
    public bool StartsRun { get; set; }
}
