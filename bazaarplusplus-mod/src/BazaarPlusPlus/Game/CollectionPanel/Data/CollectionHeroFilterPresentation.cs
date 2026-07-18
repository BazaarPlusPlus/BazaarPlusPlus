#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal readonly struct CollectionHeroFilterPresentation
{
    private CollectionHeroFilterPresentation(bool isVisible, bool isEnabled)
    {
        IsVisible = isVisible;
        IsEnabled = isEnabled;
    }

    public bool IsVisible { get; }

    public bool IsEnabled { get; }

    public static CollectionHeroFilterPresentation For(CollectionTabProfile profile) =>
        new(isVisible: true, isEnabled: profile.ShowHeroFilter);
}
