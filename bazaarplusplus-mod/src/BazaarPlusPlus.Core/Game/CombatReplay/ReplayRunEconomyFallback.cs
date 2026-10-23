#nullable enable

using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.CombatReplay;

// A battle captured before its manifest carried income and gold replays them from its Run's
// row; Storage's RunEconomyReader owns the SQL.
public static class ReplayRunEconomyFallback
{
    public static void ApplyMissingRunEconomy(
        PvpBattleManifest manifest,
        string? databasePath,
        IReplayPlaybackOutcomeSink? outcome = null
    )
    {
        if (manifest == null)
            return;
        if (
            manifest.Participants.PlayerIncome.HasValue && manifest.Participants.PlayerGold.HasValue
        )
            return;
        if (string.IsNullOrWhiteSpace(manifest.RunId))
            return;
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return;

        try
        {
            var (income, gold) = RunEconomyReader.TryRead(databasePath, manifest.RunId);
            manifest.Participants.PlayerIncome ??= income;
            manifest.Participants.PlayerGold ??= gold;
        }
        catch (Exception ex)
        {
            outcome?.ReportDegradation(ReplayPlaybackReasonCode.PlayerAttributesUnavailable, ex);
        }
    }
}
