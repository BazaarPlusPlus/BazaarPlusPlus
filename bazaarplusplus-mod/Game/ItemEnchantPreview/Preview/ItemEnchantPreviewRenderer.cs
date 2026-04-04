using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Tooltips;
using BazaarGameShared.Domain.Values;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.ItemEnchantPreview.Preview;

public static class ItemEnchantPreviewRenderer
{
    private static readonly MethodInfo CardTooltipRenderMethod = typeof(CardTooltipData).GetMethod(
        "RenderTooltip",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        new[] { typeof(TooltipBuilder) },
        null
    );

    public static List<TooltipSegment> Render(ItemCard previewCard, TEnchantment enchantment)
    {
        var segments = new List<TooltipSegment>();
        if (
            enchantment.Localization?.Tooltips == null
            || enchantment.Localization.Tooltips.Count == 0
        )
            return segments;

        foreach (var tooltip in enchantment.Localization.Tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;

            var renderedText = RenderTooltipText(previewCard, content);
            if (string.IsNullOrWhiteSpace(renderedText))
                continue;

            segments.Add(
                ItemEnchantPreviewFormatting.CreateSegment(
                    previewCard.Enchantment ?? EEnchantmentType.Heavy,
                    renderedText
                )
            );
        }

        return segments;
    }

    private static string RenderTooltipText(ItemCard previewCard, TLocalizableText content)
    {
        var localized = GetLocalizedText(content);
        if (string.IsNullOrWhiteSpace(localized))
            return string.Empty;

        try
        {
            return RenderWithCardTooltipData(previewCard, localized).TrimEnd();
        }
        catch
        {
            try
            {
                var builder = TooltipBuilder.Create(
                    new TooltipContext
                    {
                        Instance = previewCard,
                        Template = previewCard.Template!,
                        ValueContext = new ValueContext(Data.Run, previewCard),
                    },
                    localized
                );

                return RenderTooltipBuilder(builder).TrimEnd();
            }
            catch
            {
                return localized;
            }
        }
    }

    private static string RenderWithCardTooltipData(ItemCard previewCard, string localized)
    {
        var builder = TooltipBuilder.Create(
            new TooltipContext
            {
                Instance = previewCard,
                Template = previewCard.Template!,
                ValueContext = new ValueContext(Data.Run, previewCard),
            },
            localized
        );

        if (CardTooltipRenderMethod == null)
            return RenderTooltipBuilder(builder);

        var tooltipData = new CardTooltipData(previewCard, previewCard.Template!);
        var rendered =
            CardTooltipRenderMethod.Invoke(tooltipData, new object[] { builder }) as string;
        return string.IsNullOrWhiteSpace(rendered) ? RenderTooltipBuilder(builder) : rendered;
    }

    private static string GetLocalizedText(TLocalizableText content)
    {
        try
        {
            return content.GetLocalizedText();
        }
        catch
        {
            return content.Text ?? string.Empty;
        }
    }

    private static string RenderTooltipBuilder(TooltipBuilder builder)
    {
        var rendered = new StringBuilder();
        foreach (var component in builder.Components)
        {
            if (
                component is ITooltipToken token
                && token.ReferencedAttribute.HasValue
                && token.ReferencedAttribute.Value.RequiresConversionToSecondsForTooltips()
            )
            {
                var seconds = TooltipExtensions.MillisecondsToSeconds(
                    token.Resolve().GetValueOrDefault()
                );
                rendered.Append(
                    seconds.IsDecimal() ? seconds.GetDecimalValueString() : seconds.ToString()
                );
            }
            else
            {
                rendered.Append(component.Render());
            }
        }

        return rendered.ToString();
    }
}
