using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.Lobby;
using HarmonyLib;
using Il2CppTheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Writes the BPP version into the main-menu version label, the visible proof that the host
/// loaded and its hooks fire. Observe mode counts the call and leaves the label alone.
/// </summary>
[HarmonyPatch(typeof(VersionShow), nameof(VersionShow.BuildVersionLabel))]
internal static class VersionLabelPatch
{
    private const long LoggedHits = 5;

    [HarmonyPostfix]
    private static void Postfix(VersionShow __instance)
    {
        var hits = AndroidPatchHost.CountVersionLabel();
        try
        {
            // The interop proxy exposes the private field as a property; no publicizing.
            var label = __instance.versionLabel;
            if (label == null)
            {
                if (hits <= LoggedHits)
                    AndroidLog.Warn("version_label.hit", ("hits", hits), ("label", null));
                return;
            }

            var before = label.text;
            var write = AndroidPatchHost.Mode == AndroidPatchMode.Enabled;
            if (write)
                label.text = MainMenuVersionLabelFormatter.Build(
                    Application.version,
                    AndroidPatchHost.PluginVersion
                );
            if (hits <= LoggedHits)
                AndroidLog.Info(
                    "version_label.hit",
                    ("hits", hits),
                    ("write", write),
                    ("before", before),
                    ("after", label.text)
                );
        }
        catch (Exception exception)
        {
            AndroidPatchHost.ReportProbeError("version_label", exception);
        }
    }
}
