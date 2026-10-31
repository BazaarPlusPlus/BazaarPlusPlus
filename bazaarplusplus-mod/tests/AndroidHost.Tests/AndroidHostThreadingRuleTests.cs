#nullable enable
using System.Reflection;
using Architecture.Tests;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The compiled host's threading rules (ADR-0009: compiled artifacts, never source text). A
/// game <c>Il2CppSystem</c> task awaited directly resumes outside the main-thread pump, a managed
/// <c>Task.Run</c>, <c>WhenAll</c> or <c>WhenAny</c> in the replay or History code moves game calls
/// onto the pool, and a History control wired without <c>AndroidUiActions</c> starts the
/// coordinator's work outside the pump's scope; each is a native fault on a device, not an
/// exception, so the IL is held to them here.
/// </summary>
public sealed class AndroidHostThreadingRuleTests
{
    private static readonly Lazy<CompiledAssembly> Host = new(() =>
        CompiledAssembly.Load(
            typeof(AndroidHostThreadingRuleTests)
                .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "BppAndroidHostAssembly")
                .Value!
        )
    );

    [Fact]
    public void No_game_task_is_awaited_outside_the_il2cpp_task_bridge()
    {
        var violations = Host
            .Value.Types.SelectMany(type => type.Methods)
            .SelectMany(method =>
                method
                    .Instructions.Where(instruction =>
                        instruction.IsMemberAccess
                        && instruction.TargetType is { } target
                        && (
                            target.Namespace == "Il2CppSystem.Runtime.CompilerServices"
                            || (
                                target.Namespace == "Il2CppSystem.Threading.Tasks"
                                && instruction.TargetMember
                                    is "GetAwaiter"
                                        or "ConfigureAwait"
                                        or "ContinueWith"
                                        or "Wait"
                            )
                        )
                    )
                    .Select(instruction => $"{method}: {instruction}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Await game tasks through Il2CppTasks.AwaitAsync:\n  " + string.Join("\n  ", violations)
        );
    }

    [Fact]
    public void Replay_and_history_code_never_moves_work_onto_the_pool_or_joins_tasks()
    {
        string[] namespaces =
        [
            "BazaarPlusPlus.Android.CombatReplay",
            "BazaarPlusPlus.Android.Ui",
            "BazaarPlusPlus.Android.Patches",
            // The Mobile UI tree: Ten-Win Builds, Collection, History and the HUD (ADR-0025).
            "BazaarPlusPlus.Mobile",
        ];
        static bool In(CompiledType type, string ns) =>
            type.Namespace == ns || type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal);
        var scope = Host.Value.Types.Where(type => namespaces.Any(ns => In(type, ns))).ToArray();
        // A moved or renamed namespace would leave its rule inspecting nothing; the Mobile UI tree
        // holds the port, the theme, the kit, the leaves #701 steps 2 and 3 moved, Ten-Win Builds,
        // Collection, History and the HUD.
        Assert.All(namespaces, ns => Assert.Contains(scope, type => In(type, ns)));
        Assert.True(
            scope.Count(type => In(type, "BazaarPlusPlus.Mobile")) >= 54,
            "The Mobile UI tree left the Android host's threading rule."
        );
        var violations = scope
            .SelectMany(type => type.Methods)
            .SelectMany(method =>
                method
                    .Instructions.Where(instruction =>
                        instruction.IsMemberAccess
                        && instruction.TargetType is { } target
                        && target.FullName is "System.Threading.Tasks.Task"
                        && instruction.TargetMember is "Run" or "WhenAll" or "WhenAny"
                    )
                    .Select(instruction => $"{method}: {instruction}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Wait on game state through AndroidMainThread, never the pool:\n  "
                + string.Join("\n  ", violations)
        );
    }

    // Saved Replay Playback runs the Android replay start from Core (ADR-0020): its awaits must
    // resume on the context the host installed, so it never leaves it or joins tasks.
    [Fact]
    public void Saved_replay_playback_in_core_awaits_only_on_the_callers_context()
    {
        string[] scope =
        [
            "BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback",
            "BazaarPlusPlus.Game.CombatReplay.SavedReplayLifecycle",
        ];
        var core = CompiledAssembly.Load(
            typeof(BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback).Assembly.Location
        );
        var types = core.Types.Where(type => scope.Contains(type.FullName)).ToArray();
        Assert.Equal(scope.Length, types.Length);
        var instructions = types
            .SelectMany(type => type.Methods)
            .SelectMany(method => method.Instructions.Select(instruction => (method, instruction)))
            .Where(pair =>
                pair.instruction.IsMemberAccess
                && pair.instruction.TargetType is { Namespace: "System.Threading.Tasks" }
            )
            .ToArray();

        Assert.Contains(instructions, pair => pair.instruction.TargetMember == "GetAwaiter");
        var violations = instructions
            .Where(pair =>
                pair.instruction.TargetMember is "Run" or "WhenAll" or "WhenAny" or "ConfigureAwait"
            )
            .Select(pair => $"{pair.method}: {pair.instruction}")
            .ToArray();
        Assert.True(
            violations.Length == 0,
            "Saved Replay Playback awaits on the host's context, never the pool:\n  "
                + string.Join("\n  ", violations)
        );
    }

    [Fact]
    public void Saved_replay_read_worker_calls_only_the_managed_catalog_and_lease_ownership()
    {
        const string worker = "BazaarPlusPlus.Game.CombatReplay.PendingSavedReplayRead";
        var core = CompiledAssembly.Load(
            typeof(BazaarPlusPlus.Game.CombatReplay.SavedReplayPlayback).Assembly.Location
        );
        var instructions = core
            .Types.Where(type => type.FullName.StartsWith(worker, StringComparison.Ordinal))
            .SelectMany(type => type.Methods)
            .SelectMany(method => method.Instructions)
            .Where(instruction => instruction.IsMemberAccess)
            .ToArray();
        Assert.Contains(
            instructions,
            instruction =>
                instruction.TargetType?.FullName == "System.Threading.Tasks.Task"
                && instruction.TargetMember == "Run"
        );
        var violations = instructions
            .Where(instruction =>
                instruction.TargetType is { } target
                && target.Namespace.StartsWith("BazaarPlusPlus", StringComparison.Ordinal)
                && !target.FullName.StartsWith(worker, StringComparison.Ordinal)
                && target.FullName != "BazaarPlusPlus.Game.CombatReplay.SavedReplayCatalog"
            )
            .ToArray();
        Assert.Empty(violations);
    }

    [Fact]
    public void History_controls_are_wired_only_through_the_main_thread_action_scope()
    {
        string[] historyTypes =
        [
            "BazaarPlusPlus.Mobile.History.TouchHistoryEntry",
            "BazaarPlusPlus.Mobile.History.TouchHistoryPanel",
            "BazaarPlusPlus.Mobile.History.TouchHistoryPanelView",
            "BazaarPlusPlus.Mobile.History.TouchHistoryUi",
            "BazaarPlusPlus.Mobile.History.HistoryButton",
            "BazaarPlusPlus.Mobile.History.TouchHistoryBoard",
            "BazaarPlusPlus.Mobile.History.TouchHistoryInspection",
            "BazaarPlusPlus.Mobile.History.TouchHistorySkillsSheet",
            "BazaarPlusPlus.Mobile.History.TouchHistoryMoreDialog",
            "BazaarPlusPlus.Mobile.History.TouchHistoryNoteDialog",
        ];
        var history = Host
            .Value.Types.Where(type => historyTypes.Contains(type.FullName))
            .ToArray();
        Assert.Equal(historyTypes.Length, history.Length);

        var violations = history
            .SelectMany(type => type.Methods)
            .SelectMany(method =>
                method
                    .Instructions.Where(instruction =>
                        instruction.IsMemberAccess
                        && instruction.TargetMember is "AddListener" or "get_onClick"
                    )
                    .Select(instruction => $"{method}: {instruction}")
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Wire History controls through UiActions.OnTap or OnChanged:\n  "
                + string.Join("\n  ", violations)
        );
        var calls = history
            .SelectMany(type => type.Methods)
            .SelectMany(method => method.Instructions)
            .ToArray();
        // Taps, and the note's count following its edits instead of a per-frame poll.
        foreach (var wiring in new[] { "OnTap", "OnChanged" })
            Assert.Contains(
                calls,
                instruction =>
                    instruction.TargetType?.FullName == "BazaarPlusPlus.Mobile.Ui.UiActions"
                    && instruction.TargetMember == wiring
            );
    }
}
