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
        ];
        var violations = Host
            .Value.Types.Where(type =>
                namespaces.Any(ns =>
                    type.Namespace == ns
                    || type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal)
                )
            )
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

    [Fact]
    public void History_controls_are_wired_only_through_the_main_thread_action_scope()
    {
        string[] historyTypes =
        [
            "BazaarPlusPlus.Android.Ui.AndroidHistoryPanel",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryPanelView",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryUi",
            "BazaarPlusPlus.Android.Ui.HistoryButton",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryBoard",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryInspection",
            "BazaarPlusPlus.Android.Ui.AndroidHistorySkillsSheet",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryMoreDialog",
            "BazaarPlusPlus.Android.Ui.AndroidHistoryNoteDialog",
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
            "Wire History controls through AndroidUiActions.OnTap:\n  "
                + string.Join("\n  ", violations)
        );
        Assert.Contains(
            history.SelectMany(type => type.Methods).SelectMany(method => method.Instructions),
            instruction =>
                instruction.TargetType?.FullName == "BazaarPlusPlus.Android.Ui.AndroidUiActions"
                && instruction.TargetMember == "OnTap"
        );
    }
}
