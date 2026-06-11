#nullable enable
using System;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLogStoreLoggerBridge : IRunLogStoreLogger
{
    public void Warn(string component, string message) => BppLog.Warn(component, message);

    public void Error(string component, string message, Exception exception) =>
        BppLog.Error(component, message, exception);
}
