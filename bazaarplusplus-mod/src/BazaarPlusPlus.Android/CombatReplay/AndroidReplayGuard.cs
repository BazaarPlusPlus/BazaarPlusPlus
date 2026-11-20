namespace BazaarPlusPlus.Android.CombatReplay;

// Replay injection goes directly through NetMessageProcessor.Handle. Ignore any live intake during
// playback so it cannot record a phantom battle.
internal static class AndroidReplayGuard
{
    private static long _intakeMessages;
    private static long _captureSkips;

    public static Func<bool>? IsActiveSource { get; set; }

    public static bool IsActive
    {
        get
        {
            try
            {
                return IsActiveSource?.Invoke() == true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static long IntakeMessages => Interlocked.Read(ref _intakeMessages);

    public static long CaptureSkips => Interlocked.Read(ref _captureSkips);

    public static IntakeDecision Decide(bool replayActive) =>
        replayActive
            ? new IntakeDecision(CaptureBattle: false, PublishRunInitialized: true)
            : new IntakeDecision(CaptureBattle: true, PublishRunInitialized: true);

    public static long CountIntakeMessage() => Interlocked.Increment(ref _intakeMessages);

    public static void CountCaptureSkip() => Interlocked.Increment(ref _captureSkips);

    public readonly record struct IntakeDecision(bool CaptureBattle, bool PublishRunInitialized);
}
