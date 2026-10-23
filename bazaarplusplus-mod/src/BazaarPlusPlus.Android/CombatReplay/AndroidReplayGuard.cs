namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// Whether a saved replay is starting or playing, for the patches that must behave differently
/// while one is: the record chain's message intake and the replay seams. The replay injects its
/// spawn message through <c>NetMessageProcessor.Handle</c>, not the <c>ReceiveOrQueue</c> intake,
/// and replays its combat locally, so no replay message is expected at the intake; the guard
/// counts any that arrive and keeps them out of the PvP battle capture, so a replay can never
/// record a phantom battle. Run lifecycle events and <c>CombatSimObserved</c> are untouched, as
/// on the desktop: dropping them could leave the run context in a run.
/// </summary>
internal static class AndroidReplayGuard
{
    private static long _intakeMessages;
    private static long _captureSkips;

    /// <summary>Set by the replay runtime while it lives; null reads as no replay.</summary>
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

    /// <summary>
    /// The intake's decision for one message: during a replay the message is counted and kept out
    /// of the battle capture; run initialization still publishes.
    /// </summary>
    public static IntakeDecision Decide(bool replayActive) =>
        replayActive
            ? new IntakeDecision(CaptureBattle: false, PublishRunInitialized: true)
            : new IntakeDecision(CaptureBattle: true, PublishRunInitialized: true);

    /// <summary>Counts one intake message seen during a replay; returns the running count.</summary>
    public static long CountIntakeMessage() => Interlocked.Increment(ref _intakeMessages);

    public static void CountCaptureSkip() => Interlocked.Increment(ref _captureSkips);

    public readonly record struct IntakeDecision(bool CaptureBattle, bool PublishRunInitialized);
}
