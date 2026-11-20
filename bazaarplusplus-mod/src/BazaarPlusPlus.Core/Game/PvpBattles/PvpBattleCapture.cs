#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

public sealed record PvpBattleCapture(
    PvpBattleManifest Manifest,
    PvpReplayPayload? Replay,
    IReadOnlyList<BppLogField> BoardSummary
);
