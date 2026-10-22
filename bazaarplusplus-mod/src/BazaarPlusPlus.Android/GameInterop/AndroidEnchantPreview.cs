using System.Text;
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
/// render, so nothing carries over to the next card.
/// </summary>
internal static class AndroidEnchantPreview
{
    private const int PrefixSizePercent = 60;
    private const int EffectSizePercent = 55;
    private const long LoggedRenders = 10;
    private static string? _pending;
    private static long _rendered;
    private static int _failures;

    /// <summary>Whether the section is wanted at all; null before the host binds it.</summary>
    public static Func<bool>? IsEnabled { get; set; }

    public static long Rendered => Interlocked.Read(ref _rendered);

    /// <summary>The contents of a card tooltip start to render.</summary>
    public static void Prepare(CardTooltipData? tooltipData)
    {
        _pending = null;
        if (tooltipData == null || IsEnabled?.Invoke() != true)
            return;
        try
        {
            _pending = Build(tooltipData);
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

    /// <summary>The passive block of the rendering tooltip: the section, consumed once.</summary>
    public static string? TakePending()
    {
        var section = _pending;
        _pending = null;
        if (section != null)
        {
            var count = Interlocked.Increment(ref _rendered);
            if (count <= LoggedRenders)
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.enchant_preview.rendered"),
                    ("count", count),
                    ("length", section.Length)
                );
        }
        return section;
    }

    /// <summary>The render finished, whether or not the passive block consumed the section.</summary>
    public static void Clear() => _pending = null;

    private static string? Build(CardTooltipData tooltipData)
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
        var lines = new List<string>();
        foreach (var entry in enchantments)
        {
            var type = entry.Key;
            var enchantment = entry.Value;
            if (enchantment == null || (current.HasValue && current.Value == type))
                continue;
            var effect = Render(card, template, enchantment, run);
            if (string.IsNullOrWhiteSpace(effect))
                continue;
            lines.Add(Line(type, effect!));
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    // Hand and stash items, or an item on the opponent's board, as on desktop.
    private static bool IsEligibleSection(ItemCard item)
    {
        var section = AndroidGameIds.Unboxed(() => item.Section);
        return section is EInventorySection.Hand or EInventorySection.Stash;
    }

    private static string? Render(
        Card card,
        ITCard template,
        Il2CppBazaarGameShared.Domain.Cards.Enchantments.TEnchantment enchantment,
        IRun? run
    )
    {
        var tooltips = enchantment.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return null;

        var preview = Il2CppTheBazaar.CardExtensions.CreateCopy(card);
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            card.Tier
        );
        if (enchantment.Attributes != null)
        {
            foreach (var attribute in enchantment.Attributes)
                attributes[attribute.Key] = attribute.Value;
        }
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

    private static string Line(EEnchantmentType type, string effect)
    {
        var name = type.ToString();
        string label;
        try
        {
            label = new LocalizableText(name).GetLocalizedText();
        }
        catch
        {
            label = name;
        }
        return $"<size={PrefixSizePercent}%><color=#{ItemEnchantPreviewPalette.ColorHex(name)}>{label}</color>: </size>"
            + $"<size={EffectSizePercent}%>{effect}</size>";
    }
}
