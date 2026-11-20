#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;
using UnityEngine;

namespace BazaarPlusPlus.Game.CosmeticNames;

// A single observer avoids reading the feature switch and locale once per enabled cosmetic.
internal sealed class CosmeticNameOverlayObserver : MonoBehaviour
{
    private static readonly List<CosmeticNameOverlay> Overlays = new();
    private static CosmeticNameOverlayObserver? _instance;

    private BppSettings? _config;
    private bool _lastEnabled;
    private string? _lastLanguage;
    private BppChineseLocaleMode _lastChineseMode;

    internal void Initialize(BppSettings config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _instance = this;
        CaptureBaseline();
        enabled = Overlays.Count > 0;
    }

    internal static void Register(CosmeticNameOverlay overlay)
    {
        if (overlay.IsRegistered)
            return;
        overlay.IsRegistered = true;
        Overlays.Add(overlay);
        if (_instance == null || _instance.enabled)
            return;

        // An idle observer's baseline is stale. Resample on registration to catch changes before the next LateUpdate.
        _instance.CaptureBaseline();
        _instance.enabled = true;
    }

    internal static void Unregister(CosmeticNameOverlay overlay)
    {
        if (!overlay.IsRegistered)
            return;
        overlay.IsRegistered = false;
        Overlays.Remove(overlay);
        if (Overlays.Count == 0 && _instance != null)
            _instance.enabled = false;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    private void CaptureBaseline()
    {
        _lastEnabled = _config?.EnableCosmeticNamesConfig.Value ?? false;
        _lastLanguage = L.CurrentLanguageCode;
        _lastChineseMode = L.CurrentMode;
    }

    private void LateUpdate()
    {
        var featureEnabled = _config?.EnableCosmeticNamesConfig.Value ?? false;
        // Publicized LocalizationService.LocaleChanged has an ambiguous backing field, so observe the locale value.
        var language = L.CurrentLanguageCode;
        var chineseMode = L.CurrentMode;
        var changed =
            featureEnabled != _lastEnabled
            || !string.Equals(_lastLanguage, language, StringComparison.Ordinal)
            || _lastChineseMode != chineseMode;
        _lastEnabled = featureEnabled;
        _lastLanguage = language;
        _lastChineseMode = chineseMode;

        // Refresh only toggles or builds the overlay's child label; it never disables or
        // destroys the overlay itself, so the list does not change during this walk.
        var now = Time.unscaledTime;
        for (var i = Overlays.Count - 1; i >= 0; i--)
        {
            var overlay = Overlays[i];
            if (overlay == null)
            {
                Overlays.RemoveAt(i);
                continue;
            }
            if (changed)
                overlay.Invalidate();
            overlay.RefreshIfDue(now);
        }
    }
}
