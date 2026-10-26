#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Localization;
using UnityEngine;

namespace BazaarPlusPlus.Game.CosmeticNames;

// One plugin-lifetime driver for every enabled CosmeticNameOverlay: it reads the feature switch
// and the locale once per frame instead of once per collection item, and it is disabled (no
// LateUpdate) while no overlay is enabled.
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

    // OnEnable and OnDisable/OnDestroy pair up, so the overlay's own flag keeps each one listed
    // once without searching the list.
    internal static void Register(CosmeticNameOverlay overlay)
    {
        if (overlay.IsRegistered)
            return;
        overlay.IsRegistered = true;
        Overlays.Add(overlay);
        if (_instance == null || _instance.enabled)
            return;

        // An idle observer's baseline is stale. Overlays refresh with the current values as they
        // register, so take the baseline now: a change before the next LateUpdate is then seen.
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
        // Publicized native events have ambiguous backing fields; observe the locale value
        // just like config values instead of subscribing to LocalizationService.LocaleChanged.
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
