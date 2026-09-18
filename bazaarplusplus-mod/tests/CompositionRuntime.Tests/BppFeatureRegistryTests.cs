using BazaarPlusPlus.Core.Runtime;
using Xunit;

namespace BazaarPlusPlus.Tests.CompositionRuntime;

public class BppFeatureRegistryTests
{
    private sealed class RecordingFeature(
        string name,
        List<string> calls,
        bool throwOnStart = false,
        bool throwOnStop = false
    ) : IBppFeature
    {
        public void Start()
        {
            calls.Add($"start:{name}");
            if (throwOnStart)
                throw new InvalidOperationException($"{name} failed to start");
        }

        public void Stop()
        {
            calls.Add($"stop:{name}");
            if (throwOnStop)
                throw new InvalidOperationException($"{name} failed to stop");
        }
    }

    [Fact]
    public void Start_runs_features_in_registration_order()
    {
        var calls = new List<string>();
        var registry = new BppFeatureRegistry();
        registry.Register(new RecordingFeature("first", calls));
        registry.Register(new RecordingFeature("second", calls));

        registry.Start();

        Assert.Equal(new[] { "start:first", "start:second" }, calls);
    }

    [Fact]
    public void Start_continues_after_a_feature_throws()
    {
        var calls = new List<string>();
        var registry = new BppFeatureRegistry();
        registry.Register(new RecordingFeature("first", calls, throwOnStart: true));
        registry.Register(new RecordingFeature("second", calls));

        registry.Start();

        Assert.Equal(new[] { "start:first", "start:second" }, calls);
    }

    [Fact]
    public void Stop_runs_in_reverse_order_and_continues_after_a_feature_throws()
    {
        var calls = new List<string>();
        var registry = new BppFeatureRegistry();
        registry.Register(new RecordingFeature("first", calls));
        registry.Register(new RecordingFeature("second", calls, throwOnStop: true));
        registry.Register(new RecordingFeature("third", calls));

        registry.Stop();

        Assert.Equal(new[] { "stop:third", "stop:second", "stop:first" }, calls);
    }
}
