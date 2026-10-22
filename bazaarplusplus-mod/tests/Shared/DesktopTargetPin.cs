#nullable enable
using System.Reflection;
using System.Runtime.Versioning;

// The golden-producing suites run Core, Storage, and ModApi as the desktop host ships them, the
// netstandard2.1 output (tests/Directory.Build.targets, ADR-0013 §3). A reference that slips past
// that pin would quietly move the goldens onto the net10.0 output, so the suite checks what loaded.
internal static class DesktopTargetPin
{
    private const string DesktopTarget = ".NETStandard,Version=v2.1";

    internal static string? Mismatch()
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
            .Where(loaded => loaded.Target != DesktopTarget)
            .Select(loaded => $"{loaded.Name} loaded {loaded.Target ?? "an unknown target"}")
            .ToArray();
        return wrong.Length == 0
            ? null
            : $"Expected the {DesktopTarget} output of the shared projects: {string.Join("; ", wrong)}.";
    }
}
