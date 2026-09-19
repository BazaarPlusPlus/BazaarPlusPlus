#nullable enable
using System.Text.Json;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;

internal static class RunLogSchemaReleaseContractTests
{
    internal static void Run()
    {
        var contractPath = Path.Combine(
            AppContext.BaseDirectory,
            "BazaarPlusPlus.history-database.json"
        );
        if (!File.Exists(contractPath))
            throw new InvalidOperationException(
                $"History database release contract was not copied to '{contractPath}'."
            );

        using var contract = JsonDocument.Parse(TestInputs.Scratch(contractPath));
        var root = contract.RootElement;
        Equal(1, root.GetProperty("formatVersion").GetInt32(), "contract format version");
        Equal(
            RunLogSchema.LocalDatabaseSchemaVersion,
            root.GetProperty("historyDatabaseUserVersion").GetInt32(),
            "database user version"
        );
        Equal(
            RunLogSchema.RowSchemaVersion,
            root.GetProperty("historyRowSchemaVersion").GetInt32(),
            "history row schema version"
        );
    }

    private static void Equal<T>(T expected, T actual, string label)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }
}
