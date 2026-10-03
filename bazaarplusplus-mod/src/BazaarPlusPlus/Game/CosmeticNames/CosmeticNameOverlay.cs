#nullable enable
using System.Globalization;
using BazaarGameShared;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CosmeticNames;

// Lives on the native item, so list rebuilds and scene changes destroy the overlay with it.
internal sealed class CosmeticNameOverlay : MonoBehaviour
{
    private const float NameFontSize = 32f;

    private static readonly LocalizedTextSet DefaultNameFormat = new(
        "Default {0}",
        "默认{0}",
        "預設{0}",
        "Standard: {0}",
        "{0} padrão",
        "기본 {0}",
        "{0} predefinito"
    );

    private BppConfig? _config;
    private BazaarSaleItem _data;
    private RectTransform? _thumbnailViewport;
    private GameObject? _overlay;
    private TextMeshProUGUI? _label;
    private bool _placeholder;
    private bool _dirty = true;
    private bool _lastEnabled;
    private string? _lastLanguage;
    private BppChineseLocaleMode _lastChineseMode;
    private float _nextAttempt;

    internal void Initialize(
        BppConfig config,
        CosmeticItem item,
        BazaarSaleItem data,
        bool placeholder
    )
    {
        _config = config;
        _data = data;
        _placeholder = placeholder;
        // Carpets render a wide atlas image inside a square clipped viewport. Anchor
        // to the loader's visible bounds, not the image's off-center atlas rectangle.
        var viewport = item._lazyLoader?.transform as RectTransform;
        if (_thumbnailViewport != viewport && _overlay != null)
        {
            Destroy(_overlay);
            _overlay = null;
            _label = null;
        }
        _thumbnailViewport = viewport;
        Invalidate();
        Refresh();
    }

    private void OnEnable()
    {
        Invalidate();
    }

    private void OnDisable()
    {
        if (_overlay != null)
            _overlay.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_overlay != null)
            Destroy(_overlay);
    }

    private void Invalidate()
    {
        _dirty = true;
        _nextAttempt = 0f;
        // Never leave the previous locale's name visible during a language switch.
        if (_overlay != null)
            _overlay.SetActive(false);
    }

    private void LateUpdate()
    {
        var enabled = _config?.EnableCosmeticNamesConfig.Value ?? false;
        // Publicized native events have ambiguous backing fields; observe the locale value
        // just like config values instead of subscribing to LocalizationService.LocaleChanged.
        if (
            enabled != _lastEnabled
            || !string.Equals(_lastLanguage, L.CurrentLanguageCode, StringComparison.Ordinal)
            || _lastChineseMode != L.CurrentMode
        )
            Invalidate();
        if (_dirty && Time.unscaledTime >= _nextAttempt)
            Refresh();
    }

    private void Refresh()
    {
        _lastEnabled = _config?.EnableCosmeticNamesConfig.Value ?? false;
        _lastLanguage = L.CurrentLanguageCode;
        _lastChineseMode = L.CurrentMode;
        _dirty = false;
        if (!_lastEnabled || _placeholder || _thumbnailViewport == null)
        {
            if (_overlay != null)
                _overlay.SetActive(false);
            return;
        }

        try
        {
            var name = ResolveName();
            if (string.IsNullOrWhiteSpace(name) || !EnsureLabel())
            {
                RetryLater();
                return;
            }

            _label!.text = LanguageCodeMatcher.IsChinese(_lastLanguage)
                ? L.ResolveChinese(name)
                : name;
            _overlay!.transform.SetAsLastSibling();
            _overlay.SetActive(true);
            RaiseOverlayByHalfCharacter();
        }
        catch (Exception ex)
        {
            RetryLater();
            BppLog.WarnEvent(CosmeticNamesLogEvents.OverlayDegraded, ex);
        }
    }

    private string? ResolveName()
    {
        var language = L.CurrentLanguageCode;
        var service = Services.Get<LocalizationService>();
        var asset = _data.AssetData as BaseAssetDataSO;
        // Some asset names are authoring identifiers (e.g. the default Mak skin is
        // "MAK 01a"). The collectible template is the official item-name source.
        var sources = new[] { TryGetTemplateName(), asset?.LocalizableName.Text, _data.Name };
        string? englishFallback = null;
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            englishFallback ??= source;
            var translated =
                service != null && service.TryGetText(source, out var name) ? name : null;
            // An unchanged result can be an English fallback or a default asset's
            // authoring identifier. Try the other official name sources first.
            if (
                !string.IsNullOrWhiteSpace(translated)
                && !string.Equals(translated, source, StringComparison.Ordinal)
            )
                return translated;
        }

        // Defaults are separate native collectibles, often with no translated item name.
        // Identify them using IsDefault rather than treating every missing translation as one.
        if (!_data.IsDefault)
            return englishFallback;
        var supportedDefaultLanguage =
            language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            || LanguageCodeMatcher.IsChinese(language)
            || LanguageCodeMatcher.IsGerman(language)
            || LanguageCodeMatcher.IsPortuguese(language)
            || LanguageCodeMatcher.IsKorean(language)
            || LanguageCodeMatcher.IsItalian(language);
        var category = supportedDefaultLanguage
            ? _data.GetCollectionShortName()
            : _data.CollectionType switch
            {
                BazaarInventoryTypes.ECollectionType.HeroSkins => "Skin",
                BazaarInventoryTypes.ECollectionType.Boards => "Board",
                BazaarInventoryTypes.ECollectionType.CardSkins => "Card Skin",
                BazaarInventoryTypes.ECollectionType.Carpets => "Rug",
                BazaarInventoryTypes.ECollectionType.CardBacks => "Card Back",
                BazaarInventoryTypes.ECollectionType.Chests => "Chest",
                BazaarInventoryTypes.ECollectionType.Album => "Album",
                BazaarInventoryTypes.ECollectionType.Bank => "Bank",
                BazaarInventoryTypes.ECollectionType.Stash => "Stash",
                _ => "Cosmetic",
            };
        return string.Format(
            CultureInfo.InvariantCulture,
            DefaultNameFormat.Resolve(language, L.CurrentMode),
            category
        );
    }

    private string? TryGetTemplateName()
    {
        if (!Guid.TryParse(_data.CollectionItemID, out var id))
            return null;
        try
        {
            return Data.GetStatic()?.GetCollectibleById(id)?.Name;
        }
        catch
        {
            // GameData can be unavailable while the native collection is being populated.
            return null;
        }
    }

    private bool EnsureLabel()
    {
        if (_label != null)
            return true;
        if (
            NativeGameTypography.PrepareOwnedText(out var typography)
                != NativeGameTypography.Outcome.Ready
            || typography == null
        )
            return false;

        _overlay = new GameObject("BPP Cosmetic Name", typeof(RectTransform));
        _overlay.SetActive(false);
        var rect = (RectTransform)_overlay.transform;
        rect.SetParent(_thumbnailViewport, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0.24f);
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, 0f);

        var background = _overlay.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.65f);
        background.raycastTarget = false;
        var layout = _overlay.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var textObject = new GameObject("Name", typeof(RectTransform));
        textObject.transform.SetParent(rect, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 2f);
        textRect.offsetMax = new Vector2(-4f, -2f);
        _label = textObject.AddComponent<TextMeshProUGUI>();
        typography.Apply(_label);
        _label.color = Color.white;
        _label.fontSize = NameFontSize;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 20f;
        _label.fontSizeMax = NameFontSize;
        // Midline centers visible glyph geometry; Center uses font line metrics, which
        // place the game's CJK fallback glyphs below the center of this shadow strip.
        _label.alignment = TextAlignmentOptions.Midline;
        _label.textWrappingMode = TextWrappingModes.Normal;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.richText = false;
        _label.raycastTarget = false;
        return true;
    }

    private void RaiseOverlayByHalfCharacter()
    {
        _label!.ForceMeshUpdate();
        var characterHeight = NameFontSize;
        var textInfo = _label.textInfo;
        for (var i = 0; i < textInfo.characterCount; i++)
        {
            var character = textInfo.characterInfo[i];
            if (character.isVisible)
                characterHeight = Mathf.Max(
                    characterHeight,
                    character.topRight.y - character.bottomLeft.y
                );
        }

        // Move the strip with the text, preserving glyph centering inside its shadow.
        var lift = characterHeight * 0.5f;
        var rect = (RectTransform)_overlay!.transform;
        rect.offsetMin = new Vector2(6f, 6f + lift);
        rect.offsetMax = new Vector2(-6f, lift);
    }

    private void RetryLater()
    {
        if (_overlay != null)
            _overlay.SetActive(false);
        _dirty = true;
        _nextAttempt = Time.unscaledTime + 1f;
    }
}
