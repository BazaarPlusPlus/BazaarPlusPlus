#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.DayTiers;

internal enum GameDataDayTierStatus
{
    Available,
    NotApplicable,
    NotReady,
    Missing,
    Invalid,
}

internal readonly record struct GameDataDayTierWeights(
    float Bronze,
    float Silver,
    float Gold,
    float Diamond
);

internal readonly record struct GameDataDayTierSourceContext(
    GameDataDayTierStatus Status,
    object? Manager,
    Guid GameModeId,
    int? Day
)
{
    public static GameDataDayTierSourceContext Available(
        object manager,
        Guid gameModeId,
        int day
    ) => new(GameDataDayTierStatus.Available, manager, gameModeId, day);

    public static GameDataDayTierSourceContext NotApplicable() =>
        new(GameDataDayTierStatus.NotApplicable, null, Guid.Empty, null);

    public static GameDataDayTierSourceContext NotReady(int? day = null) =>
        new(GameDataDayTierStatus.NotReady, null, Guid.Empty, day);

    public static GameDataDayTierSourceContext Invalid(int? day = null) =>
        new(GameDataDayTierStatus.Invalid, null, Guid.Empty, day);
}

internal sealed class GameDataDayTierResolution
{
    private GameDataDayTierResolution(
        GameDataDayTierStatus status,
        int? day,
        GameDataDayTierTable? table
    )
    {
        Status = status;
        Day = day;
        Table = table;
    }

    public GameDataDayTierStatus Status { get; }

    public int? Day { get; }

    public GameDataDayTierTable? Table { get; }

    public ETier? MaximumTier => Table?.MaximumTier;

    internal static GameDataDayTierResolution Unavailable(GameDataDayTierStatus status, int? day) =>
        new(status, day, null);

    internal static GameDataDayTierResolution Available(int day, GameDataDayTierTable table) =>
        new(GameDataDayTierStatus.Available, day, table);
}

internal interface IGameDataDayTierSource
{
    GameDataDayTierSourceContext Capture();

    GameDataDayTierStatus ReadWeights(
        object manager,
        Guid gameModeId,
        int day,
        out GameDataDayTierWeights weights
    );
}

internal interface IGameDataDayTierResolver
{
    GameDataDayTierResolution Resolve();

    GameDataDayTierResolution Resolve(object expectedManager);
}

internal sealed class GameDataDayTierResolver(IGameDataDayTierSource source)
    : IGameDataDayTierResolver
{
    private readonly object _syncRoot = new();
    private readonly IGameDataDayTierSource _source =
        source ?? throw new ArgumentNullException(nameof(source));
    private readonly Dictionary<CacheKey, GameDataDayTierResolution> _successes = new();
    private object? _manager;

    public GameDataDayTierResolution Resolve() =>
        Resolve(expectedManager: null, requireMatch: false);

    public GameDataDayTierResolution Resolve(object expectedManager)
    {
        if (expectedManager == null)
            throw new ArgumentNullException(nameof(expectedManager));
        return Resolve(expectedManager, requireMatch: true);
    }

    private GameDataDayTierResolution Resolve(object? expectedManager, bool requireMatch)
    {
        var context = _source.Capture();
        if (context.Manager != null)
            SwitchGeneration(context.Manager);

        if (
            context.Status != GameDataDayTierStatus.Available
            || context.Manager == null
            || !context.Day.HasValue
        )
            return GameDataDayTierResolution.Unavailable(context.Status, context.Day);

        if (requireMatch && !ReferenceEquals(context.Manager, expectedManager))
            return GameDataDayTierResolution.Unavailable(
                GameDataDayTierStatus.NotReady,
                context.Day
            );

        lock (_syncRoot)
        {
            SwitchGenerationLocked(context.Manager);
            var key = new CacheKey(context.GameModeId, context.Day.Value);
            if (_successes.TryGetValue(key, out var cached))
                return cached;

            var readStatus = _source.ReadWeights(
                context.Manager,
                context.GameModeId,
                context.Day.Value,
                out var weights
            );
            if (readStatus != GameDataDayTierStatus.Available)
                return GameDataDayTierResolution.Unavailable(readStatus, context.Day);

            var table = GameDataDayTierTable.FromWeights(
                weights.Bronze,
                weights.Silver,
                weights.Gold,
                weights.Diamond
            );
            if (table == null)
                return GameDataDayTierResolution.Unavailable(
                    GameDataDayTierStatus.Invalid,
                    context.Day
                );

            var available = GameDataDayTierResolution.Available(context.Day.Value, table);
            _successes.Add(key, available);
            return available;
        }
    }

    private void SwitchGeneration(object manager)
    {
        lock (_syncRoot)
            SwitchGenerationLocked(manager);
    }

    private void SwitchGenerationLocked(object manager)
    {
        if (ReferenceEquals(_manager, manager))
            return;
        _manager = manager;
        _successes.Clear();
    }

    private readonly record struct CacheKey(Guid GameModeId, int Day);
}
