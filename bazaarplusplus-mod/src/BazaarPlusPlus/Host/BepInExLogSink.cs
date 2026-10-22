#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx.Logging;

namespace BazaarPlusPlus.Host;

/// <summary>Desktop log exit: forwards finished <see cref="BppLog"/> lines to BepInEx.</summary>
internal sealed class BepInExLogSink : IBppLogSink
{
    private readonly ManualLogSource _source;

    public BepInExLogSink(ManualLogSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Write(BppLogSeverity severity, string message)
    {
        switch (severity)
        {
            case BppLogSeverity.Debug:
                _source.LogDebug(message);
                return;
            case BppLogSeverity.Info:
                _source.LogInfo(message);
                return;
            case BppLogSeverity.Warning:
                _source.LogWarning(message);
                return;
            case BppLogSeverity.Error:
                _source.LogError(message);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(severity));
        }
    }
}
