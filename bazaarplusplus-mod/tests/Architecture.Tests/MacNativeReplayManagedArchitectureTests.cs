#nullable enable
using Xunit;
using static Architecture.Tests.ArchitectureRules;

namespace Architecture.Tests;

public sealed class MacNativeReplayManagedArchitectureTests
{
    [Fact]
    public void Native_plugin_availability_is_probed_synchronously()
    {
        var taskRun = (typeof(Task).FullName!, nameof(Task.Run));
        Holds(
            "Native encoder availability is probed on the calling thread, never via Task.Run.",
            build =>
                Accesses(
                        build.Type(
                            "BazaarPlusPlus.Game.CombatReplay.Video.CombatReplayVideoRecorder"
                        ),
                        taskRun
                    )
                    .Concat(
                        Accesses(
                            build
                                .Type("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelReplayService")
                                .MethodClosure("PrewarmRecordingAvailability"),
                            taskRun
                        )
                    )
        );
    }
}
