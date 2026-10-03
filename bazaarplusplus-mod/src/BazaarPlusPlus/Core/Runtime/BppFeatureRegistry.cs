#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Core.Runtime;

internal sealed class BppFeatureRegistry
{
    private readonly List<IBppFeature> _features = new();

    public void Register(IBppFeature feature)
    {
        _features.Add(feature);
    }

    public void Start()
    {
        foreach (var feature in _features)
        {
            try
            {
                feature.Start();
            }
            catch (Exception ex)
            {
                global::BazaarPlusPlus.Infrastructure.BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Plugin,
                        "plugin.feature_start.degraded",
                        storm: ["feature", "reason_code"]
                    ),
                    ex,
                    ("feature", feature.GetType().FullName),
                    ("reason_code", global::BazaarPlusPlus.PluginLogReasonCode.FeatureException)
                );
            }
        }
    }

    public void Stop()
    {
        for (var i = _features.Count - 1; i >= 0; i--)
        {
            try
            {
                _features[i].Stop();
            }
            catch (Exception ex)
            {
                global::BazaarPlusPlus.Infrastructure.BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Plugin,
                        "plugin.feature_stop.degraded",
                        storm: ["feature", "reason_code"]
                    ),
                    ex,
                    ("feature", _features[i].GetType().FullName),
                    ("reason_code", global::BazaarPlusPlus.PluginLogReasonCode.FeatureException)
                );
            }
        }
    }
}
