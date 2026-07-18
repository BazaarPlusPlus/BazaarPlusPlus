#nullable enable
using System.Globalization;
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogExceptionProjector
{
    private const int MessageBudget = 512;
    private const int InnerMessageBudget = 256;
    private const int TypeBudget = 128;
    private const int StackBudget = 5000;

    private readonly BppLogValueFormatter _formatter;

    internal BppLogExceptionProjector(BppLogValueFormatter formatter)
    {
        _formatter = formatter;
    }

    internal ExceptionProjection Project(Exception exception)
    {
        var tokens = new List<string>();
        var truncated = false;
        AppendException(tokens, exception, "exception", MessageBudget, ref truncated);

        var current = SafeInnerException(exception);
        for (var depth = 1; depth <= 3 && current != null; depth++)
        {
            AppendException(
                tokens,
                current,
                "exception_inner_" + depth.ToString(CultureInfo.InvariantCulture),
                InnerMessageBudget,
                ref truncated
            );
            current = SafeInnerException(current);
        }
        if (current != null)
            truncated = true;

        var stack = SafeStackTrace(exception, out var stackUnavailable);
        if (stackUnavailable)
        {
            tokens.Add("exception_stack=<unavailable>");
        }
        else if (stack == null)
        {
            tokens.Add("exception_stack=null");
        }
        else
        {
            var rendered = _formatter.RenderExceptionText(stack, StackBudget, preserveTail: true);
            tokens.Add("exception_stack=" + rendered.Text);
            truncated |= rendered.Truncated;
        }

        return new ExceptionProjection(tokens, truncated);
    }

    internal bool TryFingerprint(Exception exception, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            var remainingBudget = BppLogValueFormatter.FingerprintInputCharacterBudget;
            var canonical = new StringBuilder("exception-v2;");
            Exception? current = exception;
            var depth = 0;
            for (; depth < 4 && current != null; depth++)
            {
                if (HasUnsupportedAggregateFanOut(current))
                    return false;
                if (
                    !TryGetTypeName(current, out var typeName)
                    || !TryGetHResult(current, out var hresult)
                    || !TryGetMessage(current, out var message)
                    || !TryGetStackTrace(current, out var stack)
                )
                    return false;

                var prefix = "d" + depth.ToString(CultureInfo.InvariantCulture) + ".";
                if (
                    !AppendFingerprintComponent(
                        canonical,
                        prefix + "type",
                        typeName,
                        ref remainingBudget
                    )
                    || !AppendFingerprintComponent(
                        canonical,
                        prefix + "hresult",
                        hresult,
                        ref remainingBudget
                    )
                    || !AppendFingerprintComponent(
                        canonical,
                        prefix + "message",
                        message,
                        ref remainingBudget
                    )
                    || !AppendFingerprintComponent(
                        canonical,
                        prefix + "stack",
                        stack,
                        ref remainingBudget
                    )
                    || !TryGetInnerException(current, out current)
                )
                    return false;
            }

            if (current != null)
                return false;
            canonical.Append("depth=").Append(depth).Append(';');
            return _formatter.TryFingerprintText(canonical.ToString(), out fingerprint);
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private bool AppendFingerprintComponent(
        StringBuilder canonical,
        string label,
        string? value,
        ref int remainingBudget
    )
    {
        canonical.Append(label).Append('=');
        if (value == null)
        {
            canonical.Append("null;");
            return true;
        }
        if (value.Length > remainingBudget)
            return false;
        remainingBudget -= value.Length;
        if (!_formatter.TryFingerprintText(value, out var digest))
            return false;
        canonical.Append(value.Length).Append(':').Append(digest).Append(';');
        return true;
    }

    private static bool HasUnsupportedAggregateFanOut(Exception exception)
    {
        if (exception is not AggregateException aggregate)
            return false;
        try
        {
            return aggregate.InnerExceptions.Count > 1;
        }
        catch
        {
            return true;
        }
    }

    private void AppendException(
        List<string> tokens,
        Exception exception,
        string prefix,
        int messageBudget,
        ref bool truncated
    )
    {
        var type = _formatter.RenderExceptionText(
            SafeTypeName(exception),
            TypeBudget,
            preserveTail: false
        );
        tokens.Add(prefix + "_type=" + type.Text);
        truncated |= type.Truncated;
        tokens.Add(prefix + "_hresult=" + SafeHResult(exception));

        var message = SafeMessage(exception, out var unavailable);
        if (unavailable)
        {
            tokens.Add(prefix + "_message=<unavailable>");
            return;
        }

        var rendered = _formatter.RenderExceptionText(
            message ?? string.Empty,
            messageBudget,
            preserveTail: false
        );
        tokens.Add(prefix + "_message=" + rendered.Text);
        truncated |= rendered.Truncated;
    }

    private static string SafeTypeName(Exception exception)
    {
        try
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool TryGetTypeName(Exception exception, out string? typeName)
    {
        try
        {
            typeName = exception.GetType().FullName ?? exception.GetType().Name;
            return true;
        }
        catch
        {
            typeName = null;
            return false;
        }
    }

    private static string SafeHResult(Exception exception)
    {
        try
        {
            return "0x"
                + unchecked((uint)exception.HResult).ToString("X8", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static bool TryGetHResult(Exception exception, out string? hresult)
    {
        try
        {
            hresult =
                "0x"
                + unchecked((uint)exception.HResult).ToString("X8", CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            hresult = null;
            return false;
        }
    }

    private static string? SafeMessage(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.Message;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static bool TryGetMessage(Exception exception, out string? message)
    {
        try
        {
            message = exception.Message;
            return true;
        }
        catch
        {
            message = null;
            return false;
        }
    }

    private static string? SafeStackTrace(Exception exception, out bool unavailable)
    {
        try
        {
            unavailable = false;
            return exception.StackTrace;
        }
        catch
        {
            unavailable = true;
            return null;
        }
    }

    private static bool TryGetStackTrace(Exception exception, out string? stackTrace)
    {
        try
        {
            stackTrace = exception.StackTrace;
            return true;
        }
        catch
        {
            stackTrace = null;
            return false;
        }
    }

    private static Exception? SafeInnerException(Exception exception)
    {
        try
        {
            return exception.InnerException;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetInnerException(Exception exception, out Exception? innerException)
    {
        try
        {
            innerException = exception.InnerException;
            return true;
        }
        catch
        {
            innerException = null;
            return false;
        }
    }
}

internal readonly struct ExceptionProjection
{
    internal ExceptionProjection(IReadOnlyList<string> tokens, bool truncated)
    {
        Tokens = tokens;
        Truncated = truncated;
    }

    internal IReadOnlyList<string> Tokens { get; }

    internal bool Truncated { get; }
}
