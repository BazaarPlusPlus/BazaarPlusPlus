#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

public interface IBppLogSink
{
    void Write(BppLogSeverity severity, string message);
}
