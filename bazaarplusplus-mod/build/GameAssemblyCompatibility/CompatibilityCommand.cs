using System.Text.Json;

namespace BazaarPlusPlus.GameAssemblyCompatibility;

public static class CompatibilityCommand
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            string[] consumers;
            if (args.Length == 4 && args[0] == "--payload")
                consumers = PayloadAssemblies(args[2], args[3]);
            else if (args.Length >= 3 && args[0] == "--assemblies")
                consumers = args[2..];
            else
                throw new ArgumentException(
                    "Usage: --payload <game-json.dll> <inventory.json> <output-directory> "
                        + "| --assemblies <game-json.dll> <consumer.dll>..."
                );

            var result = MemberCompatibility.Check(args[1], consumers);
            foreach (var failure in result.Failures)
                error.WriteLine(failure);
            if (result.Types == 0)
                throw new InvalidDataException(
                    "No references to the supplied game assembly were checked."
                );
            output.WriteLine(
                $"Game assembly compatibility: {consumers.Length} assemblies, {result.Types} types, "
                    + $"{result.Members} members, {result.NamedArguments} named attribute arguments; "
                    + $"{result.Failures.Count} failures."
            );
            return result.Failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Game assembly compatibility failed: {exception.Message}");
            return 1;
        }
    }

    private static string[] PayloadAssemblies(string inventory, string directory)
    {
        using var stream = File.OpenRead(inventory);
        using var document = JsonDocument.Parse(stream);
        var result = new List<string>();
        foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            if (
                file.GetProperty("producer").GetString() != "managed"
                || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            var assembly = Path.Combine(directory, Path.GetFileName(path));
            if (
                file.TryGetProperty("optional", out var optional)
                && optional.GetBoolean()
                && !File.Exists(assembly)
            )
                continue;
            if (!File.Exists(assembly))
                throw new FileNotFoundException(
                    $"Required Payload assembly is missing: {assembly}"
                );
            result.Add(assembly);
        }
        if (result.Count == 0)
            throw new InvalidDataException("Payload Inventory selected no managed assemblies.");
        return result.ToArray();
    }
}
