#nullable enable
using Microsoft.Data.Sqlite;

internal static class TestRoot
{
    public static string Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    public static void Delete(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
