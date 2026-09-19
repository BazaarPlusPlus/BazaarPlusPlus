namespace BazaarPlusPlus.Infrastructure
{
    internal static class BppLog
    {
        internal static void WarnEvent(
            object definition,
            System.Exception exception,
            params object[] values
        ) { }
    }
}

namespace BazaarPlusPlus
{
    internal enum PluginLogReasonCode
    {
        HandlerException,
    }

    internal sealed class PluginLogFieldStub
    {
        internal object Bind(object? value) => value ?? string.Empty;
    }

    internal static class PluginLogEvents
    {
        internal static readonly object EventHandlerDegraded = new();
        internal static readonly PluginLogFieldStub EventHandlerDegradedEventId = new();
        internal static readonly PluginLogFieldStub EventHandlerDegradedHandlerId = new();
        internal static readonly PluginLogFieldStub EventHandlerDegradedReasonCode = new();
    }

    internal static class PluginLogIdentity
    {
        internal static string EventId(System.Type eventType) =>
            eventType.FullName ?? eventType.Name;

        internal static string HandlerId(System.Reflection.MethodInfo method) => method.Name;
    }
}
