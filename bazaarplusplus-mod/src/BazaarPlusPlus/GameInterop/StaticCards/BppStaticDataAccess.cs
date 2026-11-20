#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Game;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.GameInterop.StaticCards;

// Data.GetStatic() can return a manager or a task, depending on the game build.
internal static class BppStaticDataAccess
{
    private static readonly FieldInfo? DatabasePathField = typeof(JsonGameDataManager).GetField(
        "_dbPath",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly FieldInfo? LevelUpsField = typeof(JsonGameDataManager).GetField(
        "_levelUps",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );

    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData is not JsonGameDataManager manager || templateId == Guid.Empty)
            return null;

        return manager.GetCardById(templateId) as TCardBase;
    }

    public static object? TryGetReadyManagerObject()
    {
        if (!Data.IsManagerCreated())
            return null;

        object? staticData = Data.GetStatic();
        if (staticData is Task<JsonGameDataManager> task)
            return task.IsCompleted ? task.Result : null;

        return staticData as JsonGameDataManager;
    }

    // GetCardMap can run off-thread because it opens its own SQLite connection and publishes
    // the finished dictionary atomically. Keep compatibility snapshots out of the game's cache.
    public static Dictionary<Guid, ITCard>? LoadCardMap(object? source)
    {
        if (source is not JsonGameDataManager manager)
            return null;
        try
        {
            return manager.GetCardMap();
        }
        catch (Exception error) when (CompatibleCardMapReader.IsUnsupportedType(error))
        {
            var path = DatabasePathField?.GetValue(manager) as string;
            if (string.IsNullOrWhiteSpace(path))
                throw;

            var cards = CompatibleCardMapReader.Read(path!, out var unsupportedCount);
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.StaticCards,
                    "static_cards.catalog.unsupported_templates"
                ),
                ("accepted_count", cards.Count),
                ("unsupported_count", unsupportedCount)
            );
            return cards;
        }
    }

    // The manager exposes point lookups only. Copy its data-driven level table before worker use.
    public static Dictionary<int, TLevelUp>? SnapshotLevelUps(object? source)
    {
        if (source is not JsonGameDataManager manager)
            return null;
        if (LevelUpsField?.GetValue(manager) is not Dictionary<int, TLevelUp> levelUps)
            return null;
        return new Dictionary<int, TLevelUp>(levelUps);
    }

    // Capture on the Unity main thread because the fallback path and data URL read game globals.
    public static BppGameDataSourceInfo? TryCaptureGameDataSourceInfo(object? source)
    {
        if (source is not JsonGameDataManager manager)
            return null;

        try
        {
            var databasePath = DatabasePathField?.GetValue(manager) as string;
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                databasePath = Path.Combine(
                    TheBazaar.DataManagement.DataManifestActions.GetCachePath(),
                    "GameData.db"
                );
            }

            var directory = Path.GetDirectoryName(databasePath);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            return new BppGameDataSourceInfo(
                databasePath!,
                Path.Combine(directory!, "manifest.json"),
                TheBazaar.Config.DataURL ?? string.Empty
            );
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class BppGameDataSourceInfo
{
    public BppGameDataSourceInfo(string databasePath, string manifestPath, string dataBaseUrl)
    {
        DatabasePath = databasePath;
        ManifestPath = manifestPath;
        DataBaseUrl = dataBaseUrl;
    }

    public string DatabasePath { get; }

    public string ManifestPath { get; }

    public string DataBaseUrl { get; }
}
