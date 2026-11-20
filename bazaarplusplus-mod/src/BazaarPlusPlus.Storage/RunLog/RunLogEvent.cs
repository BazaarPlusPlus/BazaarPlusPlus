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

    // Present on run_started and run_resumed. This build ID comes from the game server.
    public string? ServerBuildId { get; set; }

    // Present on run_started and run_resumed. The ETag identifies the loaded GameData.db.
    public string? GameDataEtag { get; set; }

    // Present on stream_message. The game's raw MessagePack is stored in base64.
    public string? Message { get; set; }

    // Present on stream_message when it came from a recorded stream_command response.
    public long? RespondsToSeq { get; set; }

    // Present on stream_command.
    public string? Command { get; set; }

    // Present on stream_command. Arguments are keyed by parameter name.
    public JObject? CommandArgs { get; set; }
}
