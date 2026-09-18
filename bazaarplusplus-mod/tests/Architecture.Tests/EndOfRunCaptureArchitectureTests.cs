#nullable enable
using Xunit;

namespace Architecture.Tests;

public sealed class EndOfRunCaptureArchitectureTests
{
    [Fact]
    public void Patches_express_only_the_two_workflow_intents()
    {
        var root = MainSourceRoot(RepoRoot());
        var continuePatch = File.ReadAllText(
            Path.Combine(root, "Patches", "EndOfRun", "EndOfRunScreenshotPatch.cs")
        );
        var revealPatch = File.ReadAllText(
            Path.Combine(root, "Patches", "EndOfRun", "EndOfRunSummaryRevealPatch.cs")
        );

        Assert.Contains("EndOfRunCaptureWorkflow.ShouldBlockContinue(__instance)", continuePatch);
        Assert.Contains("EndOfRunCaptureWorkflow.ObserveRevealStarted(__instance)", revealPatch);
        Assert.DoesNotContain("RequestContinue", continuePatch);
        foreach (var source in new[] { continuePatch, revealPatch })
        {
            Assert.Contains("BppPatchHost.TryGetFeatures", source);
            Assert.DoesNotContain("EndOfRunCaptureDriver", source);
            Assert.DoesNotContain("ScreenshotService", source);
            Assert.DoesNotContain("EndOfRunCaptureWorkflowCore", source);
        }
    }

    [Fact]
    public void Driver_is_only_a_Unity_capture_adapter()
    {
        var root = MainSourceRoot(RepoRoot());
        var driver = File.ReadAllText(
            Path.Combine(root, "Game", "Screenshots", "EndOfRunCaptureDriver.cs")
        );
        var sampler = File.ReadAllText(
            Path.Combine(root, "Game", "Screenshots", "EndOfRunSummaryVisualSnapshotSampler.cs")
        );

        Assert.Contains("WaitForEndOfFrame", driver);
        Assert.Contains("BppUiChromeSuppression.Begin", driver);
        Assert.Contains("NativeTooltipSuppression.Begin", driver);
        Assert.Contains("BeginCaptureCurrentFrame", driver);
        Assert.Contains("_visualSampler.TryCapture", driver);
        Assert.Contains("EndOfRunHeavySampleCadence", driver);
        Assert.Contains("EndOfRunVisualStabilityTracker", driver);
        Assert.Contains("EndOfRunCleanFramePreparationCore", driver);
        Assert.Contains("preparation.ObserveCached(now)", driver);
        Assert.DoesNotContain("ScreenshotCaptureReasonCode.CleanFrameDeadline", driver);
        Assert.Contains("ResetVisualStability", driver);
        Assert.Contains("IEndOfRunCaptureSurface<EndOfRunScreenController>", driver);
        Assert.DoesNotContain("ResumeContinue", driver);
        Assert.DoesNotContain("EndOfRunNativeContinueVerifier", driver);
        Assert.DoesNotContain("AttemptCount", driver);
        Assert.DoesNotContain("MetadataDeadline", driver);
        Assert.DoesNotContain("RevealDeadline", driver);
        Assert.DoesNotContain("ScreenshotCaptureLogEvents", driver);
        Assert.Contains("ExcludedTopologySentinel", sampler);
        Assert.Contains("EndOfRunHierarchySentinelCore.Matches", sampler);
    }

    [Fact]
    public void Workflow_owns_one_run_state_and_legacy_state_sources_are_removed()
    {
        var root = MainSourceRoot(RepoRoot());
        var screenshots = Path.Combine(root, "Game", "Screenshots");
        var core = File.ReadAllText(Path.Combine(screenshots, "EndOfRunCaptureWorkflowCore.cs"));
        var contracts = File.ReadAllText(Path.Combine(screenshots, "EndOfRunCaptureWorkflow.cs"));

        Assert.Contains("interface IEndOfRunCaptureWorkflow", contracts);
        Assert.Contains("bool ShouldBlockContinue(EndOfRunScreenController screen);", contracts);
        Assert.Contains("void ObserveRevealStarted(EndOfRunSummaryController summary);", contracts);
        Assert.Contains("private RunState _state", core);
        Assert.Contains("Task FrameAcquired { get; }", core);
        Assert.Contains("_state.HasFrameAcquired", core);
        Assert.Contains("BeginCapture(surface, screen, context)", core);
        Assert.Contains("CaptureTimeoutSeconds", core);
        Assert.Contains("MetadataTimeoutSeconds", core);
        Assert.Contains("RevealDeadlineSeconds", core);
        Assert.False(File.Exists(Path.Combine(screenshots, "EndOfRunScreenshotGate.cs")));
        Assert.False(File.Exists(Path.Combine(screenshots, "ScreenshotCaptureOperation.cs")));
        Assert.False(File.Exists(Path.Combine(screenshots, "EndOfRunCaptureReadinessDetector.cs")));
        Assert.False(File.Exists(Path.Combine(screenshots, "EndOfRunScreenshotController.cs")));
        Assert.False(File.Exists(Path.Combine(screenshots, "EndOfRunNativeContinueVerifier.cs")));
        Assert.DoesNotContain("RequestContinue", contracts);
        Assert.DoesNotContain("AwaitingContinue", core);
    }

    [Fact]
    public void Composition_constructs_workflow_before_publishing_patch_features_and_mounting_driver()
    {
        var composition = File.ReadAllText(
            Path.Combine(MainSourceRoot(RepoRoot()), "BppComposition.cs")
        );
        var workflow = composition.IndexOf(
            "_endOfRunCaptureWorkflow = new EndOfRunCaptureWorkflow",
            StringComparison.Ordinal
        );
        var features = composition.IndexOf(
            "_patchFeatures = new BppPatchFeatures",
            StringComparison.Ordinal
        );
        var driver = composition.IndexOf(
            "new ComponentMount<EndOfRunCaptureDriver>",
            StringComparison.Ordinal
        );

        Assert.True(workflow >= 0 && workflow < features && features < driver);
    }

    [Fact]
    public void Screenshot_persistence_cannot_resample_live_game_state()
    {
        var root = Path.Combine(MainSourceRoot(RepoRoot()), "Game", "Screenshots");
        var persistence = File.ReadAllText(Path.Combine(root, "EndOfRunArtifactPersistence.cs"));
        var mapper = File.ReadAllText(Path.Combine(root, "RunScreenshotRecordMapper.cs"));
        Assert.DoesNotContain("RunSnapshot", persistence);
        Assert.DoesNotContain("Data.Run", persistence);
        Assert.DoesNotContain("private readonly IBppServices", persistence);
        Assert.DoesNotContain("RunBasicsSnapshot", mapper);
        Assert.DoesNotContain("RankSnapshot", mapper);
        var capture = File.ReadAllText(Path.Combine(root, "ScreenshotService.cs"));
        var snapshot = capture.IndexOf(
            "ScreenshotCaptureMetadata.Capture(",
            StringComparison.Ordinal
        );
        var acquire = capture.IndexOf("var captureAndWrite =", StringComparison.Ordinal);
        Assert.True(
            snapshot >= 0 && acquire > snapshot,
            "Metadata must be frozen before frame acquisition can release Continue."
        );
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}
