#nullable enable
#pragma warning disable CS0436
using System.Collections;
using System.Reflection;
using BazaarGameShared.Core;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using HarmonyLib;
using Networking;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Patches.RunLogging;

// The game reuses message IDs, so pair by object identity. Wrap the returned task to
// record the response before the game handles it.
[HarmonyPatch]
internal static class RunMessageStreamCommandPatch
{
    // ReestablishSession's response never passes the dispatch seam.
    private const string ReestablishSession = nameof(HttpGameClient.ReestablishSession);

    private static readonly string[] Methods =
    {
        "SendInitializeRun",
        "SendSelectItem",
        "SendMoveItem",
        "SendSelectSkill",
        "SendSelectEncounter",
        "SendReRollSelection",
        "SendExitCurrentState",
        "SendSellCard",
        "SendCommitToPedestal",
        "SendAbandonRun",
        ReestablishSession,
    };

    private static IEnumerable<MethodBase> TargetMethods() =>
        Methods
            .Select(name => AccessTools.Method(typeof(HttpGameClient), name))
            .Where(method => method != null)
            .Cast<MethodBase>();

    [HarmonyPrefix]
    private static void Prefix(MethodBase __originalMethod, object[] __args, out int? __state)
    {
        __state = null;
        try
        {
            var parameters = __originalMethod.GetParameters();
            var args = new Dictionary<string, object?>();
            for (var index = 0; index < parameters.Length && index < __args.Length; index++)
            {
                var name = parameters[index].Name;
                args[name] = DescribeArgument(__originalMethod.Name, name, __args[index]);
            }
            __state = RunMessageStreamTap.OnCommand(
                __originalMethod.Name,
                JsonConvert.SerializeObject(args)
            );
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.message_stream.command_failed",
                    storm: []
                ),
                ex
            );
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        MethodBase __originalMethod,
        int? __state,
        ref Task<Result<INetMessage>> __result
    )
    {
        try
        {
            if (__state is not { } number || __result == null)
                return;
            __result = Observe(
                __result,
                number,
                reachesSeam: __originalMethod.Name != ReestablishSession
            );
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.message_stream.command_failed",
                    storm: []
                ),
                ex
            );
        }
    }

    private static async Task<Result<INetMessage>> Observe(
        Task<Result<INetMessage>> inner,
        int number,
        bool reachesSeam
    )
    {
        var result = await inner;
        try
        {
            if (result is { IsOk: true, Value: { } response })
                RunMessageStreamTap.OnResponse(number, response, reachesSeam);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.message_stream.response_failed",
                    storm: []
                ),
                ex
            );
        }
        return result;
    }

    // HttpGameClient normalizes null desiredSockets to an empty list when building the wire DTO;
    // match that here so desktop and Android emit the same JSON for fuse purchases.
    internal static object? DescribeArgument(
        string methodName,
        string parameterName,
        object? value
    ) =>
        value == null
        && parameterName == "desiredSockets"
        && methodName is "SendSelectItem" or "SendMoveItem"
            ? Array.Empty<object>()
            : Describe(value);

    private static object? Describe(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            InstanceId id => id.Value,
            Enum member => member.ToString(),
            IEnumerable list => list.Cast<object?>().Select(Describe).ToList(),
            _ when value.GetType().IsPrimitive => value,
            _ => value.ToString(),
        };
}
