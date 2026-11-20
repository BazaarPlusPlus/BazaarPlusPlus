#nullable enable
using System.Reflection;
using System.Runtime.Versioning;

// Pin each suite to its host's target framework so regenerating goldens cannot silently switch
// between the game-supplied libraries and the pinned NuGet packages.
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
