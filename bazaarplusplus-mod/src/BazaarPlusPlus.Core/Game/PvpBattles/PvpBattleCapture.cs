#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One closed PvP Battle Capture Window: the battle's manifest, its Replay Payload (null for a
/// monster combat, or when a part could not be taken), and each board's label for the host's
/// recorded event (<c>player_hand</c>, <c>player_skills</c>, <c>opponent_hand</c>,
/// <c>opponent_skills</c>, each <c>{status}/{source}/{count}/opening={n|failed}[/live]</c>).
/// </summary>
public sealed record PvpBattleCapture(
    PvpBattleManifest Manifest,
    PvpReplayPayload? Replay,
    IReadOnlyList<BppLogField> BoardSummary
);
