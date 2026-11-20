#nullable enable
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Localization;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

// Main thread only.
internal static class NativeTagTypography
{
    private static readonly LocalizedTextSet ReferenceSuffixText = new(" Related", "相关", "相關");

    private static readonly LocalizedTextSet EconomyReferenceBaseText = new(
        "Economy",
        "经济",
        "經濟"
    );

    // Cache the method only. A locale change replaces the TooltipTypography instance.
    private static readonly MethodInfo? GetConfigurationMethod = AccessTools.Method(
        typeof(TooltipTypography),
        "GetConfiguration",
        new[] { typeof(string) }
    );

    // The attribute-keyed GetConfiguration overload searches each configuration's AttributeKeys,
    // which is the exact mapping the game uses to style attribute values — the string overload
    // only matches KeywordKey, so attribute enum names would miss most entries.
    private static readonly MethodInfo? GetAttributeConfigurationMethod = AccessTools.Method(
        typeof(TooltipTypography),
        "GetConfiguration",
        new[] { typeof(ECardAttributeType) }
    );

    // Avoid caching before async typography registration completes. Taiwan mode keeps
    // native zh-CN labels because character conversion can change game terminology.
    private static readonly Dictionary<string, NativeTagDisplay> Cache = new(
        StringComparer.Ordinal
    );
    private static TooltipTypography? _cachedTypography;
    private static string _cachedLanguageCode = string.Empty;

    private static bool _configurationPathBroken;
    private static NativeTagTypographyFailure? _pendingFailure;

    // Separate broken switch for the attribute-keyed overload: attribute icons are decorative,
    // so a renamed overload degrades that path silently without disabling keyword-string
    // resolution (and vice versa).
    private static bool _attributeConfigurationPathBroken;

    // Consumers can poll after startup or a locale change to replace string-table fallbacks
    // once the native typography has registered.
    public static bool IsNativeTypographyAvailable => Data.TooltipTypography != null;

    public static NativeTagDisplay Resolve(ECardTag tag) => Resolve(tag.ToString());

    // A card facet's name is the game member's name (ADR-0015).
    public static NativeTagDisplay Resolve(CardTag tag) => Resolve(tag.ToString());

    public static NativeTagDisplay Resolve(CardHiddenTag tag) =>
        DesktopCardFacets.HiddenTag.TryToSource(tag, out var gameTag)
            ? Resolve(gameTag)
            : Resolve(tag.ToString());

    public static NativeTagDisplay Resolve(EHiddenTag tag)
    {
        if (TryResolveReferenceTag(tag, out var referenceDisplay))
            return referenceDisplay;

        var display = Resolve(
            tag.ToString(),
            GetConfigurationAliasKey(tag),
            ShouldOverrideConfigurationStyle(tag)
        );
        return TryOverrideHiddenTagLabel(tag, display, out var overridden) ? overridden : display;
    }

    public static NativeTagDisplay Resolve(string key) =>
        Resolve(key, aliasKey: null, overrideConfigurationStyle: false);

    public static NativeTagDisplay Resolve(ECardAttributeType attributeType)
    {
        var typography = Data.TooltipTypography;
        var languageCode = L.CurrentLanguageCode;
        var cacheKey = "attr:" + attributeType;

        if (typography == null)
            return ResolveAttributeUncached(null, attributeType);

        if (
            !ReferenceEquals(typography, _cachedTypography)
            || !string.Equals(languageCode, _cachedLanguageCode, StringComparison.Ordinal)
        )
        {
            Cache.Clear();
            _cachedTypography = typography;
            _cachedLanguageCode = languageCode;
        }

        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var display = ResolveAttributeUncached(typography, attributeType);
        Cache[cacheKey] = display;
        return display;
    }

    private static NativeTagDisplay ResolveAttributeUncached(
        TooltipTypography? typography,
        ECardAttributeType attributeType
    )
    {
        var configuration = GetAttributeConfigurationOrNull(typography, attributeType);
        if (configuration == null)
            return new NativeTagDisplay(
                LocalizeThroughStringTable(attributeType.ToString()),
                accentColor: null,
                string.Empty
            );

        var label = LocalizeConfiguredText(configuration, attributeType.ToString());
        if (configuration.MakeAllUppercase)
            label = label.ToUpperInvariant();
        return new NativeTagDisplay(
            label,
            configuration.Color,
            configuration.IconName ?? string.Empty
        );
    }

    private static KeywordIconColorConfiguration? GetAttributeConfigurationOrNull(
        TooltipTypography? typography,
        ECardAttributeType attributeType
    )
    {
        if (
            typography == null
            || _attributeConfigurationPathBroken
            || GetAttributeConfigurationMethod == null
        )
        {
            _attributeConfigurationPathBroken |= GetAttributeConfigurationMethod == null;
            return null;
        }

        try
        {
            return GetAttributeConfigurationMethod.Invoke(
                    typography,
                    new object[] { attributeType }
                ) as KeywordIconColorConfiguration;
        }
        catch (Exception)
        {
            _attributeConfigurationPathBroken = true;
            return null;
        }
    }

    // A sprite tag's icon name resolves through the keyword configuration that draws it.
    public static string? IconLabel(string iconName)
    {
        var typography = Data.TooltipTypography;
        if (typography == null || string.IsNullOrEmpty(iconName))
            return null;
        try
        {
            return ConfiguredIconLabel(typography, iconName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Its own method so a game build that renamed the table fails here, inside the guard.
    private static string? ConfiguredIconLabel(TooltipTypography typography, string iconName)
    {
        foreach (var configuration in typography._keywordIconColorDictionary.Values)
        {
            if (string.Equals(configuration.IconName, iconName, StringComparison.OrdinalIgnoreCase))
                return LocalizeConfiguredText(configuration, iconName);
        }
        return null;
    }

    internal static bool TryTakeFailure(out NativeTagTypographyFailure failure)
    {
        failure = _pendingFailure!;
        _pendingFailure = null;
        return failure != null;
    }

    private static NativeTagDisplay Resolve(
        string key,
        string? aliasKey,
        bool overrideConfigurationStyle
    )
    {
        var typography = Data.TooltipTypography;
        var languageCode = L.CurrentLanguageCode;

        // Skip caching until the game's async typography registration completes.
        if (typography == null)
            return ResolveUncached(null, key, aliasKey, overrideConfigurationStyle);

        if (
            !ReferenceEquals(typography, _cachedTypography)
            || !string.Equals(languageCode, _cachedLanguageCode, StringComparison.Ordinal)
        )
        {
            Cache.Clear();
            _cachedTypography = typography;
            _cachedLanguageCode = languageCode;
        }

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var display = ResolveUncached(typography, key, aliasKey, overrideConfigurationStyle);
        Cache[key] = display;
        return display;
    }

    private static NativeTagDisplay ResolveUncached(
        TooltipTypography? typography,
        string key,
        string? aliasKey,
        bool overrideConfigurationStyle
    )
    {
        string label;
        Color? accentColor = null;
        var iconName = string.Empty;

        var configuration = GetConfigurationOrNull(typography, key);
        var styleConfiguration = configuration;
        if (
            (configuration == null || overrideConfigurationStyle)
            && !string.IsNullOrWhiteSpace(aliasKey)
        )
        {
            styleConfiguration = GetConfigurationOrNull(typography, aliasKey) ?? configuration;
        }

        if (styleConfiguration != null)
        {
            label =
                configuration != null
                    ? LocalizeConfiguredText(configuration, key)
                    : LocalizeThroughStringTable(key);
            if ((configuration ?? styleConfiguration).MakeAllUppercase)
                label = label.ToUpperInvariant();
            accentColor = styleConfiguration.Color;
            iconName = styleConfiguration.IconName ?? string.Empty;
        }
        else
        {
            // No keyword configuration (legal state: the native tooltip hides such tags, but a
            // filter option must stay visible) — fall back to the game string table.
            label = LocalizeThroughStringTable(key);
        }

        return new NativeTagDisplay(label, accentColor, iconName);
    }

    private static string? GetConfigurationAliasKey(EHiddenTag tag) =>
        tag switch
        {
            EHiddenTag.Crit => ECardAttributeType.CritChance.ToString(),
            EHiddenTag.Gold => EHiddenTag.Income.ToString(),
            EHiddenTag.EconomyReference => EHiddenTag.Income.ToString(),
            _ => null,
        };

    private static bool ShouldOverrideConfigurationStyle(EHiddenTag tag) => tag == EHiddenTag.Gold;

    private static bool TryResolveReferenceTag(
        EHiddenTag tag,
        out NativeTagDisplay referenceDisplay
    )
    {
        referenceDisplay = default;
        if (
            !DesktopCardFacets.HiddenTag.TryToFacet(tag, out var facet)
            || !CardReferenceTags.TryResolveBase(facet, out var referenceBase)
        )
            return false;

        NativeTagDisplay baseDisplay;
        if (
            referenceBase.HiddenTag is { } baseHiddenTag
            && DesktopCardFacets.HiddenTag.TryToSource(baseHiddenTag, out var gameHiddenTag)
        )
            baseDisplay = Resolve(gameHiddenTag);
        else if (
            referenceBase.CardTag is { } baseCardTag
            && DesktopCardFacets.Tag.TryToSource(baseCardTag, out var gameCardTag)
        )
            baseDisplay = Resolve(gameCardTag);
        else
            return false;

        var label = ReferenceLabel(tag, baseDisplay.Label);
        referenceDisplay = new NativeTagDisplay(
            label,
            baseDisplay.AccentColor,
            baseDisplay.IconName
        );
        return true;
    }

    private static string ReferenceLabel(EHiddenTag tag, string baseLabel)
    {
        if (tag == EHiddenTag.EconomyReference)
            baseLabel = L.Resolve(EconomyReferenceBaseText);
        return baseLabel + L.Resolve(ReferenceSuffixText);
    }

    private static bool TryOverrideHiddenTagLabel(
        EHiddenTag tag,
        NativeTagDisplay display,
        out NativeTagDisplay overridden
    )
    {
        overridden = display;
        if (tag != EHiddenTag.Quest)
            return false;

        overridden = new NativeTagDisplay(
            NativeTagLabelText.Quest(display.Label),
            display.AccentColor,
            display.IconName
        );
        return true;
    }

    private static KeywordIconColorConfiguration? GetConfigurationOrNull(
        TooltipTypography? typography,
        string key
    )
    {
        if (typography == null || _configurationPathBroken)
            return null;

        if (GetConfigurationMethod == null)
        {
            ReportConfigurationPathBroken(
                NativeTagTypographyFailureReason.ConfigurationMethodUnavailable,
                exception: null
            );
            return null;
        }

        try
        {
            return GetConfigurationMethod.Invoke(typography, new object[] { key })
                as KeywordIconColorConfiguration;
        }
        catch (Exception ex)
        {
            ReportConfigurationPathBroken(
                NativeTagTypographyFailureReason.ConfigurationInvocationException,
                ex
            );
            return null;
        }
    }

    private static void ReportConfigurationPathBroken(
        NativeTagTypographyFailureReason reason,
        Exception? exception
    )
    {
        _configurationPathBroken = true;
        _pendingFailure = new NativeTagTypographyFailure(reason, exception);
    }

    private static string LocalizeConfiguredText(
        KeywordIconColorConfiguration configuration,
        string key
    )
    {
        try
        {
            var text = configuration.Text.GetLocalizedText();
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch
        {
            return key;
        }
    }

    // The game string table keyed by the English source text; a miss returns the key itself
    // (LocalizableText's own fallback), matching the game's behavior for untranslated entries.
    private static string LocalizeThroughStringTable(string key)
    {
        try
        {
            var text = new LocalizableText(key).GetLocalizedText();
            return string.IsNullOrEmpty(text) ? key : text;
        }
        catch
        {
            return key;
        }
    }
}

internal enum NativeTagTypographyFailureReason
{
    ConfigurationMethodUnavailable,
    ConfigurationInvocationException,
}

internal sealed class NativeTagTypographyFailure
{
    internal NativeTagTypographyFailure(
        NativeTagTypographyFailureReason reason,
        Exception? exception
    )
    {
        Reason = reason;
        Exception = exception;
    }

    internal NativeTagTypographyFailureReason Reason { get; }
    internal Exception? Exception { get; }
}
