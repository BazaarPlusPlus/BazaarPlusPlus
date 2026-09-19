#nullable enable
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Storage.RunScreenshot;

EndOfRunCaptureWorkflowBehaviorTests.Run();
EndOfRunCleanFramePreparationTests.Run();
EndOfRunCaptureSuppressionLifecycleTests.Run();
EndOfRunCaptureSamplingPerformanceTests.Run();
VerifyReadinessAdapter();
VerifyVisualStabilityTracker();
VerifyArtifactMapping();
VerifyPngFileSystemAdapter();

Console.WriteLine("End-of-run capture workflow checks passed.");

static void VerifyReadinessAdapter()
{
    var readyCard = new TheBazaar.UI.EndOfRun.FakeItemController(true);
    var screen = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
        transitionCount: 0,
        new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(readyCard)
    );
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(screen, hasSummaryRevealStarted: false, hasVisuallySettledCards: false)
            .State == EndOfRunCaptureReadinessState.RevealNotStarted,
        "Face-up content must still wait for the native reveal-start signal."
    );

    screen.SetTransitionCount(1);
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(screen, hasSummaryRevealStarted: true, hasVisuallySettledCards: false)
            .State == EndOfRunCaptureReadinessState.TransitionInProgress,
        "Reveal-start must not bypass an active native transition."
    );
    screen.SetTransitionCount(0);
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(screen, hasSummaryRevealStarted: true, hasVisuallySettledCards: false)
            .State == EndOfRunCaptureReadinessState.Ready,
        "Settled face-up content should be ready."
    );

    var timelineCard = new TheBazaar.UI.EndOfRun.FakeItemController(false);
    var timelineSummary = new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(timelineCard);
    var timelineScreen = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
        transitionCount: 0,
        timelineSummary
    );
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(
                timelineScreen,
                hasSummaryRevealStarted: true,
                hasVisuallySettledCards: false
            )
            .State == EndOfRunCaptureReadinessState.RevealInProgress,
        "Face-down summary cards should remain in reveal."
    );
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(
                timelineScreen,
                hasSummaryRevealStarted: true,
                hasVisuallySettledCards: true
            )
            .State == EndOfRunCaptureReadinessState.Ready,
        "Visually settled cards should bypass an invisible FaceUp animation tail."
    );
    timelineCard.Animator.SetFaceUp(true);
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(
                timelineScreen,
                hasSummaryRevealStarted: true,
                hasVisuallySettledCards: false
            )
            .State == EndOfRunCaptureReadinessState.Ready,
        "The same summary should become ready after FaceUp."
    );

    var emptySummary = new TheBazaar.UI.EndOfRun.EndOfRunSummaryController();
    var sequence = new TheBazaar.UI.EndOfRun.FakeSequence(duration: 1f, isComplete: false);
    emptySummary.SetSkillSequence(sequence);
    var emptyScreen = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(0, emptySummary);
    Assert(
        EndOfRunCaptureWorkflow.ReadReadiness(emptyScreen, true, false).State
            == EndOfRunCaptureReadinessState.RevealInProgress,
        "Empty item board should still wait for skill reveal."
    );
    sequence.SetComplete(true);
    Assert(
        EndOfRunCaptureWorkflow.ReadReadiness(emptyScreen, true, false).State
            == EndOfRunCaptureReadinessState.Ready,
        "Settled empty item board should be ready."
    );

    var badScreen = new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
        0,
        new TheBazaar.UI.EndOfRun.EndOfRunSummaryController(
            new TheBazaar.UI.EndOfRun.BadItemController()
        )
    );
    Assert(
        EndOfRunCaptureWorkflow.ReadReadiness(badScreen, true, false).State
            == EndOfRunCaptureReadinessState.DetectionFailed,
        "Known summary reflection failure should remain distinguishable."
    );
    Assert(
        EndOfRunCaptureWorkflow.ReadReadiness(new object(), false, false).State
            == EndOfRunCaptureReadinessState.UnknownTarget,
        "Unknown target should fail open instead of becoming a screenshot target."
    );
    Assert(
        EndOfRunCaptureWorkflow
            .ReadReadiness(
                new TheBazaar.UI.EndOfRun.EndOfRunScreenController(
                    0,
                    new TheBazaar.UI.EndOfRun.OtherEndOfRunController()
                ),
                false,
                false
            )
            .State == EndOfRunCaptureReadinessState.NotSummary,
        "Known non-summary pages should not be captured."
    );
}

static void VerifyVisualStabilityTracker()
{
    var tracker = new EndOfRunVisualStabilityTracker();
    Assert(
        !tracker.Observe(2, cardSetFingerprint: 10, poseFingerprint: 100, nowSeconds: 0f),
        "The first sample should establish a baseline, not claim stability."
    );
    Assert(
        !tracker.Observe(2, 10, 100, 1f),
        "A static initial pose must not settle before actual card motion is observed."
    );
    Assert(
        !tracker.Observe(2, 10, 101, 1.1f),
        "A pose change should latch motion and restart the stability window."
    );
    Assert(
        !tracker.Observe(2, 10, 101, 1.59f),
        "Cards must remain stable for the full 500 ms window."
    );
    Assert(
        !tracker.ObserveCached(),
        "A cadence-skipped frame must reuse the last false verdict without advancing time."
    );
    Assert(
        tracker.Observe(2, 10, 101, 1.6f),
        "Observed motion followed by 500 ms stability should settle."
    );
    Assert(tracker.ObserveCached(), "A cadence-skipped frame should reuse the last true verdict.");
    Assert(!tracker.Observe(3, 11, 101, 2f), "A changed card set should reset the motion history.");
}

static void VerifyArtifactMapping()
{
    var capturedAtLocal = new DateTimeOffset(2026, 7, 11, 20, 30, 0, TimeSpan.FromHours(8));
    var probe = new ScreenshotMetadataProbe();
    var capture = new ScreenshotCaptureResult
    {
        Metadata = ScreenshotCaptureMetadata.Capture(probe),
        ScreenshotId = "shot-42",
        RunId = "run-42",
        HeroName = null,
        BattleId = "battle-42",
        CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
        RelativePath = "2026-07-11/final.png",
        CapturedAtLocal = capturedAtLocal,
        CapturedAtUtc = capturedAtLocal.ToUniversalTime(),
    };
    // Simulate live DTO mutation and then run teardown during background PNG encoding.
    probe.Basics.Victories = 0;
    probe.Basics.Day = 1;
    probe.Basics.Hero = "Pygmalien";
    probe.Ranking.Rank = "Bronze";
    probe.Ranking.Rating = 0;
    probe.Available = false;
    var record = RunScreenshotRecordMapper.CreateRecord(
        capture,
        isPrimary: true,
        buildChannel: "Online"
    );
    Assert(
        record.ScreenshotId == "shot-42"
            && record.RunId == "run-42"
            && record.HeroName == "Vanessa"
            && record.BattleId == "battle-42"
            && record.IsPrimary
            && record.ImageRelativePath == "2026-07-11/final.png"
            && record.Day == 12
            && record.PlayerRank == "Legend"
            && record.PlayerRating == 2750
            && record.PlayerPosition == 37
            && record.VictoriesAtCapture == 10
            && record.BuildChannel == "Online",
        "Screenshot metadata mapping should preserve capture and run identity."
    );

    var unavailable = new ScreenshotCaptureResult
    {
        Metadata = ScreenshotCaptureMetadata.Capture(probe),
    };
    probe.Available = true;
    var missing = RunScreenshotRecordMapper.CreateRecord(unavailable, true, "Online");
    Assert(
        missing.VictoriesAtCapture == null && missing.Day == null && missing.PlayerRank == null,
        "Unavailable capture metadata must remain unknown even if a new run becomes available."
    );
    var zero = new ScreenshotCaptureResult { Metadata = ScreenshotCaptureMetadata.Capture(probe) };
    Assert(
        RunScreenshotRecordMapper.CreateRecord(zero, true, "Online").VictoriesAtCapture == 0,
        "A genuine zero-win capture must remain zero."
    );

    var relativePath = ScreenshotPathBuilder.BuildRelativePath(
        "Run-42/Final",
        new DateTimeOffset(2026, 4, 7, 21, 30, 15, TimeSpan.FromHours(8))
    );
    Assert(
        relativePath
            == Path.Combine("2026-04-07", "2026-04-07_21-30-15-000_final_run-run-42final.png"),
        $"Unexpected screenshot path: {relativePath}"
    );
    Assert(
        ScreenshotPathBuilder.BuildRelativePath(
            runId: null,
            new DateTimeOffset(2026, 4, 7, 9, 5, 4, TimeSpan.FromHours(-7))
        ) == Path.Combine("2026-04-07", "2026-04-07_09-05-04-000_final_run-anonymous.png"),
        "Missing run id should use anonymous screenshot path."
    );
}

static void VerifyPngFileSystemAdapter()
{
    var directory = Path.Combine(Path.GetTempPath(), $"bpp-end-run-png-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var valid = Path.Combine(directory, "valid.png");
        var empty = Path.Combine(directory, "empty.png");
        var wrongExtension = Path.Combine(directory, "image.bin");
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1 };
        File.WriteAllBytes(valid, bytes);
        File.WriteAllBytes(empty, []);
        File.WriteAllBytes(wrongExtension, bytes);

        var files = new SystemEndOfRunCaptureFileSystem();
        Assert(files.IsUsablePng(valid), "PNG adapter should accept a non-empty PNG signature.");
        Assert(!files.IsUsablePng(empty), "PNG adapter should reject an empty artifact.");
        Assert(
            !files.IsUsablePng(wrongExtension),
            "PNG adapter should reject a non-PNG artifact path."
        );
        files.DeleteIfExists(valid);
        Assert(!File.Exists(valid), "Late-artifact cleanup should delete the exact file.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

namespace TheBazaar.UI.EndOfRun
{
    public sealed class EndOfRunScreenController
    {
        private object? _activeController;
        private int _transitionCount;

        public EndOfRunScreenController(int transitionCount, object? activeController)
        {
            _transitionCount = transitionCount;
            _activeController = activeController;
        }

        public void SetTransitionCount(int transitionCount) => _transitionCount = transitionCount;
    }

    public sealed class EndOfRunSummaryController
    {
        private object?[] loadedCards;
        private object? _skillSequence;

        public EndOfRunSummaryController(params object?[] loadedCards)
        {
            this.loadedCards = loadedCards;
            _skillSequence = new FakeSequence(duration: 0f, isComplete: false);
        }

        public void SetSkillSequence(object? skillSequence) => _skillSequence = skillSequence;
    }

    public sealed class OtherEndOfRunController;

    public sealed class FakeItemController
    {
        public FakeAnimator Animator;

        public FakeItemController(bool faceUp) => Animator = new FakeAnimator(faceUp);
    }

    public sealed class BadItemController;

    public class FakeTween
    {
        public float duration;
        public bool isComplete;

        protected FakeTween(float duration, bool isComplete)
        {
            this.duration = duration;
            this.isComplete = isComplete;
        }
    }

    public sealed class FakeSequence : FakeTween
    {
        public FakeSequence(float duration, bool isComplete)
            : base(duration, isComplete) { }

        public void SetComplete(bool value) => isComplete = value;
    }

    public sealed class FakeAnimator
    {
        private bool _faceUp;

        public FakeAnimator(bool faceUp) => _faceUp = faceUp;

        public bool GetBool(string parameterName) => parameterName == "FaceUp" && _faceUp;

        public void SetFaceUp(bool faceUp) => _faceUp = faceUp;
    }
}

file sealed class ScreenshotMetadataProbe : IRunSnapshotProbe
{
    internal bool Available { get; set; } = true;
    internal RunBasicsSnapshot Basics { get; } =
        new()
        {
            Day = 12,
            Victories = 10,
            Hero = "Vanessa",
        };
    internal RankSnapshot Ranking { get; } = new() { Rank = "Legend", Rating = 2750 };

    public bool TryGetRunBasics(out RunBasicsSnapshot basics)
    {
        basics = Basics;
        return Available;
    }

    public bool TryGetRankSnapshot(out RankSnapshot rank)
    {
        rank = Ranking;
        return Available;
    }

    public bool TryGetLeaderboardPosition(out int? position)
    {
        position = 37;
        return Available;
    }

    public bool TryGetPlayerStats(out PlayerStatsSnapshot stats) =>
        throw new NotSupportedException();
}
