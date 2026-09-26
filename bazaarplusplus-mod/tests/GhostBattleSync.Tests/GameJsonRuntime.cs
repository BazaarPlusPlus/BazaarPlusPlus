#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class GameJsonRuntime
{
    // The game's Newtonsoft assembly can win binding in Unity. This isolated scenario process
    // must exercise that runtime instead of silently passing with the newer NuGet test copy.
    [ModuleInitializer]
    internal static void Install()
    {
        var managedPath = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "GameManagedPath")
            .Value!;
        var file = Path.GetFullPath(Path.Combine(managedPath, "Newtonsoft.Json.dll"));
        using var expected = File.OpenRead(file);
        using var actual = File.OpenRead(typeof(Newtonsoft.Json.Linq.JToken).Assembly.Location);
        if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual)))
            throw new InvalidOperationException(
                "Ghost import tests must load the game's Newtonsoft.Json assembly."
            );
    }
}
