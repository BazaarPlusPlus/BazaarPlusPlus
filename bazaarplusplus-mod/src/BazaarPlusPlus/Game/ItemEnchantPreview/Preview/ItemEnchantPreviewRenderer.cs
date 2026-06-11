#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Tooltips;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Infrastructure;
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
        catch (Exception ex)
        {
            BppLog.Debug(
                "ItemEnchantPreview",
                $"RenderWithCardTooltipData failed, falling back to TooltipBuilder: {ex}"
            );
            try
            {
                var builder = TooltipBuilder.Create(
                    CreateTooltipContext(
                        previewCard,
                        previewCard.Template!,
                        new ValueContext(Data.Run, previewCard)
                    ),
                    localized
                );

                return RenderTooltipBuilder(builder).TrimEnd();
            }
            catch (Exception innerEx)
            {
                BppLog.Debug(
                    "ItemEnchantPreview",
                    $"TooltipBuilder fallback failed, returning raw localized text: {innerEx}"
                );
                return localized;
            }
        }
    }

    private static string RenderWithCardTooltipData(ItemCard previewCard, string localized)
    {
        var builder = TooltipBuilder.Create(
            CreateTooltipContext(
                previewCard,
                previewCard.Template!,
                new ValueContext(Data.Run, previewCard)
            ),
            localized
        );

        if (CardTooltipRenderMethod == null)
            return RenderTooltipBuilder(builder);

        var tooltipData = new CardTooltipData(previewCard, previewCard.Template!);
        var rendered =
            CardTooltipRenderMethod.Invoke(tooltipData, new object[] { builder }) as string;
        return string.IsNullOrWhiteSpace(rendered) ? RenderTooltipBuilder(builder) : rendered;
    }

    private static TooltipContext CreateTooltipContext(
        Card instance,
        ITCard template,
        ValueContext valueContext
    )
    {
        var constructor = typeof(TooltipContext).GetConstructor(
            new[] { typeof(Card), typeof(ITCard), typeof(ValueContext) }
        );
        if (constructor != null)
            return (TooltipContext)
                constructor.Invoke(new object[] { instance, template, valueContext });

        var context = default(TooltipContext);
        object boxed = context;
        SetTooltipContextField(boxed, nameof(TooltipContext.Instance), instance);
        SetTooltipContextField(boxed, nameof(TooltipContext.Template), template);
        SetTooltipContextField(boxed, nameof(TooltipContext.ValueContext), valueContext);
        return (TooltipContext)boxed;
    }

    private static void SetTooltipContextField(object context, string fieldName, object value)
    {
        var field = typeof(TooltipContext).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public
        );
        field?.SetValue(context, value);
    }

    private static string GetLocalizedText(TLocalizableText content)
    {
        try
        {
            return content.GetLocalizedText();
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "ItemEnchantPreview",
                $"GetLocalizedText failed, falling back to raw text: {ex}"
            );
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
                && token.ReferencedAttribute.Value.RequiresConversionToSeconds()
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
