using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace LiveBuildPanel.Tests;

internal sealed class ListLogSink(List<string> lines) : IBppLogSink
{
    public void Write(BppLogSeverity severity, string message) => lines.Add(message);
}
