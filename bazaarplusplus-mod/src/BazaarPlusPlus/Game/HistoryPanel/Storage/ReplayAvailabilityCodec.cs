#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

// The one mapping between ReplayAvailability and the battles table: ghost_replay_state strings
// (constrained by the RunLogSchema CHECK) for Ghost rows, local_payload_state for local rows.
internal static class ReplayAvailabilityCodec
{
    public static ReplayAvailability Parse(string? ghostReplayState) =>
        ghostReplayState switch
        {
            "remote_available" => ReplayAvailability.Remote,
            "local_ready" => ReplayAvailability.Saved,
            "expired" => ReplayAvailability.Expired,
            _ => ReplayAvailability.Unavailable,
        };

    public static string ToStoredState(ReplayAvailability availability) =>
        availability switch
        {
            ReplayAvailability.Remote => "remote_available",
            ReplayAvailability.Saved => "local_ready",
            ReplayAvailability.Expired => "expired",
            ReplayAvailability.Unavailable => "unavailable_payload",
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };

    public static ReplayAvailability FromLocalPayload(string? localPayloadState) =>
        localPayloadState == "ready" ? ReplayAvailability.Saved : ReplayAvailability.Unavailable;
}
