#nullable enable
namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One board capture on BPP card facets. Its stored JSON is Core's
/// <see cref="PvpBattleCardSetDocument"/>; <see cref="PvpBattleSnapshotsCodec"/> maps between them.
/// </summary>
public sealed class PvpBattleCardSetCapture
{
    public IList<PvpBattleCardSnapshot> Items { get; set; } = new List<PvpBattleCardSnapshot>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }
}
