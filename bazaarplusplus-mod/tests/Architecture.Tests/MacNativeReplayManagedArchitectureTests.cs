using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayManagedArchitectureTests
{
    [Fact]
    public void Native_plugin_availability_is_probed_synchronously()
    {
        AssertNativeProbeIsSynchronous(
            Path.Combine(
                TestInputs.RepoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "Video",
                "CombatReplayVideoRecorder.cs"
            )
        );
        AssertNativeProbeIsSynchronous(
            Path.Combine(
                TestInputs.RepoRoot,
                "src",
                "BazaarPlusPlus",
                "Game",
                "HistoryPanel",
                "HistoryPanelReplayService.cs"
            )
        );
    }

    private static void AssertNativeProbeIsSynchronous(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        var nativeProbe = source.IndexOf(
            "MacMetalVideoEncoder.TryGetAvailability",
            StringComparison.Ordinal
        );
        var windowsNativeProbe = source.IndexOf(
            "WindowsMediaFoundationVideoEncoder.TryGetAvailability",
            StringComparison.Ordinal
        );
        Assert.True(nativeProbe >= 0, $"Missing native availability probe in {sourcePath}.");
        Assert.True(
            windowsNativeProbe >= 0,
            $"Missing Windows native availability probe in {sourcePath}."
        );
        if (Path.GetFileName(sourcePath) == "HistoryPanelReplayService.cs")
        {
            var method = source[
                source.IndexOf(
                    "public void PrewarmRecordingAvailability()",
                    StringComparison.Ordinal
                )..source.IndexOf("public bool CanRecordReplay(", StringComparison.Ordinal)
            ];
            Assert.DoesNotContain("Task.Run", method, StringComparison.Ordinal);
        }
        else
            Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
    }
}
