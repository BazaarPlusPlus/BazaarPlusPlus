#nullable enable
using BazaarPlusPlus.GameInterop.StaticCards;
using TheBazaar;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.GameInterop.DayTiers;

internal sealed class GameDataDayTierSource : IGameDataDayTierSource
{
    public GameDataDayTierSourceContext Capture()
    {
        try
        {
            var run = Data.Run;
            if (run == null)
                return GameDataDayTierSourceContext.NotApplicable();

            var day = checked((int)run.Day);
            if (run.GameModeId == Guid.Empty || day <= 0)
                return GameDataDayTierSourceContext.Invalid(day);

            var manager = BppStaticDataAccess.TryGetReadyManagerObject();
            return manager == null
                ? GameDataDayTierSourceContext.NotReady(day)
                : GameDataDayTierSourceContext.Available(manager, run.GameModeId, day);
        }
        catch (OverflowException)
        {
            return GameDataDayTierSourceContext.Invalid();
        }
        catch (Exception)
        {
            return GameDataDayTierSourceContext.NotReady();
        }
    }

    public GameDataDayTierStatus ReadWeights(
        object manager,
        Guid gameModeId,
        int day,
        out GameDataDayTierWeights weights
    )
    {
        weights = default;
        if (manager is not JsonGameDataManager gameData)
            return GameDataDayTierStatus.NotReady;

        try
        {
            var gameMode = gameData.GetGameModeById(gameModeId);
            if (
                gameMode == null
                || gameMode.ItemSkillSpawnTierPercantagesByDay == null
                || !gameMode.ItemSkillSpawnTierPercantagesByDay.TryGetValue(
                    checked((uint)day),
                    out var probabilities
                )
                || probabilities == null
            )
                return GameDataDayTierStatus.Missing;

            weights = new GameDataDayTierWeights(
                probabilities.Bronze,
                probabilities.Silver,
                probabilities.Gold,
                probabilities.Diamond
            );
            return GameDataDayTierStatus.Available;
        }
        catch (Exception)
        {
            return GameDataDayTierStatus.NotReady;
        }
    }
}
