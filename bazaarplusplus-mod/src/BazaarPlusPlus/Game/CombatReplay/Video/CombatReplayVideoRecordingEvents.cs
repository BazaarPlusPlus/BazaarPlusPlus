#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum CurrentReplayRecorderAvailabilityPhase
{
    Unavailable,
    Preparing,
    Ready,
}

internal readonly record struct CurrentReplayRecorderAvailability(
    CurrentReplayRecorderAvailabilityPhase Phase,
    string? Reason
)
{
    internal bool IsReady => Phase == CurrentReplayRecorderAvailabilityPhase.Ready;
}

internal readonly record struct CurrentReplayRecordingArmResult(
    bool Succeeded,
    string? RecordingId,
    string? Reason
)
{
    internal static CurrentReplayRecordingArmResult Success(string recordingId) =>
        new(true, recordingId, null);

    internal static CurrentReplayRecordingArmResult Failure(string reason) =>
        new(false, null, reason);
}

internal sealed class CombatReplayVideoRecordingStarted
{
    internal string RecordingId { get; init; } = string.Empty;
    internal string BattleId { get; init; } = string.Empty;
    internal CombatReplayPlaybackSource Source { get; init; }
}

internal sealed class CombatReplayVideoRecordingCompleted
{
    internal string RecordingId { get; init; } = string.Empty;
    internal string BattleId { get; init; } = string.Empty;
    internal CombatReplayPlaybackSource Source { get; init; }
    internal string FinalFilePath { get; init; } = string.Empty;
    internal bool ArtifactUsable { get; init; }
    internal ReplayVideoAudioStatus AudioStatus { get; init; }
    internal ReplayVideoMetadataStatus MetadataStatus { get; init; }
    internal ReplayVideoRecordingReasonCode ReasonCode { get; init; }
    internal string? Reason { get; init; }
}
