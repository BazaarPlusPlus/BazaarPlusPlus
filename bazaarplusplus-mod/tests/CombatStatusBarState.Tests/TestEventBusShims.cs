using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    internal static void WarnEvent(
        BppLogEvent logEvent,
        System.Exception exception,
        params BppLogField[] fields
    ) { }
}
