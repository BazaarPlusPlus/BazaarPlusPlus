#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal readonly record struct ReplayVideoOutputFileNames(
    string FinalFileName,
    string TempFileName
)
{
    internal static ReplayVideoOutputFileNames Create(
        string sanitizedBattleId,
        string stampPart,
        string recordingId
    )
    {
        if (string.IsNullOrWhiteSpace(sanitizedBattleId))
            throw new ArgumentException("Battle ID is required.", nameof(sanitizedBattleId));
        if (string.IsNullOrWhiteSpace(stampPart))
            throw new ArgumentException("Timestamp is required.", nameof(stampPart));
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("Recording ID is required.", nameof(recordingId));

        var stem = $"{sanitizedBattleId}.{stampPart}.{recordingId}";
        return new ReplayVideoOutputFileNames($"{stem}.mp4", $"{stem}.recording.mp4");
    }
}
