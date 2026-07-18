#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches;

/// <summary>
/// Resolves, per game-build shape, the single NetMessageProcessor method that every
/// live-run net message flows through.
/// </summary>
internal static class NetMessageDispatchSeam
{
    // PTR moved dispatch into a private Receive(INetMessage, bool) and unwraps
    // NetMessageAggregate children by recursing through it, so patching the public
    // one-arg ReceiveOrQueue there would silently miss aggregated messages. Online has
    // no such method; its public ReceiveOrQueue self-recurses aggregate children.
    // Preferring the private method when present therefore targets, on each build, the
    // one method every message (including aggregate children) passes through.
    internal static MethodBase? ResolveTarget()
    {
        return AccessTools.Method(
                typeof(NetMessageProcessor),
                "Receive",
                new[] { typeof(INetMessage), typeof(bool) }
            )
            ?? AccessTools.Method(
                typeof(NetMessageProcessor),
                "ReceiveOrQueue",
                new[] { typeof(INetMessage) }
            );
    }

    // On the PTR target the second argument is true only for tournament spectate
    // playback, which must not be recorded as the player's own run.
    internal static bool IsSpectatePlayback(object[] args)
    {
        return args.Length > 1 && args[1] is true;
    }
}
