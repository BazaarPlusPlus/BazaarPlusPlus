#nullable enable
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
            feature.Start();
    }

    public void Stop()
    {
        for (var i = _features.Count - 1; i >= 0; i--)
            _features[i].Stop();
    }
}
