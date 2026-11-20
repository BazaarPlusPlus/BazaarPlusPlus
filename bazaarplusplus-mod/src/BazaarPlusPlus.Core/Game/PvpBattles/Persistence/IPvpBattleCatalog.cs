#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

public interface IPvpBattleCatalog : IReplayPayloadMaintenanceCatalog
{
    void Save(PvpBattleManifest manifest);

    void AttachToRun(string battleId, string runId);

    PvpBattleManifest? TryLoad(string battleId);

    // Tests for existence without loading the manifest.
    bool Exists(string battleId);

    IReadOnlyList<PvpBattleManifest> ListByRunId(string runId);
}
