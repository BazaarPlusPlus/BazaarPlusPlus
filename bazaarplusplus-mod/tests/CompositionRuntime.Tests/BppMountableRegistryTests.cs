using BazaarPlusPlus.Core.Runtime;
using UnityEngine;
using Xunit;

namespace BazaarPlusPlus.Tests.CompositionRuntime;

public class BppMountableRegistryTests
{
    private sealed class RecordingMountable(
        string name,
        List<string> calls,
        bool throwOnMount = false
    ) : IBppMountable
    {
        public void Mount(GameObject host, IBppServices services)
        {
            calls.Add($"mount:{name}");
            if (throwOnMount)
                throw new InvalidOperationException($"{name} failed to mount");
        }

        public void Unmount(GameObject host) => calls.Add($"unmount:{name}");
    }

    [Fact]
    public void MountAll_runs_mountables_in_registration_order()
    {
        var calls = new List<string>();
        var registry = new BppMountableRegistry();
        registry.Register(new RecordingMountable("first", calls));
        registry.Register(new RecordingMountable("second", calls));

        registry.MountAll(host: null!, services: null!);

        Assert.Equal(new[] { "mount:first", "mount:second" }, calls);
    }

    [Fact]
    public void UnmountAll_runs_mountables_in_reverse_registration_order()
    {
        var calls = new List<string>();
        var registry = new BppMountableRegistry();
        registry.Register(new RecordingMountable("first", calls));
        registry.Register(new RecordingMountable("second", calls));

        registry.UnmountAll(host: null!);

        Assert.Equal(new[] { "unmount:second", "unmount:first" }, calls);
    }

    [Fact]
    public void MountAll_stops_after_a_mountable_throws()
    {
        var calls = new List<string>();
        var registry = new BppMountableRegistry();
        registry.Register(new RecordingMountable("first", calls, throwOnMount: true));
        registry.Register(new RecordingMountable("second", calls));

        // Behavior pin, not an endorsement: unlike BppFeatureRegistry, mount failures are not
        // isolated, so a throwing mountable currently prevents later mountables from running.
        Assert.Throws<InvalidOperationException>(() =>
            registry.MountAll(host: null!, services: null!)
        );

        Assert.Equal(new[] { "mount:first" }, calls);
    }
}
