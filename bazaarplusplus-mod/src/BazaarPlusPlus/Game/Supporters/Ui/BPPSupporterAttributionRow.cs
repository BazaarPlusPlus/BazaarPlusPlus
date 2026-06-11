#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.Supporters.Ui;

internal static class BPPSupporterAttributionRow
{
    private const string SponsorIcon = "♥";

    public static VisualElement Create()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.marginTop = UiSpacing.Sm;
        UiStyle.FixedHeight(row.style, Sizes.SupporterAttributionReservedHeight);
        return row;
    }

    public static void Bind(
        VisualElement row,
        IReadOnlyList<BPPSupporterSample> supporters,
        string fallbackText
    )
    {
        row.Clear();

        var languageCode = GetLanguageCode();
        var sponsorText = BPPSupporterAttributionText.FormatSponsorAction(languageCode);
        var samples = supporters.Where(sample => sample.HasValue).Take(4).ToList();
        if (samples.Count == 0)
        {
            row.Add(CreateFallbackLabel(fallbackText));
            row.Add(CreateSponsorButton(sponsorText));
            WarmFont(fallbackText + sponsorText + SponsorIcon);
            return;
        }

        var prefix = BPPSupporterAttributionText.FormatSupportedByPrefix(languageCode);
        var suffix = BPPSupporterAttributionText.FormatSupportedBySuffix(languageCode);
        WarmFont(
            prefix
                + suffix
                + sponsorText
                + SponsorIcon
                + string.Concat(samples.Select(sample => sample.Name))
                + "·"
        );

        row.Add(CreatePrefixLabel(prefix));
        for (var index = 0; index < samples.Count; index++)
        {
            if (index > 0)
                row.Add(CreateSeparatorLabel());

            row.Add(CreateSupporterName(samples[index]));
        }

        if (!string.IsNullOrWhiteSpace(suffix))
            row.Add(CreateSuffixLabel(suffix));

        row.Add(CreateSponsorButton(sponsorText));
    }

    private static Label CreatePlainLabel(string text)
    {
        var label = new Label(text);
        label.style.fontSize = Sizes.FontSmall;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.color = Colors.HistorySubtitleText;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginRight = UiSpacing.Xs;
        label.style.marginBottom = UiSpacing.Xs;
        return label;
    }

    private static Label CreateFallbackLabel(string text)
    {
        var label = CreatePlainLabel(text);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginRight = 0f;
        return label;
    }

    private static Label CreatePrefixLabel(string text)
    {
        var label = CreatePlainLabel(text);
        label.style.marginRight = UiSpacing.Sm;
        return label;
    }

    private static Label CreateSuffixLabel(string text)
    {
        var label = CreatePlainLabel(text);
        label.style.marginLeft = UiSpacing.Sm;
        label.style.marginRight = 0f;
        return label;
    }

    private static Label CreateSeparatorLabel()
    {
        var label = CreatePlainLabel("·");
        label.style.color = Colors.WithAlpha(Colors.HistorySubtitleText, 0.56f);
        label.style.marginLeft = UiSpacing.Xs;
        label.style.marginRight = UiSpacing.Xs;
        return label;
    }

    private static Label CreateSupporterName(BPPSupporterSample sample)
    {
        var label = new Label(sample.Name);
        label.tooltip = sample.Name;
        label.style.fontSize = Sizes.SupporterAttributionNameFont;
        label.style.unityFont = BppUiFont.Default;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.maxWidth = Sizes.SupporterAttributionNameMaxWidth;
        label.style.flexShrink = 1f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.marginBottom = UiSpacing.Xs;
        label.style.color = ResolveTierText(sample.Tier);
        return label;
    }

    private static Color ResolveTierText(int tier)
    {
        return tier switch
        {
            >= 4 => Colors.SupporterTier4Text,
            3 => Colors.SupporterTier3Text,
            2 => Colors.SupporterTier2Text,
            _ => Colors.SupporterTier1Text,
        };
    }

    private static Button CreateSponsorButton(string text)
    {
        var button = new Button(OpenSupportPage) { text = $"{SponsorIcon} {text}" };
        button.tooltip = BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode());
        button.style.height = Sizes.SupporterAttributionHeight;
        button.style.minWidth = Sizes.SupporterActionMinWidth;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.marginLeft = UiSpacing.Sm;
        button.style.marginBottom = UiSpacing.Xs;
        button.style.fontSize = Sizes.FontSmall;
        button.style.unityFont = BppUiFont.Default;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.backgroundColor = Colors.WithAlpha(Colors.ButtonSelectedBackground, 0.16f);
        button.style.color = Colors.SupporterTier4Text;
        UiStyle.HorizontalPadding(button.style, UiSpacing.Sm);
        UiStyle.Radius(button.style, Radii.Status);
        UiStyle.Border(
            button.style,
            Borders.Thin,
            Colors.WithAlpha(Colors.OutcomeGoldBorder, 0.58f)
        );
        return button;
    }

    private static void WarmFont(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        BppUiFont.RequestCharactersInTexture(text, Sizes.FontSmall, FontStyle.Normal);
        BppUiFont.RequestCharactersInTexture(text, Sizes.FontSmall, FontStyle.Bold);
        BppUiFont.RequestCharactersInTexture(
            text,
            Sizes.SupporterAttributionNameFont,
            FontStyle.Bold
        );
    }

    private static void OpenSupportPage()
    {
        Application.OpenURL(BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode()));
    }

    private static string GetLanguageCode()
    {
        try
        {
            return PlayerPreferences.Data.LanguageCode ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
