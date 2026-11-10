#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.Supporters.Ui;

internal static class BPPSupporterAttributionRow
{
    public static VisualElement Create()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        row.style.marginTop = UiSpacing.Sm;
        // The same strip as BPPSupporterNativeAttributionRow: a sunk track one md control
        // high, with the sponsor button inside its right end.
        row.style.flexWrap = Wrap.NoWrap;
        row.style.overflow = Overflow.Hidden;
        UiStyle.FixedHeight(row.style, InGameSizes.ControlMd);
        row.style.paddingLeft = InGameSizes.Space12;
        row.style.paddingRight = (InGameSizes.ControlMd - InGameSizes.ControlSm) / 2f;
        KitToolkit.PaintTrack(row);
        return row;
    }

    public static void Bind(
        VisualElement row,
        IReadOnlyList<BPPSupporterSample> supporters,
        string fallbackText,
        NativeGameTypography.PanelScope typography
    )
    {
        // The Collection panel binds on every refresh with the list it sampled at open. Rebuild
        // only when an input changed; native typography availability is part of the key so the
        // panel's late-typography re-render still rebuilds the row.
        var languageCode = GetLanguageCode();
        var key = new BoundKey(
            supporters,
            fallbackText,
            languageCode,
            typography,
            NativeTagTypography.IsNativeTypographyAvailable
        );
        if (row.userData is BoundKey bound && bound.Matches(key))
            return;

        // The key is stored only once the row is complete, so a build that throws retries on the
        // next refresh.
        row.Clear();
        row.userData = null;
        var sponsorText = BPPSupporterAttributionText.FormatSponsorAction(languageCode);
        var samples = supporters
            .Where(sample =>
                sample.HasValue
                && typography.CheckExternalText(sample.Name, "supporter_attribution")
                    == NativeGameTypography.ExternalTextSupport.Supported
            )
            .Take(4)
            .ToList();
        if (samples.Count == 0)
        {
            row.Add(CreateFallbackLabel(fallbackText, typography));
            row.Add(CreateSponsorButton(sponsorText, typography));
            row.userData = key;
            return;
        }

        row.Add(
            CreatePrefixLabel(BPPSupporterAttributionText.FormatThanks(languageCode), typography)
        );
        foreach (var sample in samples)
            row.Add(CreateSupporterName(sample, typography));

        row.Add(CreateSponsorButton(sponsorText, typography));
        row.userData = key;
    }

    private sealed class BoundKey(
        IReadOnlyList<BPPSupporterSample> supporters,
        string fallbackText,
        string languageCode,
        NativeGameTypography.PanelScope typography,
        bool nativeTypographyAvailable
    )
    {
        private readonly IReadOnlyList<BPPSupporterSample> _supporters = supporters;
        private readonly string _fallbackText = fallbackText;
        private readonly string _languageCode = languageCode;
        private readonly NativeGameTypography.PanelScope _typography = typography;
        private readonly bool _nativeTypographyAvailable = nativeTypographyAvailable;

        public bool Matches(BoundKey other) =>
            ReferenceEquals(_supporters, other._supporters)
            && string.Equals(_fallbackText, other._fallbackText, StringComparison.Ordinal)
            && string.Equals(_languageCode, other._languageCode, StringComparison.Ordinal)
            && ReferenceEquals(_typography, other._typography)
            && _nativeTypographyAvailable == other._nativeTypographyAvailable;
    }

    private static Label CreatePlainLabel(string text, NativeGameTypography.PanelScope typography)
    {
        var label = new Label(text);
        typography.Apply(label);
        label.style.fontSize = Sizes.FontSmall;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.color = InGameTheme.InkMuted;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.marginRight = UiSpacing.Xs;
        // Keep the row fixed, but lower the attribution copy within it slightly.
        label.style.marginBottom = UiSpacing.None;
        return label;
    }

    private static Label CreateFallbackLabel(
        string text,
        NativeGameTypography.PanelScope typography
    )
    {
        var label = CreatePlainLabel(text, typography);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginRight = 0f;
        return label;
    }

    private static Label CreatePrefixLabel(string text, NativeGameTypography.PanelScope typography)
    {
        var label = CreatePlainLabel(text, typography);
        label.style.marginRight = UiSpacing.Sm;
        return label;
    }

    private static Label CreateSupporterName(
        BPPSupporterSample sample,
        NativeGameTypography.PanelScope typography
    )
    {
        var label = new Label(sample.Name);
        typography.Apply(label);
        label.tooltip = sample.Name;
        label.style.fontSize = Sizes.SupporterAttributionNameFont;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.maxWidth = Sizes.SupporterAttributionNameMaxWidth;
        label.style.flexShrink = 1f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.marginBottom = UiSpacing.None;
        label.style.marginRight = InGameSizes.Space8;
        label.style.color = BPPSupporterTierColors.ForRow(sample.Tier);
        return label;
    }

    // The kit's secondary sm button with the supporter heart, in the fourth tier's colour.
    private static Button CreateSponsorButton(
        string text,
        NativeGameTypography.PanelScope typography
    )
    {
        var button = new Button(OpenSupportPage);
        button.tooltip = BPPSupporterLinks.ResolveSponsorUrl(GetLanguageCode());
        button.style.height = InGameSizes.ControlSm;
        button.style.flexDirection = FlexDirection.Row;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.marginLeft = StyleKeyword.Auto;
        button.style.marginTop = button.style.marginBottom = 0f;
        button.style.marginRight = 0f;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        button.style.backgroundColor = InGameTheme.Button;
        UiStyle.HorizontalPadding(button.style, InGameSizes.Space12);
        KitToolkit.PaintButton(button);
        var heart = KitToolkit.Glyph(DesktopKitArt.IconHeart, 14f, InGameTheme.Supporter4);
        heart.style.marginRight = InGameSizes.Space6;
        button.Add(heart);
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        typography.Apply(label);
        label.style.fontSize = InGameSizes.TextMeta;
        label.style.unityFontStyleAndWeight = FontStyle.Normal;
        label.style.color = InGameTheme.Supporter4;
        UiStyle.Padding(label.style, 0f);
        label.style.marginLeft = label.style.marginRight = 0f;
        label.style.marginTop = label.style.marginBottom = 0f;
        button.Add(label);
        return button;
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
