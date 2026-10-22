#nullable enable
using System.Globalization;
using System.Text;

namespace BazaarPlusPlus.Updater;

/// <summary>
/// The Updater's structured log lines, shaped like BppLog's (<c>[BPP][Updater] event=&lt;id&gt;
/// field=value</c>) without referencing it: the host passes its own loader logger.
/// </summary>
internal sealed class UpdaterLog
{
    private readonly Action<string> _info;
    private readonly Action<string> _warn;
    private readonly Action<string> _error;

    public UpdaterLog(Action<string> info, Action<string> warn, Action<string> error)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public void Info(string eventId, params (string Name, object? Value)[] fields) =>
        _info(Format(eventId, fields));

    public void Warn(string eventId, params (string Name, object? Value)[] fields) =>
        _warn(Format(eventId, fields));

    public void Error(
        string eventId,
        Exception exception,
        params (string Name, object? Value)[] fields
    ) =>
        _error(
            Format(
                eventId,
                fields
                    .Concat(
                        new (string, object?)[]
                        {
                            ("exception", exception.GetType().Name),
                            ("message", exception.Message),
                        }
                    )
                    .ToArray()
            )
        );

    public static string Format(string eventId, (string Name, object? Value)[] fields)
    {
        var builder = new StringBuilder("[BPP][Updater] event=").Append(eventId);
        foreach (var (name, value) in fields)
        {
            builder.Append(' ').Append(name).Append('=');
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            if (text.Length == 0 || text.IndexOfAny(new[] { ' ', '"', '=', '\t', '\n' }) >= 0)
                builder.Append('"').Append(text.Replace("\"", "\\\"")).Append('"');
            else
                builder.Append(text);
        }
        return builder.ToString();
    }
}
