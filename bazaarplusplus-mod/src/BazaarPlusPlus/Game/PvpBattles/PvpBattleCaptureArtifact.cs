#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

internal sealed class PvpBattleCaptureArtifact
{
    public PvpBattleManifest Manifest { get; set; } = new();

    public PvpReplayPayload Payload { get; set; } = new();
}
