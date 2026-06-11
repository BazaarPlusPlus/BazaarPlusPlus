#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class SocketBehaviorBridge
{
    internal static object EnsureSocketBehavior()
    {
        var socketBehavior = TryGetSocketBehavior();
        if (socketBehavior != null)
            return socketBehavior;

        throw new InvalidOperationException("SocketBehavior is unavailable.");
    }

    internal static object? TryGetSocketBehavior()
    {
        try
        {
            var replayHostType = ResolveReplayHostType();
            if (replayHostType == null)
                return null;

            var getInstanceMethod = replayHostType.GetMethod(
                "GetInstance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (getInstanceMethod == null)
                return null;

            return getInstanceMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "SocketBehaviorBridge",
                $"Failed to resolve SocketBehavior via runtime API: {ex.Message}"
            );
            return null;
        }
    }

    internal static void DisposeSocketBehavior()
    {
        try
        {
            var socketBehavior = TryGetSocketBehavior();
            if (socketBehavior == null)
                return;
            var disposeMethod = socketBehavior
                .GetType()
                .GetMethod(
                    "Dispose",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            disposeMethod?.Invoke(socketBehavior, null);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "SocketBehaviorBridge",
                $"Failed to dispose socket during replay rollback: {ex.Message}"
            );
        }
    }

    internal static NetMessageProcessor GetProcessor(object? socketBehavior)
    {
        socketBehavior ??= EnsureSocketBehavior();

        var method = socketBehavior
            .GetType()
            .GetMethod(
                "GetProcessor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var processor = method?.Invoke(socketBehavior, null) as NetMessageProcessor;
        if (processor != null)
            return processor;

        throw new InvalidOperationException("SocketBehavior did not expose a NetMessageProcessor.");
    }

    internal static Action<CombatSequenceMessages> CreateSetLastCombatSequence(object processor)
    {
        var property = processor
            .GetType()
            .GetProperty(
                "LastCombatSequence",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (property == null)
            throw new MissingMemberException(processor.GetType().FullName, "LastCombatSequence");

        return sequence => property.SetValue(processor, sequence);
    }

    internal static Action CreateTriggerCombatSequenceCreated(object processor)
    {
        return () =>
        {
            var field = processor
                .GetType()
                .GetField(
                    "CombatSequenceCreated",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            var action = field?.GetValue(processor) as Action;
            action?.Invoke();
        };
    }

    internal static Func<NetMessageGameSim, Task> CreateHandleSpawnMessageAsync(
        NetMessageProcessor processor,
        GameSimHandler gameSimHandler
    )
    {
        return async spawnMessage =>
        {
            if (!processor.Handle(spawnMessage))
                throw new InvalidOperationException(
                    "NetMessageProcessor rejected the replay spawn message."
                );

            Data.UpdateFromGameSimAsync(spawnMessage);
            MarkGameSimMessageHandled(gameSimHandler, spawnMessage.MessageId);
        };
    }

    private static void MarkGameSimMessageHandled(GameSimHandler gameSimHandler, string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return;

        var handledMessagesField = gameSimHandler
            .GetType()
            .BaseType?.GetField("_handledMessages", BindingFlags.Instance | BindingFlags.NonPublic);
        if (handledMessagesField?.GetValue(gameSimHandler) is not List<string> handledMessages)
            throw new MissingFieldException(
                gameSimHandler.GetType().BaseType?.FullName,
                "_handledMessages"
            );

        if (!handledMessages.Contains(messageId))
            handledMessages.Add(messageId);
    }

    private static Type? ResolveReplayHostType()
    {
        return typeof(NetMessageProcessor).Assembly.GetType("Networking.NetworkManager", false)
            ?? typeof(NetMessageProcessor).Assembly.GetType("Networking.SocketBehavior", false)
            ?? FindType("Networking.NetworkManager")
            ?? FindType("Networking.SocketBehavior")
            ?? FindTypeByName("NetworkManager")
            ?? FindTypeByName("SocketBehavior");
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(fullName, throwOnError: false);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    private static Type? FindTypeByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, type => type != null)!;
            }
            catch
            {
                continue;
            }

            foreach (var candidate in types)
            {
                if (
                    candidate != null
                    && string.Equals(candidate.Name, typeName, StringComparison.Ordinal)
                )
                    return candidate;
            }
        }

        return null;
    }
}
