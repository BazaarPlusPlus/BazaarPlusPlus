#nullable enable
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
                    global::BazaarPlusPlus.PluginLogEvents.FeatureStartDegraded,
                    ex,
                    global::BazaarPlusPlus.PluginLogEvents.FeatureDegradedFeature.Bind(
                        global::BazaarPlusPlus.PluginLogIdentity.FeatureId(feature.GetType())
                    ),
                    global::BazaarPlusPlus.PluginLogEvents.FeatureDegradedReasonCode.Bind(
                        global::BazaarPlusPlus.PluginLogReasonCode.FeatureException
                    )
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
                    global::BazaarPlusPlus.PluginLogEvents.FeatureStopDegraded,
                    ex,
                    global::BazaarPlusPlus.PluginLogEvents.FeatureDegradedFeature.Bind(
                        global::BazaarPlusPlus.PluginLogIdentity.FeatureId(_features[i].GetType())
                    ),
                    global::BazaarPlusPlus.PluginLogEvents.FeatureDegradedReasonCode.Bind(
                        global::BazaarPlusPlus.PluginLogReasonCode.FeatureException
                    )
                );
            }
        }
    }
}
