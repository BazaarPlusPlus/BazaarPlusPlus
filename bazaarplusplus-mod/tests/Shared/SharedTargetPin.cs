#nullable enable
using System.Reflection;
using System.Runtime.Versioning;

// The golden-producing suites run Core, Storage, and ModApi as a host ships them. The desktop
// binding is the netstandard2.1 output (tests/Directory.Build.targets, ADR-0013 §3); the
// *.Net10.Tests bindings name the net10.0 output the Android host ships. A reference that slips
// past either would quietly move the goldens onto the other output, so the suite checks what loaded.
internal static class SharedTargetPin
{
    internal const string Desktop = ".NETStandard,Version=v2.1";
    internal const string Net10 = ".NETCoreApp,Version=v10.0";

    internal static string? Mismatch(string expected)
    {
        var shared = new[]
        {
            typeof(BazaarPlusPlus.Core.Events.InMemoryBppEventBus).Assembly,
            typeof(BazaarPlusPlus.Storage.Paths.PathConstants).Assembly,
            typeof(BazaarPlusPlus.ModApi.Clients.ModApiSession).Assembly,
        };
        var wrong = shared
            .Select(assembly =>
                (
                    Name: assembly.GetName().Name,
                    Target: assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
                )
            )
            .Where(loaded => loaded.Target != expected)
            .Select(loaded => $"{loaded.Name} loaded {loaded.Target ?? "an unknown target"}")
            .ToArray();
        return wrong.Length == 0
            ? null
            : $"Expected the {expected} output of the shared projects: {string.Join("; ", wrong)}.";
    }
}
