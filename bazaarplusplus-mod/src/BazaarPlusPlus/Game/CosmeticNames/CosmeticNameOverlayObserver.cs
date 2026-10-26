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
    private bool _observed;
    private bool _lastEnabled;
    private string? _lastLanguage;
    private BppChineseLocaleMode _lastChineseMode;

    internal void Initialize(BppSettings config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _instance = this;
        enabled = Overlays.Count > 0;
    }

    internal static void Register(CosmeticNameOverlay overlay)
    {
        if (!Overlays.Contains(overlay))
            Overlays.Add(overlay);
        if (_instance != null)
            _instance.enabled = true;
    }

    internal static void Unregister(CosmeticNameOverlay overlay)
    {
        Overlays.Remove(overlay);
        if (Overlays.Count == 0 && _instance != null)
            _instance.enabled = false;
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    private void LateUpdate()
    {
        var featureEnabled = _config?.EnableCosmeticNamesConfig.Value ?? false;
        // Publicized native events have ambiguous backing fields; observe the locale value
        // just like config values instead of subscribing to LocalizationService.LocaleChanged.
        var language = L.CurrentLanguageCode;
        var chineseMode = L.CurrentMode;
        var changed =
            _observed
            && (
                featureEnabled != _lastEnabled
                || !string.Equals(_lastLanguage, language, StringComparison.Ordinal)
                || _lastChineseMode != chineseMode
            );
        _observed = true;
        _lastEnabled = featureEnabled;
        _lastLanguage = language;
        _lastChineseMode = chineseMode;

        // Refresh can destroy an overlay, which unregisters it, so walk the list backwards.
        var now = Time.unscaledTime;
        for (var i = Overlays.Count - 1; i >= 0; i--)
        {
            if (i >= Overlays.Count)
                continue;
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
