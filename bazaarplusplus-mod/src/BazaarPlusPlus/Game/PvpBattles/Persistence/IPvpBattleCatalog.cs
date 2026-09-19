#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal interface IPvpBattleCatalog : IReplayPayloadMaintenanceCatalog
{
    void Save(PvpBattleManifest manifest);

    void AttachToRun(string battleId, string runId);

    PvpBattleManifest? TryLoad(string battleId);

    IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit);

    IReadOnlyList<PvpBattleManifest> ListByRunId(string runId);
}
