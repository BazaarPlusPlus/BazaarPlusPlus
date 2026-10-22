using System.Globalization;
using System.Text;
using MelonLoader;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The Android host's log exit: renders <c>[BPP][Android] event=&lt;id&gt; name=value ...</c>,
/// the desktop line shape, and writes it through MelonLogger into
/// <c>MelonLoader/Latest.log</c> and logcat. It stands in for the BppLog pipeline until
/// BazaarPlusPlus.Core exists; then this becomes the host's IBppLogSink.
/// </summary>
internal static class AndroidLog
{
    private const string Prefix = "[BPP][Android] event=";
    private static MelonLogger.Instance? _logger;

    public static void Install(MelonLogger.Instance logger)
    {
        Volatile.Write(ref _logger, logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public static void Info(string eventId, params (string Name, object? Value)[] fields) =>
        Volatile.Read(ref _logger)?.Msg(Render(eventId, fields));

    public static void Warn(string eventId, params (string Name, object? Value)[] fields) =>
        Volatile.Read(ref _logger)?.Warning(Render(eventId, fields));

    public static void Error(
        string eventId,
        Exception exception,
        params (string Name, object? Value)[] fields
    ) =>
        Volatile
            .Read(ref _logger)
            ?.Error(
                Render(
                    eventId,
                    [
                        .. fields,
                        ("exception_type", exception.GetType().FullName),
                        ("exception", exception.ToString()),
                    ]
                )
            );

    private static string Render(string eventId, (string Name, object? Value)[] fields)
    {
        var line = new StringBuilder(Prefix).Append(eventId);
        foreach (var (name, value) in fields)
            line.Append(' ').Append(name).Append('=').Append(Format(value));
        return line.ToString();
    }

    private static string Format(object? value)
    {
        var text = value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        if (text.Length > 0 && !text.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return text;
        return "\""
            + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
            + "\"";
    }
}
