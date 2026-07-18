#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Game;
using TheBazaar;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// <c>Data.GetStatic()</c> has shipped as both a synchronous manager return and a completed
/// task-returning accessor. This helper centralises that version seam.
/// </summary>
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

    /// <summary>
    /// Reads the current GameData generation's item/skill tier weights for one run day.
    /// Returning the raw weights keeps normalization aligned with <see cref="TierTable"/>,
    /// which rolls against the positive-weight sum rather than assuming a total of one.
    /// </summary>
    public static TieredSpawnProbabilities? GetItemSkillSpawnTierProbabilities(
        object? staticData,
        Guid gameModeId,
        int day
    )
    {
        if (staticData is not JsonGameDataManager manager || gameModeId == Guid.Empty || day <= 0)
            return null;

        try
        {
            var gameMode = manager.GetGameModeById(gameModeId);
            return
                gameMode?.ItemSkillSpawnTierPercantagesByDay.TryGetValue(
                    (uint)day,
                    out var probabilities
                ) == true
                ? probabilities
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Non-blocking handle to the static data manager. Returns the manager as an opaque object
    /// only when it is fully materialised (created, and any task-returning <c>GetStatic()</c>
    /// already completed); returns <c>null</c> otherwise. Never blocks the main thread waiting
    /// on the static-data task.
    /// </summary>
    public static object? TryGetReadyManagerObject()
    {
        if (!Data.IsManagerCreated())
            return null;

        object? staticData = Data.GetStatic();
        if (staticData is Task<JsonGameDataManager> task)
            return task.IsCompleted ? task.Result : null;

        return staticData as JsonGameDataManager;
    }

    /// <summary>
    /// Materialises the full card map (<c>JsonGameDataManager.GetCardMap()</c> → <c>ReadAllCards</c>:
    /// a full-table SQLite read plus polymorphic JSON deserialize — the dominant first-open cost).
    /// Intended to run on a worker thread: the game opens its own SQLite connection, deserializes
    /// on PLINQ workers, builds a fresh dictionary, and publishes it via an atomic reference
    /// assignment, so calling it off the main thread does not tear the shared map. <paramref
    /// name="source"/> must come from <see cref="TryGetReadyManagerObject"/>.
    /// </summary>
    public static Dictionary<Guid, ITCard>? LoadCardMap(object? source) =>
        source is JsonGameDataManager manager ? manager.GetCardMap() : null;

    /// <summary>
    /// Copies the manager's eagerly-loaded level-up table so background preview compilation never
    /// retains the game's mutable dictionary. Reflection is centralised here because the manager
    /// exposes only point lookups and the level range is data-driven.
    /// </summary>
    public static Dictionary<int, TLevelUp>? SnapshotLevelUps(object? source)
    {
        if (source is not JsonGameDataManager manager)
            return null;
        if (LevelUpsField?.GetValue(manager) is not Dictionary<int, TLevelUp> levelUps)
            return null;
        return new Dictionary<int, TLevelUp>(levelUps);
    }

    /// <summary>
    /// Captures the ordinary string inputs needed to identify the current GameData generation.
    /// Call this on the Unity main thread: the fallback path and data URL touch game globals;
    /// workers consume only the returned strings.
    /// </summary>
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
