namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// How much of the Android host's Harmony surface is live, read from the settings file at
/// startup: <see cref="Enabled"/> installs every hook and lets them change the game,
/// <see cref="Observe"/> installs every hook but only counts and logs, <see cref="Disabled"/>
/// installs none. Comparing the modes on one DLL is how a hook's effect is proved on a device.
/// </summary>
internal enum AndroidPatchMode
{
    Enabled,
    Observe,
    Disabled,
}
