using System.Text;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameClient.Domain.Tooltips;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Runs;
using Il2CppBazaarGameShared.Domain.Values;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Native enchantment effects shared by the live tooltip and Collection. Under
/// <c>[EnchantPreview] Mode</c> the live tooltip lists them in its main text, below the passive
/// text, as the desktop does: <c>Always</c> on every eligible item, <c>AutoOnPedestalChoice</c>
/// only while an enchant pedestal is offered (<see cref="AndroidChoicePedestals"/>) and then only
/// the enchantment(s) it applies, <c>Off</c> never (Core's <see cref="EnchantPreviewVisibility"/>).
/// The section is built when the tooltip's contents render (<see cref="Prepare"/>) and consumed
/// by the passive block of that same render (<see cref="TakePending"/>), so nothing carries over to
/// the next card. A tooltip whose main text lists none keeps the collapsed switch in the native
/// description scroll (the Mobile UI's <c>Previews/TouchPreviewSwitches</c>), which renders every tier.
/// </summary>
internal static class AndroidEnchantPreview
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

    /// <summary>The <c>[EnchantPreview] Mode</c> setting; null before the host binds it.</summary>
    public static Func<PreviewVisibilityMode>? Mode { get; set; }

    public static long Rendered { get; private set; }

    public static long Inline => Interlocked.Read(ref _inline);

    /// <summary>
    /// The enchantments the tooltip's main text lists under <c>[EnchantPreview] Mode</c>, in
    /// <see cref="Candidates"/> order, or none: the mode is off, no enchant pedestal is offered
    /// under the automatic mode, or the item takes no other enchantment.
    /// </summary>
    public static IReadOnlyList<EEnchantmentType> InlineCandidates(CardTooltipData tooltip)
    {
        if (Mode == null)
            return [];
        var mode = Mode();
        ChoicePedestalSnapshot? pedestal = null;
        if (EnchantPreviewVisibility.ReadsPedestal(mode))
        {
            var outcome = AndroidChoicePedestals.Read();
            if (outcome.IsSuccess)
                pedestal = outcome.Snapshot;
        }
        if (!EnchantPreviewVisibility.Shows(mode, pedestal))
            return [];
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
            if (AndroidUpgradePreview.Holds(tooltip.CardInstance))
                return;
            var candidates = InlineCandidates(tooltip);
            if (candidates.Count == 0)
                return;
            var run = Data.Run?.TryCast<IRun>();
            var lines = new List<(string Name, string Label, string Effect)>();
            foreach (var type in candidates)
            {
                var effect = RenderEffect(tooltip, type, run);
                if (!string.IsNullOrWhiteSpace(effect))
                    lines.Add((type.ToString(), Label(type), effect.Replace('\n', ' ')));
            }
            _pending = lines.Count == 0 ? null : lines;
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref _failures) <= 5)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Android,
                        "android.enchant_preview.degraded",
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
                new BppLogEvent(BppLogFeatureScope.Android, "android.enchant_preview.rendered"),
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
        if (Data.IsInCombat || AppState.CurrentState?.TryCast<ReplayState>() != null)
            return [];
        var item = tooltip.CardInstance?.TryCast<ItemCard>();
        var template = tooltip.CardTemplate?.TryCast<TCardItem>();
        if (item == null || template?.Enchantments == null || !IsEligibleSection(item))
            return [];
        var current = AndroidGameIds.Unboxed(() => item.Enchantment);
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
                var effect = RenderEffect(tooltip, type, Data.Run?.TryCast<IRun>(), tier);
                if (
                    !string.IsNullOrWhiteSpace(effect)
                    && AndroidCardFacets.Tier.TryToFacet(tier, out var facet)
                )
                    effects.Add(new CollectionTierTooltipText(facet, effect));
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
        Rendered++;
        return lines;
    }

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item)
    {
        var section = AndroidGameIds.Unboxed(() => item.Section);
        return section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;
    }

    // The game's lock-mode preview card: the item at its tier with this enchantment applied,
    // which carries the enchantment's attributes and lets its ability tokens resolve.
    internal static string? RenderEffect(
        CardTooltipData tooltipData,
        EEnchantmentType type,
        IRun? run,
        ETier? tier = null
    )
    {
        var card = tooltipData.CardInstance;
        var template = tooltipData.CardTemplate;
        var itemTemplate = template?.TryCast<TCardItem>();
        if (
            card == null
            || template == null
            || itemTemplate?.Enchantments == null
            || !itemTemplate.Enchantments.ContainsKey(type)
        )
            return null;
        var tooltips = itemTemplate.Enchantments[type]?.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return null;

        var preview = Il2CppTheBazaar.CardExtensions.CreateCopy(card);
        var previewItem = preview.TryCast<ItemCard>();
        if (previewItem == null)
            return null;
        preview.Tier = tier ?? card.Tier;
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            preview.Tier
        );
        Il2CppTheBazaar.CardExtensions.ApplyPreviewEnchantment(
            itemTemplate,
            previewItem,
            attributes,
            new Il2CppSystem.Nullable<EEnchantmentType>(type)
        );
        preview.Attributes = attributes;
        var context = new TooltipContext(
            preview,
            template,
            new ValueContext(run!, preview.TryCast<ICard>())
        );
        var previewTooltip = new CardTooltipData(preview, template, null);

        var text = new StringBuilder();
        foreach (var tooltip in tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;
            var localized = TooltipExtensions.GetLocalizedText(content);
            if (string.IsNullOrWhiteSpace(localized))
                continue;
            var rendered = previewTooltip.RenderTooltip(TooltipBuilder.Create(context, localized));
            if (string.IsNullOrWhiteSpace(rendered))
                continue;
            if (text.Length > 0)
                text.Append('\n');
            text.Append(rendered.Trim());
        }
        return text.Length == 0 ? null : text.ToString();
    }

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
            return new LocalizableText(name).GetLocalizedText();
        }
        catch
        {
            return name;
        }
    }
}
