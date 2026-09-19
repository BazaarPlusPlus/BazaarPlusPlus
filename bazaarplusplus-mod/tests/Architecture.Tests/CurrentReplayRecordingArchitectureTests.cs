using Xunit;

namespace Architecture.Tests;

public sealed class CurrentReplayRecordingArchitectureTests
{
    [Fact]
    public void Saved_replay_distinguishes_unavailable_persistence_from_a_busy_payload_gate()
    {
        var runtimeSource = File.ReadAllText(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CombatReplayRuntime.cs"
            )
        );

        Assert.Contains("if (persistence == null)", runtimeSource);
        Assert.Contains("ReplayRequestRejectionReasonCode.RuntimeUnavailable", runtimeSource);
        Assert.Contains("ReplayRequestRejectionReasonCode.PayloadOperationBusy", runtimeSource);
        Assert.DoesNotContain("PayloadMaintenanceActive", runtimeSource);
    }

    [Fact]
    public void Ordinary_saved_replay_reuses_the_native_replay_action_for_recording()
    {
        var repoRoot = RepoRoot();
        var runtimeSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CombatReplayRuntime.cs"
            )
        );
        var controllerSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "CurrentReplayRecordingButtonController.cs"
            )
        );
        var gateSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "GameInterop",
                "CombatReplay",
                "NativeReplayRestartGate.cs"
            )
        );
        var probeSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "src",
                "BazaarPlusPlus",
                "GameInterop",
                "CombatReplay",
                "NativeReplayRestartProbe.cs"
            )
        );

        Assert.Contains(
            "ReplayRecordingButtonSnapshotPolicy.OrdinaryManagedReplay(",
            runtimeSource
        );
        Assert.Contains("TryStartManagedReplayRecording(", runtimeSource);
        Assert.Contains("operation.TryPromoteToRecording()", runtimeSource);
        Assert.Contains(
            "publisher.TryPromoteActiveSessionToRecording(operation.BattleId)",
            runtimeSource
        );
        Assert.Contains("invokeNativeReplay();", runtimeSource);
        Assert.Contains("NativeReplayRestartProbe.Observe(", runtimeSource);
        Assert.DoesNotContain("Data.IsStorageOpen", runtimeSource);
        Assert.Contains("ReplayInProgress: replay?.IsReplaying == true", probeSource);
        Assert.Contains("NativeReplayRestartGate.Evaluate(", probeSource);
        Assert.Contains("StorageOpen: Data.IsStorageOpen", probeSource);
        Assert.Contains("ConnectionLost: boardManager?.SocketConnectionLost == true", probeSource);
        Assert.Contains("FailManagedReplayRecordingRestart(", runtimeSource);
        Assert.DoesNotContain("Finish the current replay before recording it.", runtimeSource);
        Assert.Contains("StorageMoving", gateSource);
        Assert.Contains("StorageOpen", gateSource);
        Assert.Contains("InputBlocked", gateSource);
        Assert.Contains("ConnectionLost", gateSource);
        Assert.Contains(
            "_button.interactable = snapshot.CanReveal || (nativeActionsBound && snapshot.CanStart)",
            controllerSource
        );
        Assert.Contains("_startFailureFeedback.Observe(snapshot)", controllerSource);
        Assert.Contains(
            "CurrentReplayRecordingText.StartFailure(startFailureStatusCode.Value)",
            controllerSource
        );
        Assert.Contains(
            "_startFailureFeedback.ReportFailure(startStatusCode, GetDisplaySnapshot())",
            controllerSource
        );
        Assert.Contains("_startFailureFeedback.Clear()", controllerSource);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
