#nullable enable
using System.Diagnostics;
using System.Globalization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class SystemBazaarAgentClock : IBazaarAgentClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public double NowSeconds => _stopwatch.Elapsed.TotalSeconds;

    public string UtcNowIsoString() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
}
