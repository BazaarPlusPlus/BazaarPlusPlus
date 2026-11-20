using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.GameInterop;

internal static class AndroidDayTiers
{
    private static int _failures;

    public static GameDataDayTierTable? Current()
    {
        try
        {
            var run = Il2CppTheBazaar.Data.Run;
            if (run == null || !Il2CppTheBazaar.Data.IsManagerCreated())
                return null;
            var day = (int)run.Day;
            if (day <= 0)
                return null;
            var byDay = Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetGameModeById(run.GameModeId)
                ?.ItemSkillSpawnTierPercantagesByDay;
            if (byDay == null || !byDay.ContainsKey((uint)day))
                return null;
            var odds = byDay[(uint)day];
            return odds == null
                ? null
                : GameDataDayTierTable.FromWeights(
                    (float)odds.Bronze,
                    (float)odds.Silver,
                    (float)odds.Gold,
                    (float)odds.Diamond
                );
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref _failures) <= 3)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.day_tiers.unavailable",
                        storm: ["reason_code"]
                    ),
                    exception,
                    ("reason_code", "read_exception")
                );
            return null;
        }
    }
}
