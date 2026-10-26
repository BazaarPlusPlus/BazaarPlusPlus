#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

public interface IPvpBattleCatalog : IReplayPayloadMaintenanceCatalog
{
    void Save(PvpBattleManifest manifest);

    void AttachToRun(string battleId, string runId);

    PvpBattleManifest? TryLoad(string battleId);

    /// <summary>Whether <see cref="TryLoad"/> would find the battle, without building its manifest.</summary>
    bool Exists(string battleId);

    IReadOnlyList<PvpBattleManifest> ListByRunId(string runId);
}
