using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using Il2CppTheBazaar;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Patches;

/// <summary>
/// Writes the BPP version and the self-update status into the main-menu version label, the
/// visible proof that the host loaded and its hooks fire. Observe mode counts the call and leaves
/// the label alone. The label is kept so that a status the release check publishes later
/// rewrites it (<see cref="RefreshCurrent"/>).
/// </summary>
[HarmonyPatch(typeof(VersionShow), nameof(VersionShow.BuildVersionLabel))]
internal static class VersionLabelPatch
{
    private const long LoggedHits = 5;
    private static TextMeshProUGUI? _label;

    /// <summary>The live label's font, which the touch UI borrows; null before the menu loads.</summary>
    public static TMP_FontAsset? Font => IsAlive(_label) ? _label!.font : null;

    /// <summary>Main thread: rewrites the kept label with the current status, if it still lives.</summary>
    public static void RefreshCurrent()
    {
        if (AndroidPatchHost.Mode == AndroidPatchMode.Enabled && IsAlive(_label))
            _label!.text = Build();
    }

    private static readonly LocalizedTextSet ManualUpdateText = new(
        MainMenuVersionLabelFormatter.AndroidManualUpdateText,
        "请下载新版安装包",
        "請下載新版安裝包"
    );

    private static string Build() =>
        MainMenuVersionLabelFormatter.Build(
            Application.version,
            AndroidPatchHost.PluginVersion,
            MainMenuVersionUpdateState.Current.Status,
            L.Resolve(ManualUpdateText)
        );

    private static bool IsAlive(TextMeshProUGUI? label) => label != null && !label.WasCollected;

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
                    BppLog.WarnEvent(
                        new BppLogEvent(BppLogFeatureScope.Android, "android.version_label.hit"),
                        ("hits", hits),
                        ("label", null)
                    );
                return;
            }

            _label = label;
            var before = label.text;
            var write = AndroidPatchHost.Mode == AndroidPatchMode.Enabled;
            if (write)
                label.text = Build();
            if (hits <= LoggedHits)
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.version_label.hit"),
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
