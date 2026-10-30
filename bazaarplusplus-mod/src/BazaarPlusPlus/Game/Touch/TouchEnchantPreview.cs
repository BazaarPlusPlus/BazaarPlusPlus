#nullable enable
using System.Text.RegularExpressions;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// Native enchantment effects on a mobile platform, as the Android host's
/// <c>AndroidEnchantPreview</c>. Under <c>[EnchantPreview] Mode</c> the live tooltip lists them in
/// its main text, below the passive text: <c>Always</c> on every eligible item,
/// <c>AutoOnPedestalChoice</c> only while an enchant pedestal is offered and then only the
/// enchantment(s) it applies, <c>Off</c> never (Core's <see cref="EnchantPreviewVisibility"/>).
/// The section is built when the tooltip's contents render (<see cref="Prepare"/>) and consumed
/// by the passive block of that same render (<see cref="TakePending"/>), so nothing carries over
/// to the next card. A tooltip whose main text lists none keeps the collapsed Enchantments toggle
/// in the native description scroll (<see cref="TouchGameplayPreviews"/>), which lists each
/// enchantment's effect from the item's tier up through the game's own lock-mode preview card
/// (<see cref="TouchCardText"/>).
/// </summary>
internal static class TouchEnchantPreview
{
    private const int PrefixSizePercent = 50;
    private const int EffectSizePercent = 46;

    // The line height over the text size, as the desktop's 60% line height over its 46% text.
    private const float LineHeightOverSize = 1.3f;
    private static readonly Regex NativeLineHeight = new(
        "</?line-height[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );
    private const long LoggedInline = 10;
    private static List<(string Name, string Label, string Effect)>? _pending;
    private static long _inline;
    private static int _failures;
    private static Func<PreviewVisibilityMode>? _mode;
    private static Func<ChoicePedestalSnapshot?>? _pedestal;

    /// <summary>
    /// Binds the <c>[EnchantPreview] Mode</c> setting and the enchant pedestal read; until then
    /// the main text lists nothing.
    /// </summary>
    public static void Bind(
        Func<PreviewVisibilityMode> mode,
        Func<ChoicePedestalSnapshot?> pedestal
    )
    {
        _mode = mode;
        _pedestal = pedestal;
    }

    /// <summary>
    /// The enchantments the tooltip's main text lists under <c>[EnchantPreview] Mode</c>, in
    /// <see cref="Candidates"/> order, or none: the mode is off, no enchant pedestal is offered
    /// under the automatic mode, or the item takes no other enchantment.
    /// </summary>
    public static IReadOnlyList<EEnchantmentType> InlineCandidates(CardTooltipData tooltip)
    {
        if (_mode == null)
            return Array.Empty<EEnchantmentType>();
        var mode = _mode();
        var pedestal = EnchantPreviewVisibility.ReadsPedestal(mode) ? _pedestal?.Invoke() : null;
        if (!EnchantPreviewVisibility.Shows(mode, pedestal))
            return Array.Empty<EEnchantmentType>();
        var restriction = EnchantPreviewVisibility.Restriction(mode, pedestal);
        return Candidates(tooltip).Where(type => Allowed(type, restriction)).ToArray();
    }

    /// <summary>The contents of a card tooltip start to render: build its main-text section.</summary>
    public static void Prepare(CardTooltipData? tooltip)
    {
        _pending = null;
        if (tooltip?.CardInstance == null)
            return;
        try
        {
            // The upgrade preview wins over the enchant preview, as on the desktop.
            if (
                tooltip.CardInstance is ItemCard upgradable
                && upgradable.CanCardUpgrade()
                && TouchUpgradePreview.Holds(upgradable)
            )
                return;
            var candidates = InlineCandidates(tooltip);
            if (candidates.Count == 0)
                return;
            var lines = new List<(string Name, string Label, string Effect)>();
            foreach (var type in candidates)
            {
                var effect = TouchCardText.RenderEffect(tooltip, type);
                if (!string.IsNullOrWhiteSpace(effect))
                    lines.Add((type.ToString(), Label(type), effect!.Replace('\n', ' ')));
            }
            _pending = lines.Count == 0 ? null : lines;
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref _failures) <= 5)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Touch,
                        "touch.enchant_preview.degraded",
                        storm: ["reason_code"]
                    ),
                    exception,
                    ("reason_code", "build_exception")
                );
        }
    }

    /// <summary>
    /// The passive block of the rendering tooltip: the section, consumed once, sized for a label
    /// whose font renders <paramref name="fontPixels"/> screen pixels high (zero when unknown).
    /// The lines keep the desktop's reduced size only while that still reads at
    /// <see cref="TouchMetrics.MinBodySp"/> (<see cref="TouchMetrics.ReadablePercent"/>).
    /// </summary>
    public static string? TakePending(float fontPixels)
    {
        var lines = _pending;
        _pending = null;
        if (lines == null)
            return null;
        var dpi = UnityEngine.Screen.dpi;
        var effect = TouchMetrics.ReadablePercent(EffectSizePercent, fontPixels, dpi);
        var prefix = Math.Max(PrefixSizePercent, effect);
        // A short blank first line keeps the list off the block's top divider.
        var section =
            "<size=40%> </size>\n"
            + string.Join("\n", lines.Select(line => Line(line, prefix, effect)));
        var count = Interlocked.Increment(ref _inline);
        if (count <= LoggedInline)
        {
            var pixelsPerSp = TouchMetrics.EffectiveDpi(dpi) / TouchMetrics.BaselineDpi;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.enchant_preview.rendered"),
                ("count", count),
                ("lines", lines.Count),
                ("length", section.Length),
                ("font_px", Math.Round(fontPixels, 1)),
                ("effect_percent", effect),
                ("effect_sp", Math.Round(fontPixels * effect / 100f / pixelsPerSp, 1))
            );
        }
        return section;
    }

    /// <summary>The render finished, whether or not the passive block consumed the section.</summary>
    public static void Clear() => _pending = null;

    public static IReadOnlyList<EEnchantmentType> Candidates(CardTooltipData tooltip)
    {
        if (Data.IsInCombat || AppState.CurrentState is ReplayState)
            return Array.Empty<EEnchantmentType>();
        if (
            tooltip.CardInstance is not ItemCard item
            || tooltip.CardTemplate is not TCardItem template
            || template.Enchantments == null
            || !IsEligibleSection(item)
        )
            return Array.Empty<EEnchantmentType>();
        var current = item.Enchantment;
        var result = new List<EEnchantmentType>();
        foreach (var entry in template.Enchantments)
            if (entry.Value != null && (!current.HasValue || entry.Key != current.Value))
                result.Add(entry.Key);
        return result.OrderBy(type => (int)type).ToArray();
    }

    public static IReadOnlyList<(string Label, string Text)> Build(
        CardTooltipData tooltip,
        IReadOnlyList<EEnchantmentType> candidates
    )
    {
        var lines = new List<(string, string)>();
        foreach (var type in candidates)
        {
            var effects = new List<CollectionTierTooltipText>();
            var current = tooltip.CardInstance.Tier;
            var last = current == ETier.Legendary ? current : ETier.Diamond;
            for (var tier = current; tier <= last; tier++)
            {
                var effect = TouchCardText.RenderEffect(tooltip, type, tier);
                if (
                    !string.IsNullOrWhiteSpace(effect)
                    && DesktopCardFacets.Tier.TryToFacet(tier, out var facet)
                )
                    effects.Add(new CollectionTierTooltipText(facet, effect!));
            }
            if (effects.Count > 0)
                lines.Add(
                    (
                        $"<color=#{ItemEnchantPreviewText.ColorHex(type.ToString())}>{Label(type)}</color>",
                        CollectionTierTooltipTextMerger.MergeLines(
                            effects,
                            keepValuesTogether: true
                        )
                    )
                );
        }
        return lines;
    }

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item) =>
        item.Section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;

    // The desktop's ItemEnchantPreviewCandidateSelector: a pedestal that names its enchantment(s)
    // keeps only those; an empty or absent restriction keeps every one.
    private static bool Allowed(EEnchantmentType type, IReadOnlyList<string>? restriction)
    {
        if (restriction == null || restriction.Count == 0)
            return true;
        var name = type.ToString();
        foreach (var allowed in restriction)
        {
            if (string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Line(
        (string Name, string Label, string Effect) line,
        int prefixPercent,
        int effectPercent
    )
    {
        // The game's rendered text sets its own line height for full-size text; the preview's
        // smaller lines replace it so that the whole list fits a phone-sized tooltip.
        var scaled = ItemEnchantPreviewText.ScaleInlineSizes(
            NativeLineHeight.Replace(line.Effect, ""),
            effectPercent / 100f
        );
        var lineHeight = (int)Math.Round(effectPercent * LineHeightOverSize);
        return $"<line-height={lineHeight}%><size={prefixPercent}%><color=#{ItemEnchantPreviewText.ColorHex(line.Name)}>{line.Label}</color>: </size>"
            + $"<size={effectPercent}%>{scaled}</size></line-height>";
    }

    private static string Label(EEnchantmentType type)
    {
        var name = type.ToString();
        try
        {
            return new TheBazaar.Utilities.LocalizableText(name).GetLocalizedText();
        }
        catch
        {
            return name;
        }
    }
}
