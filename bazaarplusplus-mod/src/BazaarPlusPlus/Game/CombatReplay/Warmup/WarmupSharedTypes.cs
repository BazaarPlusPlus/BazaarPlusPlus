#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal sealed class ReplayWarmupStats
{
    public int SharedAssetsPreloaded;
    public int SharedAssetsSkipped;
    public int CardsPreloaded;
    public int CardsSkipped;
    public int CardsFailed;
    public int OverrideAssetsPreloaded;
    public int OverrideAssetsSkipped;
    public int OverrideAssetsFailed;
    public int VfxPrewarmed;
    public int VfxSkipped;
    public int VfxFailed;
}

internal sealed class ReplayAudioWarmupStats
{
    public int BoardBanksLoaded;
    public int BoardBanksAlreadyLoaded;
    public int BoardBanksFailed;
    public int BoardBanksSkipped;
    public int SoundtrackBanksLoaded;
    public int SoundtrackBanksAlreadyLoaded;
    public int SoundtrackBanksFailed;
    public int SoundtrackBanksSkipped;
}

internal static class WarmupConstants
{
    internal const int ReplayWarmupConcurrency = 4;
}
