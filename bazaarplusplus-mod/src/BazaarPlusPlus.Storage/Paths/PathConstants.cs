#nullable enable
namespace BazaarPlusPlus.Storage.Paths;

public static class PathConstants
{
    public const string RunLogDatabaseFileName = "bazaarplusplus.db";
    public const string DataRootDirectoryName = "BazaarPlusPlusV6";
    private const string CombatReplayDirectoryName = "CombatReplays";
    private const string ScreenshotsDirectoryName = "Screenshots";
    private const string BundleOutboxDirectoryName = "BundleOutbox";
    private const string CombatReplayVideoDirectoryName = "CombatReplayVideos";
    private const string FavoriteBattlesDirectoryName = "FavoriteBattles";

    public static string RunLogDatabase(string dataRoot) =>
        Combine(dataRoot, RunLogDatabaseFileName);

    public static string CombatReplays(string dataRoot) =>
        Combine(dataRoot, CombatReplayDirectoryName);

    public static string Screenshots(string dataRoot) =>
        Combine(dataRoot, ScreenshotsDirectoryName);

    public static string BundleOutbox(string dataRoot) =>
        Combine(dataRoot, BundleOutboxDirectoryName);

    public static string CombatReplayVideos(string dataRoot) =>
        Combine(dataRoot, CombatReplayVideoDirectoryName);

    // A favorite's own replay copy: never evicted by replay retention, removed only with the favorite.
    public static string FavoriteBattles(string dataRoot) =>
        Combine(dataRoot, FavoriteBattlesDirectoryName);

    private static string Combine(string dataRoot, string child)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("Data root is required.", nameof(dataRoot));
        return Path.Combine(dataRoot, child);
    }
}
