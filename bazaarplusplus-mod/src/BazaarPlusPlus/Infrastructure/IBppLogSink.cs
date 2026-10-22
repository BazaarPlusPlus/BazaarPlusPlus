#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

/// <summary>
/// The host's log exit. <see cref="BppLog"/> renders, redacts, and storm-limits every event
/// before it reaches the sink, so a sink only forwards the finished line to its loader.
/// </summary>
internal interface IBppLogSink
{
    void Write(BppLogSeverity severity, string message);
}
