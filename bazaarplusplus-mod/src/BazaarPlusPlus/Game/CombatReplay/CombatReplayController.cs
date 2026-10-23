#nullable enable
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

// The desktop's saved replay: Core's SavedReplayCatalog for the manifest and payload, and the
// Mono loader for the game's messages.
internal sealed class CombatReplayController
{
    private readonly SavedReplayCatalog _saved;
    private readonly CombatReplayLoader _loader;

    public CombatReplayController(
        IPvpBattleCatalog battleCatalog,
        CombatReplayPayloadStore payloadStore,
        CombatReplayLoader loader
    )
    {
        _saved = new SavedReplayCatalog(battleCatalog, payloadStore);
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public bool HasSavedReplay(string battleId) => _saved.HasSavedReplay(battleId);

    public PvpBattleManifest? LoadBattle(string battleId) => _saved.LoadBattle(battleId);

    public PvpReplayPayload? LoadPayload(PvpBattleManifest manifest) =>
        _saved.LoadPayload(manifest);

    public CombatSequenceMessages LoadReplay(PvpReplayPayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        return _loader.Load(payload);
    }
}
