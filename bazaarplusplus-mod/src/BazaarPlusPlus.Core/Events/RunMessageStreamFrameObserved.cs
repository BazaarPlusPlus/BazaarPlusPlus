#nullable enable
namespace BazaarPlusPlus.Core.Events;

/// <summary>
/// One frame of the Run Message Stream (ADR-0028), published by the host as the game receives a
/// message or sends a command. A frame carries either <see cref="Message"/> or <see cref="Command"/>.
/// </summary>
public sealed class RunMessageStreamFrameObserved
{
    /// <summary>A received message: the game's own MessagePack bytes for one message.</summary>
    public byte[]? Message { get; set; }

    /// <summary>On a message: the <see cref="CommandNumber"/> of the command whose response carried it.</summary>
    public int? RespondsToCommand { get; set; }

    /// <summary>On a command: the host's number for it, unique while the game runs.</summary>
    public int? CommandNumber { get; set; }

    /// <summary>A sent command: the game's command method.</summary>
    public string? Command { get; set; }

    /// <summary>On a command: its arguments as a JSON object, by parameter name.</summary>
    public string? CommandArgsJson { get; set; }

    /// <summary>The command starts a new run: frames held from before it belong to no run.</summary>
    public bool StartsRun { get; set; }
}
