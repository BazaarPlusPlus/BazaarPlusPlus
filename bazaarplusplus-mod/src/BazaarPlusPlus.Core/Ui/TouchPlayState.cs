#nullable enable

namespace BazaarPlusPlus.Core.Ui;

public readonly record struct TouchPlayState(bool InCombat, bool InReplay, bool RecapOpen)
{
    public bool IsPlaying => InCombat || InReplay;

    public bool ShowsPreviewSwitches => !InCombat;

    public bool OffersEnchantments => !InCombat && !InReplay;

    public bool PreviewsUpgrade => !InCombat && !InReplay && !RecapOpen;
}
