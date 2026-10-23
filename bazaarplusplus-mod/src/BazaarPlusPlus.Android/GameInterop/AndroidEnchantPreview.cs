using System.Text;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
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
/// The enchant preview on Android: what each enchantment the item can take would do, appended
/// below the tooltip's passive text. Each line renders the enchantment's own tooltip text
/// through the game's tooltip builder against a copy of the item carrying that enchantment's
/// attributes, as the game's lock-mode enchantment option builds its preview card. The section
/// is computed when the tooltip's contents render and consumed by the passive block of that same
/// render, so nothing carries over to the next card. Core's <see cref="EnchantPreviewVisibility"/>
/// decides when it shows under <c>[EnchantPreview] Mode</c>: always, never, or only while an
/// enchant pedestal is offered (<see cref="AndroidChoicePedestals"/>), and then only for the
/// enchantment(s) that pedestal applies. The lines are sized when the passive block takes them:
/// the desktop's reduced size, raised until they read at <see cref="TouchMetrics.MinBodySp"/> on
/// this screen (<see cref="TouchMetrics.ReadablePercent"/>, from the passive label's rendered
/// font size).
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
    private const long LoggedRenders = 10;
    private static List<(string Name, string Label, string Effect)>? _pending;
    private static long _rendered;
    private static long _pedestalHidden;
    private static long _pedestalShown;
    private static int _failures;

    /// <summary>The <c>[EnchantPreview] Mode</c> setting; null before the host binds it.</summary>
    public static Func<PreviewVisibilityMode>? Mode { get; set; }

    public static long Rendered => Interlocked.Read(ref _rendered);

    /// <summary>Tooltips under <c>AutoOnPedestalChoice</c> with no enchant pedestal offered.</summary>
    public static long PedestalHidden => Interlocked.Read(ref _pedestalHidden);

    /// <summary>Tooltips under <c>AutoOnPedestalChoice</c> while an enchant pedestal is offered.</summary>
    public static long PedestalShown => Interlocked.Read(ref _pedestalShown);

    /// <summary>The contents of a card tooltip start to render.</summary>
    public static void Prepare(CardTooltipData? tooltipData)
    {
        _pending = null;
        if (tooltipData == null || Mode == null)
            return;
        try
        {
            var mode = Mode();
            var readsPedestal = EnchantPreviewVisibility.ReadsPedestal(mode);
            ChoicePedestalSnapshot? pedestal = null;
            if (readsPedestal)
            {
                var outcome = AndroidChoicePedestals.Read();
                if (outcome.IsSuccess)
                    pedestal = outcome.Snapshot;
            }
            if (!EnchantPreviewVisibility.Shows(mode, pedestal))
            {
                if (readsPedestal)
                    Interlocked.Increment(ref _pedestalHidden);
                return;
            }
            if (readsPedestal)
                Interlocked.Increment(ref _pedestalShown);
            _pending = Build(tooltipData, EnchantPreviewVisibility.Restriction(mode, pedestal));
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
        var count = Interlocked.Increment(ref _rendered);
        if (count <= LoggedRenders)
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

    private static List<(string Name, string Label, string Effect)>? Build(
        CardTooltipData tooltipData,
        IReadOnlyList<string>? restriction
    )
    {
        if (Data.IsInCombat || AppState.CurrentState?.TryCast<ReplayState>() != null)
            return null;
        var card = tooltipData.CardInstance;
        var item = card?.TryCast<ItemCard>();
        var template = tooltipData.CardTemplate;
        var itemTemplate = template?.TryCast<TCardItem>();
        if (card == null || item == null || template == null || itemTemplate == null)
            return null;
        // The upgrade preview wins over the enchant preview, as on desktop.
        if (AndroidUpgradePreview.Holds(card))
            return null;
        if (!IsEligibleSection(item))
            return null;
        var enchantments = itemTemplate.Enchantments;
        if (enchantments == null || enchantments.Count == 0)
            return null;

        var current = AndroidGameIds.Unboxed(() => item.Enchantment);
        var run = Data.Run?.TryCast<IRun>();
        var lines = new List<(string Name, string Label, string Effect)>();
        foreach (var entry in enchantments)
        {
            var type = entry.Key;
            var enchantment = entry.Value;
            if (enchantment == null || (current.HasValue && current.Value == type))
                continue;
            if (!Allowed(type, restriction))
                continue;
            var effect = Render(card, template, itemTemplate, type, enchantment, run);
            if (string.IsNullOrWhiteSpace(effect))
                continue;
            lines.Add((type.ToString(), Label(type), effect!));
        }
        return lines.Count == 0 ? null : lines;
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

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item)
    {
        var section = AndroidGameIds.Unboxed(() => item.Section);
        return section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;
    }

    // The game's lock-mode preview card: the item at its tier with this enchantment applied,
    // which carries the enchantment's attributes and lets its ability tokens resolve.
    private static string? Render(
        Card card,
        ITCard template,
        TCardItem itemTemplate,
        EEnchantmentType type,
        Il2CppBazaarGameShared.Domain.Cards.Enchantments.TEnchantment enchantment,
        IRun? run
    )
    {
        var tooltips = enchantment.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return null;

        var preview = Il2CppTheBazaar.CardExtensions.CreateCopy(card);
        var previewItem = preview.TryCast<ItemCard>();
        if (previewItem == null)
            return null;
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            card.Tier
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
        var tooltipData = new CardTooltipData(preview, template, null);

        var text = new StringBuilder();
        foreach (var tooltip in tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;
            var localized = TooltipExtensions.GetLocalizedText(content);
            if (string.IsNullOrWhiteSpace(localized))
                continue;
            var rendered = tooltipData.RenderTooltip(TooltipBuilder.Create(context, localized));
            if (string.IsNullOrWhiteSpace(rendered))
                continue;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(rendered.Trim());
        }
        return text.Length == 0 ? null : text.ToString();
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
}
