using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using MelonLoader;

namespace BazaarPlusPlus.Android.Host;

internal sealed class MelonLogSink : IBppLogSink
{
    private readonly MelonLogger.Instance _logger;

    public MelonLogSink(MelonLogger.Instance logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Write(BppLogSeverity severity, string message)
    {
        switch (severity)
        {
            case BppLogSeverity.Debug:
            case BppLogSeverity.Info:
                _logger.Msg(message);
                return;
            case BppLogSeverity.Warning:
                _logger.Warning(message);
                return;
            case BppLogSeverity.Error:
                _logger.Error(message);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(severity));
        }
    }
}
