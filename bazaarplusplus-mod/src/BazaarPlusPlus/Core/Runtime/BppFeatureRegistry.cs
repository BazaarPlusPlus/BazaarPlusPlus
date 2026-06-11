#nullable enable
using System;
using System.Collections.Generic;

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
                global::BazaarPlusPlus.Infrastructure.BppLog.Error(
                    "FeatureRegistry",
                    $"Feature failed to start: {feature.GetType().FullName}",
                    ex
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
                global::BazaarPlusPlus.Infrastructure.BppLog.Error(
                    "FeatureRegistry",
                    $"Feature failed to stop: {_features[i].GetType().FullName}",
                    ex
                );
            }
        }
    }
}
