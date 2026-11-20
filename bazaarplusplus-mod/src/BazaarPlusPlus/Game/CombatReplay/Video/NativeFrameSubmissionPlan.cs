#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

// A PTS gap after a renderer hitch would stretch the preceding frame and inflate the video's duration.
internal readonly record struct NativeFrameSubmissionPlan(
    int EncodeFrameCount,
    int TimelineFrameCount,
    int CapturedFrameCount,
    int RepeatedFrameCount,
    int PacerDroppedFrameCount
)
{
    internal int DroppedFrameCountOnSubmissionFailure =>
        checked(EncodeFrameCount + PacerDroppedFrameCount);

    internal static NativeFrameSubmissionPlan Create(
        int emitFrameCount,
        int repeatFrameCount,
        int pacerDroppedFrameCount
    )
    {
        if (emitFrameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(emitFrameCount));
        if (repeatFrameCount < 0 || repeatFrameCount > emitFrameCount)
            throw new ArgumentOutOfRangeException(nameof(repeatFrameCount));
        if (pacerDroppedFrameCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pacerDroppedFrameCount));

        return new NativeFrameSubmissionPlan(
            emitFrameCount,
            emitFrameCount,
            emitFrameCount - repeatFrameCount,
            repeatFrameCount,
            pacerDroppedFrameCount
        );
    }
}
