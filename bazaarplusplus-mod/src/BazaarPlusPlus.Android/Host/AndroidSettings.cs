using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The settings only the Android host has, in the <c>[Android]</c> section of the same
/// <c>BazaarPlusPlus.cfg</c> that holds the shared <see cref="BppSettings"/>. Touch replaces the
/// desktop's hotkeys: the upgrade preview, held on Shift there, is a persisted switch here.
/// </summary>
internal sealed class AndroidSettings
{
    public AndroidSettings(IBppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        TouchMenu = store.Bind(
            "Android",
            "TouchMenu",
            true,
            "Whether the BPP touch button is shown. It opens the settings, History, and the update prompts."
        );
        UpgradePreview = store.Bind(
            "Android",
            "UpgradePreview",
            false,
            "Whether item tooltips show the next tier's values, the touch counterpart of holding Shift on desktop."
        );
        CollectionPanel = store.Bind(
            "Android",
            "CollectionPanel",
            true,
            "Whether the touch menu offers the Collection panel, which shows the game's own cards."
        );
    }

    public ISetting<bool> TouchMenu { get; }

    public ISetting<bool> CollectionPanel { get; }

    public ISetting<bool> UpgradePreview { get; }
}
