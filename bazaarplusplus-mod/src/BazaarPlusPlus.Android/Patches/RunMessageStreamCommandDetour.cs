using System.Runtime.InteropServices;
using BazaarPlusPlus.Android.GameInterop;
using Il2CppBazaarGameShared.Infra.Commands;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using Il2CppNetworking;

namespace BazaarPlusPlus.Android.Patches;

// Every HttpGameClient command method ends in SendCommand<T> with the command object it built, and
// IL2CPP shares one native body across the command classes, so one detour sees every command and
// the task the game awaits. It passes every argument through unchanged.
internal static unsafe class RunMessageStreamCommandDetour
{
    private const string TargetMethod = "SendCommand";
    private const int TargetParameterCount = 5;

    // The game reads each flag as one byte.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SendCommandNative(
        IntPtr client,
        IntPtr endpoint,
        IntPtr command,
        byte requiresSessionId,
        byte enableSessionRecovery,
        byte recoverableTransportFailure,
        IntPtr method
    );

    // Static, so the delegates the native code calls stay alive for the process.
    private static readonly List<Entry> Entries = new();

    // The native bodies detoured: one when the game shares SendCommand<T> across command classes.
    public static int Install()
    {
        if (Entries.Count > 0)
            return Entries.Count;
        var client = Il2CppClassPointerStore<HttpGameClient>.NativeClassPtr;
        var definition = IL2CPP.il2cpp_class_get_method_from_name(
            client,
            TargetMethod,
            TargetParameterCount
        );
        if (definition == IntPtr.Zero)
            throw new MissingMethodException(nameof(HttpGameClient), TargetMethod);
        var generic = new Il2CppSystem.Reflection.MethodInfo(
            IL2CPP.il2cpp_method_get_object(definition, client)
        );
        var bodies = new HashSet<IntPtr>();
        foreach (var command in Commands())
        {
            var closed = generic.MakeGenericMethod(
                new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { command })
            );
            var method = IL2CPP.il2cpp_method_get_from_reflection(closed.Pointer);
            var body = UnityVersionHandler.Wrap((Il2CppMethodInfo*)method).MethodPointer;
            if (body == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"HttpGameClient.SendCommand<{command.Name}> has no native body."
                );
            bodies.Add(body);
        }
        foreach (var body in bodies)
            Entries.Add(new Entry(body));
        return Entries.Count;
    }

    private static Il2CppSystem.Type[] Commands() =>
        [
            Il2CppType.Of<InitializeRunCommand>(),
            Il2CppType.Of<SelectItemCommand>(),
            Il2CppType.Of<MoveItemCommand>(),
            Il2CppType.Of<SelectSkillCommand>(),
            Il2CppType.Of<SelectEncounterCommand>(),
            Il2CppType.Of<RerollCommand>(),
            Il2CppType.Of<ExitCurrentStateCommand>(),
            Il2CppType.Of<SellCardCommand>(),
            Il2CppType.Of<CommitToPedestalCommand>(),
            Il2CppType.Of<AbandonRunCommand>(),
        ];

    private sealed class Entry
    {
        private readonly SendCommandNative _replacement;
        private readonly SendCommandNative _original;

        public Entry(IntPtr body)
        {
            _replacement = OnSendCommand;
            var detour = Il2CppInteropRuntime.Instance.DetourProvider.Create(body, _replacement);
            detour.Apply();
            _original =
                detour.GenerateTrampoline<SendCommandNative>()
                ?? throw new InvalidOperationException(
                    "The HttpGameClient.SendCommand detour has no trampoline."
                );
        }

        // No managed exception may escape this native callback.
        private IntPtr OnSendCommand(
            IntPtr client,
            IntPtr endpoint,
            IntPtr command,
            byte requiresSessionId,
            byte enableSessionRecovery,
            byte recoverableTransportFailure,
            IntPtr method
        )
        {
            AndroidRunMessageStream.SentCommand? sent = null;
            var bus = AndroidPatchHost.Composition?.EventBus;
            try
            {
                if (bus != null)
                    sent = AndroidRunMessageStream.OnCommand(
                        bus,
                        command,
                        sessionRecovery: enableSessionRecovery != 0
                    );
            }
            catch (Exception exception)
            {
                AndroidPatchHost.ReportProbeError("message_stream_command", exception);
            }
            var task = _original(
                client,
                endpoint,
                command,
                requiresSessionId,
                enableSessionRecovery,
                recoverableTransportFailure,
                method
            );
            try
            {
                if (sent is { } command_)
                    AndroidRunMessageStream.OnCommandSent(command_, task);
            }
            catch (Exception exception)
            {
                AndroidPatchHost.ReportProbeError("message_stream_response", exception);
            }
            return task;
        }
    }
}
