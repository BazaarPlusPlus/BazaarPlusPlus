#nullable enable
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Storage.RunLog;

public sealed class RunLogEvent
{
    public string RunId { get; set; } = string.Empty;

    public long Seq { get; set; }

    public DateTimeOffset Ts { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int? Day { get; set; }

    public int? Hour { get; set; }

    public string? Hero { get; set; }

    public string? GameMode { get; set; }

    public int? Victories { get; set; }

    public int? Losses { get; set; }

    public string? State { get; set; }

    public string? EncounterId { get; set; }

    public string? CombatKind { get; set; }

    public string? BattleId { get; set; }

    public string? OpponentName { get; set; }

    /// <summary>On run_started and run_resumed: the game server's build id for the run.</summary>
    public string? ServerBuildId { get; set; }

    /// <summary>On run_started and run_resumed: the ETag of the GameData.db the game had loaded.</summary>
    public string? GameDataEtag { get; set; }

    /// <summary>On stream_message: the received message, the game's MessagePack bytes in base64.</summary>
    public string? Message { get; set; }

    /// <summary>On stream_message: the seq of the stream_command whose response carried it.</summary>
    public long? RespondsToSeq { get; set; }

    /// <summary>On stream_command: the game's command method.</summary>
    public string? Command { get; set; }

    /// <summary>On stream_command: the method's arguments by parameter name.</summary>
    public JObject? CommandArgs { get; set; }
}
