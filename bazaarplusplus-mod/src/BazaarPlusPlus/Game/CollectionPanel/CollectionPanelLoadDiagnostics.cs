#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelLoadDiagnostics
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly List<string> _segments = new();

    public long Now() => Stopwatch.GetTimestamp();

    public void AddSegment(string name, long startedAt)
    {
        var elapsed = ElapsedMs(startedAt, Stopwatch.GetTimestamp());
        _segments.Add($"{name}={FormatMs(elapsed)}");
    }

    public void AddValue(string name, int value) => _segments.Add($"{name}={value}");

    public void AddValue(string name, string value) => _segments.Add($"{name}={value}");

    public void Log(string outcome)
    {
        var total = ElapsedMs(_startedAt, Stopwatch.GetTimestamp());
        var detail = _segments.Count == 0 ? string.Empty : ", " + string.Join(", ", _segments);
        BppLog.Info("CollectionPanelLoad", $"outcome={outcome}, total={FormatMs(total)}{detail}");
    }

    private static double ElapsedMs(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;

    private static string FormatMs(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + "ms";
}
