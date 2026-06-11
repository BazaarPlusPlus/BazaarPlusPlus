#nullable enable
using System;

namespace BazaarPlusPlus.Storage.RunLog;

public interface IRunLogStoreLogger
{
    void Warn(string component, string message);

    void Error(string component, string message, Exception exception);
}
