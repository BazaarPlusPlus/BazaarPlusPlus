#nullable enable
#pragma warning disable CS0436
using System.Reflection;
using BazaarGameShared.Infra.Messages;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.Patches;

internal static class NetMessageDispatchSeam
{
    // Builds with private Receive(INetMessage, bool) recurse there for aggregate children.
    // Other builds recurse through ReceiveOrQueue. Patch the recursive seam to catch both.
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
